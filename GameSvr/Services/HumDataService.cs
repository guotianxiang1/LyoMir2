using System.Collections.Concurrent;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public static class HumDataService
    {
        private const int LoadTimeoutMilliseconds = 5_000;
        private const long CachedLoadLifetimeMilliseconds = 30_000;

        private sealed class PendingLoad
        {
            public TaskCompletionSource<NativeHumanLoadData> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class CachedLoad
        {
            public NativeHumanLoadData Data;
            public long ReceivedAt;
        }

        private static readonly ConcurrentDictionary<string, PendingLoad>
            PendingLoads = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, CachedLoad>
            CachedLoads = new(StringComparer.OrdinalIgnoreCase);

        public static bool DBSocketConnected() =>
            M2Share.DataServer?.Connected == true;

        public static void AddNativeLoadFrame(LegacyDbServerFrame frame)
        {
            if (!NativeHumanDbCodec.TryDecodeLoadFrame(frame,
                    out var load, out var error))
            {
                M2Share.g_Config.nLoadDBErrorCount++;
                M2Share.ErrorMessage("[RunDB] 原生人物推送拒绝: " + error);
                return;
            }

            var key = CreateLoadKey(load.Account, load.CharacterName);
            if (PendingLoads.TryGetValue(key, out var pending))
            {
                pending.Completion.TrySetResult(load);
                return;
            }

            CachedLoads[key] = new CachedLoad
            {
                Data = load,
                ReceivedAt = Environment.TickCount64
            };

            // Close the add/wait race without duplicating the same push.
            if (PendingLoads.TryGetValue(key, out pending)
                && CachedLoads.TryRemove(key, out var cached))
                pending.Completion.TrySetResult(cached.Data);
        }

        public static void NotifyDisconnected()
        {
            CachedLoads.Clear();
            foreach (var pair in PendingLoads)
                pair.Value.Completion.TrySetResult(null);
            PendingLoads.Clear();
        }

        public static bool LoadHumRcdFromDB(string account, string characterName,
            string userAddress, ref THumDataInfo humanRecord, int sessionId)
        {
            return LoadHumRcdFromDB(account, characterName, userAddress,
                ref humanRecord, sessionId, out _);
        }

        public static bool LoadHumRcdFromDB(string account, string characterName,
            string userAddress, ref THumDataInfo humanRecord, int sessionId,
            out NativeHumanLoadData nativeLoad)
        {
            nativeLoad = null;
            humanRecord = new THumDataInfo();
            M2Share.g_Config.nLoadDBCount++;
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(characterName))
                return false;

            var key = CreateLoadKey(account, characterName);
            if (TryTakeCached(key, out var cached))
                return TryAcceptLoad(cached, account, characterName,
                    ref humanRecord, out nativeLoad);

            var pending = new PendingLoad();
            if (!PendingLoads.TryAdd(key, pending))
            {
                M2Share.ErrorMessage(
                    $"[RunDB] 人物推送已有等待者 account={account} chr={characterName}");
                return false;
            }

            try
            {
                if (TryTakeCached(key, out cached))
                    pending.Completion.TrySetResult(cached);

                if (!pending.Completion.Task.Wait(LoadTimeoutMilliseconds))
                {
                    M2Share.ErrorMessage(
                        $"[RunDB] 等待原生人物推送超时 account={account} chr={characterName}");
                    return false;
                }

                var load = pending.Completion.Task.GetAwaiter().GetResult();
                return TryAcceptLoad(load, account, characterName,
                    ref humanRecord, out nativeLoad);
            }
            finally
            {
                ((ICollection<KeyValuePair<string, PendingLoad>>)PendingLoads)
                    .Remove(new KeyValuePair<string, PendingLoad>(key, pending));
            }
        }

        public static bool SaveHumRcdToDB(string account, string characterName,
            ushort saveMode, int param1, int param2,
            THumDataInfo humanRecord, byte[] switchExtension = null)
        {
            M2Share.g_Config.nSaveDBCount++;
            if (!NativeHumanDbCodec.TryEncodeSaveFrame(account, characterName,
                    saveMode, param1, param2, humanRecord, switchExtension,
                    out var frame, out var error))
            {
                M2Share.ErrorMessage(
                    $"[RunDB] 原生人物保存编码失败 chr={characterName}: {error}");
                return false;
            }
            if (!LegacyDbServerFrameCodec.TryEncode(frame,
                    out var wire, out error))
            {
                M2Share.ErrorMessage(
                    $"[RunDB] 原生人物保存封装失败 chr={characterName}: {error}");
                return false;
            }

            var dataServer = M2Share.DataServer;
            if (dataServer == null || !dataServer.SendNativeFrame(wire))
            {
                M2Share.ErrorMessage(
                    $"[RunDB] 原生人物保存未入发送队列 chr={characterName}");
                return false;
            }
            return true;
        }

        private static bool TryTakeCached(string key,
            out NativeHumanLoadData load)
        {
            load = null;
            if (!CachedLoads.TryRemove(key, out var cached)) return false;
            if (cached?.Data == null
                || Environment.TickCount64 - cached.ReceivedAt
                > CachedLoadLifetimeMilliseconds)
                return false;
            load = cached.Data;
            return true;
        }

        private static bool TryAcceptLoad(NativeHumanLoadData load,
            string account, string characterName, ref THumDataInfo humanRecord,
            out NativeHumanLoadData acceptedLoad)
        {
            acceptedLoad = null;
            if (load?.HumanRecord?.Data == null
                || !string.Equals(load.Account, account,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(load.CharacterName, characterName,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            humanRecord = load.HumanRecord;
            humanRecord.Data.sAccount = account;
            humanRecord.Data.sCharName = characterName;
            acceptedLoad = load;
            return true;
        }

        private static string CreateLoadKey(string account,
            string characterName) => (account ?? string.Empty) + '\0'
                                     + (characterName ?? string.Empty);
    }
}

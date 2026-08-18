using System.Buffers.Binary;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr.Services
{
    internal enum NativeAccountStorageResponseDisposition
    {
        InvalidFrame,
        PlayerNotFound,
        AccountMismatch,
        AlreadyLoaded,
        MalformedLoadData,
        LoadApplied,
        SaveStatusIgnored,
        SaveApplied
    }

    internal sealed class NativeAccountStorageState
    {
        internal int Capacity = -1;
        internal bool Dirty;
        internal readonly List<TUserItem> Items = new();
    }

    internal static class NativeAccountStorageClient
    {
        internal const ushort LoadCommand = 0x016B;
        internal const ushort SaveCommand = 0x016C;
        internal const ushort LoadResponseCommand = 0x0062;
        internal const ushort SaveResponseCommand = 0x0063;
        internal const int HeaderSize = 0x48;
        internal const int ItemSize = 0xD0;
        internal const int MaximumCapacity = 300;
        private const int AccountOffset = 0x10;
        private const int AccountCapacity = 20;
        private const int CharacterOffset = 0x25;
        private const int CharacterCapacity = 15;

        internal static bool TryChangeCapacity(
            NativeAccountStorageState state, int delta)
        {
            if (state == null || state.Capacity == -1) return false;
            var capacity = unchecked(state.Capacity + delta);
            if (capacity is < 0 or > MaximumCapacity) return false;
            state.Capacity = capacity;
            state.Dirty = true;
            return true;
        }

        internal static bool IsDepositRestricted(
            GoodItem stdItem, TUserItem item)
        {
            return stdItem == null || item == null
                || (stdItem.NativeReserved02 & 0x80) != 0
                || item.btValue == null || item.btValue.Length < 12
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    item.btValue.AsSpan(10, 2)) == 1;
        }

        internal static int GetGameDataLogQuantity(
            GoodItem stdItem, TUserItem item)
        {
            if (stdItem == null || item == null) return 0;
            return NativeItemFactory.IsPileItem(stdItem) ? item.Dura : 1;
        }

        internal static bool TryEncodeLoadRequest(TPlayObject player,
            ushort mode, out byte[] wire, out string error)
        {
            wire = null;
            error = string.Empty;
            if (player == null)
            {
                error = "native account storage player is null";
                return false;
            }
            if (mode > 1)
            {
                error = "native account storage load mode must be 0 or 1";
                return false;
            }

            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, LoadCommand);
            // DBServer echoes this DWORD in 0062; value 1 requests callback publish.
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), mode);
            WriteIdentity(payload, player);
            return LegacyDbServerFrameCodec.TryEncode(
                new LegacyDbServerFrame(1, 0, payload), out wire, out error);
        }

        internal static bool TryEncodeSaveRequest(TPlayObject player,
            NativeAccountStorageState state, out byte[] wire,
            out string error)
        {
            wire = null;
            error = string.Empty;
            if (player == null || state == null)
            {
                error = "native account storage save context is null";
                return false;
            }
            if (state.Capacity is < 0 or > ushort.MaxValue)
            {
                error = "native account storage capacity is invalid";
                return false;
            }
            if (state.Items.Count > MaximumCapacity)
            {
                error = $"native account storage item count exceeds {MaximumCapacity}";
                return false;
            }

            var payload = new byte[HeaderSize + 4 + state.Items.Count * ItemSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, SaveCommand);
            WriteIdentity(payload, player);
            var tail = payload.AsSpan(HeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(tail,
                (ushort)state.Capacity);
            BinaryPrimitives.WriteUInt16LittleEndian(tail.Slice(2, 2),
                (ushort)state.Items.Count);
            for (var index = 0; index < state.Items.Count; index++)
            {
                if (!NativeMailAttachmentCodec.TryEncode(state.Items[index],
                        out var record, out error))
                {
                    error = $"native account storage item {index} cannot be encoded: "
                            + error;
                    return false;
                }
                record.CopyTo(payload, HeaderSize + 4 + index * ItemSize);
            }

            return LegacyDbServerFrameCodec.TryEncode(
                new LegacyDbServerFrame(1, 0, payload), out wire, out error);
        }

        internal static bool SendLoadRequest(TPlayObject player, ushort mode) =>
            SendLoadRequest(player, mode,
                wire => M2Share.DataServer != null
                        && M2Share.DataServer.SendNativeFrame(wire));

        internal static bool SendLoadRequest(TPlayObject player, ushort mode,
            Func<byte[], bool> send)
        {
            if (!TryEncodeLoadRequest(player, mode, out var wire,
                    out var error))
            {
                M2Share.ErrorMessage(
                    "[AccountStorage] 原生016B请求编码失败: " + error);
                return false;
            }
            return send != null && send(wire);
        }

        internal static bool SendDirtySave(TPlayObject player) =>
            SendDirtySave(player,
                wire => M2Share.DataServer != null
                        && M2Share.DataServer.SendNativeFrame(wire));

        internal static bool SendDirtySave(TPlayObject player,
            Func<byte[], bool> send)
        {
            if (player == null) return false;
            var state = player.GetNativeAccountStorageState();
            if (!state.Dirty) return true;
            if (!TryEncodeSaveRequest(player, state, out var wire,
                    out var error))
            {
                M2Share.ErrorMessage(
                    "[AccountStorage] 原生016C请求编码失败: " + error);
                return false;
            }

            // Only an exact 0063 status=1 acknowledgement clears Dirty.
            return send != null && send(wire);
        }

        internal static NativeAccountStorageResponseDisposition ProcessResponse(
            LegacyDbServerFrame frame)
        {
            return ProcessResponse(frame, FindOnlinePlayer, DecodeItemRecord,
                static (player, state) =>
                    player.PublishNativeAccountStorage(state));
        }

        internal static NativeAccountStorageResponseDisposition ProcessResponse(
            LegacyDbServerFrame frame,
            Func<byte[], TPlayObject> findPlayer,
            Func<byte[], TUserItem> decodeItem,
            Action<TPlayObject, NativeAccountStorageState> publish)
        {
            if (!TryDecodeHeader(frame, out var command, out var status,
                    out var publishFlag, out var account,
                    out var characterName, out var tail)
                || findPlayer == null || decodeItem == null || publish == null)
                return NativeAccountStorageResponseDisposition.InvalidFrame;

            var player = findPlayer(characterName);
            if (player == null)
                return NativeAccountStorageResponseDisposition.PlayerNotFound;
            if (!HUtil32.GbkEncoding.GetBytes(player.m_sUserID ?? string.Empty)
                    .AsSpan().SequenceEqual(account))
                return NativeAccountStorageResponseDisposition.AccountMismatch;

            var state = player.GetNativeAccountStorageState();
            if (command == SaveResponseCommand)
            {
                if (status != 1)
                    return NativeAccountStorageResponseDisposition.SaveStatusIgnored;
                state.Dirty = false;
                return NativeAccountStorageResponseDisposition.SaveApplied;
            }

            if (state.Capacity != -1)
                return NativeAccountStorageResponseDisposition.AlreadyLoaded;
            if (status == 0)
            {
                state.Capacity = 0;
                if (publishFlag == 1) publish(player, state);
                return NativeAccountStorageResponseDisposition.LoadApplied;
            }
            if (tail.Length < 4)
                return NativeAccountStorageResponseDisposition.MalformedLoadData;

            var capacity = BinaryPrimitives.ReadUInt16LittleEndian(tail);
            var count = BinaryPrimitives.ReadUInt16LittleEndian(
                tail.AsSpan(2, 2));
            state.Capacity = capacity;

            // The original validates the exact tail size only when count > 0.
            if (count > 0 && tail.Length != 4 + count * ItemSize)
                return NativeAccountStorageResponseDisposition.MalformedLoadData;

            for (var index = 0; index < count; index++)
            {
                var record = tail.AsSpan(4 + index * ItemSize, ItemSize)
                    .ToArray();
                var item = decodeItem(record);
                if (item == null) continue;

                player.ReassignClientItemId(item);
                NormalizeNativeDay(item, CurrentDelphiDay());
                if (state.Items.Count + 1 > state.Capacity) continue;
                state.Dirty = true;
                state.Items.Add(item);
            }

            if (publishFlag == 1) publish(player, state);
            return NativeAccountStorageResponseDisposition.LoadApplied;
        }

        internal static TPlayObject FindOnlinePlayer(byte[] characterName)
        {
            return FindOnlinePlayer(M2Share.UserEngine?.GetPlayerList(),
                characterName);
        }

        internal static TPlayObject FindOnlinePlayer(
            IEnumerable<TPlayObject> players, byte[] characterName)
        {
            if (players == null || characterName == null) return null;
            foreach (var player in players)
            {
                if (player == null) continue;
                var candidate = HUtil32.GbkEncoding.GetBytes(
                    player.m_sCharName ?? string.Empty);
                if (!candidate.AsSpan().SequenceEqual(characterName)) continue;

                // The native name table returns its first exact match, then
                // rejects that object when it is ghosted or not ReadyRun.
                return player.m_boGhost || !player.m_boReadyRun
                    ? null : player;
            }
            return null;
        }

        internal static TUserItem DecodeItemRecord(byte[] record)
        {
            return DecodeItemRecord(record, index =>
                M2Share.UserEngine?.GetStdItem(index) != null);
        }

        internal static TUserItem DecodeItemRecord(byte[] record,
            Func<ushort, bool> itemExists)
        {
            if (record == null || record.Length != ItemSize
                               || itemExists == null
                               || BinaryPrimitives.ReadUInt32LittleEndian(record)
                               == 0)
                return null;
            if (!NativeMailAttachmentCodec.TryDecode(record, out var item,
                    out _)
                || !itemExists(item.wIndex))
                return null;
            return item;
        }

        internal static void NormalizeNativeDay(TUserItem item,
            int currentDelphiDay)
        {
            if (item?.NativeRecord == null
                || item.NativeRecord.Length != ItemSize
                || item.btValue == null || item.btValue.Length < 12)
                return;
            var storedDay = BinaryPrimitives.ReadUInt16LittleEndian(
                item.NativeRecord.AsSpan(0x14, 2));
            if (storedDay <= 1
                || Math.Abs((long)currentDelphiDay - storedDay) <= 3)
                return;
            BinaryPrimitives.WriteUInt16LittleEndian(
                item.NativeRecord.AsSpan(0x14, 2), 0);
            item.btValue[10] = 0;
            item.btValue[11] = 0;
        }

        private static int CurrentDelphiDay() =>
            (int)Math.Truncate(DateTime.Now.ToOADate());

        private static bool TryDecodeHeader(LegacyDbServerFrame frame,
            out ushort command, out ushort status, out int publishFlag,
            out byte[] account, out byte[] characterName, out byte[] tail)
        {
            command = 0;
            status = 0;
            publishFlag = 0;
            account = null;
            characterName = null;
            tail = null;
            if (frame == null || frame.Type != 1
                              || frame.Payload == null
                              || frame.Payload.Length < HeaderSize)
                return false;

            var payload = frame.Payload.AsSpan();
            command = BinaryPrimitives.ReadUInt16LittleEndian(payload);
            if (command is not LoadResponseCommand
                and not SaveResponseCommand)
                return false;
            status = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.Slice(2, 2));
            publishFlag = BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(4, 4));
            if (!TryReadShortString(payload, AccountOffset, AccountCapacity,
                    out account)
                || !TryReadShortString(payload, CharacterOffset,
                    CharacterCapacity, out characterName))
                return false;
            tail = payload.Slice(HeaderSize).ToArray();
            return true;
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value)
        {
            value = null;
            var length = source[offset];
            if (length > capacity) return false;
            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }

        private static void WriteIdentity(Span<byte> destination,
            TPlayObject player)
        {
            WriteShortString(destination, AccountOffset, AccountCapacity,
                HUtil32.GbkEncoding.GetBytes(player.m_sUserID ?? string.Empty));
            WriteShortString(destination, CharacterOffset, CharacterCapacity,
                HUtil32.GbkEncoding.GetBytes(player.m_sCharName ?? string.Empty));
        }

        private static void WriteShortString(Span<byte> destination,
            int offset, int capacity, byte[] value)
        {
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            destination[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
        }
    }
}

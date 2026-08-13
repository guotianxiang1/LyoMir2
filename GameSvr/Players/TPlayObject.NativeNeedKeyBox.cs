using System.Buffers.Binary;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const int NativeNeedKeyBoxOpenMessage = 950;
        internal const int NativeNeedKeyBoxSelectMessage = 952;
        internal const int NativeNeedKeyBoxCloseMessage = 953;
        internal const int NativeNeedKeyBoxWireSlotCount = 9;
        internal const int NativeNeedKeyBoxWireSlotSize = 24;
        internal const int NativeNeedKeyBoxWireBodySize = 216;
        internal const int NativeNeedKeyBoxRequiredFreeSlots = 6;

        internal const int NativeNeedKeyBoxYuanbaoIdent = 125;
        internal const int NativeNeedKeyBoxYuanbaoSelector = 10000;
        internal const int NativeNeedKeyBoxYuanbaoAmount = 1;

        private const int NativeNeedKeyBoxVisibleSlotCount = 8;
        private const int NativeNeedKeyBoxRandomRange = 1000;
        private const int NativeNeedKeyBoxGlobalRareInterval = 2000;
        private const int NativeNeedKeyBoxPersonalRareInterval = 100;
        private const int NativeNeedKeyBoxNameMaximumGbkBytes = 15;
        private const int NativeNeedKeyBoxDescriptorMaximumGbkBytes = 20;

        private static int s_nativeNeedKeyBoxGlobalRareCounter;
        private static readonly object s_nativeNeedKeyBoxConfigSync = new();
        private static NativeNeedKeyBoxConfig s_nativeNeedKeyBoxConfig;

        private int _nativeNeedKeyBoxPersonalRareCounter;
        private int _nativeNeedKeyBoxSelectedSlot;
        private byte _nativeCattleNeedKeyBoxMode;
        private byte[] _nativeNeedKeyBoxDefaultReward;
        private byte[] _nativeNeedKeyBoxSelectedReward;
        private NativeNeedKeyBoxReward[] _nativeNeedKeyBoxSlots =
            new NativeNeedKeyBoxReward[NativeNeedKeyBoxWireSlotCount];
        private byte[] _nativeNeedKeyBoxWireBody =
            new byte[NativeNeedKeyBoxWireBodySize];
        private bool _nativeNeedKeyBoxRepeatEligible;
        private bool _nativeNeedKeyBoxYuanbaoPending;
        private Func<bool> _nativeNeedKeyBoxOtherPendingReward;

        // Tests replace this delegate. Runtime uses the server's Delphi-style
        // random source.
        private Func<int, int> _nativeNeedKeyBoxRandom;

        internal bool NativeNeedKeyBoxRepeatEligible =>
            _nativeNeedKeyBoxRepeatEligible;

        internal bool NativeNeedKeyBoxYuanbaoPending =>
            _nativeNeedKeyBoxYuanbaoPending;

        internal int NativeNeedKeyBoxSelectedSlot =>
            _nativeNeedKeyBoxSelectedSlot;

        internal int NativeNeedKeyBoxPersonalRareCounter =>
            _nativeNeedKeyBoxPersonalRareCounter;

        internal int NativeNeedKeyBoxRareMode =>
            _nativeCattleNeedKeyBoxMode;

        internal byte NativeCattleNeedKeyBoxMode
        {
            get => _nativeCattleNeedKeyBoxMode;
            set => _nativeCattleNeedKeyBoxMode = value;
        }

        internal void SetNativeNeedKeyBoxOtherPendingRewardPredicate(
            Func<bool> predicate) =>
            _nativeNeedKeyBoxOtherPendingReward = predicate;

        internal byte[] GetNativeNeedKeyBoxWireBody() =>
            _nativeNeedKeyBoxWireBody.ToArray();

        internal static bool InitializeNativeNeedKeyBoxConfig(
            string rootPath, string baseDirectory)
        {
            if (string.IsNullOrEmpty(rootPath) ||
                string.IsNullOrEmpty(baseDirectory))
                return InitializeNativeNeedKeyBoxConfigFromPath(null);

            return InitializeNativeNeedKeyBoxConfigFromPath(
                Path.Combine(rootPath, baseDirectory, "Config",
                    "宝藏天赐.ini"));
        }

        internal static bool InitializeNativeNeedKeyBoxConfigFromPath(
            string configPath)
        {
            var loaded = NativeNeedKeyBoxConfig.TryLoad(configPath,
                out var config);
            lock (s_nativeNeedKeyBoxConfigSync)
                s_nativeNeedKeyBoxConfig = loaded ? config : null;
            return loaded;
        }

        private static NativeNeedKeyBoxConfig CaptureNativeNeedKeyBoxConfig()
        {
            lock (s_nativeNeedKeyBoxConfigSync)
                return s_nativeNeedKeyBoxConfig;
        }

        internal NativeNeedKeyBoxOpenResult TryOpenNativeNeedKeyBox(
            bool hasScriptContext, out byte[] body)
        {
            body = null;
            var config = CaptureNativeNeedKeyBoxConfig();
            if (config == null)
                return NativeNeedKeyBoxOpenResult.ConfigurationUnavailable;

            var key = FindNativeNeedKeyBoxBagItem(config.KeyStdIndex);
            if (key == null)
                return NativeNeedKeyBoxOpenResult.MissingKey;

            if (HasNativeNeedKeyBoxPendingReward())
                return NativeNeedKeyBoxOpenResult.Busy;

            if (!HasNativeNeedKeyBoxFreeSlots())
                return NativeNeedKeyBoxOpenResult.BagFull;

            var keyDefinition = M2Share.UserEngine?.GetStdItem(key.wIndex);
            if (keyDefinition == null ||
                string.IsNullOrEmpty(keyDefinition.Name) ||
                !TryTakeNativeNeedKeyBoxItemByName(keyDefinition.Name, 1))
                return NativeNeedKeyBoxOpenResult.KeyCommitFailed;

            // Native order commits the key before reward construction. A
            // construction failure intentionally does not restore the key.
            if (!TryBuildNativeNeedKeyBoxState(config, out body) ||
                !SendNativeNeedKeyBoxOpenPacket(body))
                return NativeNeedKeyBoxOpenResult.StateBuildFailed;

            return NativeNeedKeyBoxOpenResult.Opened;
        }

        internal NativeNeedKeyBoxYuanbaoResult TryBeginNativeNeedKeyBoxYuanbao(
            bool hasScriptContext,
            Func<int, int, int, int, int, bool> enqueue)
        {
            if (m_nGameGold < NativeNeedKeyBoxYuanbaoAmount)
                return NativeNeedKeyBoxYuanbaoResult.InsufficientCredit;

            if (!_nativeNeedKeyBoxRepeatEligible ||
                HasNativeNeedKeyBoxPendingReward())
                return NativeNeedKeyBoxYuanbaoResult.Busy;

            if (!HasNativeNeedKeyBoxFreeSlots())
                return NativeNeedKeyBoxYuanbaoResult.BagFull;

            if (enqueue == null || !enqueue(NativeNeedKeyBoxYuanbaoIdent,
                    NativeNeedKeyBoxYuanbaoSelector, 0, 0,
                    NativeNeedKeyBoxYuanbaoAmount))
                return NativeNeedKeyBoxYuanbaoResult.EnqueueFailed;

            _nativeNeedKeyBoxRepeatEligible = false;
            _nativeNeedKeyBoxYuanbaoPending = true;
            return NativeNeedKeyBoxYuanbaoResult.Submitted;
        }

        internal bool TryCompleteNativeNeedKeyBoxYuanbaoSuccess(
            out byte[] body)
        {
            body = null;
            var config = CaptureNativeNeedKeyBoxConfig();
            if (!_nativeNeedKeyBoxYuanbaoPending ||
                HasNativeNeedKeyBoxPendingReward() ||
                config == null)
                return false;

            // F45 remains set until a successful claim. This is deliberate.
            return TryBuildNativeNeedKeyBoxState(config, out body) &&
                   SendNativeNeedKeyBoxOpenPacket(body);
        }

        internal void CompleteNativeNeedKeyBoxYuanbaoFailure()
        {
            // Native failure clears only the generic DB busy flag, which is
            // owned by the future YB adapter. F44=0/F45=1 stays stuck here.
        }

        internal bool TrySelectNativeNeedKeyBox(out int selectedSlot)
        {
            selectedSlot = 0;
            if (_nativeNeedKeyBoxDefaultReward == null ||
                _nativeNeedKeyBoxDefaultReward.Length == 0)
                return false;

            selectedSlot = _nativeNeedKeyBoxSelectedSlot;
            _nativeNeedKeyBoxDefaultReward = null;
            return true;
        }

        internal NativeNeedKeyBoxClaimResult TryClaimNativeNeedKeyBox(
            Func<byte[], bool> giveReward)
        {
            if (!HasNativeNeedKeyBoxFreeSlots())
                return NativeNeedKeyBoxClaimResult.BagFull;

            var descriptor = _nativeNeedKeyBoxDefaultReward != null &&
                             _nativeNeedKeyBoxDefaultReward.Length != 0
                ? _nativeNeedKeyBoxDefaultReward
                : _nativeNeedKeyBoxSelectedReward;
            if (descriptor == null || descriptor.Length == 0)
                return NativeNeedKeyBoxClaimResult.NoReward;

            // Preserve the fixed raw descriptor through Give. The original
            // grant result is ignored and both reward fields are cleared.
            giveReward?.Invoke(descriptor.ToArray());

            var paidOpen = _nativeNeedKeyBoxYuanbaoPending;
            _nativeNeedKeyBoxDefaultReward = null;
            _nativeNeedKeyBoxSelectedReward = null;

            if (m_nGameGold > 0 && m_NPC != null)
            {
                if (paidOpen)
                {
                    _nativeNeedKeyBoxRepeatEligible = false;
                    _nativeNeedKeyBoxYuanbaoPending = false;
                }
                else
                {
                    _nativeNeedKeyBoxRepeatEligible =
                        RollNativeNeedKeyBoxRepeat(m_Abil?.Level ?? 0,
                            NextNativeNeedKeyBoxRandom);
                }
            }

            return NativeNeedKeyBoxClaimResult.Success;
        }

        internal void ClearNativeNeedKeyBoxState()
        {
            ClearNativeNeedKeyBoxRewardState();
            _nativeNeedKeyBoxRepeatEligible = false;
            _nativeNeedKeyBoxYuanbaoPending = false;
        }

        private void ClearNativeNeedKeyBoxRewardState()
        {
            _nativeNeedKeyBoxSelectedSlot = 0;
            _nativeCattleNeedKeyBoxMode = 0;
            _nativeNeedKeyBoxDefaultReward = null;
            _nativeNeedKeyBoxSelectedReward = null;
            Array.Clear(_nativeNeedKeyBoxSlots, 0,
                _nativeNeedKeyBoxSlots.Length);
            Array.Clear(_nativeNeedKeyBoxWireBody, 0,
                _nativeNeedKeyBoxWireBody.Length);
        }

        private bool HasNativeNeedKeyBoxPendingReward()
        {
            if ((_nativeNeedKeyBoxDefaultReward?.Length ?? 0) != 0 ||
                (_nativeNeedKeyBoxSelectedReward?.Length ?? 0) != 0)
                return true;

            var cattle = m_NativeCattle;
            if (cattle != null &&
                (cattle.HasRevealPending || cattle.HasClaimPending))
                return true;

            var predicate = _nativeNeedKeyBoxOtherPendingReward;
            if (predicate == null) return false;
            try
            {
                return predicate();
            }
            catch (Exception)
            {
                return true;
            }
        }

        private bool HasNativeNeedKeyBoxFreeSlots() =>
            m_ItemList.Count + NativeNeedKeyBoxRequiredFreeSlots <=
            BagCapacity.Of(this);

        internal bool SendNativeNeedKeyBoxOpenPacket(byte[] body)
        {
            if (body == null || body.Length != NativeNeedKeyBoxWireBodySize)
                return false;

            m_DefMsg = Grobal2.MakeDefaultMsg(NativeNeedKeyBoxOpenMessage,
                0, 0, 0, 0);
            SendSocket(m_DefMsg, body);
            return true;
        }

        private TUserItem FindNativeNeedKeyBoxBagItem(int stdItemIndex)
        {
            if (stdItemIndex <= 0 || stdItemIndex > ushort.MaxValue)
                return null;
            var target = unchecked((ushort)stdItemIndex);
            for (var index = 0; index < m_ItemList.Count; index++)
            {
                var item = m_ItemList[index];
                if (item != null && item.wIndex == target)
                    return item;
            }
            return null;
        }

        private bool TryTakeNativeNeedKeyBoxItemByName(string canonicalName,
            int amount)
        {
            if (string.IsNullOrEmpty(canonicalName) || amount <= 0)
                return false;

            long available = 0;
            for (var index = m_ItemList.Count - 1; index >= 0; index--)
            {
                var item = m_ItemList[index];
                var definition = item == null
                    ? null
                    : M2Share.UserEngine?.GetStdItem(item.wIndex);
                if (definition == null || !string.Equals(definition.Name,
                        canonicalName, StringComparison.OrdinalIgnoreCase))
                    continue;

                available += definition.StdMode == 7 ? item.Dura : 1;
                if (available >= amount) break;
            }
            if (available < amount) return false;

            var remaining = amount;
            for (var index = m_ItemList.Count - 1;
                 index >= 0 && remaining > 0;
                 index--)
            {
                var item = m_ItemList[index];
                var definition = item == null
                    ? null
                    : M2Share.UserEngine?.GetStdItem(item.wIndex);
                if (definition == null || !string.Equals(definition.Name,
                        canonicalName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (definition.StdMode == 7)
                {
                    if (item.Dura == 0) continue;
                    if (item.Dura > remaining)
                    {
                        var consumed = remaining;
                        item.Dura = unchecked((ushort)(item.Dura - consumed));
                        AddNativeNeedKeyBoxTakeLog(definition.Name, item,
                            consumed);
                        remaining = 0;
                        SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                            EnsureClientItemId(item), item.Dura, item.DuraMax,
                            0, string.Empty);
                        continue;
                    }

                    remaining -= item.Dura;
                    AddNativeNeedKeyBoxTakeLog(definition.Name, item,
                        item.Dura);
                }
                else
                {
                    remaining--;
                    AddNativeNeedKeyBoxTakeLog(definition.Name, item, 1);
                }

                m_ItemList.RemoveAt(index);
                SendDelItems(item);
                Dispose(item);
            }

            WeightChanged();
            return true;
        }

        private void AddNativeNeedKeyBoxTakeLog(string itemName,
            TUserItem item, int amount)
        {
            M2Share.AddGameDataLog(string.Join('\t', 10, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, itemName,
                unchecked((uint)item.MakeIndex), amount, "NPC收取"));
        }

        private bool TryBuildNativeNeedKeyBoxState(
            NativeNeedKeyBoxConfig config, out byte[] body)
        {
            body = null;
            if (config == null) return false;

            try
            {
                var candidates = new List<NativeNeedKeyBoxReward>(8);
                for (var pool = 0; pool < 7; pool++)
                {
                    var reward = SelectNativeNeedKeyBoxReward(
                        config.Pools[pool]);
                    if (reward != null) candidates.Add(reward);
                }

                var rareKind = NextNativeNeedKeyBoxRareKind();
                if (rareKind == 0)
                {
                    var roll = NextNativeNeedKeyBoxRandom(
                        NativeNeedKeyBoxRandomRange);
                    for (var index = 0; index < candidates.Count &&
                         index < config.Probabilities.Length; index++)
                    {
                        if (config.Probabilities[index] < roll) continue;
                        candidates[index] = candidates[index].WithSelected();
                        break;
                    }
                }

                var special = SelectNativeNeedKeyBoxReward(
                    config.Pools[7 + rareKind]);
                if (special != null)
                {
                    candidates.Add(rareKind > 0
                        ? special.WithSelected()
                        : special);
                }

                var slots = new NativeNeedKeyBoxReward[
                    NativeNeedKeyBoxWireSlotCount];
                var selectedSlot = 0;
                byte[] selectedReward = null;
                for (var slot = 0;
                     slot < NativeNeedKeyBoxVisibleSlotCount &&
                     candidates.Count > 0;
                     slot++)
                {
                    var candidateIndex = NextNativeNeedKeyBoxRandom(
                        candidates.Count);
                    var reward = candidates[candidateIndex];
                    candidates.RemoveAt(candidateIndex);
                    slots[slot] = reward;
                    if (!reward.Selected) continue;
                    selectedSlot = slot + 1;
                    selectedReward = reward.Descriptor;
                }

                var defaultReward = SelectNativeNeedKeyBoxReward(
                    config.Pools[10]);
                slots[8] = defaultReward;

                var wireBody = BuildNativeNeedKeyBoxWireBody(slots);
                _nativeNeedKeyBoxSlots = slots;
                _nativeNeedKeyBoxWireBody = wireBody;
                _nativeNeedKeyBoxSelectedSlot = selectedSlot;
                _nativeNeedKeyBoxSelectedReward = selectedReward;
                _nativeNeedKeyBoxDefaultReward = defaultReward?.Descriptor;
                _nativeCattleNeedKeyBoxMode = rareKind > 0 ? (byte)4 : (byte)0;
                body = wireBody.ToArray();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private NativeNeedKeyBoxReward SelectNativeNeedKeyBoxReward(
            IReadOnlyList<NativeNeedKeyBoxReward> rewards)
        {
            var roll = NextNativeNeedKeyBoxRandom(
                NativeNeedKeyBoxRandomRange);
            if (rewards == null || rewards.Count == 0) return null;
            for (var index = 0; index < rewards.Count; index++)
            {
                if (rewards[index].Threshold >= roll)
                    return rewards[index];
            }
            return null;
        }

        private int NextNativeNeedKeyBoxRareKind()
        {
            var global = Interlocked.Increment(
                ref s_nativeNeedKeyBoxGlobalRareCounter);
            _nativeNeedKeyBoxPersonalRareCounter = unchecked(
                _nativeNeedKeyBoxPersonalRareCounter + 1);

            if (global % NativeNeedKeyBoxGlobalRareInterval == 0)
                return 2;
            return _nativeNeedKeyBoxPersonalRareCounter %
                NativeNeedKeyBoxPersonalRareInterval == 0 ? 1 : 0;
        }

        private int NextNativeNeedKeyBoxRandom(int range)
        {
            if (range <= 0) return 0;
            var random = _nativeNeedKeyBoxRandom ??
                (maximum => (M2Share.RandomNumber ??
                    RandomNumber.GetInstance()).Random(maximum));
            var value = random(range);
            if ((uint)value >= (uint)range)
                throw new InvalidOperationException(
                    $"NeedKeyBox random returned {value} outside 0..{range - 1}");
            return value;
        }

        private static bool RollNativeNeedKeyBoxRepeat(int level,
            Func<int, int> random)
        {
            if (level < 40 || random == null) return false;
            var threshold = level < 47 ? 90
                : level < 56 ? 50
                : level < 60 ? 30
                : 10;
            var roll = random(100);
            if ((uint)roll >= 100)
                throw new InvalidOperationException(
                    $"NeedKeyBox repeat random returned {roll} outside 0..99");
            return roll < threshold;
        }

        private static byte[] BuildNativeNeedKeyBoxWireBody(
            IReadOnlyList<NativeNeedKeyBoxReward> slots)
        {
            var body = new byte[NativeNeedKeyBoxWireBodySize];
            if (slots == null) return body;

            for (var slot = 0;
                 slot < NativeNeedKeyBoxWireSlotCount && slot < slots.Count;
                 slot++)
            {
                var reward = slots[slot];
                if (reward == null || reward.DisplayName.Length == 0)
                    continue;

                var offset = slot * NativeNeedKeyBoxWireSlotSize;
                WriteNativeNeedKeyBoxShortName(
                    body.AsSpan(offset, 16), reward.DisplayName);
                BinaryPrimitives.WriteInt32LittleEndian(
                    body.AsSpan(offset + 16, 4), reward.Looks);
                BinaryPrimitives.WriteInt32LittleEndian(
                    body.AsSpan(offset + 20, 4), reward.Amount);
            }
            return body;
        }

        private static void WriteNativeNeedKeyBoxShortName(
            Span<byte> destination, ReadOnlySpan<byte> name)
        {
            destination.Clear();
            var length = Math.Min(name.Length,
                NativeNeedKeyBoxNameMaximumGbkBytes);
            destination[0] = unchecked((byte)length);
            name.Slice(0, length).CopyTo(destination.Slice(1));
        }

        private static bool TryResolveNativeNeedKeyBoxLooks(string name,
            out int looks)
        {
            looks = 0;
            var itemIndex = M2Share.UserEngine?.GetStdItemIdx(name) ?? 0;
            if (itemIndex > 0)
            {
                var definition = M2Share.UserEngine.GetStdItem(itemIndex);
                if (definition == null) return false;
                looks = definition.Looks;
                return true;
            }

            if (string.Equals(name, "经验",
                    StringComparison.OrdinalIgnoreCase))
                looks = 1186;
            else if (string.Equals(name, "金刚石",
                         StringComparison.OrdinalIgnoreCase))
                looks = 1187;
            else if (string.Equals(name, "牛气值",
                         StringComparison.OrdinalIgnoreCase))
                looks = 1588;
            else if (string.Equals(name, "声望",
                         StringComparison.OrdinalIgnoreCase))
                looks = 1185;
            else if (string.Equals(name, "金币",
                         StringComparison.OrdinalIgnoreCase))
                looks = 115;
            else
                return false;
            return true;
        }

        private sealed class NativeNeedKeyBoxConfig
        {
            internal int KeyStdIndex { get; private init; }
            internal int[] Probabilities { get; private init; }
            internal List<NativeNeedKeyBoxReward>[] Pools { get; private init; }

            internal static bool TryLoad(string configPath,
                out NativeNeedKeyBoxConfig config)
            {
                config = null;
                if (string.IsNullOrEmpty(configPath) ||
                    !File.Exists(configPath) || M2Share.UserEngine == null)
                    return false;

                try
                {
                    var ini = new NativeNeedKeyBoxIni(configPath);
                    var keyName = ini.ReadString("Setup", "ValuedItem",
                        string.Empty);
                    var keyStdIndex = M2Share.UserEngine.GetStdItemIdx(keyName);
                    if (keyStdIndex <= 0) return false;

                    var pools = new List<NativeNeedKeyBoxReward>[11];
                    for (var pool = 1; pool <= pools.Length; pool++)
                        pools[pool - 1] = ReadPool(ini, pool);

                    var probabilities = new int[7];
                    for (var index = 0; index < probabilities.Length; index++)
                    {
                        probabilities[index] = ini.ReadInteger("宝箱1",
                            "概率" + (index + 1), 0);
                    }

                    config = new NativeNeedKeyBoxConfig
                    {
                        KeyStdIndex = keyStdIndex,
                        Pools = pools,
                        Probabilities = probabilities
                    };
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            private static List<NativeNeedKeyBoxReward> ReadPool(
                NativeNeedKeyBoxIni ini, int pool)
            {
                var rewards = new List<NativeNeedKeyBoxReward>();
                var section = pool + "类奖励";
                for (var number = 1; number <= 100; number++)
                {
                    var value = ini.ReadString(section, "奖品" + number,
                        null);
                    if (string.IsNullOrEmpty(value)) break;
                    if (TryParseReward(value, out var reward))
                        rewards.Add(reward);
                }
                return rewards;
            }

            private static bool TryParseReward(string value,
                out NativeNeedKeyBoxReward reward)
            {
                reward = null;
                var slash = value.IndexOf('/');
                if (slash <= 0 || slash == value.Length - 1)
                    return false;

                var descriptorSource = value.Substring(0, slash);
                var colon = descriptorSource.IndexOf(':');
                if (colon <= 0 || colon == descriptorSource.Length - 1 ||
                    !int.TryParse(descriptorSource.Substring(colon + 1),
                        out var amount) ||
                    !int.TryParse(value.Substring(slash + 1),
                        out var threshold))
                    return false;

                var sourceName = descriptorSource.Substring(0, colon);
                if (!TryResolveNativeNeedKeyBoxLooks(sourceName,
                        out var looks))
                    return false;

                var nameBytes = HUtil32.GbkEncoding.GetBytes(sourceName);
                var name = nameBytes.AsSpan(0, Math.Min(nameBytes.Length,
                    NativeNeedKeyBoxNameMaximumGbkBytes)).ToArray();
                var descriptorBytes = HUtil32.GbkEncoding.GetBytes(
                    descriptorSource);
                var descriptor = descriptorBytes.AsSpan(0,
                    Math.Min(descriptorBytes.Length,
                        NativeNeedKeyBoxDescriptorMaximumGbkBytes)).ToArray();
                reward = new NativeNeedKeyBoxReward(descriptor, name,
                    looks, amount, threshold, false);
                return true;
            }
        }

        private sealed class NativeNeedKeyBoxIni : IniFile
        {
            internal NativeNeedKeyBoxIni(string fileName) : base(fileName)
            {
                Load();
            }
        }

        private sealed class NativeNeedKeyBoxReward
        {
            internal NativeNeedKeyBoxReward(byte[] descriptor,
                byte[] displayName,
                int looks, int amount, int threshold, bool selected)
            {
                Descriptor = descriptor?.ToArray() ?? Array.Empty<byte>();
                DisplayName = displayName?.ToArray() ?? Array.Empty<byte>();
                Looks = looks;
                Amount = amount;
                Threshold = threshold;
                Selected = selected;
            }

            internal byte[] Descriptor { get; }
            internal byte[] DisplayName { get; }
            internal int Looks { get; }
            internal int Amount { get; }
            internal int Threshold { get; }
            internal bool Selected { get; }

            internal NativeNeedKeyBoxReward WithSelected() =>
                new(Descriptor, DisplayName, Looks, Amount, Threshold, true);
        }
    }

    internal enum NativeNeedKeyBoxOpenResult : byte
    {
        Opened,
        ConfigurationUnavailable,
        MissingKey,
        Busy,
        BagFull,
        KeyCommitFailed,
        StateBuildFailed
    }

    internal enum NativeNeedKeyBoxYuanbaoResult : byte
    {
        Submitted,
        InsufficientCredit,
        Busy,
        BagFull,
        EnqueueFailed
    }

    internal enum NativeNeedKeyBoxClaimResult : sbyte
    {
        BagFull = 0,
        Success = 1,
        NoReward = 2
    }
}

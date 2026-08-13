using System.Buffers.Binary;
using GameSvr.PasEngine;
using SystemModule;
using SystemModule.Common;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeExchangeBookUnavailableMessage =
            "你此时不能开启";
        internal const string NativeExchangeBookPairMismatchMessage =
            "你要开启的天赐不匹配";
        internal const string NativeExchangeBookBagCapacityMessage =
            "包裹空位不足，不能开启";
        internal const string NativeExchangeBookClaimBagCapacityMessage =
            "你没有6个包裹空位，不能领取";

        private const int ExchangeBookVisibleSlotCount = 8;
        private const int ExchangeBookSlotCount = 12;
        private const int ExchangeBookWireSlotSize = 24;
        private const int ExchangeBookWireBodySize =
            ExchangeBookSlotCount * ExchangeBookWireSlotSize;
        // Delphi ShortString[20]: one length byte plus up to 20 CP936 bytes.
        private const int ExchangeBookDescriptorSize = 21;
        private const int ExchangeBookDescriptorPayloadSize =
            ExchangeBookDescriptorSize - 1;
        private const int ExchangeBookRequiredActionFreeSlots = 4;
        private const int ExchangeBookRequiredClaimFreeSlots = 6;
        private const int ExchangeBookForcedWeight = 10000;

        private static readonly int[] ExchangeBookPersonalRareIntervals =
        {
            40, 80, 160, 27, 200, 500, 500, 200
        };

        private static readonly int[] ExchangeBookGlobalRareIntervals =
        {
            650, 1300, 2600, 433, 2500, 2500, 2500, 2500
        };

        private static readonly int[] ExchangeBookGlobalRareCounters =
            new int[8];

        private static readonly string[] ExchangeBookBoxNames =
        {
            "赤金天赐", "白银天赐", "紫铜天赐", "神秘天赐",
            "新年天赐", "春节天赐", "节日天赐", "感恩天赐"
        };

        private static readonly string[] ExchangeBookKeyNames =
        {
            "赤金钥匙", "白银钥匙", "紫铜钥匙", "神秘钥匙",
            "新年钥匙", "春节钥匙", "节日钥匙", "感恩钥匙"
        };

        private int _nativeExchangeBookPairIndex = -1;
        // Native +0x1804 belongs to the deferred item-use path; +0x1808 stores
        // the box StdItem index discovered by CM_MERCHANTQUERYEXCHGBOOK.
        private int _nativeExchangeBookDeferredBoxClientItemId;
        private int _nativeExchangeBookBoxStdItemIndex;
        private int _nativeExchangeBookRound;
        private int _nativeExchangeBookSelectedSlot = -1;
        private int _nativeExchangeBookRareState;
        private readonly int[] _nativeExchangeBookPersonalRareCounters =
            new int[8];
        private readonly byte[] _nativeExchangeBookInitialPrize =
            new byte[ExchangeBookDescriptorSize];
        private readonly byte[] _nativeExchangeBookRotatePrize =
            new byte[ExchangeBookDescriptorSize];
        private ExchangeBookReward[] _nativeExchangeBookSlots =
            new ExchangeBookReward[ExchangeBookSlotCount];
        private byte[] _nativeExchangeBookWireBody =
            new byte[ExchangeBookWireBodySize];

        internal void RestoreNativeExchangeBookPersonalRareCounters(
            int[] counters)
        {
            Array.Clear(_nativeExchangeBookPersonalRareCounters, 0,
                _nativeExchangeBookPersonalRareCounters.Length);
            if (counters == null) return;
            Array.Copy(counters, _nativeExchangeBookPersonalRareCounters,
                Math.Min(counters.Length,
                    _nativeExchangeBookPersonalRareCounters.Length));
        }

        internal void ClientMerchantQueryExchgBook(int boxClientItemId,
            int keyClientItemId)
        {
            if (HasExchangeBookDescriptor(_nativeExchangeBookInitialPrize) ||
                HasExchangeBookDescriptor(_nativeExchangeBookRotatePrize))
            {
                SendExchangeBookOpenFailure();
                return;
            }

            // The native handler silently ignores stale item identifiers.
            if (!TryFindExchangeBookBagItem(boxClientItemId, out var box,
                    out var boxIndex))
                return;

            _nativeExchangeBookBoxStdItemIndex = box.wIndex;
            if (!TryFindExchangeBookBagItem(keyClientItemId, out var key,
                    out var keyIndex))
                return;

            // During rounds 1..3, CM_MERCHANTQUERYEXCHGBOOK is an alternate
            // rotate request. The first instance is validated but retained.
            if (_nativeExchangeBookRound != 0)
            {
                RotateExchangeBook(keyClientItemId, key, keyIndex);
                return;
            }

            if (ReferenceEquals(box, key) || boxIndex == keyIndex ||
                !TryResolveExchangeBookPair(box, key, out var pairIndex) ||
                _nativeExchangeBookPairIndex != -1 &&
                _nativeExchangeBookPairIndex != pairIndex)
            {
                SendExchangeBookOpenFailure(
                    NativeExchangeBookPairMismatchMessage);
                return;
            }

            if (!CanRunExchangeBookAction())
            {
                SendExchangeBookOpenFailure(
                    NativeExchangeBookBagCapacityMessage);
                return;
            }

            var configPath = Path.Combine(M2Share.sRootPath, "Share", "config",
                ExchangeBookBoxNames[pairIndex] + "2.ini");

            // Native order is observable on every selector/configuration fault:
            // remove the validated box first, then the key, and only then run
            // the reward selector and its random/counter transitions.
            ConsumeExchangeBookBagItem(boxIndex, box);
            if (keyIndex > boxIndex) keyIndex--;
            ConsumeExchangeBookBagItem(keyIndex, key);
            _nativeExchangeBookPairIndex = pairIndex;

            if (!File.Exists(configPath) ||
                !TryBuildExchangeBookState(configPath, pairIndex, out var slots,
                    out var body, out var selectedSlot))
            {
                SendExchangeBookOpenFailure();
                return;
            }

            _nativeExchangeBookRound = 0;
            _nativeExchangeBookSelectedSlot = selectedSlot;
            _nativeExchangeBookSlots = slots;
            _nativeExchangeBookWireBody = body;
            CopyExchangeBookDescriptor(_nativeExchangeBookInitialPrize,
                slots[8]?.Descriptor);
            CopyExchangeBookDescriptor(_nativeExchangeBookRotatePrize,
                selectedSlot >= 0 ? slots[selectedSlot]?.Descriptor : null);

            m_DefMsg = Grobal2.MakeDefaultMsg(
                (short)Grobal2.SM_MERCHANTQUERYEXCHGBOOK, 0, pairIndex, 0, 0);
            SendSocket(m_DefMsg, body);
        }

        internal void ClientExchangeBookRotate(int ignoredRecog,
            int keyClientItemId)
        {
            _ = ignoredRecog;

            if (_nativeExchangeBookRound == 0)
            {
                if (!HasExchangeBookDescriptor(
                        _nativeExchangeBookInitialPrize))
                    return;

                // The first spin was selected when the 288-byte state was built.
                // It only abandons the immediately claimable centre prize.
                Array.Clear(_nativeExchangeBookInitialPrize, 0,
                    _nativeExchangeBookInitialPrize.Length);
                SendExchangeBookRotateSuccess();
                return;
            }

            if (!TryFindExchangeBookBagItem(keyClientItemId, out var key,
                    out var keyIndex))
            {
                SendExchangeBookRotateFailure();
                return;
            }

            RotateExchangeBook(keyClientItemId, key, keyIndex);
        }

        private void RotateExchangeBook(int keyClientItemId, TUserItem key,
            int keyIndex)
        {
            if (_nativeExchangeBookPairIndex < 0 ||
                _nativeExchangeBookPairIndex >= ExchangeBookBoxNames.Length ||
                _nativeExchangeBookRound <= 0 ||
                _nativeExchangeBookRound > 3)
            {
                SendExchangeBookRotateFailure(
                    NativeExchangeBookPairMismatchMessage);
                return;
            }

            if (!CanRunExchangeBookAction())
            {
                SendExchangeBookRotateFailure(
                    NativeExchangeBookBagCapacityMessage);
                return;
            }

            // Native order: remove the supplied instance, then select the next
            // reward. No item-name or MakeIndex fallback is performed here.
            ConsumeExchangeBookBagItem(keyIndex, key);

            var selectedSlot = SelectExchangeBookSlot(_nativeExchangeBookSlots);
            if (selectedSlot < 0)
            {
                SendExchangeBookRotateFailure();
                return;
            }

            _nativeExchangeBookSelectedSlot = selectedSlot;
            CopyExchangeBookDescriptor(_nativeExchangeBookRotatePrize,
                _nativeExchangeBookSlots[selectedSlot].Descriptor);
            SendExchangeBookRotateSuccess();
        }

        internal void ClientExchangeBookGetPrize()
        {
            if (BagCapacity.Of(this) - m_ItemList.Count <
                ExchangeBookRequiredClaimFreeSlots)
            {
                SendDefMessage((short)Grobal2.SM_EXCHANGEBOOK_GET_PRIZE,
                    1, 0, 0, 0,
                    NativeExchangeBookClaimBagCapacityMessage);
                return;
            }

            var hasInitialPrize = HasExchangeBookDescriptor(
                _nativeExchangeBookInitialPrize);
            var hasRotatePrize = HasExchangeBookDescriptor(
                _nativeExchangeBookRotatePrize);
            if (!hasInitialPrize && !hasRotatePrize)
            {
                SendDefMessage((short)Grobal2.SM_EXCHANGEBOOK_GET_PRIZE,
                    2, 0, 0, 0, string.Empty);
                return;
            }

            var descriptor = ReadExchangeBookDescriptorPayload(
                hasInitialPrize
                    ? _nativeExchangeBookInitialPrize
                    : _nativeExchangeBookRotatePrize);
            var bridge = new PasApiBridge { CurrentPlayer = this };
            _ = bridge.TryNativeExchangeBookGiveGbk(descriptor);
            if (!hasInitialPrize)
            {
                var rareBroadcast = BuildNativeExchangeBookRareBroadcast(
                    descriptor);
                if (rareBroadcast != null)
                    M2Share.GateManager?.BroadcastLegacyType18(rareBroadcast);
            }

            if (hasInitialPrize)
            {
                // Native initial-prize claim clears +0x1804 but deliberately
                // leaves the +0x1808 box StdItem index until CM_BOX2_CLOSE.
                ClearNativeExchangeBookState(clearBoxStdItemIndex: false);
            }
            else
            {
                Array.Clear(_nativeExchangeBookInitialPrize, 0,
                    _nativeExchangeBookInitialPrize.Length);
                Array.Clear(_nativeExchangeBookRotatePrize, 0,
                    _nativeExchangeBookRotatePrize.Length);

                if (_nativeExchangeBookSelectedSlot >= 0 &&
                    _nativeExchangeBookSelectedSlot <
                        ExchangeBookVisibleSlotCount &&
                    _nativeExchangeBookRound < 3)
                {
                    var replacementIndex = 9 + _nativeExchangeBookRound;
                    _nativeExchangeBookSlots[_nativeExchangeBookSelectedSlot] =
                        _nativeExchangeBookSlots[replacementIndex];
                    _nativeExchangeBookSlots[replacementIndex] = null;
                }

                _nativeExchangeBookRound++;
                if (_nativeExchangeBookRound >= 4)
                    _nativeExchangeBookRound = 0;
            }

            SendDefMessage((short)Grobal2.SM_EXCHANGEBOOK_GET_PRIZE,
                0, 0, 0, 0, string.Empty);
        }

        private LegacyGateType18 BuildNativeExchangeBookRareBroadcast(
            byte[] descriptor)
        {
            if ((_nativeExchangeBookRareState != 6 &&
                 _nativeExchangeBookRareState != 7) ||
                descriptor == null)
                return null;

            var separator = Array.IndexOf(descriptor, (byte)':');
            if (separator <= 0) return null;

            var greeting = HUtil32.GbkEncoding.GetBytes("恭喜:");
            var playerName = HUtil32.GbkEncoding.GetBytes(
                m_sCharName ?? string.Empty);
            var suffix = HUtil32.GbkEncoding.GetBytes(
                "在开启天赐时获得:");
            var nameLength = Math.Min(playerName.Length, 19 - greeting.Length);
            var prefixLength = greeting.Length + nameLength + suffix.Length;
            var body = new byte[prefixLength + separator];
            var offset = 0;
            greeting.CopyTo(body, offset);
            offset += greeting.Length;
            playerName.AsSpan(0, nameLength).CopyTo(body.AsSpan(offset));
            offset += nameLength;
            suffix.CopyTo(body, offset);
            offset += suffix.Length;
            descriptor.AsSpan(0, separator).CopyTo(body.AsSpan(offset));

            return new LegacyGateType18
            {
                IgnoredConnectionId = 0,
                FilterUserIndex = 0,
                Recog = 0,
                Ident = Grobal2.SM_SYSMESSAGE,
                Param = 0x38FF,
                Tag = 0,
                Series = 0,
                TextBytes = body
            };
        }

        internal void ClientExchangeBookClose()
        {
            ClearNativeExchangeBookState(clearBoxStdItemIndex: true);
        }

        private bool CanRunExchangeBookAction() =>
            m_ItemList.Count + ExchangeBookRequiredActionFreeSlots <=
            BagCapacity.Of(this);

        private void SendExchangeBookOpenFailure(string message =
            NativeExchangeBookUnavailableMessage)
        {
            SendDefMessage((short)Grobal2.SM_MERCHANTQUERYEXCHGBOOK,
                1, 0, 0, 0, message);
        }

        private void SendExchangeBookRotateFailure(string message =
            NativeExchangeBookUnavailableMessage)
        {
            SendDefMessage((short)Grobal2.SM_EXCHANGEBOOK_ROTATE,
                1, 0, 0, 0, message);
        }

        private void SendExchangeBookRotateSuccess()
        {
            SendDefMessage((short)Grobal2.SM_EXCHANGEBOOK_ROTATE,
                0, _nativeExchangeBookSelectedSlot, 0, 0, string.Empty);
        }

        private void ClearNativeExchangeBookState(bool clearBoxStdItemIndex)
        {
            _nativeExchangeBookPairIndex = -1;
            _nativeExchangeBookDeferredBoxClientItemId = 0;
            if (clearBoxStdItemIndex)
                _nativeExchangeBookBoxStdItemIndex = 0;
            _nativeExchangeBookRound = 0;
            _nativeExchangeBookSelectedSlot = -1;
            Array.Clear(_nativeExchangeBookInitialPrize, 0,
                _nativeExchangeBookInitialPrize.Length);
            Array.Clear(_nativeExchangeBookRotatePrize, 0,
                _nativeExchangeBookRotatePrize.Length);
            Array.Clear(_nativeExchangeBookSlots, 0,
                _nativeExchangeBookSlots.Length);
            Array.Clear(_nativeExchangeBookWireBody, 0,
                _nativeExchangeBookWireBody.Length);
        }

        private bool TryFindExchangeBookBagItem(int clientItemId,
            out TUserItem item, out int itemIndex)
        {
            item = null;
            itemIndex = -1;
            if (clientItemId == 0) return false;

            for (var index = 0; index < m_ItemList.Count; index++)
            {
                var candidate = m_ItemList[index];
                if (candidate == null || candidate.wIndex == 0) continue;
                if (candidate.ClientItemID == clientItemId)
                {
                    item = candidate;
                    itemIndex = index;
                    return true;
                }
            }
            return false;
        }

        private static bool TryResolveExchangeBookPair(TUserItem box,
            TUserItem key, out int pairIndex)
        {
            pairIndex = -1;
            for (var index = 0; index < ExchangeBookBoxNames.Length; index++)
            {
                if (ExchangeBookItemNameEquals(box,
                        ExchangeBookBoxNames[index]) &&
                    ExchangeBookItemNameEquals(key,
                        ExchangeBookKeyNames[index]))
                {
                    pairIndex = index;
                    return true;
                }
            }
            return false;
        }

        private static bool ExchangeBookItemNameEquals(TUserItem item,
            string expected)
        {
            var stdItem = item == null
                ? null
                : M2Share.UserEngine?.GetStdItem(item.wIndex);
            return stdItem != null && string.Equals(stdItem.Name, expected,
                StringComparison.OrdinalIgnoreCase);
        }

        private void ConsumeExchangeBookBagItem(int index, TUserItem item)
        {
            m_ItemList.RemoveAt(index);
            SendDelItems(item);
            Dispose(item);
            WeightChanged();
        }

        private bool TryBuildExchangeBookState(string configPath, int pairIndex,
            out ExchangeBookReward[] slots, out byte[] body,
            out int selectedSlot)
        {
            slots = null;
            body = null;
            selectedSlot = -1;

            try
            {
                var ini = new ExchangeBookIni(configPath);
                var firstPools = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                ShuffleExchangeBookPools(firstPools, 6);
                var rareKind = NextExchangeBookRareKind(pairIndex);

                slots = new ExchangeBookReward[ExchangeBookSlotCount];
                for (var index = 0; index < firstPools.Length; index++)
                {
                    var pool = firstPools[index];
                    if (pool == 8 && rareKind > 0)
                    {
                        selectedSlot = index;
                        pool = 9 + rareKind;
                    }
                    var weight = pool <= 7
                        ? ReadExchangeBookWeight(ini, pool)
                        : 0;
                    slots[index] = ReadExchangeBookPool(ini, pool, weight);
                }

                slots[8] = ReadExchangeBookPool(ini, 12, 0);
                var replacementPools = new[] { 9, 10, 11 };
                ShuffleExchangeBookPools(replacementPools, 3);
                for (var index = 0; index < replacementPools.Length; index++)
                {
                    var pool = replacementPools[index];
                    var weight = pool == 9
                        ? ReadExchangeBookWeight(ini, 8)
                        : 0;
                    slots[9 + index] = ReadExchangeBookPool(ini, pool, weight);
                }

                if (selectedSlot < 0)
                    selectedSlot = SelectWeightedExchangeBookSlot(slots);
                if (selectedSlot < 0) return false;
                body = BuildExchangeBookWireBody(slots);
                return true;
            }
            catch (Exception)
            {
                slots = null;
                body = null;
                selectedSlot = -1;
                return false;
            }
        }

        private static int ReadExchangeBookWeight(ExchangeBookIni ini,
            int probabilityIndex) => Math.Max(0, ini.ReadInteger(
                "宝箱1", "概率" + probabilityIndex, 0));

        private static ExchangeBookReward ReadExchangeBookPool(
            ExchangeBookIni ini, int pool, int weight)
        {
            var rewards = new List<(string Descriptor, int Threshold)>();
            var section = pool + "类奖励";
            for (var number = 1; number <= 100; number++)
            {
                var source = ini.ReadString(section, "奖品" + number, null);
                if (string.IsNullOrEmpty(source)) break;

                var separator = source.IndexOf('/');
                if (separator <= 0 || separator == source.Length - 1 ||
                    !int.TryParse(source.Substring(separator + 1),
                        out var threshold))
                    continue;
                rewards.Add((source.Substring(0, separator), threshold));
            }

            if (rewards.Count == 0)
                return ExchangeBookReward.Empty(pool, weight);

            var roll = NextExchangeBookRandom(1000);
            foreach (var reward in rewards)
            {
                if (roll <= reward.Threshold)
                    return CreateExchangeBookReward(reward.Descriptor, pool,
                        weight);
            }
            return ExchangeBookReward.Empty(pool, weight);
        }

        private static ExchangeBookReward CreateExchangeBookReward(
            string descriptor, int pool, int weight)
        {
            var rawDescriptor = CreateExchangeBookDescriptor(descriptor);
            var name = descriptor ?? string.Empty;
            var amount = 1;
            var separator = name.IndexOf(':');
            if (separator >= 0)
            {
                var amountText = name.Substring(separator + 1);
                name = name.Substring(0, separator);
                if (!int.TryParse(amountText, out amount) || amount <= 0)
                    amount = 1;
            }

            int looks;
            if (string.Equals(name, "金币", StringComparison.OrdinalIgnoreCase))
                looks = 115;
            else if (string.Equals(name, "声望", StringComparison.OrdinalIgnoreCase))
                looks = 1185;
            else if (string.Equals(name, "经验", StringComparison.OrdinalIgnoreCase))
                looks = 1186;
            else if (string.Equals(name, "金刚石", StringComparison.OrdinalIgnoreCase))
                looks = 1187;
            else if (string.Equals(name, "灵符", StringComparison.OrdinalIgnoreCase))
                looks = 1564;
            else
                looks = M2Share.UserEngine?.GetStdItemIdx(name) ?? 0;

            return new ExchangeBookReward(rawDescriptor, name, looks, amount,
                pool, weight);
        }

        private static byte[] CreateExchangeBookDescriptor(string descriptor)
        {
            var result = new byte[ExchangeBookDescriptorSize];
            var source = HUtil32.GbkEncoding.GetBytes(descriptor ?? string.Empty);
            var length = Math.Min(source.Length,
                ExchangeBookDescriptorPayloadSize);
            result[0] = (byte)length;
            source.AsSpan(0, length).CopyTo(result.AsSpan(1));
            return result;
        }

        private static bool HasExchangeBookDescriptor(byte[] descriptor) =>
            descriptor != null && descriptor.Length > 0 && descriptor[0] != 0;

        private static byte[] ReadExchangeBookDescriptorPayload(
            byte[] descriptor)
        {
            if (descriptor == null || descriptor.Length <= 1)
                return Array.Empty<byte>();
            var length = Math.Min(descriptor[0],
                Math.Min(ExchangeBookDescriptorPayloadSize,
                    descriptor.Length - 1));
            return descriptor.AsSpan(1, length).ToArray();
        }

        private static void CopyExchangeBookDescriptor(byte[] destination,
            byte[] source)
        {
            Array.Clear(destination, 0, destination.Length);
            if (source == null) return;
            source.AsSpan(0, Math.Min(source.Length, destination.Length))
                .CopyTo(destination);
        }

        private int SelectExchangeBookSlot(
            ExchangeBookReward[] slots)
        {
            var rareKind = NextExchangeBookRareKind(
                _nativeExchangeBookPairIndex);
            var totalWeight = 0;
            var selectedSlot = -1;
            for (var index = 0; index < ExchangeBookVisibleSlotCount; index++)
            {
                var weight = Math.Max(0, slots[index]?.Weight ?? 0);
                if (weight == ExchangeBookForcedWeight)
                {
                    selectedSlot = index;
                    _nativeExchangeBookRareState = 6;
                    break;
                }
                totalWeight += weight;
            }

            if (rareKind > 0)
            {
                var rarePool = 9 + rareKind;
                for (var index = 0; index < slots.Length; index++)
                {
                    var reward = slots[index];
                    if (reward?.Pool != rarePool) continue;

                    reward.Weight = ExchangeBookForcedWeight;
                    if (index < ExchangeBookVisibleSlotCount &&
                        selectedSlot < 0)
                        selectedSlot = index;
                    break;
                }
            }

            return selectedSlot >= 0
                ? selectedSlot
                : SelectWeightedExchangeBookSlot(slots, totalWeight);
        }

        private static int SelectWeightedExchangeBookSlot(
            ExchangeBookReward[] slots, int? knownTotalWeight = null)
        {
            var totalWeight = knownTotalWeight ?? 0;
            if (!knownTotalWeight.HasValue)
            {
                for (var index = 0;
                     index < ExchangeBookVisibleSlotCount; index++)
                    totalWeight += Math.Max(0,
                        slots[index]?.Weight ?? 0);
            }

            var roll = NextExchangeBookRandom(totalWeight);
            for (var index = 0; index < ExchangeBookVisibleSlotCount; index++)
            {
                var weight = Math.Max(0, slots[index]?.Weight ?? 0);
                roll -= weight;
                if (roll <= 0) return index;
            }
            return -1;
        }

        private int NextExchangeBookRareKind(int pairIndex)
        {
            _nativeExchangeBookRareState = 0;
            if ((uint)pairIndex >= ExchangeBookBoxNames.Length) return 0;

            var globalCount = Interlocked.Increment(
                ref ExchangeBookGlobalRareCounters[pairIndex]);
            var personalCount = unchecked(
                _nativeExchangeBookPersonalRareCounters[pairIndex] + 1);
            _nativeExchangeBookPersonalRareCounters[pairIndex] = personalCount;

            if (globalCount % ExchangeBookGlobalRareIntervals[pairIndex] == 0)
            {
                _nativeExchangeBookRareState = 7;
                return 2;
            }
            if (personalCount % ExchangeBookPersonalRareIntervals[pairIndex] == 0)
            {
                _nativeExchangeBookRareState = 6;
                return 1;
            }
            return 0;
        }

        private static void ShuffleExchangeBookPools(int[] pools, int swaps)
        {
            for (var index = 0; index < swaps; index++)
            {
                var left = NextExchangeBookRandom(pools.Length);
                var right = NextExchangeBookRandom(pools.Length);
                (pools[left], pools[right]) = (pools[right], pools[left]);
            }
        }

        private static int NextExchangeBookRandom(int range)
        {
            if (range <= 1) return 0;
            return (M2Share.RandomNumber ?? RandomNumber.GetInstance())
                .Random(range);
        }

        private static byte[] BuildExchangeBookWireBody(
            ExchangeBookReward[] slots)
        {
            var body = new byte[ExchangeBookWireBodySize];
            for (var index = 0; index < slots.Length; index++)
            {
                var reward = slots[index];
                if (reward == null || string.IsNullOrEmpty(reward.Name))
                    continue;

                var offset = index * ExchangeBookWireSlotSize;
                WriteExchangeBookName(body.AsSpan(offset, 16), reward.Name);
                BinaryPrimitives.WriteInt32LittleEndian(
                    body.AsSpan(offset + 16, 4), reward.Looks);
                BinaryPrimitives.WriteInt32LittleEndian(
                    body.AsSpan(offset + 20, 4), reward.Amount);
            }
            return body;
        }

        private static void WriteExchangeBookName(Span<byte> destination,
            string name)
        {
            destination.Clear();
            var source = HUtil32.GbkEncoding.GetBytes(name ?? string.Empty);
            var length = Math.Min(source.Length,
                Math.Min(15, destination.Length - 1));
            destination[0] = (byte)length;
            source.AsSpan(0, length).CopyTo(destination.Slice(1));
        }

        private sealed class ExchangeBookIni : IniFile
        {
            internal ExchangeBookIni(string fileName) : base(fileName)
            {
                Load();
            }
        }

        private sealed class ExchangeBookReward
        {
            internal ExchangeBookReward(byte[] descriptor, string name,
                int looks, int amount, int pool, int weight)
            {
                Descriptor = new byte[ExchangeBookDescriptorSize];
                CopyExchangeBookDescriptor(Descriptor, descriptor);
                Name = name;
                Looks = looks;
                Amount = amount;
                Pool = pool;
                Weight = weight;
            }

            internal byte[] Descriptor { get; }
            internal string Name { get; }
            internal int Looks { get; }
            internal int Amount { get; }
            internal int Pool { get; }
            internal int Weight { get; set; }

            internal static ExchangeBookReward Empty(int pool, int weight) =>
                new ExchangeBookReward(new byte[ExchangeBookDescriptorSize],
                    string.Empty, 0, 0,
                    pool, weight);
        }
    }
}

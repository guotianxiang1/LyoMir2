using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using GameSvr.PasEngine;
using SystemModule;
using SystemModule.Common;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const int NativeCattlePrizeOpenMessage = 950;
        internal const int NativeCattlePrizeRevealMessage = 952;
        internal const int NativeCattlePrizeClaimMessage = 953;
        internal const int NativeCattlePrizeWireSlotCount = 9;
        internal const int NativeCattlePrizeWireSlotSize = 24;
        internal const int NativeCattlePrizeWireBodySize =
            NativeCattlePrizeWireSlotCount * NativeCattlePrizeWireSlotSize;
        internal const int NativeCattlePrizeRequiredFreeSlots = 6;
        internal const string NativeCattleFuriousAnnouncement =
            "系统公告：被围困的富贵兽，已经狂躁不安。豪华宝物，近在咫尺。请勇士们速速集结，杀怪夺宝。";

        private static readonly object NativeCattlePrizeConfigSync = new();
        private static NativeCattlePrizeConfig s_nativeCattlePrizeConfig;
        private static bool s_nativeCattlePrizeConfigAttempted;

        internal TBodyCattleU m_NativeCattle;

        internal bool AddNativeCattle(int amount)
        {
            m_NativeCattle.Add(amount);
            return true;
        }

        internal int AddNativeCattleEvent(int threshold, int amount,
            ref bool furious)
        {
            return m_NativeCattle.AddEvent(threshold, amount, ref furious);
        }

        internal void RunNativeCattle()
        {
            m_NativeCattle.ProcessSceneType(
                m_PEnvir?.Flag?.SceneType ?? (byte)0);
        }

        internal static bool InitializeNativeCattlePrizeConfig(
            string rootDirectory, string baseDirectory)
        {
            return InitializeNativeCattlePrizeConfigFromPath(Path.Combine(
                rootDirectory ?? string.Empty,
                string.IsNullOrWhiteSpace(baseDirectory) ? "Share" :
                    baseDirectory,
                "Config", "CattlePrize.ini"));
        }

        internal static bool InitializeNativeCattlePrizeConfigFromPath(
            string configPath)
        {
            if (M2Share.RandomNumber != null)
                TBodyCattleU.InitializeEventKillCounter(
                    M2Share.RandomNumber.Random);

            var loaded = NativeCattlePrizeConfig.TryLoad(configPath,
                out var config);
            lock (NativeCattlePrizeConfigSync)
            {
                s_nativeCattlePrizeConfig = loaded ? config : null;
                s_nativeCattlePrizeConfigAttempted = true;
            }
            return loaded;
        }

        internal static NativeCattlePrizeConfig CaptureNativeCattlePrizeConfig()
        {
            lock (NativeCattlePrizeConfigSync)
            {
                if (s_nativeCattlePrizeConfigAttempted)
                    return s_nativeCattlePrizeConfig;
            }

            InitializeNativeCattlePrizeConfigFromPath(
                GetNativeCattlePrizeConfigPath());
            lock (NativeCattlePrizeConfigSync)
                return s_nativeCattlePrizeConfig;
        }

        private static string GetNativeCattlePrizeConfigPath()
        {
            var baseDirectory = M2Share.g_Config?.sBaseDir;
            return Path.Combine(M2Share.sRootPath ?? string.Empty,
                string.IsNullOrWhiteSpace(baseDirectory) ? "Share" :
                    baseDirectory,
                "Config", "CattlePrize.ini");
        }

        internal bool ClientNativeCattleRevealPrize()
        {
            return m_NativeCattle?.ClientRevealPrize() ?? false;
        }

        internal void ClientNativeCattleClaimPrize()
        {
            m_NativeCattle?.ClientClaimPrize();
        }

        internal void ClientNativeNeedKeyBoxClaimPrize()
        {
            var result = TryClaimNativeNeedKeyBox(descriptor =>
            {
                var bridge = new PasApiBridge { CurrentPlayer = this };
                _ = bridge.TryNativeExchangeBookGiveGbk(descriptor);
                return true;
            });
            SendDefMessage((short)Grobal2.SM_CATTLE_PRIZE_CLAIM,
                (int)result, 1, 0, 0, string.Empty);
        }

        internal void SendNativeCattlePrizePacket(byte[] body)
        {
            m_DefMsg = Grobal2.MakeDefaultMsg(NativeCattlePrizeOpenMessage,
                0, 0, 0, 0);
            SendSocket(m_DefMsg, body);
        }

        internal void GiveNativeCattlePrize(byte[] descriptor)
        {
            if (descriptor == null || descriptor.Length == 0) return;

            GiveNativePrizeDescriptor(
                HUtil32.GbkEncoding.GetString(descriptor));
        }

        internal void GiveNativeCattleActivityPrize(string descriptor)
        {
            GiveNativePrizeDescriptor(descriptor);
        }

        internal void GrantNativeCattleGlobalPrize(string prefix, int amount)
        {
            GrantNativeCattleGlobalPrize(
                HUtil32.GbkEncoding.GetBytes(prefix ?? string.Empty), amount);
        }

        internal void GrantNativeCattleGlobalPrize(byte[] prefix, int amount)
        {
            if (amount <= 0) return;

            M2Share.GateManager?.BroadcastLegacyType18(
                BuildNativeCattleGlobalPrizeBroadcast(prefix, amount));
            AddNativeLingFuReasonUsage(9, amount);
            lock (m_CreditCard.SyncRoot)
            {
                var value = unchecked(m_CreditCard.Value + amount);
                m_CreditCard.Value = value < 0 ? 0 : value;
                m_CreditCard.Dirty = true;
                m_CreditCard.DirtyVersion++;
            }
            RefreshNativeLingFu();

            M2Share.AddGameDataLog(string.Join('\t', 9, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, "灵符2", 222222, amount,
                "牛气服务器大奖"));
        }

        internal static void BroadcastNativeCattleMessage(string text)
        {
            M2Share.GateManager?.BroadcastLegacyType18(
                BuildNativeCattleBroadcast(
                    HUtil32.GbkEncoding.GetBytes(text ?? string.Empty)));
        }

        internal static LegacyGateType18 BuildNativeCattleGlobalPrizeBroadcast(
            byte[] prefix, int amount)
        {
            var suffix = HUtil32.GbkEncoding.GetBytes(amount + "张灵符");
            return BuildNativeCattleBroadcast(ConcatRaw(prefix, suffix));
        }

        internal static LegacyGateType18 BuildNativeCattleBroadcast(
            byte[] textBytes)
        {
            return new LegacyGateType18
            {
                FilterUserIndex = 0,
                Recog = 0,
                Ident = Grobal2.SM_SYSMESSAGE,
                Param = 0x38FF,
                Tag = 0,
                Series = 0,
                TextBytes = textBytes == null
                    ? Array.Empty<byte>()
                    : (byte[])textBytes.Clone()
            };
        }

        internal static byte[] ConcatRaw(byte[] first, byte[] second)
        {
            first ??= Array.Empty<byte>();
            second ??= Array.Empty<byte>();
            var result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private void GiveNativePrizeDescriptor(string descriptor)
        {
            if (string.IsNullOrEmpty(descriptor)) return;

            // sub_6CF76C invokes the same native Give executor and ignores its
            // Boolean result. CallPlayerFunc exposes that executor directly;
            // the result deliberately has no effect on the pending state.
            var bridge = new PasApiBridge { CurrentPlayer = this };
            _ = bridge.CallPlayerFunc("give", new List<PasValue>
            {
                PasValue.FromString(descriptor),
                PasValue.FromInt(1)
            }, out _);
        }
    }

    internal sealed class TBodyCattleU
    {
        internal static readonly int[] Thresholds =
            { 5000, 15000, 30000, 50000 };

        private const string TierRaisedMessage =
            "恭喜：你的牛气天赐的奖励又提升了一个档次";
        private const string NearFullMessage =
            "恭喜：你的牛气池即将填满，为了避免不必要的损失，请预留6格包裹空位";
        private const int NativeCattleShortNameCapacity = 15;
        private const int NativeCattleDescriptorCapacity = 20;
        private const int NativeCattleEventKillRange = 1750;
        private const string NativeCattleCalmKillText =
            "在富贵兽平静的时候把富贵兽消灭了，获得了";
        private const string NativeCattleEnragedText =
            "激怒了富贵兽。获得了";
        private const string NativeCattleFuriousKillText =
            "在富贵兽狂暴的时候把富贵兽消灭了，获得了";
        private const string NativeCattleBountyPrefix =
            "悬赏捕杀富贵兽，目前赏金额度已经提高到";
        private const string NativeCattleBountySuffix =
            "张灵符，请勇士们速速前往猎杀";

        private static readonly object NativeCattleEventStateSync = new();
        private static int s_nativeCattleEventGlobalPool;
        private static int s_nativeCattleEventKillMaximum =
            NativeCattleEventKillRange;
        private static int s_nativeCattleEventKillCurrent;
        private static int s_nativeCattleEventKillTarget = 1;

        // These are the native semantic offsets, not a claimed CLR layout.
        private readonly TPlayObject _owner; // +04
        internal int Progress;               // +08
        internal int Value;                  // +0C
        internal byte Tier;                  // +10
        internal bool NearFullNotified;      // +11
        internal bool BarVisible;            // +12

        // The original player record stores these at +0D48/+0D5D/+0D72/+0D94.
        // Keep descriptor bytes so the fixed GBK ShortString[20] boundary is
        // preserved until the shared Give executor is called.
        private byte[] _revealPending = Array.Empty<byte>();
        private byte[] _claimPending = Array.Empty<byte>();
        internal byte SelectedPrizeSlot;     // +0D72, one-based
        internal byte PrizeMode              // shared player +0D94
        {
            get => _owner.NativeCattleNeedKeyBoxMode;
            set => _owner.NativeCattleNeedKeyBoxMode = value;
        }

        internal bool HasRevealPending => _revealPending.Length != 0;
        internal bool HasClaimPending => _claimPending.Length != 0;
        internal string RevealPendingDescriptor => DecodeDescriptor(
            _revealPending);
        internal string ClaimPendingDescriptor => DecodeDescriptor(
            _claimPending);

        internal TBodyCattleU(TPlayObject owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal static int EventGlobalPool
        {
            get
            {
                lock (NativeCattleEventStateSync)
                    return s_nativeCattleEventGlobalPool;
            }
        }

        internal static int EventKillCounterCurrent
        {
            get
            {
                lock (NativeCattleEventStateSync)
                    return s_nativeCattleEventKillCurrent;
            }
        }

        internal static int EventKillCounterTarget
        {
            get
            {
                lock (NativeCattleEventStateSync)
                    return s_nativeCattleEventKillTarget;
            }
        }

        internal static void InitializeEventKillCounter(Func<int, int> random)
        {
            if (random == null) return;
            lock (NativeCattleEventStateSync)
                ResetEventKillCounterUnsafe(random);
        }

        internal static void ResetEventStateForCheck(int globalPool,
            int killCurrent, int killTarget)
        {
            lock (NativeCattleEventStateSync)
            {
                s_nativeCattleEventGlobalPool = globalPool;
                s_nativeCattleEventKillMaximum = NativeCattleEventKillRange;
                s_nativeCattleEventKillCurrent = killCurrent;
                s_nativeCattleEventKillTarget = killTarget;
            }
        }

        internal void Add(int amount)
        {
            Add(amount, M2Share.RandomNumber.Random);
        }

        internal void Add(int amount, Func<int, int> random)
        {
            Value = unchecked(Value + amount);
            _owner.SendMsg(_owner, Grobal2.RM_CATTLE_SYSMESSAGE, 0xFB,
                0, 0, 0, amount + " 点牛气值增加");
            UpdateTier(random);
            SendBarChange();
        }

        internal int AddEvent(int threshold, int requestedAmount,
            ref bool furious)
        {
            return AddEvent(threshold, requestedAmount, ref furious,
                M2Share.RandomNumber.Random,
                _owner.GiveNativeCattleActivityPrize,
                _owner.GrantNativeCattleGlobalPrize,
                TPlayObject.BroadcastNativeCattleMessage);
        }

        internal int AddEvent(int threshold, int requestedAmount,
            ref bool furious, Func<int, int> random,
            Action<string> giveActivityReward,
            Action<byte[], int> grantGlobalPrize,
            Action<string> broadcast)
        {
            ArgumentNullException.ThrowIfNull(random);
            ArgumentNullException.ThrowIfNull(giveActivityReward);
            ArgumentNullException.ThrowIfNull(grantGlobalPrize);
            ArgumentNullException.ThrowIfNull(broadcast);

            var amount = requestedAmount > 1 ? 10 : requestedAmount;
            var result = amount;
            var multiplier = furious ? 5 : 100;

            Progress = unchecked(Progress + amount);
            var valueIncrease = unchecked(amount * multiplier);
            _owner.SendMsg(_owner, Grobal2.RM_CATTLE_SYSMESSAGE, 0xFB,
                0, 0, 0, valueIncrease + " 点牛气值增加");
            Value = unchecked(Value + valueIncrease);

            var config = TPlayObject.CaptureNativeCattlePrizeConfig();
            if (config != null && config.TrySelectActivity(amount, furious,
                    random, out var activityReward))
                giveActivityReward(activityReward.Descriptor);

            UpdateTier(random);

            lock (NativeCattleEventStateSync)
            {
                s_nativeCattleEventGlobalPool = unchecked(
                    s_nativeCattleEventGlobalPool + amount);
                if (s_nativeCattleEventGlobalPool >= threshold)
                {
                    if (furious)
                    {
                        if (AdvanceEventKillCounterUnsafe(amount, random))
                        {
                            ResetEventKillCounterUnsafe(random);
                            var awardPrefix = BuildEventAwardPrefix(
                                NativeCattleFuriousKillText);
                            var awardAmount = unchecked(
                                s_nativeCattleEventGlobalPool * 2) / 5;
                            result = 10000;
                            if (awardAmount > 0)
                                grantGlobalPrize(awardPrefix, awardAmount);
                            s_nativeCattleEventGlobalPool = 0;
                        }
                        else if (random(20) == 0)
                        {
                            broadcast(NativeCattleBountyPrefix +
                                unchecked(s_nativeCattleEventGlobalPool * 2) /
                                5 + NativeCattleBountySuffix);
                        }
                    }
                    else
                    {
                        if (random(10000) < unchecked(5 * amount))
                        {
                            var awardPrefix = BuildEventAwardPrefix(
                                NativeCattleEnragedText);
                            var awardAmount =
                                s_nativeCattleEventGlobalPool / 5;
                            if (awardAmount > 0)
                                grantGlobalPrize(awardPrefix, awardAmount);
                        }
                        furious = true;
                    }
                }
                else if (amount > random(5000))
                {
                    var awardPrefix = BuildEventAwardPrefix(
                        NativeCattleCalmKillText);
                    var awardAmount = s_nativeCattleEventGlobalPool / 5;
                    result = 10000;
                    if (awardAmount > 0)
                        grantGlobalPrize(awardPrefix, awardAmount);
                    s_nativeCattleEventGlobalPool = 0;
                }
            }

            SendBarChange();
            return result;
        }

        private byte[] BuildEventAwardPrefix(string suffix)
        {
            var name = CapRaw(HUtil32.GbkEncoding.GetBytes(
                _owner.m_sCharName ?? string.Empty), 0x0E);
            var playerPrefix = AppendRawCapped(
                HUtil32.GbkEncoding.GetBytes("恭喜"), name, 0x12);
            var maximumBytes = suffix == NativeCattleEnragedText
                ? 0x26
                : 0x3A;
            return AppendRawCapped(playerPrefix,
                HUtil32.GbkEncoding.GetBytes(suffix ?? string.Empty),
                maximumBytes);
        }

        private static byte[] CapRaw(byte[] source, int maximumBytes)
        {
            source ??= Array.Empty<byte>();
            var length = Math.Min(source.Length, maximumBytes);
            return source.AsSpan(0, length).ToArray();
        }

        private static byte[] AppendRawCapped(byte[] first, byte[] second,
            int maximumBytes)
        {
            var combined = TPlayObject.ConcatRaw(first, second);
            return CapRaw(combined, maximumBytes);
        }

        private static bool AdvanceEventKillCounterUnsafe(int amount,
            Func<int, int> random)
        {
            var wasAboveTarget = s_nativeCattleEventKillCurrent >
                                 s_nativeCattleEventKillTarget;
            s_nativeCattleEventKillCurrent = unchecked(
                s_nativeCattleEventKillCurrent + amount);
            var crossed = !wasAboveTarget &&
                          s_nativeCattleEventKillCurrent >=
                          s_nativeCattleEventKillTarget;
            if (s_nativeCattleEventKillCurrent >=
                s_nativeCattleEventKillMaximum)
                ResetEventKillCounterUnsafe(random);
            return crossed;
        }

        private static void ResetEventKillCounterUnsafe(
            Func<int, int> random)
        {
            s_nativeCattleEventKillMaximum = NativeCattleEventKillRange;
            s_nativeCattleEventKillCurrent = 0;
            s_nativeCattleEventKillTarget = unchecked(
                random(NativeCattleEventKillRange) + 1);
        }

        internal void ProcessSceneType(byte sceneType)
        {
            if (BarVisible)
            {
                if (sceneType == 3) return;

                BarVisible = false;
                _owner.SendDefMessage((short)Grobal2.SM_CATTLE_BAR_HIDE,
                    0, 0, 0, 0, string.Empty);
                return;
            }

            if (sceneType != 3) return;

            BarVisible = true;
            _owner.SendDefMessage((short)Grobal2.SM_CATTLE_BAR_SHOW,
                0, 0, 0, 0, string.Empty);
            Value = 0;
            Tier = 1;
        }

        internal bool ClientRevealPrize()
        {
            if (!HasRevealPending) return false;

            _owner.SendDefMessage(
                (short)TPlayObject.NativeCattlePrizeRevealMessage,
                SelectedPrizeSlot, 0, 0, 0, string.Empty);
            _revealPending = Array.Empty<byte>();
            return true;
        }

        internal void ClientClaimPrize()
        {
            var result = 0;
            if (_owner.m_ItemList.Count +
                TPlayObject.NativeCattlePrizeRequiredFreeSlots <=
                BagCapacity.Of(_owner))
            {
                if (HasRevealPending)
                {
                    _owner.GiveNativeCattlePrize(_revealPending);
                    _revealPending = Array.Empty<byte>();
                    _claimPending = Array.Empty<byte>();
                    result = 1;
                }
                else if (HasClaimPending)
                {
                    _owner.GiveNativeCattlePrize(_claimPending);
                    _claimPending = Array.Empty<byte>();
                    result = 1;
                }
                else
                {
                    result = 2;
                }
            }

            _owner.SendDefMessage(
                (short)TPlayObject.NativeCattlePrizeClaimMessage,
                result, 0, 0, 0, string.Empty);
        }

        // This is the native sub_716174 state construction. It is internal so
        // the compatibility check can inspect the exact 216-byte body without
        // observing a live gate buffer.
        internal bool TryCreatePrizeState(int prizeTier, Func<int, int> random,
            out byte[] body)
        {
            body = null;
            if (random == null || prizeTier is < 1 or > 4) return false;

            var config = TPlayObject.CaptureNativeCattlePrizeConfig();
            if (config == null || !config.TrySelectPersonal(prizeTier, random,
                    out var selected))
                return false;

            if (!TryResolveReward(config, selected, random, out var actual))
                return false;
            actual = actual.WithActual(true);

            var candidates = new List<NativeCattleReward>(8) { actual };
            var excludedBoxIndex = random(8) + 1;
            var boxRewards = config.GetBoxRewards(prizeTier);
            for (var index = 0; index < boxRewards.Length; index++)
            {
                if (index + 1 == excludedBoxIndex) continue;
                if (!TryResolveReward(config, boxRewards[index], random,
                        out var decoy))
                    return false;
                candidates.Add(decoy);
            }

            if (candidates.Count != 8) return false;

            body = new byte[TPlayObject.NativeCattlePrizeWireBodySize];
            var selectedSlot = (byte)0;
            for (var slot = 0; slot < 8; slot++)
            {
                var candidateIndex = random(candidates.Count);
                if (candidateIndex < 0 || candidateIndex >= candidates.Count)
                {
                    body = null;
                    return false;
                }

                var candidate = candidates[candidateIndex];
                candidates.RemoveAt(candidateIndex);
                WritePrizeRecord(body.AsSpan(slot *
                    TPlayObject.NativeCattlePrizeWireSlotSize,
                    TPlayObject.NativeCattlePrizeWireSlotSize), candidate);
                if (candidate.IsActual)
                    selectedSlot = unchecked((byte)(slot + 1));
            }

            if (selectedSlot == 0)
            {
                body = null;
                return false;
            }

            PrizeMode = 3;
            SelectedPrizeSlot = selectedSlot;
            _revealPending = EncodeDescriptor(actual.Name, actual.Amount);
            _claimPending = (byte[])_revealPending.Clone();
            return true;
        }

        private void UpdateTier(Func<int, int> random)
        {
            var newTier = CalculateTier(Value);
            if (newTier > Tier)
            {
                NearFullNotified = false;
                SendTierMessage(TierRaisedMessage);

                // The native transition calls sub_716174(newTier - 1) before
                // the overfill draw. Tier zero is intentionally a no-op.
                if (TryCreatePrizeState(newTier - 1, random, out var body))
                    _owner.SendNativeCattlePrizePacket(body);

                if (newTier > 4)
                {
                    var roll = random(100);
                    Value = unchecked(Value - (roll < 10
                        ? 20000
                        : roll < 30 ? 35000 : 45000));
                    newTier = CalculateTier(Value);
                    if (newTier > 4) newTier--;
                }

                Tier = (byte)newTier;
                return;
            }

            if (NearFullNotified || newTier > Thresholds.Length) return;
            if (unchecked(Thresholds[newTier - 1] - Value) >= 500) return;

            NearFullNotified = true;
            SendTierMessage(NearFullMessage);
        }

        private static bool TryResolveReward(NativeCattlePrizeConfig config,
            NativeCattleReward source, Func<int, int> random,
            out NativeCattleReward resolved)
        {
            resolved = default;
            var looks = source.Looks;
            if (looks == -2 &&
                !NativeCattlePrizeConfig.TryResolveDisplayLooks(source.Name,
                    out looks))
                return false;

            if (looks == -1)
            {
                // 金牛装备 is only a selector. The second draw resolves an
                // actual standard item; it is never sent to the client itself.
                if (!config.TrySelectGoldEquipment(random, out var equipment))
                    return false;

                var stdItem = M2Share.UserEngine?.GetStdItem(equipment.Name);
                resolved = new NativeCattleReward(equipment.Name,
                    equipment.Amount, equipment.Threshold,
                    stdItem?.Looks ?? looks,
                    source.IsActual);
                return true;
            }

            resolved = new NativeCattleReward(source.Name, source.Amount,
                source.Threshold, looks, source.IsActual);
            return true;
        }

        private static void WritePrizeRecord(Span<byte> destination,
            NativeCattleReward reward)
        {
            destination.Clear();
            WriteShortName(destination.Slice(0, 16), reward.Name);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16, 4),
                reward.Looks);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(20, 4),
                reward.Amount);
        }

        private static void WriteShortName(Span<byte> destination, string name)
        {
            destination.Clear();
            var source = HUtil32.GbkEncoding.GetBytes(name ?? string.Empty);
            var length = Math.Min(NativeCattleShortNameCapacity, source.Length);
            destination[0] = unchecked((byte)length);
            source.AsSpan(0, length).CopyTo(destination.Slice(1));
        }

        private static byte[] EncodeDescriptor(string name, int amount)
        {
            var source = HUtil32.GbkEncoding.GetBytes((name ?? string.Empty) +
                ':' + amount);
            var length = Math.Min(NativeCattleDescriptorCapacity, source.Length);
            return source.AsSpan(0, length).ToArray();
        }

        private static string DecodeDescriptor(byte[] descriptor) =>
            descriptor == null || descriptor.Length == 0
                ? string.Empty
                : HUtil32.GbkEncoding.GetString(descriptor);

        private void SendTierMessage(string message)
        {
            _owner.SendMsg(_owner, Grobal2.RM_SYSMESSAGE, 0,
                0xFF, 0xFC, 0, message);
        }

        private void SendBarChange()
        {
            var normalizedTier = Math.Clamp((int)Tier, 1, 4);
            _owner.SendDefMessage((short)Grobal2.SM_CATTLE_BAR_CHANGE,
                normalizedTier, Tier, Thresholds[normalizedTier - 1], Value,
                string.Empty);
        }

        private static int CalculateTier(int value)
        {
            var tier = 1;
            while (tier < 5 && value >= Thresholds[tier - 1]) tier++;
            return tier;
        }
    }

    internal sealed class NativeCattlePrizeConfig
    {
        private const int RandomRange = 10000;
        private readonly NativeCattleReward[][] _personalRewards;
        private readonly NativeCattleReward[][] _activityRewards;
        private readonly NativeCattleReward[][] _boxRewards;
        private readonly NativeCattleReward[] _goldEquipment;

        private NativeCattlePrizeConfig(NativeCattleReward[][] activityRewards,
            NativeCattleReward[][] personalRewards,
            NativeCattleReward[][] boxRewards,
            NativeCattleReward[] goldEquipment)
        {
            _activityRewards = activityRewards;
            _personalRewards = personalRewards;
            _boxRewards = boxRewards;
            _goldEquipment = goldEquipment;
        }

        internal NativeCattleReward[] GetBoxRewards(int prizeTier) =>
            _boxRewards[prizeTier - 1];

        internal bool TrySelectPersonal(int prizeTier, Func<int, int> random,
            out NativeCattleReward reward) => TrySelect(
            _personalRewards[prizeTier - 1], random, out reward);

        internal bool TrySelectActivity(int amount, bool furious,
            Func<int, int> random, out NativeCattleReward reward)
        {
            var poolIndex = amount == 1
                ? furious ? 1 : 0
                : furious ? 3 : 2;
            return TrySelect(_activityRewards[poolIndex], random, out reward);
        }

        internal bool TrySelectGoldEquipment(Func<int, int> random,
            out NativeCattleReward reward) => TrySelect(_goldEquipment, random,
            out reward);

        internal static bool TryLoad(string configPath,
            out NativeCattlePrizeConfig config)
        {
            config = null;
            if (string.IsNullOrWhiteSpace(configPath) ||
                !File.Exists(configPath))
                return false;

            try
            {
                var ini = new CattlePrizeIni(configPath);
                var activityRewards = new NativeCattleReward[4][];
                var personalRewards = new NativeCattleReward[4][];
                var boxRewards = new NativeCattleReward[4][];
                for (var tier = 1; tier <= 4; tier++)
                {
                    if (!TryReadCumulativePool(ini, "配置" + tier,
                            out activityRewards[tier - 1]) ||
                        !TryReadCumulativePool(ini, "个人奖" + tier,
                            out personalRewards[tier - 1]) ||
                        !TryReadBoxRewards(ini, "宝箱" + tier,
                            out boxRewards[tier - 1]))
                        return false;
                }

                if (!TryReadCumulativePool(ini, "金牛装备",
                        out var goldEquipment))
                    return false;

                config = new NativeCattlePrizeConfig(activityRewards,
                    personalRewards,
                    boxRewards, goldEquipment);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryResolveDisplayLooks(string name, out int looks)
        {
            switch (name)
            {
                case "经验":
                    looks = 1186;
                    return true;
                case "金牛装备":
                    looks = -1;
                    return true;
                case "牛气值":
                    looks = 1679;
                    return true;
                case "金刚石":
                    looks = 1187;
                    return true;
                case "灵符":
                    looks = 1564;
                    return true;
                case "声望":
                    looks = 1185;
                    return true;
                case "金币":
                    looks = 115;
                    return true;
                case "内功经验":
                    looks = 2171;
                    return true;
                case "英雄内功经验":
                    looks = 2172;
                    return true;
                case "副将内功经验":
                    looks = 2173;
                    return true;
                default:
                    var stdItem = M2Share.UserEngine?.GetStdItem(name);
                    looks = stdItem?.Looks ?? -2;
                    return looks != -2;
            }
        }

        private static bool TryReadCumulativePool(CattlePrizeIni ini,
            string section, out NativeCattleReward[] rewards)
        {
            var result = new List<NativeCattleReward>();
            var previousThreshold = 0;
            for (var index = 1; index < 100; index++)
            {
                var source = ini.ReadString(section, "奖品" + index,
                    string.Empty);
                if (string.IsNullOrEmpty(source)) break;
                if (!TryParseReward(source, out var reward) ||
                    reward.Threshold <= previousThreshold ||
                    reward.Threshold > RandomRange)
                {
                    rewards = null;
                    return false;
                }

                previousThreshold = reward.Threshold;
                result.Add(reward);
            }

            rewards = result.ToArray();
            return rewards.Length != 0;
        }

        private static bool TryReadBoxRewards(CattlePrizeIni ini,
            string section, out NativeCattleReward[] rewards)
        {
            var result = new NativeCattleReward[8];
            for (var index = 0; index < result.Length; index++)
            {
                var source = ini.ReadString(section, "奖品" + (index + 1),
                    string.Empty);
                if (string.IsNullOrEmpty(source) ||
                    !TryParseBoxReward(source, out var reward) ||
                    !TryResolveDisplayLooks(reward.Name, out var looks))
                {
                    rewards = null;
                    return false;
                }

                result[index] = new NativeCattleReward(reward.Name,
                    reward.Amount, reward.Threshold, looks, false);
            }

            rewards = result;
            return true;
        }

        private static bool TryParseReward(string source,
            out NativeCattleReward reward)
        {
            reward = default;
            var colon = source.IndexOf(':');
            var slash = colon < 0 ? -1 : source.IndexOf('/', colon + 1);
            if (colon <= 0 || slash <= colon + 1 ||
                slash >= source.Length - 1)
                return false;

            if (!int.TryParse(source.AsSpan(colon + 1,
                    slash - colon - 1), out var amount) ||
                !int.TryParse(source.AsSpan(slash + 1), out var threshold))
                return false;

            reward = new NativeCattleReward(source[..colon], amount,
                threshold, -2, false);
            return true;
        }

        private static bool TryParseBoxReward(string source,
            out NativeCattleReward reward)
        {
            reward = default;
            var colon = source.IndexOf(':');
            var slash = colon < 0 ? -1 : source.IndexOf('/', colon + 1);
            if (colon <= 0 || slash <= colon + 1 ||
                !int.TryParse(source.AsSpan(colon + 1,
                    slash - colon - 1), out var amount))
                return false;

            reward = new NativeCattleReward(source[..colon], amount,
                0, -2, false);
            return true;
        }

        private static bool TrySelect(NativeCattleReward[] pool,
            Func<int, int> random, out NativeCattleReward reward)
        {
            reward = default;
            if (pool == null || pool.Length == 0 || random == null)
                return false;

            var roll = random(RandomRange);
            for (var index = 0; index < pool.Length; index++)
            {
                if (roll > pool[index].Threshold) continue;
                reward = pool[index];
                return true;
            }
            return false;
        }

        private sealed class CattlePrizeIni : IniFile
        {
            internal CattlePrizeIni(string fileName) : base(fileName)
            {
                Load();
            }
        }
    }

    internal readonly struct NativeCattleReward
    {
        internal NativeCattleReward(string name, int amount, int threshold,
            int looks, bool isActual)
        {
            Name = name;
            Amount = amount;
            Threshold = threshold;
            Looks = looks;
            IsActual = isActual;
        }

        internal string Name { get; }
        internal int Amount { get; }
        internal int Threshold { get; }
        internal int Looks { get; }
        internal bool IsActual { get; }
        internal string Descriptor => Name + ':' + Amount;

        internal NativeCattleReward WithActual(bool isActual) =>
            new NativeCattleReward(Name, Amount, Threshold, Looks, isActual);
    }
}

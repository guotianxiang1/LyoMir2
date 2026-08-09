using System.IO;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private const int TimedAbilityMessage = 3555;
        private const uint TimedAbilityProcessInterval = 500;
        private const byte TimedAbilityValueGateState = 16;
        private const byte TimedAbilityGlobalBlockState = 52;

        private sealed class TimedAbilityNode
        {
            public byte Flag;
            public byte InternalType;
            public int RemainingMilliseconds;
            public int LastTick;
            public int Value;
            public TimedAbilityNode Next;
        }

        private TimedAbilityNode m_TimedAbilityHead;
        private int m_TimedAbilityProcessTick;

        public void AddTimedAbility(int scriptType, int value, int seconds)
        {
            if (!IsSupportedTimedAbilityType(scriptType))
            {
                return;
            }

            var internalType = (byte)(scriptType + 32);
            var duration = seconds == -1 ? -1 : unchecked(seconds * 1000);
            AddTimedAbilityInternal(internalType, value, duration, 0);
        }

        private bool AddTimedAbilityInternal(byte internalType, int value,
            int duration, byte newNodeFlag)
        {
            if (!CanAddNativeTimedAbility(internalType))
            {
                return false;
            }

            if (internalType == 18 && HasNativeActiveState(NativeState26Type))
            {
                RemoveTimedAbilityInternal(NativeState26Type);
            }

            var node = FindTimedAbilityInternal(internalType);
            var abilityChanged = false;

            if (node == null)
            {
                node = new TimedAbilityNode
                {
                    Flag = newNodeFlag,
                    InternalType = internalType,
                    RemainingMilliseconds = duration,
                    Value = value,
                    Next = m_TimedAbilityHead
                };
                m_TimedAbilityHead = node;
                abilityChanged = true;
            }
            else
            {
                node.Flag = newNodeFlag;
                if (value > node.Value)
                {
                    node.Value = value;
                    node.RemainingMilliseconds = duration;
                    abilityChanged = true;
                }
                else if (value == node.Value && duration > node.RemainingMilliseconds)
                {
                    node.RemainingMilliseconds = duration;
                }
            }

            if (abilityChanged)
            {
                if (node.InternalType == 45 && this is TPlayObject player)
                {
                    player.CancelNativeType51PendingForTimedAbility();
                }
                SetNativeActiveState(node.InternalType);
                ApplyNativeTimedAbilityMutation(node.InternalType);
                if (node.InternalType == 20 && node.Value > 3)
                {
                    AddTimedAbilityInternal(19, 0, -1, 1);
                }
            }

            if (abilityChanged && RequiresTimedAbilityRecalc(node.InternalType))
            {
                MarkAbilityRecalcPending();
            }
            SendTimedAbilityState(node, false);
            node.LastTick = HUtil32.GetTickCount();
            return true;
        }

        internal bool AddNativeBubbleTimedAbility(byte level, ushort seconds)
        {
            if (HasNativeActiveState(20))
            {
                return false;
            }

            AddTimedAbilityInternal(20, level,
                unchecked(seconds * 1000), 0);
            return true;
        }

        public void ProcessTimedAbilities()
        {
            ProcessTimedAbilities(HUtil32.GetTickCount());
        }

        public void ProcessTimedAbilities(int now)
        {
            ProcessNativeSkill152Status(now);
            if (unchecked((uint)(now - m_TimedAbilityProcessTick)) <
                TimedAbilityProcessInterval)
            {
                return;
            }
            m_TimedAbilityProcessTick = now;

            TimedAbilityNode previous = null;
            var node = m_TimedAbilityHead;
            TimedAbilityNode expiredHead = null;

            while (node != null)
            {
                var next = node.Next;
                var expired = false;
                if (node.RemainingMilliseconds != -1)
                {
                    node.RemainingMilliseconds = unchecked(
                        node.RemainingMilliseconds - (now - node.LastTick));
                    node.LastTick = now;
                    expired = node.RemainingMilliseconds <= 0;
                }

                if (expired)
                {
                    ClearNativeActiveState(node.InternalType);
                    if (previous == null)
                    {
                        m_TimedAbilityHead = next;
                    }
                    else
                    {
                        previous.Next = next;
                    }
                    node.Next = expiredHead;
                    expiredHead = node;
                }
                else
                {
                    previous = node;
                }
                node = next;
            }

            // Native first detaches the whole expired batch, then invokes callbacks
            // through the reversed temporary list (oldest state first).
            node = expiredHead;
            while (node != null)
            {
                var next = node.Next;
                SendTimedAbilityState(node, true);
                RemoveTimedAbilityCompanion(node.InternalType);
                if (RequiresTimedAbilityRecalc(node.InternalType))
                {
                    MarkAbilityRecalcPending();
                }
                node = next;
            }
        }

        public bool RemoveTimedAbility(int scriptType)
        {
            if (!IsSupportedTimedAbilityType(scriptType))
            {
                return false;
            }

            var internalType = (byte)(scriptType + 32);
            return RemoveTimedAbilityInternal(internalType);
        }

        private bool RemoveTimedAbilityInternal(byte internalType)
        {
            if (!HasNativeActiveState(internalType))
            {
                return false;
            }

            ClearNativeActiveState(internalType);
            TimedAbilityNode previous = null;
            var node = m_TimedAbilityHead;
            while (node != null)
            {
                if (node.InternalType == internalType)
                {
                    if (previous == null)
                    {
                        m_TimedAbilityHead = node.Next;
                    }
                    else
                    {
                        previous.Next = node.Next;
                    }

                    SendTimedAbilityState(node, true);
                    RemoveTimedAbilityCompanion(node.InternalType);
                    if (RequiresTimedAbilityRecalc(node.InternalType))
                    {
                        MarkAbilityRecalcPending();
                    }
                    return true;
                }

                previous = node;
                node = node.Next;
            }
            return false;
        }

        private void RemoveTimedAbilityCompanion(byte internalType)
        {
            if (internalType == 20)
            {
                RemoveTimedAbilityInternal(19);
            }
        }

        public bool HasTimedAbility(int scriptType)
        {
            return IsSupportedTimedAbilityType(scriptType) &&
                   FindTimedAbilityInternal((byte)(scriptType + 32)) != null;
        }

        public int GetTimedAbilityValue(int scriptType)
        {
            if (!IsSupportedTimedAbilityType(scriptType))
            {
                return 0;
            }
            return GetNativeTimedAbilityValue((byte)(scriptType + 32));
        }

        public int GetTimedAbilityRemainingMilliseconds(int scriptType)
        {
            if (!IsSupportedTimedAbilityType(scriptType))
            {
                return 0;
            }
            return FindTimedAbilityInternal((byte)(scriptType + 32))?.RemainingMilliseconds ?? 0;
        }

        public int GetTimedHolyDefense(int baseHolyDefense)
        {
            var result = baseHolyDefense;
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                switch (node.InternalType)
                {
                    case 96:
                        result = unchecked(result + node.Value);
                        break;
                    case 100:
                        var percent = unchecked((int)(long)Math.Round(
                            unchecked((uint)result) * (double)node.Value / 100.0,
                            MidpointRounding.ToEven));
                        result = unchecked(result + percent);
                        break;
                }
            }
            return result;
        }

        internal static bool IsNativeTimedAbilityType(int scriptType)
        {
            return scriptType >= 0 && scriptType <= 28 ||
                   scriptType >= 43 && scriptType <= 46 ||
                   scriptType >= 58 && scriptType <= 62 ||
                   scriptType >= 64 && scriptType <= 69 ||
                   scriptType == 74;
        }

        internal static bool IsSupportedTimedAbilityType(int scriptType)
        {
            return scriptType switch
            {
                0 or 1 or 2 or 4 or 5 or 6 or 7 or 8 or 9 or 12 or 13 or 17 or 27 or 43 or 44 or 45 or 59 or 60 or 61 or 62 or 64 or 68 => true,
                _ => false
            };
        }

        internal bool CanAddNativeTimedAbility(byte internalType)
        {
            if (internalType > NativeActiveStateMax)
            {
                return false;
            }

            if (HasNativeActiveState(TimedAbilityGlobalBlockState))
            {
                return false;
            }

            if (HasNativeActiveState(TimedAbilityValueGateState))
            {
                if (IsBlockedByNativeState16(internalType))
                {
                    return false;
                }

                if ((internalType == 45 || internalType == 53) &&
                    GetNativeTimedAbilityValue(TimedAbilityValueGateState) >= 5)
                {
                    return false;
                }
            }

            return internalType != NativeState26Type ||
                   !HasNativeActiveState(18) &&
                   !IsNativeState26DeadlineActive(HUtil32.GetTickCount());
        }

        internal int GetNativeTimedAbilityValue(byte internalType)
        {
            return FindTimedAbilityInternal(internalType)?.Value ?? 0;
        }

        internal bool TryGetNativeTimedAbilityValue(byte internalType,
            out int value)
        {
            var node = FindTimedAbilityInternal(internalType);
            value = node?.Value ?? 0;
            return node != null;
        }

        internal int GetNativeTimedAbilityRemainingMilliseconds(
            byte internalType)
        {
            return FindTimedAbilityInternal(internalType)?
                .RemainingMilliseconds ?? 0;
        }

        internal bool ReduceNativeTimedAbilityRemaining(byte internalType,
            int milliseconds)
        {
            var node = FindTimedAbilityInternal(internalType);
            if (node == null)
            {
                return false;
            }

            node.RemainingMilliseconds = unchecked(
                node.RemainingMilliseconds - milliseconds);
            return true;
        }

        private TimedAbilityNode FindTimedAbilityInternal(byte internalType)
        {
            if (!HasNativeActiveState(internalType))
            {
                return null;
            }

            return FindTimedAbilityNode(internalType);
        }

        private TimedAbilityNode FindTimedAbilityNode(byte internalType)
        {
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                if (node.InternalType == internalType)
                {
                    return node;
                }
            }
            return null;
        }

        protected void ClearTimedAbilitiesOnExit()
        {
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                ClearNativeActiveState(node.InternalType);
            }

            m_TimedAbilityHead = null;
            m_TimedAbilityProcessTick = 0;
            m_boAbilityRecalcPending = false;
            ClearNativeSkill152StateOnExit();
        }

        private static bool RequiresTimedAbilityRecalc(byte internalType)
        {
            return internalType != 19 && internalType != 20 &&
                   internalType != NativeState26Type &&
                   internalType != 45 && internalType != 49 &&
                   internalType != NativeSkill153ShieldState;
        }

        protected void MarkAbilityRecalcPending()
        {
            m_boAbilityRecalcPending = true;
        }

        protected virtual void QueueTimedAbilitySnapshotAfterRecalc()
        {
        }

        protected void ConsumeAbilityRecalcPending()
        {
            if (!m_boAbilityRecalcPending)
            {
                return;
            }

            RecalcAbilitys();
            QueueTimedAbilitySnapshotAfterRecalc();
            m_boAbilityRecalcPending = false;
        }

        private void SendTimedAbilityState(TimedAbilityNode node, bool removed)
        {
            SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED, 0,
                unchecked((ushort)m_nHitSpeed), 0, 0, string.Empty,
                GetBodyStateBuffer());

            if (node.InternalType == 75)
            {
                var text = removed
                    ? "火墙抗性回复正常"
                    : $"火墙抗性瞬间提高{unchecked((ushort)(node.RemainingMilliseconds / 1000))}秒";
                if (this is HeroObject hero)
                {
                    if (hero.m_Master is TPlayObject master)
                    {
                        master.SendMsg(hero, Grobal2.RM_SYSMESSAGE, 0,
                            0xDB, 0xFF, 0, "(英雄) " + text);
                    }
                }
                else if (this is TPlayObject)
                {
                    SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                        0xDB, 0xFF, 0, text);
                }
            }

            SendTimedAbilityClientState(node.InternalType,
                node.RemainingMilliseconds, node.Value, removed);
        }

        protected virtual void SendTimedAbilityClientState(byte internalType,
            int remainingMilliseconds, int value, bool removed)
        {
        }

        internal static (ClientPacket Header, byte[] Body) BuildTimedAbilityClientState(
            byte internalType, int remainingMilliseconds, int value, bool removed)
        {
            var header = Grobal2.MakeDefaultMsg(TimedAbilityMessage,
                removed ? 0 : remainingMilliseconds, internalType, 0, 0);
            if (removed)
            {
                return (header, Array.Empty<byte>());
            }

            using var stream = new MemoryStream(10);
            using var writer = new BinaryWriter(stream);
            writer.Write(internalType);
            writer.Write((byte)0);
            writer.Write(remainingMilliseconds);
            writer.Write(value);
            return (header, stream.ToArray());
        }

        private void ApplyTimedAbilityBonuses()
        {
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                var value = node.Value;
                switch (node.InternalType - 32)
                {
                    case 0:
                        m_WAbil.DC = AddTimedRange(m_WAbil.DC, value);
                        break;
                    case 1:
                        m_WAbil.MC = AddTimedRange(m_WAbil.MC, value);
                        break;
                    case 2:
                        m_WAbil.SC = AddTimedRange(m_WAbil.SC, value);
                        break;
                    case 4:
                        m_WAbil.MaxHP = unchecked(m_WAbil.MaxHP + value);
                        break;
                    case 5:
                        m_WAbil.MaxMP = unchecked(m_WAbil.MaxMP + value);
                        break;
                    case 6:
                        m_wSpeedPoint = unchecked((ushort)(m_wSpeedPoint +
                            (ushort)value));
                        break;
                    case 7:
                        m_nAntiMagic = unchecked((ushort)(m_nAntiMagic +
                            (ushort)value));
                        break;
                    case 8:
                        m_WAbil.AC = AddTimedRange(m_WAbil.AC, value);
                        break;
                    case 9:
                        m_WAbil.MAC = AddTimedRange(m_WAbil.MAC, value);
                        break;
                    case 12:
                        m_WAbil.MaxWeight = AddTimedWord(m_WAbil.MaxWeight,
                            m_WAbil.MaxWeight, ushort.MaxValue);
                        m_WAbil.MaxWearWeight = AddTimedWord(m_WAbil.MaxWearWeight,
                            m_WAbil.MaxWearWeight, ushort.MaxValue);
                        m_WAbil.MaxHandWeight = AddTimedWord(m_WAbil.MaxHandWeight,
                            m_WAbil.MaxHandWeight, ushort.MaxValue);
                        break;
                    case 43:
                        AddNativeHqFastness(value);
                        break;
                    case 44:
                        AddNativeUnionFastness(value);
                        break;
                    case 45:
                        AddNativeNearHitFastness(value);
                        break;
                    case 59:
                        ApplyTimedJobAttack(value);
                        break;
                    case 60:
                        m_wNativeDrugJobBonus = unchecked((ushort)(
                            m_wNativeDrugJobBonus + (ushort)value));
                        break;
                    case 61:
                        m_wEffectStrength = unchecked((ushort)(m_wEffectStrength +
                            (ushort)value));
                        break;
                    case 62:
                        m_wEffectResistance = unchecked((ushort)(m_wEffectResistance +
                            (ushort)value));
                        break;
                }
            }
        }

        private void ApplyTimedJobAttack(int value)
        {
            switch (m_btJob)
            {
                case 0:
                    m_WAbil.DC = AddTimedUpper(m_WAbil.DC, value);
                    break;
                case 1:
                    m_WAbil.MC = AddTimedUpper(m_WAbil.MC, value);
                    break;
                case 2:
                    m_WAbil.SC = AddTimedUpper(m_WAbil.SC, value);
                    break;
            }
        }

        private static int AddTimedRange(int ability, int value)
        {
            return HUtil32.MakeLong(
                unchecked((ushort)(HUtil32.LoWord(ability) + value)),
                unchecked((ushort)(HUtil32.HiWord(ability) + value)));
        }

        private static int AddTimedUpper(int ability, int value)
        {
            return HUtil32.MakeLong(HUtil32.LoWord(ability),
                unchecked((ushort)(HUtil32.HiWord(ability) + value)));
        }

        private static ushort AddTimedWord(ushort current, int value, int maximum)
        {
            return unchecked((ushort)(current + value));
        }

    }
}

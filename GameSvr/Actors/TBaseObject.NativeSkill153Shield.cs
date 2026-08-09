using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal const ushort NativeSkill153ShieldMagicId = 153;
        internal const int NativeSkill153ShieldState = 59;
        internal const int NativeSkill153ShieldCooldownMilliseconds = 30_000;
        internal const int NativeSkill153ShieldWindowMilliseconds = 10_000;

        internal ushort m_wNativeSkill153ShieldCharges;
        private int m_dwNativeSkill153ShieldCastTick;
        private bool m_boNativeSkill153ShieldCooldown;

        internal static byte GetNativeSkill153ShieldEffectiveLevel(
            TUserMagic magic)
        {
            if (magic?.MagicInfo == null)
                return 0;

            return (byte)Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        internal bool TryActivateNativeSkill153Shield(TUserMagic magic)
        {
            return TryActivateNativeSkill153Shield(magic,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill153Shield(TUserMagic magic,
            int now)
        {
            byte effectiveLevel =
                GetNativeSkill153ShieldEffectiveLevel(magic);
            ushort charges = effectiveLevel switch
            {
                1 => 2,
                2 => 4,
                3 => 8,
                _ => 0
            };
            if (charges == 0)
                return false;

            int remaining = GetNativeSkill153ShieldCooldownRemaining(now);
            if (remaining != 0)
            {
                SysMsg($"还需要{unchecked((uint)remaining) / 1000}秒才能释放该技能",
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            m_wNativeSkill153ShieldCharges = charges;
            SetNativeActiveState(NativeSkill153ShieldState);
            StatusChanged();
            m_dwNativeSkill153ShieldCastTick = now;
            m_boNativeSkill153ShieldCooldown = true;
            return true;
        }

        internal int GetNativeSkill153ShieldCooldownRemaining(int now)
        {
            if (!m_boNativeSkill153ShieldCooldown)
                return 0;

            uint elapsed = unchecked((uint)(now -
                m_dwNativeSkill153ShieldCastTick));
            if (elapsed >= NativeSkill153ShieldCooldownMilliseconds)
            {
                m_boNativeSkill153ShieldCooldown = false;
                return 0;
            }

            return unchecked((int)(
                NativeSkill153ShieldCooldownMilliseconds - elapsed));
        }

        internal void ProcessNativeSkill153Shield(int now)
        {
            uint elapsed = unchecked((uint)(now -
                m_dwNativeSkill153ShieldCastTick));
            if (m_boNativeSkill153ShieldCooldown &&
                elapsed >= NativeSkill153ShieldCooldownMilliseconds)
            {
                m_boNativeSkill153ShieldCooldown = false;
            }

            if (m_wNativeSkill153ShieldCharges == 0 ||
                elapsed <= NativeSkill153ShieldWindowMilliseconds)
            {
                return;
            }

            m_wNativeSkill153ShieldCharges = 0;
            ClearNativeActiveState(NativeSkill153ShieldState);
            StatusChanged();
            SysMsg("无极盾状态消失", MsgColor.Red, MsgType.Hint);
        }

        internal int ConsumeNativeSkill153ShieldCharge(int damage)
        {
            if (m_wNativeSkill153ShieldCharges == 0)
                return damage;

            int reduction = GetNativeSkill153ShieldReduction();
            int result = unchecked(damage - reduction);
            m_wNativeSkill153ShieldCharges--;
            if (m_wNativeSkill153ShieldCharges == 0)
            {
                ClearNativeActiveState(NativeSkill153ShieldState);
                StatusChanged();
            }
            return result;
        }

        internal int GetNativeSkill153ShieldReduction()
        {
            int high = m_btJob switch
            {
                M2Share.jWarr => HUtil32.HiWord(m_WAbil.DC),
                M2Share.jWizard => HUtil32.HiWord(m_WAbil.MC),
                M2Share.jTaos => HUtil32.HiWord(m_WAbil.SC),
                _ => 0
            };
            return (int)Math.Truncate(high * 2.5d);
        }
    }
}

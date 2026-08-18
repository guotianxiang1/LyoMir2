using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 237 真龙护体. Outer arm 0x6BCB9B `E8 FB 92 08 00 call 0x745EA0`.
    /// MP cost is the literal 0x1F4, not UserMagic.Spell.
    /// </summary>
    public partial class TBaseObject
    {
        private const int NativeSkill237ColdTimeKey = 0xED;
        private const int NativeSkill237ManaCost = 0x1F4;
        private const byte NativeSkill237State = 0x3F;
        private const int NativeSkill237StateMilliseconds = 8 * 1000;
        private const int NativeSkill237CooldownMilliseconds = 0x2BF20;

        internal bool TryActivateNativeSkill237Dragon(TUserMagic userMagic)
        {
            return TryActivateNativeSkill237Dragon(userMagic,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill237Dragon(TUserMagic userMagic,
            int now)
        {
            // 0x745EC0 `C6 45 FF 00`. 0x745EC9 `85 F6 / 0F 84 22 01 00 00`.
            if (userMagic == null)
            {
                return false;
            }

            int remaining = GetNativeColdTimeRemaining(
                NativeSkill237ColdTimeKey);
            if (remaining != 0)
            {
                // 0x745FB8 `B9 E8 03 00 00` / `33 D2` / `F7 F1` — UNSIGNED.
                uint seconds = unchecked((uint)remaining) / 1000u;
                SendNativeSkill237Hint("还需要" + seconds +
                    "秒才能释放真龙护体技能");
                return false;
            }

            if (NativeSkill237ManaCost > m_WAbil.MP)
            {
                SendNativeSkill237Hint("需要魔法点" + NativeSkill237ManaCost +
                    "才能释放真龙护体技能");
                return false;
            }

            DamageSpell(unchecked((ushort)NativeSkill237ManaCost));
            // 0x745F37..0x745F76 call 0x769258: ident 0x11 (SM_SPELL)
            // through VMT+0xE0, X/Y = self+0x12C/+0x130.
            MagicManager.SendNativeSpell(this, userMagic, m_nCurrX,
                m_nCurrY);
            AddTimedAbilityInternal(NativeSkill237State, 0,
                NativeSkill237StateMilliseconds, 0);
            SetNativeColdTime(NativeSkill237ColdTimeKey,
                NativeSkill237CooldownMilliseconds, now);
            // 0x745FA5 `E8 1A CA 02 00 call 0x7729C4` broadcasts ident
            // 0x291 (SM_CHARSTATUSCHANGED) via VMT+0xE0 with [eax+0x274]
            // and the name at +0x168.
            StatusChanged();
            return true;
        }

        /// <summary>VMT+0xD4 with cx=0xFFDB at 0x745F24 and 0x745FE5.</summary>
        private void SendNativeSkill237Hint(string text)
        {
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0, text);
            }
        }
    }
}

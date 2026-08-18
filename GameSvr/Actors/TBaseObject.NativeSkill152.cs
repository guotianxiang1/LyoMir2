using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal const int NativeSkill152Id = 152;
        private const int NativeSkill152CooldownMilliseconds = 30000;
        private const int NativeSkill152OneShotMilliseconds = 10000;
        private const int NativeSkill152StatusProcessMilliseconds = 250;
        private const int NativeSkill152PlayerStateMessage = 3556;
        private const int NativeSkill152HeroStateMessage = 4367;

        private int m_nNativeSkill152CooldownRemaining;
        private int m_dwNativeSkill152StatusProcessTick;

        internal bool TryActivateNativeSkill152(TUserMagic userMagic)
        {
            return TryActivateNativeSkill152(userMagic,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill152(TUserMagic userMagic,
            int now)
        {
            byte effectiveLevel = GetNativeSkill152EffectiveLevel(userMagic);
            if (effectiveLevel < 1 || effectiveLevel > 3)
            {
                return false;
            }

            if (m_nNativeSkill152CooldownRemaining != 0)
            {
                uint seconds = unchecked(
                    (uint)m_nNativeSkill152CooldownRemaining) / 1000u;
                SendNativeSkill152Hint(
                    $"还需要{seconds}秒才能释放该技能");
                return false;
            }

            RecalcAbilitys();
            if (!TryCalculateNativeSkill152Damage(m_btJob,
                    effectiveLevel, m_WAbil.DC, m_WAbil.MC,
                    m_WAbil.SC, out int oneShotDamage))
            {
                return false;
            }

            m_nNativeOneShotMagicDamage = oneShotDamage;
            SendNativeSkill152Hint(
                "进入绝杀之意状态，下次攻击会造成额外伤害");
            m_nNativeSkill152CooldownRemaining =
                NativeSkill152CooldownMilliseconds;
            if (m_dwNativeSkill152StatusProcessTick == 0)
                m_dwNativeSkill152StatusProcessTick = now;
            SendNativeSkill152State(m_nNativeSkill152CooldownRemaining);
            return true;
        }

        internal void ProcessNativeSkill152Status(int now)
        {
            int elapsed = unchecked(
                now - m_dwNativeSkill152StatusProcessTick);
            if (elapsed <= NativeSkill152StatusProcessMilliseconds)
                return;

            m_dwNativeSkill152StatusProcessTick = now;
            if (m_nNativeSkill152CooldownRemaining != 0)
            {
                m_nNativeSkill152CooldownRemaining = unchecked(
                    m_nNativeSkill152CooldownRemaining - elapsed);
                if (m_nNativeSkill152CooldownRemaining <= 0)
                {
                    m_nNativeSkill152CooldownRemaining = 0;
                    SendNativeSkill152State(0);
                }
            }

            int oneShotElapsed = unchecked(
                NativeSkill152CooldownMilliseconds -
                m_nNativeSkill152CooldownRemaining);
            if (m_nNativeOneShotMagicDamage > 0 &&
                oneShotElapsed > NativeSkill152OneShotMilliseconds)
            {
                m_nNativeOneShotMagicDamage = 0;
                SendNativeSkill152Hint("绝杀之意状态消失");
            }
        }

        internal int ApplyNativeSkill152OneShotBonus(int skillId,
            int damage)
        {
            if (m_nNativeOneShotMagicDamage > 0 &&
                IsNativeSkill152DamageSkill(skillId))
            {
                return unchecked(damage +
                    m_nNativeOneShotMagicDamage);
            }
            return damage;
        }

        internal static byte GetNativeSkill152EffectiveLevel(
            TUserMagic magic)
        {
            if (magic?.MagicInfo == null)
                return 0;

            return (byte)Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        internal static bool TryCalculateNativeSkill152Damage(byte job,
            byte level, int dc, int mc, int sc, out int damage)
        {
            damage = 0;
            if (level < 1 || level > 3)
                return false;

            int mainStat;
            switch (job)
            {
                case M2Share.jWarr:
                    mainStat = HUtil32.HiWord(dc);
                    break;
                case M2Share.jWizard:
                    mainStat = HUtil32.HiWord(mc);
                    break;
                case M2Share.jTaos:
                    mainStat = HUtil32.HiWord(sc);
                    break;
                default:
                    // The C# ability carrier has no fourth profession stat.
                    return false;
            }

            mainStat = Math.Min(mainStat, 5000);
            int scaled = level switch
            {
                1 => mainStat / 4,
                2 => mainStat / 2,
                _ => mainStat
            };
            damage = unchecked(20 * scaled);
            return true;
        }

        internal static bool IsNativeSkill152DamageSkill(int skillId)
        {
            return skillId is 1 or 35 or 59 or 232;
        }

        internal static (ClientPacket Header, byte[] Body)
            BuildNativeSkill152StatePacket(bool hero,
                int remainingMilliseconds)
        {
            int command = hero
                ? NativeSkill152HeroStateMessage
                : NativeSkill152PlayerStateMessage;
            var header = Grobal2.MakeDefaultMsg(command,
                remainingMilliseconds, 0, 1, NativeSkill152Id);
            var body = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, 4),
                NativeSkill152Id);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4),
                remainingMilliseconds);
            return (header, body);
        }

        private void SendNativeSkill152State(int remainingMilliseconds)
        {
            bool heroState = this is HeroObject;
            var state = BuildNativeSkill152StatePacket(heroState,
                remainingMilliseconds);
            if (this is TPlayObject player)
            {
                player.SendSocket(state.Header, state.Body);
            }
            else if (this is HeroObject hero &&
                hero.m_Master is TPlayObject master)
            {
                master.SendSocket(state.Header, state.Body);
            }
        }

        private void SendNativeSkill152Hint(string text)
        {
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

        private void ClearNativeSkill152StateOnExit()
        {
            m_nNativeSkill152CooldownRemaining = 0;
            m_dwNativeSkill152StatusProcessTick = 0;
            m_nNativeOneShotMagicDamage = 0;
        }
    }
}

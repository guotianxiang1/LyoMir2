using SystemModule;

namespace GameSvr
{
    public partial class HeroObject
    {
        /// <summary>Native hero [+0x6CB] — set by sub_68A6D4 @0x68A820 after 0x68A7C4 safe-zone / guild-war gate.</summary>
        private bool m_boNativeHeroGuildWarAttack;

        /// <summary>
        /// Native sub_68A6D4 prologue @0x68A7B3-0x68A820: when master has a guild, derive
        /// whether the hero target scan applies the 0x68A8FE safe-zone / siege predicates.
        /// </summary>
        private void RefreshNativeHeroGuildWarAttackFlag(TPlayObject master)
        {
            m_boNativeHeroGuildWarAttack = false;
            if (master?.m_MyGuild == null)
                return;

            var allowConfigPath = false;
            // 0x68A7C4 call sub_76858C(self) — in safe zone skip the config/castle block.
            if (!InNativeSafeZone12())
            {
                // 0x68A7CD [[0x7D6214]+0x29] — CastleManager siege-active byte.
                var castleMgr = M2Share.CastleManager;
                if (castleMgr?.AnyCastleUnderWar == true
                    && !m_boInFreePKArea
                    && castleMgr.InCastleWarArea(this) == null)
                {
                    allowConfigPath = true;
                }
            }

            if (allowConfigPath)
            {
                m_boNativeHeroGuildWarAttack = true;
                return;
            }

            // 0x68A806-0x68A81E: master guild at war (sub_706B30).
            if (master.m_MyGuild.GuildWarList.Count > 0)
                m_boNativeHeroGuildWarAttack = true;
        }

        public override bool IsAttackTarget(TBaseObject baseObject)
        {
            if (m_Master == null || baseObject?.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                return base.IsAttackTarget(baseObject);

            var result = base.IsAttackTarget(baseObject);
            // 0x68A8FE call sub_76858C(target) — not the pet path's InSafeZone @0x767334.
            if (baseObject.InNativeSafeZone12())
                return false;
            if (!result && baseObject.InSafeZone())
                result = true;
            return result;
        }
    }
}

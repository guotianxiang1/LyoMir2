using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Warrior Hero (战士英雄) - Job 0.
    /// Specializes in close-combat physical attacks with high HP and physical damage.
    /// Native classes: TFieldWarHero (selector 0) and TMirDotaMatchHumMon_War (selector 4).
    /// </summary>
    public class WarriorHero : HeroObject
    {
        public WarriorHero() : base()
        {
        }

        /// <summary>
        /// Warrior-specific ability initialization.
        /// Native VMT+0x2C: sub_60B8BC (ordinary) or sub_60D134 (Dota).
        ///
        /// Ordinary War formula (from NativeFieldHeroNineClasses.cs):
        /// - MaxHP = R(L*(L/2+10+L/20))+50, -3*(L-60) when L>60
        /// - MaxMP = R(3.5*L)+11
        /// - AC = (0, L/7)
        /// - DC = (max(L/5-1,1), max(L/5,1))
        /// - MC/SC/CC = 0
        /// </summary>
        protected override void InitializeJobSpecificAbilities()
        {
            // The base HeroObject already calls the native ability initializer.
            // This override is a hook for future warrior-specific logic.
            base.InitializeJobSpecificAbilities();
        }
    }
}

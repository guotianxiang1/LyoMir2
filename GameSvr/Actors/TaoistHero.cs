using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Taoist Hero (道士英雄) - Job 2.
    /// Specializes in support, healing, and summoning with balanced HP/MP and magic defense.
    /// Native classes: TFieldTaosHero (selector 2) and TMirDotaMatchHumMon_Taos (selector 6).
    /// </summary>
    public class TaoistHero : HeroObject
    {
        public TaoistHero() : base()
        {
        }

        /// <summary>
        /// Taoist-specific ability initialization.
        /// Native VMT+0x2C: sub_60BF14 (ordinary) or sub_60DB50 (Dota).
        ///
        /// Ordinary Taos formula (from NativeFieldHeroNineClasses.cs):
        /// - MaxHP = R(L*(L/6+10))+50, +33*(L-60) when L>60
        /// - MaxMP = R((L/8)*2.2*L)+13
        /// - AC = 0
        /// - DC = SC = (max(L/7-1,0), max(L/7,1))
        /// - MC/CC = 0
        /// - MAC = (L/12, L/6+1)
        /// </summary>
        protected override void InitializeJobSpecificAbilities()
        {
            // The base HeroObject already calls the native ability initializer.
            // This override is a hook for future taoist-specific logic.
            base.InitializeJobSpecificAbilities();
        }
    }
}

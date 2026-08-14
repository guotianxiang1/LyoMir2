using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Wizard Hero (法师英雄) - Job 1.
    /// Specializes in magic attacks with high MP and magic damage, maintaining distance.
    /// Native classes: TFieldWizHero (selector 1) and TMirDotaMatchHumMon_Wiz (selector 5).
    /// </summary>
    public class WizardHero : HeroObject
    {
        public WizardHero() : base()
        {
        }

        /// <summary>
        /// Wizard-specific ability initialization.
        /// Native VMT+0x2C: sub_60C3FC (ordinary) or sub_60D850 (Dota).
        ///
        /// Ordinary Wiz formula (from NativeFieldHeroNineClasses.cs):
        /// - MaxHP = R(L*(L/15+5))+50, +30*(L-60) when L>60
        /// - MaxMP = R((L/5+2)*2.2*L)+13
        /// - AC = 0
        /// - DC = MC = (max(L/7-1,0), max(L/7,1))
        /// - SC/CC = 0
        /// - MAC = 0
        /// </summary>
        protected override void InitializeJobSpecificAbilities()
        {
            // The base HeroObject already calls the native ability initializer.
            // This override is a hook for future wizard-specific logic.
            base.InitializeJobSpecificAbilities();
        }
    }
}

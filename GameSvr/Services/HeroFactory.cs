using System;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Factory for creating job-specific HeroObject subclasses.
    /// Native engine instantiates different concrete classes based on job:
    /// - Job 0 (Warrior): TFieldWarHero / TMirDotaMatchHumMon_War
    /// - Job 1 (Wizard): TFieldWizHero / TMirDotaMatchHumMon_Wiz
    /// - Job 2 (Taoist): TFieldTaosHero / TMirDotaMatchHumMon_Taos
    /// - Job 3 (Assassin): TFieldAssHero / TMirDotaMatchHumMon_Ass
    ///
    /// This factory mirrors that pattern by returning typed subclasses.
    /// Evidence: NativeFieldHeroNineClasses.cs documents the nine native classes
    /// with their job bytes at actor+0x72 (set by constructors sub_60B6EC, sub_60C1DC, etc).
    /// </summary>
    public static class HeroFactory
    {
        /// <summary>
        /// Creates a HeroObject of the appropriate subclass for the given job.
        /// Job values from M2Share: jWarrior=0, jWizard=1, jTaos=2, jAssassin=3.
        /// </summary>
        /// <param name="job">Hero job byte (0-3)</param>
        /// <returns>
        /// Job-specific HeroObject subclass, or base HeroObject for unknown jobs.
        /// </returns>
        public static HeroObject Create(byte job)
        {
            return job switch
            {
                0 => new WarriorHero(),
                1 => new WizardHero(),
                2 => new TaoistHero(),
                // Job 3 (Assassin) uses HeroObject base for now - can add AssassinHero later
                _ => new HeroObject()
            };
        }

        /// <summary>
        /// Creates a HeroObject extracting the job from a native hero record.
        /// The job is at record offset 0x04 (NativeHeroRecord.Job).
        /// </summary>
        public static HeroObject CreateFromRecord(NativeHeroRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            return Create(record.Job);
        }
    }
}

namespace GameSvr.Services
{
    /// <summary>
    /// Dormant orchestration model for the nine FieldHero VMT+0x78 Initialize
    /// entries. Native bodies outside this closed slice remain explicit
    /// callbacks, while every proven field write and mirror copy is exposed by
    /// its exact actor-relative offset.
    /// </summary>
    public static class NativeFieldHeroInitializeCore
    {
        public const uint InheritedInitialize = 0x0071D904;
        public const uint EquipmentAggregate = 0x0075EE78;
        public const uint CommonRecalculate = 0x0060A5D4;

        public const int NameColorOffset = 0x155;
        public const int MapPublicationFailedOffset = 0x156;
        public const int CurrentHpOffset = 0x2AC;
        public const int MaxHpOffset = 0x2B0;
        public const int CurrentMpOffset = 0x2B4;
        public const int MaxMpOffset = 0x2B8;
        public const int InitializeMarkerOffset = 0x47C;

        /// <summary>
        /// Executes sub_60A3B4. The publication-failure byte is read after the
        /// inherited Initialize callback. Callback exceptions propagate and
        /// stop the remaining native sequence without rollback.
        /// </summary>
        public static void RunCommon(
            Action inheritedInitialize,
            Func<bool> readMapPublicationFailed,
            Action aggregateEquipment,
            Action initializeClassAbilities,
            Action recalculate,
            Action<int, byte> writeByte,
            Action<int, int> copyInt32)
        {
            ArgumentNullException.ThrowIfNull(inheritedInitialize);
            ArgumentNullException.ThrowIfNull(readMapPublicationFailed);
            ArgumentNullException.ThrowIfNull(aggregateEquipment);
            ArgumentNullException.ThrowIfNull(initializeClassAbilities);
            ArgumentNullException.ThrowIfNull(recalculate);
            ArgumentNullException.ThrowIfNull(writeByte);
            ArgumentNullException.ThrowIfNull(copyInt32);

            inheritedInitialize();
            if (!readMapPublicationFailed())
            {
                aggregateEquipment();
                initializeClassAbilities();
                recalculate();
                writeByte(NameColorOffset, 0x93);
                copyInt32(MaxHpOffset, CurrentHpOffset);
                copyInt32(MaxMpOffset, CurrentMpOffset);
            }

            writeByte(InitializeMarkerOffset, 0xFF);
        }

        /// <summary>
        /// Executes sub_60C694: common Initialize followed by the Dota marker
        /// override. If common Initialize throws, the zero write is not made.
        /// </summary>
        public static void RunDotaCommon(
            Action inheritedInitialize,
            Func<bool> readMapPublicationFailed,
            Action aggregateEquipment,
            Action initializeClassAbilities,
            Action recalculate,
            Action<int, byte> writeByte,
            Action<int, int> copyInt32)
        {
            RunCommon(inheritedInitialize, readMapPublicationFailed,
                aggregateEquipment, initializeClassAbilities, recalculate,
                writeByte, copyInt32);
            writeByte(InitializeMarkerOffset, 0);
        }

        /// <summary>
        /// Executes the exact skill/common-Initialize order for one concrete
        /// selector outcome. A false skill-append result mirrors an unresolved
        /// native magic definition: it is ignored and the wrapper continues.
        /// </summary>
        public static void RunForClass(
            NativeType2FieldHeroActorKind actorKind,
            Func<NativeFieldHeroSkill, bool> tryAppendSkill,
            Action inheritedInitialize,
            Func<bool> readMapPublicationFailed,
            Action aggregateEquipment,
            Action initializeClassAbilities,
            Action recalculate,
            Action<int, byte> writeByte,
            Action<int, int> copyInt32)
        {
            ArgumentNullException.ThrowIfNull(tryAppendSkill);
            var contract = NativeFieldHeroNineClasses.Get(actorKind);

            void RunSelectedCommon()
            {
                if (contract.CommonInitializeKind ==
                    NativeFieldHeroCommonInitializeKind.Dota)
                {
                    RunDotaCommon(inheritedInitialize,
                        readMapPublicationFailed, aggregateEquipment,
                        initializeClassAbilities, recalculate, writeByte,
                        copyInt32);
                    return;
                }

                RunCommon(inheritedInitialize, readMapPublicationFailed,
                    aggregateEquipment, initializeClassAbilities, recalculate,
                    writeByte, copyInt32);
            }

            switch (contract.InitOrder)
            {
                case NativeFieldHeroInitOrder.SkillsBeforeInitialize:
                    AppendSkills(contract, tryAppendSkill);
                    RunSelectedCommon();
                    break;
                case NativeFieldHeroInitOrder.InitializeBeforeSkills:
                    RunSelectedCommon();
                    AppendSkills(contract, tryAppendSkill);
                    break;
                case NativeFieldHeroInitOrder.NoSkills:
                    RunSelectedCommon();
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown native FieldHero Initialize order " +
                        contract.InitOrder + ".");
            }
        }

        private static void AppendSkills(NativeFieldHeroClassContract contract,
            Func<NativeFieldHeroSkill, bool> tryAppendSkill)
        {
            foreach (var skill in contract.Skills)
            {
                _ = tryAppendSkill(skill);
            }
        }
    }
}

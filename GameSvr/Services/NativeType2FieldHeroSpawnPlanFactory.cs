namespace GameSvr.Services
{
    public enum NativeType2FieldHeroActorKind
    {
        FieldWarHero,
        FieldWizHero,
        FieldTaosHero,
        FieldAssHero,
        MirDotaMatchHumMonWar,
        MirDotaMatchHumMonWiz,
        MirDotaMatchHumMonTaos,
        MirDotaMatchHumMonAss,
        ModelHero
    }

    /// <summary>
    /// Immutable, dormant construction plan. Keeping the captured selection
    /// alive also keeps its runtime publication and borrowed item bindings
    /// alive. Equipment materialization remains an explicit later step.
    /// </summary>
    public sealed class NativeType2FieldHeroSpawnPlan
    {
        private readonly NativeType2FieldHeroSpawnSelection _selection;

        internal NativeType2FieldHeroSpawnPlan(
            NativeType2FieldHeroSpawnSelection selection,
            NativeType2FieldHeroActorKind actorKind)
        {
            _selection = selection;
            ActorKind = actorKind;
        }

        public NativeType2FieldHeroActorKind ActorKind { get; }
        public NativeType2FieldHeroDefinition Definition =>
            _selection.Definition;
        public long Generation => _selection.Generation;
        public byte EffectiveJob => _selection.EffectiveJob;

        public NativeType2FieldHeroMaterialization MaterializeEquipment() =>
            _selection.MaterializeEquipment();
    }

    /// <summary>
    /// Pure selector-to-plan mapping. Placement, actor allocation, fill,
    /// initialization, and map/list publication remain outside this boundary.
    /// </summary>
    public static class NativeType2FieldHeroSpawnPlanFactory
    {
        public static NativeType2FieldHeroSpawnPlan Create(
            NativeType2FieldHeroSpawnSelection selection)
        {
            if (selection == null)
                throw new ArgumentNullException(nameof(selection));

            var actorKind = selection.EffectiveJob switch
            {
                0 => NativeType2FieldHeroActorKind.FieldWarHero,
                1 => NativeType2FieldHeroActorKind.FieldWizHero,
                2 => NativeType2FieldHeroActorKind.FieldTaosHero,
                3 => NativeType2FieldHeroActorKind.FieldAssHero,
                4 => NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar,
                5 => NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz,
                6 => NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos,
                7 => NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss,
                _ => NativeType2FieldHeroActorKind.ModelHero
            };
            return new NativeType2FieldHeroSpawnPlan(selection, actorKind);
        }
    }
}

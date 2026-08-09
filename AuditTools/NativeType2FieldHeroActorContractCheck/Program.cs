using System.Reflection;
using GameSvr;
using GameSvr.Services;

CheckHierarchyAndDormantBoundary();
CheckOriginalClassMetadata();
CheckOrdinaryAbilities();
CheckModelAbility();
CheckDotaAbilities();
CheckSkillContracts();

Console.WriteLine("PASS NativeType2FieldHeroActorContractCheck " +
                  "classes=9 hierarchy=AnimalObject formulas=closed " +
                  "skills=ordered runtime=NO-GO");

static void CheckHierarchyAndDormantBoundary()
{
    Equal(typeof(AnimalObject), typeof(TFieldHero).BaseType,
        "FieldHero direct managed base");
    Check(typeof(TFieldHero).IsAbstract, "FieldHero base is abstract");
    Equal(typeof(TFieldHero), typeof(TMirDotaMatchHumMon).BaseType,
        "Dota common base");
    Check(typeof(TMirDotaMatchHumMon).IsAbstract,
        "Dota common actor is abstract");
    var fieldHeroConstructor = typeof(TFieldHero).GetConstructors(
        BindingFlags.NonPublic | BindingFlags.Instance).Single();
    Check(fieldHeroConstructor.IsFamilyAndAssembly,
        "FieldHero construction is private protected");
    var dotaConstructor = typeof(TMirDotaMatchHumMon).GetConstructors(
        BindingFlags.NonPublic | BindingFlags.Instance).Single();
    Check(dotaConstructor.IsFamilyAndAssembly,
        "Dota base construction is private protected");

    var concrete = new[]
    {
        typeof(TFieldWarHero),
        typeof(TFieldWizHero),
        typeof(TFieldTaosHero),
        typeof(TFieldAssHero),
        typeof(TModelHero),
        typeof(TMirDotaMatchHumMon_War),
        typeof(TMirDotaMatchHumMon_Wiz),
        typeof(TMirDotaMatchHumMon_Taos),
        typeof(TMirDotaMatchHumMon_Ass)
    };
    Equal(9, concrete.Distinct().Count(), "nine distinct actor types");
    foreach (var type in concrete)
    {
        Check(type.IsSealed && !type.IsAbstract,
            type.Name + " is a sealed concrete actor");
        Check(typeof(TFieldHero).IsAssignableFrom(type),
            type.Name + " derives from FieldHero");
        Equal(0, type.GetConstructors(BindingFlags.Public |
                                      BindingFlags.Instance).Length,
            type.Name + " has no public construction entry");
        var constructors = type.GetConstructors(BindingFlags.NonPublic |
                                                BindingFlags.Instance);
        Equal(1, constructors.Length, type.Name + " internal constructor");
        var parameters = constructors[0].GetParameters();
        Equal(2, parameters.Length, type.Name + " constructor arity");
        Equal(typeof(NativeType2FieldHeroSpawnPlan),
            parameters[0].ParameterType,
            type.Name + " retains the spawn plan handle");
        Equal(typeof(NativeType2FieldHeroMaterialization),
            parameters[1].ParameterType,
            type.Name + " retains the materialization handle");
    }

    Check(!TFieldHero.ProductionReady, "production gate remains closed");
    Check(TFieldHero.ProductionNoGoReason.StartsWith("NO-GO:",
            StringComparison.Ordinal),
        "NO-GO reason is explicit");
    Equal(typeof(TFieldHero), typeof(TFieldHero).GetMethod(
            nameof(TFieldHero.Initialize))!.DeclaringType,
        "Initialize is owned by dormant base");
    Equal(typeof(TFieldHero), typeof(TFieldHero).GetMethod(
            nameof(TFieldHero.Run))!.DeclaringType,
        "Run cannot fall through to AnimalObject");
}

static void CheckOriginalClassMetadata()
{
    Equal(0x00606F1C, TFieldHero.OriginalVmtAddress, "FieldHero VMT");
    Equal(0x006094E8, TFieldHero.OriginalConstructorAddress,
        "FieldHero constructor");
    Equal(0x69C, TFieldHero.OriginalInstanceSize, "FieldHero size");

    CheckMetadata(TFieldWarHero.OriginalVmt,
        TFieldWarHero.OriginalConstructor, TFieldWarHero.OriginalSize,
        0x006071CC, 0x0060B6EC, 0x6A8, "FieldWarHero");
    CheckMetadata(TFieldWizHero.OriginalVmt,
        TFieldWizHero.OriginalConstructor, TFieldWizHero.OriginalSize,
        0x0060773C, 0x0060C1DC, 0x6A4, "FieldWizHero");
    CheckMetadata(TFieldTaosHero.OriginalVmt,
        TFieldTaosHero.OriginalConstructor, TFieldTaosHero.OriginalSize,
        0x006079F4, 0x0060BD88, 0x6A8, "FieldTaosHero");
    CheckMetadata(TFieldAssHero.OriginalVmt,
        TFieldAssHero.OriginalConstructor, TFieldAssHero.OriginalSize,
        0x00607484, 0x00608D68, 0x6A0, "FieldAssHero");
    CheckMetadata(TModelHero.OriginalVmt,
        TModelHero.OriginalConstructor, TModelHero.OriginalSize,
        0x00607CAC, 0x00609038, 0x6A0, "ModelHero");
    CheckMetadata(TMirDotaMatchHumMon_War.OriginalVmt,
        TMirDotaMatchHumMon_War.OriginalConstructor,
        TMirDotaMatchHumMon_War.OriginalSize,
        0x00608230, 0x0060CDDC, 0x6B8, "DotaWar");
    CheckMetadata(TMirDotaMatchHumMon_Wiz.OriginalVmt,
        TMirDotaMatchHumMon_Wiz.OriginalConstructor,
        TMirDotaMatchHumMon_Wiz.OriginalSize,
        0x006087C0, 0x0060D644, 0x6B4, "DotaWiz");
    CheckMetadata(TMirDotaMatchHumMon_Taos.OriginalVmt,
        TMirDotaMatchHumMon_Taos.OriginalConstructor,
        TMirDotaMatchHumMon_Taos.OriginalSize,
        0x00608A88, 0x0060DA0C, 0x6B8, "DotaTaos");
    CheckMetadata(TMirDotaMatchHumMon_Ass.OriginalVmt,
        TMirDotaMatchHumMon_Ass.OriginalConstructor,
        TMirDotaMatchHumMon_Ass.OriginalSize,
        0x006084F8, 0x0060D3C0, 0x6B0, "DotaAss");
}

static void CheckOrdinaryAbilities()
{
    var war = TFieldWarHero.CalculateNativeAbility(45);
    Equal(1614, war.MaxHp, "war level 45 MaxHP");
    Equal(169, war.MaxMp, "war level 45 MaxMP");
    CheckPair(war.AC, 0, 6, "war AC");
    CheckPair(war.DC, 8, 9, "war DC");
    Equal(21, TFieldWarHero.CalculateNativeAbility(3).MaxMp,
        "war x87 tie-to-even projection");
    Equal(2704, TFieldWarHero.CalculateNativeAbility(61).MaxHp,
        "war post-60 subtraction");

    var wiz = TFieldWizHero.CalculateNativeAbility(45);
    Equal(410, wiz.MaxHp, "wiz level 45 MaxHP");
    Equal(1102, wiz.MaxMp, "wiz level 45 MaxMP");
    CheckPair(wiz.DC, 5, 6, "wiz DC");
    CheckPair(wiz.MC, 5, 6, "wiz MC");
    Equal(633, TFieldWizHero.CalculateNativeAbility(61).MaxHp,
        "wiz post-60 addition");

    var taos = TFieldTaosHero.CalculateNativeAbility(45);
    Equal(838, taos.MaxHp, "taos level 45 MaxHP");
    Equal(570, taos.MaxMp, "taos level 45 MaxMP");
    CheckPair(taos.MAC, 3, 8, "taos MAC");
    CheckPair(taos.DC, 5, 6, "taos DC");
    CheckPair(taos.SC, 5, 6, "taos SC");
    Equal(1313, TFieldTaosHero.CalculateNativeAbility(61).MaxHp,
        "taos post-60 addition");

    var ass = TFieldAssHero.CalculateNativeAbility(45);
    Equal(1614, ass.MaxHp, "ass level 45 MaxHP");
    Equal(1614, ass.MaxMp, "ass level 45 MaxMP");
    CheckPair(ass.AC, 0, 6, "ass AC");
    CheckPair(ass.CC, 8, 9, "ass CC");
    CheckPair(TFieldAssHero.CalculateNativeAbility(0).CC, -1, 0,
        "ass low-level CC remains unclamped");
}

static void CheckModelAbility()
{
    var model = TModelHero.CalculateNativeAbility(65535);
    Equal(5000, model.MaxHp, "model fixed MaxHP");
    Equal(1000, model.MaxMp, "model fixed MaxMP");
    CheckPair(model.AC, 0, 1000, "model AC");
    CheckPair(model.MAC, 0, 1000, "model MAC");
    Equal((int?)5000, model.ForcedCurrentHp, "model current HP");
    Equal((int?)5000, model.ForcedCurrentMp,
        "model current MP exceeds MaxMP exactly");
}

static void CheckDotaAbilities()
{
    var war = TMirDotaMatchHumMon_War.CalculateNativeAbility(2);
    Equal(100000, war.MaxHp, "Dota war MaxHP");
    Equal(10000, war.MaxMp, "Dota war MaxMP");
    CheckPair(war.DC, 1000, 1000, "Dota war DC");

    var wiz = TMirDotaMatchHumMon_Wiz.CalculateNativeAbility(2);
    Equal(10000, wiz.MaxHp, "Dota wiz MaxHP");
    Equal(100000, wiz.MaxMp, "Dota wiz MaxMP");
    CheckPair(wiz.MC, 1600, 1600, "Dota wiz MC");

    var taos = TMirDotaMatchHumMon_Taos.CalculateNativeAbility(2);
    Equal(50000, taos.MaxHp, "Dota taos MaxHP");
    Equal(50000, taos.MaxMp, "Dota taos MaxMP");
    CheckPair(taos.SC, 2000, 2000, "Dota taos SC");

    var ass = TMirDotaMatchHumMon_Ass.CalculateNativeAbility(2);
    Equal(50000, ass.MaxHp, "Dota ass MaxHP");
    Equal(50000, ass.MaxMp, "Dota ass MaxMP");
    CheckPair(ass.CC, 1000, 1000, "Dota ass CC");
}

static void CheckSkillContracts()
{
    CheckSkills(TFieldWarHero.SkillContracts,
        TFieldWarHero.SkillPlacementContract,
        new ushort[] { 3, 12, 26, 34 }, 3, "ordinary war");
    CheckSkills(TFieldWizHero.SkillContracts,
        TFieldWizHero.SkillPlacementContract,
        new ushort[] { 11, 35, 31, 10 }, 3, "ordinary wiz");
    CheckSkills(TFieldTaosHero.SkillContracts,
        TFieldTaosHero.SkillPlacementContract,
        new ushort[] { 4, 6, 13, 36 }, 3, "ordinary taos");
    CheckSkills(TFieldAssHero.SkillContracts,
        TFieldAssHero.SkillPlacementContract,
        new ushort[] { 260, 264, 268 }, 4, "ordinary ass");
    Equal(0, TModelHero.SkillContracts.Count, "model has no skills");
    Equal(NativeFieldHeroSkillPlacement.None,
        TModelHero.SkillPlacementContract, "model skill placement");
    CheckSkills(TMirDotaMatchHumMon_War.SkillContracts,
        TMirDotaMatchHumMon_War.SkillPlacementContract,
        new ushort[] { 3, 12, 26, 34 }, 3, "Dota war");
    CheckSkills(TMirDotaMatchHumMon_Wiz.SkillContracts,
        TMirDotaMatchHumMon_Wiz.SkillPlacementContract,
        new ushort[] { 11, 35, 31, 10 }, 3, "Dota wiz");
    CheckSkills(TMirDotaMatchHumMon_Taos.SkillContracts,
        TMirDotaMatchHumMon_Taos.SkillPlacementContract,
        new ushort[] { 4, 6, 13, 36 }, 3, "Dota taos");
    CheckSkills(TMirDotaMatchHumMon_Ass.SkillContracts,
        TMirDotaMatchHumMon_Ass.SkillPlacementContract,
        new ushort[] { 260, 264, 268 }, 4, "Dota ass");
}

static void CheckSkills(IReadOnlyList<NativeFieldHeroSkillContract> actual,
    NativeFieldHeroSkillPlacement placement, IReadOnlyList<ushort> ids,
    byte level, string description)
{
    Equal(ids.Count, actual.Count, description + " skill count");
    for (var index = 0; index < ids.Count; index++)
    {
        Equal(ids[index], actual[index].MagicId,
            description + " skill order " + index);
        Equal(level, actual[index].Level,
            description + " skill level " + index);
    }

    Equal(level == 4
            ? NativeFieldHeroSkillPlacement.AfterCommonInitialize
            : NativeFieldHeroSkillPlacement.BeforeCommonInitialize,
        placement, description + " initialization placement");
}

static void CheckMetadata(int vmt, int constructor, int size,
    int expectedVmt, int expectedConstructor, int expectedSize,
    string description)
{
    Equal(expectedVmt, vmt, description + " VMT");
    Equal(expectedConstructor, constructor,
        description + " constructor");
    Equal(expectedSize, size, description + " size");
}

static void CheckPair(NativeFieldHeroAbilityPair actual, int low, int high,
    string description)
{
    Equal(low, actual.Low, description + " low");
    Equal(high, actual.High, description + " high");
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected={expected}, actual={actual}");
    }
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

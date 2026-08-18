namespace GameSvr
{
    internal static class NativeItemFactory
    {
        // Original Delphi factory: M2Server.exe 0x0074C338 (StdMode +0x14, Shape +0x15).
        internal static string GetClassName(GoodItem item)
        {
            return item == null ? null : GetClassName(item.StdMode, item.Shape, item.DuraMax);
        }

        internal static string GetClassName(byte stdMode, byte shape, ushort duraMax)
        {
            switch (stdMode)
            {
                case 0:
                    return shape switch
                    {
                        1 => "TQuickDrug",
                        2 => "TShengShui",
                        12 => "TSpecialDrug",
                        13 => "TTmpBuffDrug",
                        14 => "TStrengthDrug",
                        15 => "TNewTmpBuffDrug",
                        16 => "TXiuFuLingShui",
                        17 => "TPercentAddDrug",
                        18 => "TPercentResumeDrug",
                        19 => "TQuickDrugForce",
                        20 => "THuoYuanMedicament",
                        21 => "TShuiYuanMedicament",
                        22 => "TMuYuanMedicament",
                        23 => "TJinYuanMedicament",
                        24 => "TTuYuanMedicament",
                        25 => "TNewTempBuffDrugEx",
                        _ => "TSlowDrug"
                    };
                case 1:
                    return shape switch
                    {
                        0 => "TNoEffectItem",
                        1 => "TDoubleExpProp",
                        5 => "TAntiDecExpProp",
                        20 => "THappyCake",
                        21 or 22 => "TChgHairProp",
                        23 or 24 or 25 => "TColorSayProp",
                        26 => "TWhiteGoldCard",
                        27 => "TWoodBox",
                        28 => "TGoldActCred",
                        30 => "TGroupAddExpItem",
                        31 => "TNewQkBag",
                        32 => "TBirthdayCake",
                        33 => "TNewFireworks",
                        34 => "TBufferFlower",
                        35 => "TFixedCoordStone",
                        36 => "TBufferItem",
                        130 => "THappyCake",
                        _ => "TBaseItem"
                    };
                case 2:
                    return shape switch
                    {
                        0 => "TNoEffectItem",
                        1 or 2 or 3 => "TRope",
                        4 or 5 or 54 => "TCityStone",
                        6 or 7 => "TCallMobProp",
                        8 => "TRndFlyStone",
                        9 => "TRepairWater",
                        10 => "TExpBall",
                        11 => "TForceExpHeap",
                        12 => "TForceExpBook",
                        13 => "TForceExpBall",
                        14 => "TFireDragonBall",
                        15 => "TQianKunBag",
                        16 => "TWine",
                        17 => "TForceBall",
                        18 => "TDreamFlyStone",
                        19 => "TDreamCityScroll",
                        23 => "THeroExpBall",
                        24 => "TSkillTrainScroll",
                        25 => "TCallMonStone",
                        26 => "TSuperForceBall",
                        30 => "TRopeExt",
                        31 => "TCastleCityFlyStone",
                        32 => "TSpiritWater",
                        33 => "THumanRenameToken",
                        51 => "TDragonBlood",
                        55 => "TNBJieJing",
                        56 => "THorseYearBadge",
                        _ => "TBaseItem"
                    };
                case 3:
                    if (shape is 1 or 2 or 3 or 5) return "TMoveScroll";
                    if (shape == 4) return "TLuckOil";
                    if (shape is 9 or 10) return "TRepairOil";
                    if (shape == 11) return "TNoEffectItem";
                    if (shape is >= 13 and <= 19 || shape is 21 or 22 or 24 or 25 || shape is >= 27 and <= 29)
                        return "TFireFlower";
                    return shape switch
                    {
                        23 => "TMakeMonFireFlower",
                        26 => "TMicroWhelk",
                        32 => "TTimerBomb",
                        34 => "TRabbitYearFire",
                        35 => "TFoxMapScroll",
                        36 => "TStealthItem",
                        37 => "TMonsterBlowItem",
                        _ => "TBaseItem"
                    };
                case 4:
                    return "TSkillBook";
                case 5:
                    if (shape == 6 && duraMax == 100) return "TBrokenWeapon";
                    return shape is >= 61 and <= 63 ? "TSpade" : "TLWeapon";
                case 6:
                    return "TLWeapon";
                case 7:
                    return shape switch
                    {
                        0 => "TCryCharm",
                        1 => "THPCharm",
                        2 => "TMPCharm",
                        3 => "THPMPCharm",
                        >= 5 and <= 9 => "TMarkStoneCharm",
                        _ => "TCharm"
                    };
                case 8:
                    return shape == 1 ? "TDragonSeal" : "TBaseItem";
                case 10:
                    return shape == 28 ? "TTemporaryManClothes" : "TManClothes";
                case 11:
                    return shape == 28 ? "TTemporaryWomanClothes" : "TWomanClothes";
                case 15:
                    return "THelmet";
                case 16:
                    return "THeadMask";
                case 19:
                case 20:
                case 21:
                    return "TNecklace";
                case 22:
                case 23:
                    return "TRing";
                case 24:
                case 26:
                    return "TArmRing";
                case 25:
                    return shape switch
                    {
                        1 or 2 => "TPoisons",
                        5 => "TBujuk",
                        7 => "TUnionItem",
                        8 => "TVessel",
                        9 => "TDragonHeart",
                        10 => "TSuperDragonHeart",
                        _ => "TBaseItem"
                    };
                case 27:
                    return "TBelt";
                case 28:
                    return "TBoots";
                case 29:
                    return "TWarDrum";
                case 30:
                    return "TRWeapon";
                case 31:
                    if (shape is >= 1 and <= 3) return "TNormalBox";
                    if (shape == 5) return "TUnBundleItem";
                    if (shape == 6) return "TCNFeastBox";
                    if (shape is >= 7 and <= 9) return "TCoupleFeastBox";
                    if (shape is 10 or 11) return "TNormalCrystal";
                    if (shape == 15) return "TNewPlayerBox";
                    if (shape == 16) return "TRabbitPrize";
                    if (shape == 17) return "TUnionUpBook";
                    return shape is >= 81 and <= 150 ? "TBundleItem" : null;
                case 32:
                    return "TCloseAttrItm";
                case 33:
                    return "THeroHighExpItem";
                case 34:
                    return "TMaPai";
                case 40:
                    return "TMeatItem";
                case 42:
                    return "TBaneMaterial";
                case 43:
                    return "TOreItem";
                case 47:
                    return shape == 1 ? "TGoldPackage" : "TBaseItem";
                case 56:
                    return shape switch
                    {
                        2 => "TIdentifyScrollItem",
                        3 => "TTaskIdentifyScroll",
                        _ => null
                    };
                case 60:
                    return "TConjurator";
                case 61:
                    return "TMagicStone";
                case 62:
                    return "TAnimalMascot";
                case 76:
                    return "TAbilRouleauItem";
                case 77:
                    return "TAutoTransScore";
                case 78:
                    return "TOffiRankStone";
                case 79:
                    if (shape < 2) return "TJewelStone";
                    if (shape < 4) return "TJewelFragment";
                    return "TBaseItem";
                case 96:
                    return "TSpecialDropItem";
                case 151:
                    return "TGoldAcus";
                case 152:
                    return shape == 16 ? "TJingXiuBook" : "TBasePileItem";
                case 154:
                    return "TLuckOil";
                case 155:
                    return shape switch
                    {
                        1 => "TNewHappyCake",
                        2 => "THeroJingmaiDrug",
                        3 => "TShiMenCall",
                        4 => "TSuperExpItem",
                        5 => "TLevelBuffItem",
                        7 => "THeroHypericum",
                        8 => "THeroFileDragonScroll",
                        9 => "THeroExpScroll",
                        10 => "TTaoFaLingAddExpItem",
                        11 => "TPneumaStone",
                        _ => null
                    };
                case 156:
                    return shape is 1 or 2 ? "TPileFlower" : null;
                case 159:
                    return null;
                default:
                    return stdMode >= 150 ? "TBasePileItem" : "TBaseItem";
            }
        }

        /// <summary>
        /// Every class the factory can reach whose VMT parent chain ends at
        /// TBasePileItem (VMT 0x781C24, cell 0x781BD8).  Resolved by walking
        /// vmtParent at classref-0x24, which is a PPClass and so needs a double
        /// dereference, over all 141 class cells the factory loads.
        /// </summary>
        private static readonly HashSet<string> NativePileClasses =
            new(StringComparer.Ordinal)
            {
                "TBasePileItem",         // 0x781BD8
                "TLuckOil",              // 0x781CAC
                "TPneumaStone",          // 0x781D78
                "TTaoFaLingAddExpItem",  // 0x781E4C
                "TGoldAcus",             // 0x781F30
                "TShiMenCall",           // 0x781FFC
                "TSuperExpItem",         // 0x7820CC
                "TLevelBuffItem",        // 0x7821A0
                "TNewHappyCake",         // 0x782274
                "THeroJingmaiDrug",      // 0x782348
                "TPileFlower",           // 0x782424
                "THeroHypericum",        // 0x782920
                "THeroFileDragonScroll", // 0x7829F4
                "THeroExpScroll",        // 0x782AD8
                "TJingXiuBook",          // 0x782D64
            };

        /// <summary>
        /// "Was built by the pile constructor", i.e. carries the native pile
        /// marker <c>mov byte [esi+0x14],7</c>.  That marker is written by
        /// TBasePileItem.Create @0x788118 and re-written by the six subclass
        /// constructors that chain it (@0x788C01 / @0x788C84 / @0x78B27C /
        /// @0x78B2D8 / @0x78B328 / @0x78B544), so the predicate is class
        /// ancestry, not a StdMode range.
        ///
        /// StdMode &gt;= 150 is NOT equivalent: the StdMode 3 arm hands Shape 4
        /// to the pile constructor as well —
        ///   0074CCE2  51              push ecx
        ///   0074CCE3  B2 01           mov  dl,1
        ///   0074CCE5  A1 AC 1C 78 00  mov  eax,[0x781CAC]   ; TLuckOil
        ///   0074CCEA  8B CB           mov  ecx,ebx
        ///   0074CCEC  E8 FF B3 03 00  call 0x7880F0         ; TBasePileItem.Create
        /// and 0x7880F0 forces <c>word[esi+0x26] = 1</c> @0x788112, i.e. a stack
        /// of one.  All fifteen classes also share <c>[VMT+0x28] = 0x7882B4</c>,
        /// a bare <c>ret</c>, so they take no drop-time durability roll.
        /// </summary>
        internal static bool IsPileItem(GoodItem item)
        {
            var className = GetClassName(item);
            return className != null && NativePileClasses.Contains(className);
        }

        // --------------------------------------------------------------------
        // Delphi `is` semantics.
        //
        // Native type gates are the `is` operator, NOT a name comparison:
        //   0x6866F9  mov edx,[0x75E7C4]        ; the TDragonHeart class ref cell
        //   0x6866FF  call 0x404828             ; sub_404828 = Delphi `is`
        // and `is` walks the parent chain in sub_4048C8:
        //   0x4048CC  cmp eax,edx / je -> true
        //   0x4048D0  mov eax,[eax-0x24]        ; parent class REFERENCE cell
        //   0x4048CA  mov eax,[eax]             ; deref it to the parent VMT
        // i.e. vmt-0x24 holds a POINTER TO the parent reference, so resolving a
        // parent takes a DOUBLE dereference. The practical consequence is that
        // `is TFoo` ACCEPTS every descendant of TFoo, so a C# gate written as
        // GetClassName(x) == "TFoo" is only equivalent when TFoo is childless.
        //
        // The table contains the image-proven ancestry needed by current native
        // type gates: the DragonHeart descendant and the complete factory-
        // reachable TEquipItem subtree used by sub_63EE14.
        private static readonly Dictionary<string, string> NativeClassParents =
            new()
            {
                // child                  parent
                { "TRWeapon", "TEquipItem" },
                { "TLWeapon", "TEquipItem" },
                { "THeadMask", "TEquipItem" },
                { "TWarDrum", "TEquipItem" },
                { "THelmet", "TEquipItem" },
                { "TNecklace", "TEquipItem" },
                { "TRing", "TEquipItem" },
                { "TArmRing", "TEquipItem" },
                { "TBelt", "TEquipItem" },
                { "TBoots", "TEquipItem" },
                { "TMaPai", "TEquipItem" },
                { "TCharm", "TEquipItem" },
                { "TClothes", "TEquipItem" },
                { "TEquipBujuk", "TEquipItem" },

                { "TBrokenWeapon", "TLWeapon" },
                { "TSpade", "TLWeapon" },
                { "TManClothes", "TClothes" },
                { "TWomanClothes", "TClothes" },
                { "TTemporaryManClothes", "TManClothes" },
                { "TTemporaryWomanClothes", "TWomanClothes" },
                { "TCryCharm", "TCharm" },
                { "THPCharm", "TCharm" },
                { "TMPCharm", "TCharm" },
                { "THPMPCharm", "TCharm" },
                { "TMarkStoneCharm", "TCharm" },
                { "TPoisons", "TEquipBujuk" },
                { "TBujuk", "TEquipBujuk" },
                { "TUnionItem", "TEquipBujuk" },
                { "TVessel", "TEquipBujuk" },
                { "TDragonHeart", "TEquipBujuk" },
                { "TSuperDragonHeart", "TDragonHeart" },
            };

        /// <summary>
        /// Delphi <c>is</c>: true when <paramref name="className"/> is
        /// <paramref name="baseClassName"/> or any descendant of it.
        /// </summary>
        internal static bool IsClassOrDescendantOf(string className,
            string baseClassName)
        {
            if (className == null || baseClassName == null) return false;
            var current = className;
            // The chain is shallow and acyclic; the bound only guards against a
            // malformed table.
            for (var guard = 0; guard < 32; guard++)
            {
                if (current == baseClassName) return true;
                if (!NativeClassParents.TryGetValue(current, out var parent))
                    return false;
                current = parent;
            }
            return false;
        }

        internal static bool IsClassOrDescendantOf(GoodItem item,
            string baseClassName) =>
            IsClassOrDescendantOf(GetClassName(item), baseClassName);
    }
}

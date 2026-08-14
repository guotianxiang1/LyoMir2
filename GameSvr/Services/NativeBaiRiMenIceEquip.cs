using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 白日门凝冰系列装备名比对 — native sub_762D98 @0x762D98.
    /// Compares item std-name against four canonical names; returns true when
    /// none match (native bl=1 at 0x762E26 when all four CompareStr fail).
    /// Used as a synthesis guard: only the 凝冰 belt/乾坤 variants pass.
    /// </summary>
    public static class NativeBaiRiMenIceEquip
    {
        public const uint NativeCompareEa = 0x00762D98;

        private static readonly string[] NativeIceEquipNames =
        {
            "白日门凝冰腰带",
            "白日门凝冰乾坤",
            "白日门冰石乾坤",
            "白日门冰石腰带"
        };

        /// <summary>
        /// True when <paramref name="itemName"/> is one of the four 凝冰 series names.
        /// </summary>
        public static bool IsNativeIceEquipName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return false;

            for (var i = 0; i < NativeIceEquipNames.Length; i++)
            {
                if (string.Equals(itemName, NativeIceEquipNames[i],
                        StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Native return: true => NOT an ice-equip name (reject synthesis leg).
        /// </summary>
        public static bool NativeRejectNonIceEquipName(string itemName)
            => !IsNativeIceEquipName(itemName);
    }
}

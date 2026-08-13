using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Item VMT +0x28 on the drop path. Native drop arms pass edx=0:
    ///   0x71FBCE  33 D2 / 0x71FBD5  FF 51 28   exclusive chain
    ///   0x71FD9B  33 D2 / 0x71FDA2  FF 51 28   monster table
    /// Base class is sub_783EFC (Random(80) dura). Equipment overrides add a
    /// second Random gate and, on 0, the extra-attribute body. Pile +0x28 is
    /// a bare ret (0x7882B4 C3) — Dura stays at the ctor's 1.
    /// </summary>
    internal static class NativeItemPlus28
    {
        private const ushort DuraCap = 0xFDE8; // 65000, min() at 0x4C700C
        private const byte ExtraAttrFlag = 0x40; // test byte [StdItem+2], 0x40

        public static void ApplyOnDrop(TUserItem item, GoodItem std)
        {
            if (item == null) return;
            if (std != null && NativeItemFactory.IsPileItem(std))
                return;

            var className = NativeItemFactory.GetClassName(std);
            switch (className)
            {
                case "TLWeapon":
                case "TSpade":
                case "TBrokenWeapon":
                    ApplyWeapon(item, std);
                    return;
                case "TClothes":
                case "TManClothes":
                case "TWomanClothes":
                case "TTemporaryManClothes":
                case "TTemporaryWomanClothes":
                    ApplyClothes(item, std);
                    return;
                case "THelmet":
                    ApplyHelmet(item, std);
                    return;
                case "TNecklace":
                    ApplyNecklace(item, std);
                    return;
                case "TRing":
                    ApplyRing(item, std);
                    return;
                case "TArmRing":
                    ApplyArmRing(item, std);
                    return;
                case "TJewelStone":
                    ApplyDura80(item);
                    // 0x78C6C9 mov eax,0xC / 0x78C6CE call Random / inc edx
                    // then sub_78C5EC (table write). The helper has no Random
                    // in its first 0x40; the draw is consumed to keep the seed.
                    _ = Random(12);
                    return;
                default:
                    ApplyDura80(item);
                    return;
            }
        }

        // 0x783EFC: Dura = Round(DuraMax / 100.0 * (20 + Random(80)))
        private static void ApplyDura80(TUserItem item)
        {
            item.Dura = (ushort)HUtil32.Round(
                item.DuraMax / 100.0 * (20 + Random(80)));
        }

        // Shape 130/131/132: add al,0x7E / sub al,3 / jae normal → call [vmt+8].
        // Those three skip 783EFC. The +0x08 body is not modelled here.
        private static bool IsUnknownShape(GoodItem std)
        {
            return std != null && std.Shape is 130 or 131 or 132;
        }

        private static bool HasExtraAttrFlag(GoodItem std)
        {
            return std != null && (std.NativeReserved02 & ExtraAttrFlag) != 0;
        }

        // 0x7608D4
        private static void ApplyWeapon(TUserItem item, GoodItem std)
        {
            ApplyDura80(item);
            if (Random(10) != 0 || !HasExtraAttrFlag(std)) return;

            MaybeStore(item, 0, 6, 20, 30);
            MaybeStore(item, 1, 12, 15, 30);
            MaybeStore(item, 2, 12, 15, 30);

            // +0x30: GRR(12,15); Random(20)==0; (n+1)/3; Random(3)==0 → +10
            var n = GetRandomRange(12, 15);
            if (Random(20) == 0)
            {
                var slot = (n + 1) / 3;
                if (slot > 0)
                {
                    if (Random(3) != 0) item.btValue[6] = (byte)slot;
                    else item.btValue[6] = (byte)(slot + 10);
                }
            }

            n = GetRandomRange(12, 15);
            if (Random(24) == 0)
                item.btValue[5] = (byte)(n / 2);

            n = GetRandomRange(12, 12);
            if (Random(3) < 2)
                AddDura(item, (n + 1) * 2000);

            n = GetRandomRange(12, 15);
            if (Random(10) == 0)
                item.btValue[7] = (byte)(n / 2 + 1);
        }

        // 0x7639DC → 0x783F40
        private static void ApplyClothes(TUserItem item, GoodItem std)
        {
            ApplyDura80(item);
            if (Random(10) != 0 || !HasExtraAttrFlag(std)) return;
            MaybeStore(item, 0, 6, 20, 20);
            MaybeStore(item, 1, 6, 20, 20);
            MaybeStore(item, 2, 6, 20, 30);
            MaybeStore(item, 3, 6, 20, 30);
            MaybeStore(item, 4, 6, 20, 30);
            var n = GetRandomRange(6, 10);
            if (Random(8) < 6)
                AddDura(item, (n + 1) * 2000);
        }

        // 0x7611C8
        private static void ApplyHelmet(TUserItem item, GoodItem std)
        {
            if (IsUnknownShape(std)) return;
            ApplyDura80(item);
            if (Random(10) != 0 || !HasExtraAttrFlag(std)) return;
            if (std.Shape == 1) item.btValue[0] = 0;
            else MaybeStore(item, 0, 6, 20, 20);
            if (std.Shape == 1) item.btValue[1] = 0;
            else MaybeStore(item, 1, 6, 20, 20);
            MaybeStore(item, 2, 6, 20, 30);
            MaybeStore(item, 3, 6, 20, 30);
            MaybeStore(item, 4, 6, 20, 30);
            var n = GetRandomRange(6, 12);
            if (Random(4) < 3)
                AddDura(item, (n + 1) * 1000);
        }

        // 0x76178C → 0x7617BC
        private static void ApplyNecklace(TUserItem item, GoodItem std)
        {
            ApplyDura80(item);
            if (Random(10) != 0 || !HasExtraAttrFlag(std)) return;
            MaybeStore(item, 0, 6, 20, 40);
            MaybeStore(item, 1, 6, 20, 40);
            MaybeStore(item, 2, 6, 20, 30);
            MaybeStore(item, 3, 6, 20, 30);
            MaybeStore(item, 4, 6, 20, 30);
            var n = GetRandomRange(6, 10);
            if (Random(4) < 3)
                AddDura(item, (n + 1) * 1000);
        }

        // 0x761CC4 → 0x761D08
        private static void ApplyRing(TUserItem item, GoodItem std)
        {
            if (IsUnknownShape(std)) return;
            ApplyDura80(item);
            if (Random(9) != 0 || !HasExtraAttrFlag(std)) return;
            ApplyRingBody(item);
        }

        // 0x7625BC: dura first, then the 130-132 +0x08 skip, then Random(10)
        private static void ApplyArmRing(TUserItem item, GoodItem std)
        {
            ApplyDura80(item);
            if (IsUnknownShape(std)) return;
            if (Random(10) != 0 || !HasExtraAttrFlag(std)) return;
            ApplyRingBody(item);
        }

        private static void ApplyRingBody(TUserItem item)
        {
            MaybeStore(item, 0, 6, 20, 20);
            MaybeStore(item, 1, 6, 20, 20);
            MaybeStore(item, 2, 6, 20, 30);
            MaybeStore(item, 3, 6, 20, 30);
            MaybeStore(item, 4, 6, 20, 30);
            var n = GetRandomRange(6, 12);
            if (Random(4) < 3)
                AddDura(item, (n + 1) * 1000);
        }

        // GRR always runs; Random(gate) always runs; store n+1 only on 0.
        private static void MaybeStore(TUserItem item, int slot, int count, int rate, int gate)
        {
            var n = GetRandomRange(count, rate);
            if (Random(gate) == 0)
                item.btValue[slot] = (byte)(n + 1);
        }

        private static void AddDura(TUserItem item, int add)
        {
            item.DuraMax = (ushort)Math.Min(DuraCap, item.DuraMax + add);
            item.Dura = (ushort)Math.Min(DuraCap, item.Dura + add);
        }

        // sub_4C707C: nCount<=0 → 0 draws; nRate is never tested.
        private static int GetRandomRange(int nCount, int nRate)
        {
            var result = 0;
            for (var i = 0; i < nCount; i++)
            {
                if (Random(nRate) == 0) result++;
            }
            return result;
        }

        private static int Random(int bound) => M2Share.RandomNumber.Random(bound);
    }
}

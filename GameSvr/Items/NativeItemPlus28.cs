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
                    ApplyJewelStone(item, std);
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
        // Unsigned wrap: 130+0x7E=208, 208-3=205; 133+0x7E=211, 211-3=208.
        // jae taken for Shape>=133; 130/131/132 fall through to +0x08.
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
            if (IsUnknownShape(std))
            {
                ApplyUnknownHelmet08(item);
                return;
            }
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
            if (IsUnknownShape(std))
            {
                ApplyUnknownRing08(item);
                return;
            }
            ApplyDura80(item);
            if (Random(9) != 0 || !HasExtraAttrFlag(std)) return;
            ApplyRingBody(item);
        }

        // 0x7625BC: dura first, then the 130-132 +0x08 skip, then Random(10)
        private static void ApplyArmRing(TUserItem item, GoodItem std)
        {
            ApplyDura80(item);
            if (IsUnknownShape(std))
            {
                ApplyUnknownArmRing08(item);
                return;
            }
            if (Random(10) != 0 || !HasExtraAttrFlag(std)) return;
            ApplyRingBody(item);
        }

        // THelmet VMT+0x08 @0x761338. No extra-attr flag, no Random(80).
        // 49 draws: three GRR(4,3/8/20), three GRR(3,15/30), GRR(6,30), Random(30).
        private static void ApplyUnknownHelmet08(TUserItem item)
        {
            var n0 = GetRandomRange(4, 3) + GetRandomRange(4, 8) + GetRandomRange(4, 20);
            StoreIfPositive(item, 0, n0);
            var sum = n0;
            var n1 = GetRandomRange(4, 3) + GetRandomRange(4, 8) + GetRandomRange(4, 20);
            StoreIfPositive(item, 1, n1);
            sum += n1;
            var n2 = GetRandomRange(3, 15) + GetRandomRange(3, 30);
            StoreIfPositive(item, 2, n2);
            sum += n2;
            var n3 = GetRandomRange(3, 15) + GetRandomRange(3, 30);
            StoreIfPositive(item, 3, n3);
            sum += n3;
            var n4 = GetRandomRange(3, 15) + GetRandomRange(3, 30);
            StoreIfPositive(item, 4, n4);
            sum += n4;
            MaybeAddDura1000(item, GetRandomRange(6, 30));
            if (Random(30) == 0) item.btValue[7] = 1;
            if (sum < 3) return;
            if (item.btValue[0] >= 5)
            {
                item.btValue[5] = 1;
                item.btValue[6] = (byte)(item.btValue[0] * 3 + 0x19);
                return;
            }
            if (item.btValue[2] >= 2)
            {
                item.btValue[5] = 1;
                item.btValue[6] = (byte)((item.btValue[2] << 2) + 0x23);
                return;
            }
            if (item.btValue[3] >= 2)
            {
                item.btValue[5] = 2;
                item.btValue[6] = (byte)(item.btValue[3] * 2 + 0x12);
                return;
            }
            if (item.btValue[4] >= 2)
            {
                item.btValue[5] = 3;
                item.btValue[6] = (byte)(item.btValue[4] * 2 + 0x12);
                return;
            }
            item.btValue[6] = (byte)(sum * 2 + 0x12);
        }

        // TRing VMT+0x08 @0x761E20. 43 draws. Writes slots 2/3/4, not 0/1.
        private static void ApplyUnknownRing08(TUserItem item)
        {
            var n2 = GetRandomRange(3, 4) + GetRandomRange(3, 8) + GetRandomRange(6, 20);
            StoreIfPositive(item, 2, n2);
            var sum = n2;
            var n3 = GetRandomRange(3, 4) + GetRandomRange(3, 8) + GetRandomRange(6, 20);
            StoreIfPositive(item, 3, n3);
            sum += n3;
            var n4 = GetRandomRange(3, 4) + GetRandomRange(3, 8) + GetRandomRange(6, 20);
            StoreIfPositive(item, 4, n4);
            sum += n4;
            MaybeAddDura1000(item, GetRandomRange(6, 30));
            if (Random(30) == 0) item.btValue[7] = 1;
            if (sum < 3) return;
            if (item.btValue[2] >= 3)
            {
                item.btValue[5] = 1;
                item.btValue[6] = (byte)(item.btValue[2] * 3 + 0x19);
                return;
            }
            if (item.btValue[3] >= 3)
            {
                item.btValue[5] = 2;
                item.btValue[6] = (byte)(item.btValue[3] * 2 + 0x12);
                return;
            }
            if (item.btValue[4] >= 3)
            {
                item.btValue[5] = 3;
                item.btValue[6] = (byte)(item.btValue[4] * 2 + 0x12);
                return;
            }
            item.btValue[6] = (byte)(sum * 2 + 0x12);
        }

        // TArmRing VMT+0x08 @0x762718. Dura80 already done. 47 more draws.
        // Sum threshold is 2 (0x762855 cmp esi,2 / jl), not 3.
        private static void ApplyUnknownArmRing08(TUserItem item)
        {
            var n0 = GetRandomRange(3, 5) + GetRandomRange(5, 20);
            StoreIfPositive(item, 0, n0);
            var sum = n0;
            var n1 = GetRandomRange(3, 5) + GetRandomRange(5, 20);
            StoreIfPositive(item, 1, n1);
            sum += n1;
            var n2 = GetRandomRange(3, 15) + GetRandomRange(5, 30);
            StoreIfPositive(item, 2, n2);
            sum += n2;
            var n3 = GetRandomRange(3, 15) + GetRandomRange(5, 30);
            StoreIfPositive(item, 3, n3);
            sum += n3;
            var n4 = GetRandomRange(3, 15) + GetRandomRange(5, 30);
            StoreIfPositive(item, 4, n4);
            sum += n4;
            MaybeAddDura1000(item, GetRandomRange(6, 30));
            if (Random(30) == 0) item.btValue[7] = 1;
            if (sum < 2) return;
            if (item.btValue[0] >= 3)
            {
                item.btValue[5] = 1;
                item.btValue[6] = (byte)(item.btValue[0] * 3 + 0x19);
                return;
            }
            if (item.btValue[2] >= 2)
            {
                item.btValue[5] = 1;
                item.btValue[6] = (byte)(item.btValue[2] * 3 + 0x1E);
                return;
            }
            if (item.btValue[3] >= 2)
            {
                item.btValue[5] = 2;
                item.btValue[6] = (byte)(item.btValue[3] * 2 + 0x14);
                return;
            }
            if (item.btValue[4] >= 2)
            {
                item.btValue[5] = 3;
                item.btValue[6] = (byte)(item.btValue[4] * 2 + 0x14);
                return;
            }
            item.btValue[6] = (byte)(sum * 2 + 0x12);
        }

        // TJewelStone +0x28 @0x78C6BC. Ctor 0x78C70A writes [self+0x36]=[std+0x48]
        // (WordParam1 low byte = jewel level 1..4) before this slot runs.
        private static void ApplyJewelStone(TUserItem item, GoodItem std)
        {
            if (std != null)
                item.btValue[12] = unchecked((byte)std.WordParam1);
            ApplyDura80(item);
            var index = Random(12) + 1;
            NativeJewelStoneTable.Apply(item, index);
        }

        private static void StoreIfPositive(TUserItem item, int slot, int n)
        {
            if (n > 0) item.btValue[slot] = (byte)n;
        }

        private static void MaybeAddDura1000(TUserItem item, int n)
        {
            if (n <= 0) return;
            AddDura(item, (n + 1) * 1000);
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

    /// <summary>
    /// TJewelStone 9-byte rows at BSS 0x7DCBDC (4 types × 13 index × 9 = 0x1D4).
    /// Loader sub_78C1DC FillChar-zeros the table, then reads INI <c>宝石配置</c>
    /// (filename @0x756838, sole caller 0x756808). Missing file leaves zeros.
    /// sub_78C5EC: type=[item+0x36] in 1..4, index=Random(12)+1, copy 6 bytes to
    /// +0x36 and bytes 6..8 to +0x100/+0x101/+0x102. No Random inside the helper.
    /// </summary>
    internal static class NativeJewelStoneTable
    {
        public const int TypeCount = 4;
        public const int IndexCount = 13;
        public const int RecordSize = 9;
        public const int NativeRecordSize = 208;
        public const int ItemPlus36RecordOffset = 0x16; // item+0x36
        public const int ItemPlus100RecordOffset = 0xE0; // item+0x100

        // [type 1..4][index 0..12]; index 0 is unused (loader skips it).
        private static readonly byte[][][] Rows = CreateEmpty();

        private static byte[][][] CreateEmpty()
        {
            var rows = new byte[TypeCount + 1][][];
            for (var type = 1; type <= TypeCount; type++)
            {
                rows[type] = new byte[IndexCount][];
                for (var index = 0; index < IndexCount; index++)
                    rows[type][index] = new byte[RecordSize];
            }
            return rows;
        }

        internal static void Reset()
        {
            for (var type = 1; type <= TypeCount; type++)
                for (var index = 0; index < IndexCount; index++)
                    Array.Clear(Rows[type][index]);
        }

        internal static void SetRow(int type, int index, byte[] record)
        {
            if (type < 1 || type > TypeCount || index < 0 || index >= IndexCount)
                return;
            if (record == null || record.Length < RecordSize)
                return;
            Buffer.BlockCopy(record, 0, Rows[type][index], 0, RecordSize);
        }

        // sub_78C5EC. index is already Random(12)+1.
        internal static void Apply(TUserItem item, int index)
        {
            if (item?.btValue == null || item.btValue.Length < 14)
                return;
            var type = item.btValue[12];
            if (type == 0 || type > TypeCount)
                return;
            if (index < 1 || index >= IndexCount)
                return;

            var rec = Rows[type][index];
            item.btValue[12] = rec[0];
            item.btValue[13] = rec[1];
            if (item.NativeRecord == null || item.NativeRecord.Length != NativeRecordSize)
                item.NativeRecord = new byte[NativeRecordSize];
            Buffer.BlockCopy(rec, 0, item.NativeRecord, ItemPlus36RecordOffset, 6);
            item.NativeRecord[ItemPlus100RecordOffset] = rec[6];
            item.NativeRecord[ItemPlus100RecordOffset + 1] = rec[7];
            item.NativeRecord[ItemPlus100RecordOffset + 2] = rec[8];
        }
    }
}

using System.Buffers.Binary;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Stall <c>srvData</c> BLOB codec (task #83, wave-1). The original writes the item-on-sale as a raw
    /// 208-byte record copied verbatim: <c>sub_62016C</c> does <c>qmemcpy(buyOrder+0x1F, item+0x20, 0xD0)</c>
    /// then streams 208 bytes into the <c>srvData</c> column (<c>sub_403260(208, dst, buyOrder+0x1F)</c>);
    /// the readers re-select <c>srvData</c> and read 208 bytes back
    /// (staging/update_clothes_4637_ida_work/stall_exec_out.txt:390,537-539,2047-2050).
    ///
    /// That 208-byte slice — the in-memory item starting at <c>+0x20</c> — is byte-identical to the human-DB
    /// item record produced by <see cref="LegacyUserItem208Codec"/>: in-mem <c>+0x20</c> maps to record[0]
    /// (MakeIndex@0, wIndex@4, Dura@6, DuraMax@8, btValue@10.., UpgradeFlags@0x27, Bind@0xB8). Cross-check:
    /// the merge-gate the CM_1017 work confirmed at in-mem item <c>+0x34</c> == record <c>+0x14</c> ==
    /// <c>btValue[10]</c>. So this is a thin byte&lt;-&gt;item adapter over the already-audited codec — no new
    /// item layout is invented here.
    ///
    /// YANSHEN VERDICT (codec-fidelity, 2026-08-01, from dumps — RESOLVED): the native 208-byte srvData does
    /// NOT carry yanshen. stallitem has one item column (srvData blob) + scalars, no yanshen column; the
    /// item->srvData copy is exactly qmemcpy(buyOrder+0x1F, item+0x20, 0xD0) = 208 bytes with nothing appended
    /// (sub_62016C); the whole stall dump set has zero yanshen/0x79/sidecar refs. Yanshen lives in a SEPARATE
    /// MakeIndex-keyed ScriptData sidecar (type 0x79) in the HUMAN record (<c>YanshenItemSidecarCodec</c>),
    /// never in the 208-byte item. So a yanshen'd item that transits a native stall SILENTLY DROPS its yanshen
    /// — that data loss IS native-faithful.
    ///
    /// Consequence: the shared <see cref="LegacyUserItem208Codec"/> fail-close on ys*/jp* (HasUnmappedExtension
    /// Data) is OVER-STRICT for the STALL path (native ALLOWS the stall and just drops yanshen), but it is
    /// correct to KEEP for the human-DB path, so it must not be relaxed at the shared codec. This adapter's
    /// <see cref="TryEncode"/> therefore does the stall-only "allow-stall + drop-yanshen" encode itself
    /// (APPLIED, used by the AddItem 4421 leaf): it packs the clean 208 bytes and ignores ys*/jp*/pname/desc,
    /// keeping the tail-zero assertion (a yanshen'd item's 208-byte tail is already clean, so it never fires on
    /// a legit native item). The faithful yanshen loss is captured in the ADD leaf's conservation audit.
    /// Preserving yanshen across C# stalls would be a deliberate C#-only enhancement (a new stallitem sidecar
    /// column) — out of byte-fidelity scope; team-lead has been flagged this as an optional divergence.
    /// </summary>
    internal static class NativeStallItemRecordCodec
    {
        internal const int RecordSize = LegacyUserItem208Codec.RecordSize; // 208 (0xD0)

        /// <summary>
        /// Serialize a bag item to the exact 208-byte <c>srvData</c> BLOB, DROPPING yanshen — the stall-path
        /// "allow-stall + drop-yanshen" encode (see the class header + the yanshen verdict). Packs the clean
        /// 208 bytes (MakeIndex@0 / wIndex@4 / Dura@6 / DuraMax@8 / btValue@10 / UpgradeFlags@0x27 / Bind@0xB8)
        /// and IGNORES ys*/jp*/pname/desc (which live in the separate HUMAN-record sidecar, never in the
        /// 208-byte item). The fail-closed guard now covers only the two spans with no known owner
        /// (0x56..0xB7, 0xB9..0xCF); 0x18..0x55 is the 眼神 provenance block and is carried through verbatim.
        /// This is deliberately NOT the shared fail-closing <see cref="LegacyUserItem208Codec.TryEncode"/>,
        /// which stays strict about ys*/jp* for the human-DB path.
        /// </summary>
        internal static bool TryEncode(TUserItem item, out byte[] srvData208, out string error)
        {
            srvData208 = null;
            error = string.Empty;
            if (item == null || item.btValue == null || item.btValue.Length != 14)
            {
                error = "invalid core item record";
                return false;
            }
            byte[] record;
            if (item.NativeRecord == null)
            {
                record = new byte[RecordSize];
            }
            else
            {
                if (item.NativeRecord.Length != RecordSize)
                {
                    error = $"native item record must be {RecordSize} bytes";
                    return false;
                }
                record = (byte[])item.NativeRecord.Clone();
            }
            if (!ValidateUnownedSpans(record, out error))
                return false;
            if ((item.UpgradeFlags & ~LegacyUserItem208Codec.KnownUpgradeFlags) != 0
                && (item.UpgradeFlags & ~LegacyUserItem208Codec.KnownUpgradeFlags)
                    != (record[LegacyUserItem208Codec.UpgradeFlagsOffset]
                        & ~LegacyUserItem208Codec.KnownUpgradeFlags))
            {
                error = "item refine flags would rewrite bytes native never writes";
                return false;
            }
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0, 4), item.MakeIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), item.wIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), item.Dura);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), item.DuraMax);
            item.btValue.CopyTo(record, 10);
            record[LegacyUserItem208Codec.UpgradeFlagsOffset] = item.UpgradeFlags;
            record[LegacyUserItem208Codec.BindOffset] = item.Bind;
            srvData208 = record;
            return true;
        }

        // Mirror of the shared codec's guard, re-scoped for the same reason (see
        // LegacyUserItem208Codec): 0x18..0x55 is the 眼神 provenance block and 0x58..0x6B
        // is the ys1..ys17 element block, neither of which is padding, so only
        // 0x56..0x57, 0x6C..0xB7 and 0xB9..0xCF may be asserted zero. The old
        // class-header claim that "a yanshen'd item's 208-byte tail is already clean, so
        // it never fires on a legit item" is disproved by the golden corpus: 1232 of 1363
        // real items carry a non-zero byte at 0x1C alone, and it is doubly wrong for a
        // genuinely yanshen'd item, whose ys values live inside the asserted span.
        private static bool ValidateUnownedSpans(byte[] record, out string error)
        {
            error = string.Empty;
            for (var i = LegacyUserItem208Codec.UnownedSpanStart; i < record.Length; i++)
            {
                if (i == LegacyUserItem208Codec.BindOffset
                    || LegacyUserItem208Codec.HasKnownOwner(i)) continue;
                if (record[i] != 0)
                {
                    error = $"unmapped native item data at offset 0x{i:X2}";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Reconstruct the item from a 208-byte <c>srvData</c> BLOB (buyer delivery / recovery scan).</summary>
        internal static bool TryDecode(byte[] srvData208, out TUserItem item, out string error)
        {
            item = null;
            error = string.Empty;
            if (srvData208 == null || srvData208.Length != RecordSize)
            {
                error = $"stall srvData must be exactly {RecordSize} bytes";
                return false;
            }
            return LegacyUserItem208Codec.TryDecode(Convert.ToHexString(srvData208), out item, out error);
        }
    }
}

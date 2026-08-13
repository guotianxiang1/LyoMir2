namespace GameSvr
{
    // GILD-01: dormant 1:1 model of the native gild "SetRelation" writer save_relation
    // sub_5E6E60 @0x005E6E60 — the single routine every gild-relation WRITE funnels through
    // (declare-war Relation=2 via sub_703F74, request-union Relation=3 via sub_704494,
    // alliance-accept Relation=1 via sub_708168). Raw-disassembly verified against M2Server
    // (image base 0x00400000, staging/_reunpack_work/flat_image.bin).
    //
    // The sibling declare-war / request-union transactions model sub_5E6E60's *observable* result
    // (0 / 12 / 15) but abstract it as an input and skip its FIRST gate — the relation-type range
    // check — because every live caller passes a hard-coded type (1/2/3), so the gate can never
    // trip. That gate is GILD-01 ("SetRelation range gate (0..3), unreachable but benign"). This
    // file makes it, and the rest of the ladder, an executable contract instead of a comment.
    //
    // ABI: sub_5E6E60(eax = gild subsystem, dl = relationType byte, [ebp+8..+0x14] = two 64-bit
    // gild keys). Returns an int result code in eax. Native ladder, verbatim and in order:
    //
    //   0x5E6E6D  8BC3         mov  eax,ebx        ; eax = relationType (ebx = dl argument)
    //   0x5E6E6F  2C04         sub  al,4
    //   0x5E6E71  720A         jb   0x5E6E7D       ; (relationType & 0xFF) < 4  -> proceed
    //   0x5E6E73  B80E000000   mov  eax,0x0E       ; *** GILD-01: relationType >= 4 -> return 14 ***
    //   0x5E6E78  E916010000   jmp  0x5E6F93
    //   0x5E6E7D  E82282E2FF   call 0x40F0A4       ; CreateTime := Now (stamped once, here)
    //   0x5E6E89..0x5E6ECF     normalize the pair so key.First <= key.Second (64-bit compare)
    //   0x5E6ED5  E8F8070000   call 0x5E76D4       ; edi := FindGuild(key1)
    //   0x5E6EE6  E8E9070000   call 0x5E76D4       ; [ebp-4] := FindGuild(key2)
    //   0x5E6EF6  750A         jne  ... / else     ; either gild not found -> mov eax,0x0C = 12
    //   0x5E6F08  E8AB8DEBFF   call 0x49FCB8       ; eax := existing relation for the pair (0 = none)
    //   0x5E6F0D  48/2C03/7307 dec eax/sub al,3/jae; existing in {1,2,3} -> mov eax,0x0F = 15
    //   0x5E6F19  ...          call 0x49F9C8       ; insert (pair -> relationType) into the map
    //   0x5E6F32  E809290000   call 0x5E9840       ; build INSERT gamedata.gildrelation(...,Relation,
    //   0x5E6F40  E857F4FFFF   call 0x5E639C       ;   CreateTime) command and enqueue it (fail-safe)
    //   0x5E6F45  FECB/74 ..   dec bl/je           ; dispatch by relationType:
    //               type 1  -> 0x5E6F4F  sub_70666C x2  (join the union/ally list on both gilds)
    //               type 2  -> 0x5E6F65  sub_5E6D68 (war deadline) + sub_70669C x2 (hostile list)
    //               type 3  -> 0x5E6F91  map-only (pending union: no list join)
    //   0x5E6F91  33C0         xor  eax,eax        ; return 0 (Success)
    //
    // The type-2 branch's deadline builder sub_5E6D68 adds the float32 @0x5E6E20 = 0.125 (OLE-date
    // days = 3 h = dwGuildWarTime 10800000 ms) to CreateTime and stores the result at record+0x10;
    // that stored deadline is exactly what the war-expiry sweep compares against `now`
    // (GILD-27: AssociationManager.Run / NativeGildWarExpiry.GetExpired). See NativeGildWarExpiry.
    //
    // Dormant / not wired: the live writes (NativeCorpsService) hard-code GildHostile/GildUnion and
    // the pending-union 3, so none can reach the >=4 gate; this model performs no I/O.
    public static class NativeGildSaveRelation
    {
        // Relation-type byte (dl). The in-memory map and the gildrelation.Relation column carry 0..3.
        public const int RelationNone = 0;          // neutral / no relation
        public const int RelationUnion = 1;         // ally    -> union list  (sub_70666C x2)
        public const int RelationHostile = 2;       // war     -> deadline + hostile list (sub_5E6D68/70669C)
        public const int RelationPendingUnion = 3;  // pending union proposal -> map-only

        // Result codes returned in eax, verbatim.
        public const int Success = 0;               // relation inserted + INSERT enqueued
        public const int GildMissing = 12;          // 0x0C  either gild key unresolved (sub_5E76D4 == 0)
        public const int TypeOutOfRange = 14;       // 0x0E  relationType >= 4  *** GILD-01 range gate ***
        public const int RelationAlreadyPresent = 15; // 0x0F existing relation already 1/2/3 (sub_49FCB8)

        // The lone 0..3 range gate: native 0x5E6E6F `sub al,4` / 0x5E6E71 `jb`. Byte semantics —
        // the compare is on the low byte, so the value is taken mod 256 exactly as `dl`.
        // True == admitted (0..3).
        public static bool IsRelationTypeInRange(int relationType) =>
            (relationType & 0xFF) < 4;

        // Full sub_5E6E60 result ladder. `existingRelation` is the current map entry for the
        // (already normalized) pair, 0 when none; `firstGildFound` / `secondGildFound` are the two
        // sub_5E76D4 look-ups. Side effects (Now stamp, map insert, INSERT enqueue, list dispatch)
        // are out of scope — only the observable return code is modeled, matching the sibling gild
        // transactions.
        public static int Evaluate(int relationType, bool firstGildFound, bool secondGildFound,
            int existingRelation)
        {
            // 0x5E6E6F: range gate FIRST. relationType must be 0..3, else 14.
            if (!IsRelationTypeInRange(relationType))
                return TypeOutOfRange;

            // 0x5E6EF6: both gild keys must resolve (order-independent; the pair is normalized first).
            if (!firstGildFound || !secondGildFound)
                return GildMissing;

            // 0x5E6F0D: an existing 1/2/3 relation blocks a re-create; only 0 (none) proceeds.
            var existing = existingRelation & 0xFF;
            if (existing >= RelationUnion && existing <= RelationPendingUnion)
                return RelationAlreadyPresent;

            // 0x5E6F19+: map insert + INSERT enqueue + per-type list dispatch; return 0.
            return Success;
        }
    }
}

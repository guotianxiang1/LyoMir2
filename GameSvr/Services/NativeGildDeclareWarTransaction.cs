namespace GameSvr;

// Dormant model of Gild declare-war write ops 4579 (declare_war_id) and 4585 (declare_war_name).
// Hex-Rays + raw disassembly verified (image base 0x00400000).
//
// Handlers:
//   4579 sub_6F68F0 @0x006F68F0:
//     esi = 36 (0x24);
//     if ( player[+0x15C] >= 30000 (0x7530) )                       // gold gate
//         esi = sub_6ADA3C(caller).[vtbl+0x6C](targetKeyLo, targetKeyHi, callerKeyLo, callerKeyHi);
//     if ( !esi ) sub_6C30BC(player, 30000);                         // deduct 30000 gold on success
//     player.[vtbl+0x250]( SM=0x11E3=4579, wParam=esi, 0,0,0 );      // SendDefMessage
//   4585 sub_6F6958 @0x006F6958 (name variant, SEH-wrapped):
//     ebx = 12 (0x0C);
//     if ( sub_5E76F0() )                                            // name-resolution / gild-subsystem guard
//     {
//         ebx = 36 (0x24);
//         if ( player[+0x15C] >= 30000 )                            // same gold gate
//             ebx = sub_6ADA3C(caller).[vtbl+0x6C]( current_owner_gild(nameObj), callerKey );
//     }
//     if ( !ebx ) sub_6C30BC(player, 30000);                         // same deduction
//     player.[vtbl+0x250]( SM=0x11E3=4579, wParam=ebx, 0,0,0 );      // NOTE: 4585 also replies with 4579
//
// Ordering that matters: the gold gate (36) is evaluated BEFORE the role dispatch, so a non-owner
// with < 30000 gold receives 36, not 555. For 4585 the sub_5E76F0 guard (12) precedes the gold gate.
//
// Role dispatch sub_6ADA3C @0x006ADA3C -> {no_corps | member | corps | gild_member | gild_vice |
// gild_owner} strategy object. Slot +0x6C:
//   gild_owner  (VMT 0x007018EC) +0x6C = sub_703F74 @0x00703F74  -> the real declare-war ladder.
//   every other role (+0x6C = sub_701BD8 @0x00701BD8)            -> return 555 (stub).
//
// gild_owner strategy sub_703F74 (arg_0/arg_4 = target gild key, arg_8/arg_C = caller key):
//   if ( !(arg_8 | arg_C) )                       edi = 555 (0x22B)  // defensive: caller key absent
//   else if ( !sub_5EC030(callerKey) )            edi = 5            // player object not resolved
//   else if ( !player[+4] )                       edi = 12 (0x0C)    // caller has no gild
//   else if ( !sub_5E76D4(targetKey) )            edi = 25 (0x19)    // target gild not found
//   else if ( targetGild == callerGild )          edi = 19 (0x13)    // cannot declare war on self
//   else switch ( sub_5E7890(targetKey, callerGildKey) )             // current relation state
//         case 1:                                 edi = 32 (0x20)
//         case 2:                                 edi = 15 (0x0F)
//         default:                                                    // state 0 or >= 3
//             edi = sub_5E6E60(callerGildKey, /*Relation type dl=*/2, targetGildKey);  // save_relation
//             if ( !edi ) { /* INSERT enqueued inside the helper + refresh/broadcast/social writes */ }
//   return edi;
//
// save_relation sub_5E6E60 @0x005E6E60 (called with relation type = 2):
//   type >= 4 -> 14; either gild missing -> 12; sub_49FCB8()-1 < 3 -> 15;
//   else build record + sub_5E9840 command + sub_5E639C enqueue INSERT GildRelation(Relation=2), return 0.
//   For type 2 the reachable outputs are 0 (INSERT enqueued) or 15; 12/14 are defensive. Its result is
//   genuinely polymorphic, so it is abstracted here as RelationHelperResult (an input), matching how the
//   sibling dormant models inject sub-results. Success (0) is what makes the handler deduct 30000 gold.
//
// On success (final code 0) the INSERT is enqueued inside sub_5E6E60 and THEN the handler deducts 30000
// gold (sub_6C30BC @0x006C30BC, gold field player[+0x15C]). A later async SQL failure only logs
// "[SQL Failed]"; it does NOT refund the gold or roll back the in-memory relation.
//
// Dormant / fail-closed in C#: the live 45xx handlers still return 1000 pending the Gild store
// (GildRelation persistence + DB command FIFO). This models the exact result ladder, role dispatch and
// the gold-deduction outcome only; it performs no writes and is not wired.
//
// NativeGildRole { NoCorps, Member, Corps, GildMember, GildVice, GildOwner } is declared in
// NativeGildViceTransaction.cs and reused here.

public enum NativeGildDeclareWarOp
{
    DeclareWarId = 4579,   // sub_6F68F0
    DeclareWarName = 4585, // sub_6F6958 (still replies with SM 4579)
}

public sealed class NativeGildDeclareWarContext
{
    /// <summary>Caller's resolved Gild role (sub_6ADA3C @0x006ADA3C).</summary>
    public NativeGildRole Role { get; init; }

    /// <summary>4585 only: sub_5E76F0 name/subsystem guard succeeded. Ignored by 4579; defaults true.</summary>
    public bool NameResolved { get; init; } = true;

    /// <summary>Gold gate: player[+0x15C] &gt;= 30000 (0x7530).</summary>
    public bool HasGold { get; init; }

    /// <summary>sub_703F74 guard: caller key present (arg_8|arg_C != 0). Defensive; defaults true.</summary>
    public bool CallerKeyPresent { get; init; } = true;

    /// <summary>Caller player object resolved (sub_5EC030 != 0). Defensive; defaults true.</summary>
    public bool PlayerResolved { get; init; } = true;

    /// <summary>Caller has a Gild (playerObject[+4] != 0).</summary>
    public bool HasGild { get; init; }

    /// <summary>Target Gild resolved (sub_5E76D4 != 0).</summary>
    public bool TargetGildFound { get; init; }

    /// <summary>Target Gild equals the caller's own Gild.</summary>
    public bool TargetIsSelf { get; init; }

    /// <summary>Relation-state byte from sub_5E7890 (1 -&gt; 32, 2 -&gt; 15, else -&gt; helper).</summary>
    public int RelationState { get; init; }

    /// <summary>
    /// save_relation sub_5E6E60 result for Relation type 2 (0 = INSERT GildRelation(Relation=2)
    /// enqueued -&gt; success). Polymorphic sub-result abstracted as an input.
    /// </summary>
    public int RelationHelperResult { get; init; }
}

public static class NativeGildDeclareWarTransaction
{
    public const int ReplySmId = 4579;           // 0x11E3 wParam SM ident for BOTH 4579 and 4585
    public const int VtblStrategy = 0x6C;        // strategy slot invoked by both handlers (108)
    public const int VtblSendDefMessage = 0x250; // player SendDefMessage
    public const int GoldCost = 30000;           // 0x7530 deducted on success (sub_6C30BC)
    public const int GoldThreshold = 30000;      // player[+0x15C] must be >= this to reach the strategy
    public const int RelationType = 2;           // sub_5E6E60 dl=2 -> INSERT GildRelation(Relation=2)

    public const int GoldInsufficient = 36;      // 0x24  gold gate
    public const int NameUnresolved = 12;        // 0x0C  4585 sub_5E76F0 == false
    public const int NoPermission = 555;         // 0x22B non-owner role, or sub_703F74 caller-key guard
    public const int NoPlayer = 5;               // sub_5EC030 == 0
    public const int NoGild = 12;                // 0x0C  playerObject[+4] == 0
    public const int TargetNotFound = 25;        // 0x19  sub_5E76D4 == 0
    public const int TargetIsSelfCode = 19;      // 0x13  target == own gild
    public const int RelationBusyState1 = 32;    // 0x20  sub_5E7890 == 1
    public const int RelationBusyState2 = 15;    // 0x0F  sub_5E7890 == 2
    // 0x0F from save_relation's own existence gate 0x5E6F0D `48 dec eax` /
    // `2C03 sub al,3` / `73 07 jae` -> 0x5E6F12 `mov eax,0x0F`. The ladder above
    // consumes 1 and 2, so the state this gate still catches is a pending union
    // proposal (3).
    public const int SaveRelationRelationExists = 15;
    public const int Success = 0;

    /// <summary>Raw result code that goes verbatim into SendDefMessage wParam (SM 4579 for both ops).</summary>
    public static int Evaluate(NativeGildDeclareWarOp op, NativeGildDeclareWarContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        // 4585 name path: the sub_5E76F0 guard runs before the gold gate; false -> 12.
        if (op == NativeGildDeclareWarOp.DeclareWarName && !context.NameResolved)
            return NameUnresolved;

        // Gold gate (both handlers) precedes role dispatch: insufficient gold -> 36, strategy not called.
        if (!context.HasGold)
            return GoldInsufficient;

        return Strategy(context);
    }

    /// <summary>
    /// True when the final result triggers the 30000 gold deduction (handler: if(!result) sub_6C30BC).
    /// The same success path also enqueues the INSERT GildRelation(Relation=2) inside sub_5E6E60.
    /// </summary>
    public static bool DeductsGold(int result) => result == Success;

    /// <summary>True when the success path enqueued the INSERT GildRelation(Relation=2) command.</summary>
    public static bool InsertsRelation(int result) => result == Success;

    // strategy[+0x6C]: only gild_owner reaches sub_703F74; every other role's slot is sub_701BD8 -> 555.
    private static int Strategy(NativeGildDeclareWarContext c)
    {
        if (c.Role != NativeGildRole.GildOwner)
            return NoPermission;
        return OwnerStrategy(c);
    }

    // gild_owner +0x6C = sub_703F74 @0x00703F74.
    private static int OwnerStrategy(NativeGildDeclareWarContext c)
    {
        if (!c.CallerKeyPresent) return NoPermission;      // 555 (arg_8|arg_C == 0)
        if (!c.PlayerResolved) return NoPlayer;            // 5
        if (!c.HasGild) return NoGild;                     // 12
        if (!c.TargetGildFound) return TargetNotFound;     // 25
        if (c.TargetIsSelf) return TargetIsSelfCode;       // 19
        if (c.RelationState == 1) return RelationBusyState1; // 32
        if (c.RelationState == 2) return RelationBusyState2; // 15
        // else (state 0 or >= 3): sub_5E6E60 save_relation with Relation=2; 0 -> INSERT + success.
        return c.RelationHelperResult;
    }
}

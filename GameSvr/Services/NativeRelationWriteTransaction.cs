namespace GameSvr.Services
{
    // Dormant model of the native Relation write ops 4433-4440 (friend / attention / blacklist add,
    // remove, colour). Hex-Rays + raw disassembly verified against M2Server (image base 0x00400000).
    // This captures the exact original result-code ladder so the live C# TPlayObject.NativeRelation
    // path can be checked against it; it performs no writes and is not wired.
    //
    // Dispatcher (idents <= 0x116F branch) routes each CM to its handler; every handler replies via
    // player.[vtbl+0x250] (SendDefMessage) with the negative wParam code below. Shared helpers:
    //   sub_652784  -> resolve the named target player (online lookup); 0 == not found / offline.
    //   sub_6FE734  -> does the target already occupy this relation list (adds) / is it present (dels).
    //   sub_6FE894  -> current relation-list count; the cap compared against is 200 (0xC8).
    //   sub_6F39B4(2, caller) -> queue a friend REQUEST (relation kind 2) to the target.
    //   sub_6FF748(255) -> apply the attention add with default colour 0xFF.
    //   sub_700080(colour) -> apply the attention colour change.
    //
    //  4433 add_friend    sub_6F4B94 @0x006F4B94  (order: target/self/exists/full)
    //        target offline/not found (sub_652784==0) -> -1
    //        target is the caller                     -> -2
    //        already a friend (sub_6FE734)            -> -3
    //        friend list full (sub_6FE894 >= 200)     -> -4
    //        else -> 0: sub_6F39B4(2) queues the request. NO reply is sent on success; the SM 4433 OK
    //        arrives later from the accept flow. Any NON-zero code is sent as SM 4434 (ADD_FRIEND_FAIL).
    //  4435 add_attention sub_6F4E58 @0x006F4E58  (order: name/target/self/full/exists)
    //  4436 add_blacklist sub_6F4FE8 @0x006F4FE8  (identical ladder)
    //        empty name (msg len == 0)                -> -1
    //        target offline/not found                 -> -2
    //        target is the caller                     -> -3
    //        list full (count >= 200, checked first)  -> -4
    //        already in the list (sub_6FE734)         -> -5
    //        else -> 0. 4435 then applies sub_6FF748(255) and replies SM 4435 with 0; 4436 replies SM
    //        4436 with 0 and then applies the add via the blacklist manager (a1[695].[+0x10]).
    //  4437 del_friend    sub_6F4C58 @0x006F4C58
    //        target is the caller                     -> -1
    //        not in the friend list (sub_6FE734==0)   -> -2
    //        else -> 0 (remove via friend manager a1[693].[+0x14]); reply SM 4437.
    //  4438 del_attention sub_6F4F18 @0x006F4F18
    //  4439 del_blacklist sub_6F50A4 @0x006F50A4
    //        not in the list (sub_6FE734==0)          -> -1
    //        else -> 0 (remove via a1[694]/a1[695].[+0x14]); reply SM 4438 / 4439.
    //  4440 update_attention_colour sub_6F4F78 @0x006F4F78
    //        not in the attention list (sub_6FE734==0)     -> -1
    //        colour change failed (sub_700080==0)          -> -2
    //        else -> 0; reply SM 4440.
    //
    // The reads 4430 query_friend (sub_6F5118), 4431 query_attention (sub_6F4FD4), 4432 query_blacklist
    // (sub_6F5104) return list data, not a code, and are not modelled here.
    //
    // The manager add/remove tail is a side-effect (enqueue + publish) and does not change the wParam
    // code, so it is not abstracted as an input; the only genuinely polymorphic reply gate (4435's
    // sub_6FF748 success byte) is documented above and surfaced via RepliesOnSuccess.

    public enum NativeRelationWriteOp
    {
        AddFriend = 4433,            // sub_6F4B94  (error reply SM 4434, success: no reply)
        AddAttention = 4435,         // sub_6F4E58
        AddBlacklist = 4436,         // sub_6F4FE8
        DelFriend = 4437,            // sub_6F4C58
        DelAttention = 4438,         // sub_6F4F18
        DelBlacklist = 4439,         // sub_6F50A4
        UpdateAttentionColor = 4440, // sub_6F4F78
    }

    public sealed class NativeRelationWriteContext
    {
        /// <summary>Adds 4435/4436 only: received name is empty (msg len == 0) -&gt; -1.</summary>
        public bool NameEmpty { get; init; }

        /// <summary>Target resolved as an online player (sub_652784 != 0). Adds require it.</summary>
        public bool TargetOnline { get; init; } = true;

        /// <summary>Target is the caller (a1 == sub_652784 result).</summary>
        public bool IsSelf { get; init; }

        /// <summary>sub_6FE734: target already occupies the list (adds) / is present in it (dels/update).</summary>
        public bool RelationExists { get; init; }

        /// <summary>Adds: relation list is full, sub_6FE894 count &gt;= 200.</summary>
        public bool ListFull { get; init; }

        /// <summary>4440 only: sub_700080 applied the colour change. Defaults true.</summary>
        public bool ColorApplied { get; init; } = true;
    }

    public static class NativeRelationWriteTransaction
    {
        public const int ListCap = 200;               // 0xC8, sub_6FE894 comparison
        public const int FriendRequestKind = 2;       // sub_6F39B4(2, caller)
        public const int DefaultAttentionColor = 255; // 0xFF, sub_6FF748(255)
        public const int VtblSendDefMessage = 0x250;  // player reply slot (offset 592)

        public const int AddFriendFailSm = 4434;      // SM_ADD_RELATION_FRIEND_FAIL (error reply for 4433)
        public const int AddFriendOkSm = 4433;        // SM_ADD_RELATION_FRIEND_OK (sent by the accept flow, not the handler)

        public const int Success = 0;

        // negative validation codes (verbatim wParam)
        public const int FriendTargetOffline = -1;
        public const int FriendSelf = -2;
        public const int FriendAlready = -3;
        public const int FriendListFull = -4;

        public const int AddEmptyName = -1;
        public const int AddTargetOffline = -2;
        public const int AddSelf = -3;
        public const int AddListFull = -4;
        public const int AddAlready = -5;

        public const int DelFriendSelf = -1;
        public const int DelFriendNotListed = -2;
        public const int DelNotListed = -1;   // attention / blacklist delete

        public const int ColorNotListed = -1;
        public const int ColorApplyFailed = -2;

        /// <summary>Raw result code that goes verbatim into the SendDefMessage wParam.</summary>
        public static int Evaluate(NativeRelationWriteOp op, NativeRelationWriteContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return op switch
            {
                NativeRelationWriteOp.AddFriend => AddFriend(context),
                NativeRelationWriteOp.AddAttention => AddDirected(context),
                NativeRelationWriteOp.AddBlacklist => AddDirected(context),
                NativeRelationWriteOp.DelFriend => DelFriend(context),
                NativeRelationWriteOp.DelAttention => DelDirected(context),
                NativeRelationWriteOp.DelBlacklist => DelDirected(context),
                NativeRelationWriteOp.UpdateAttentionColor => UpdateColor(context),
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };
        }

        /// <summary>
        /// True if the handler sends a reply on the success (0) path. add_friend does NOT (it queues a
        /// kind-2 request instead); every other op replies with its own SM number on success too.
        /// </summary>
        public static bool RepliesOnSuccess(NativeRelationWriteOp op) =>
            op != NativeRelationWriteOp.AddFriend;

        /// <summary>SM ident used for this op's reply (4434 for the add_friend failure path, else the op).</summary>
        public static int ReplySmId(NativeRelationWriteOp op) =>
            op == NativeRelationWriteOp.AddFriend ? AddFriendFailSm : (int)op;

        // 4433 sub_6F4B94: target -> self -> already-friend -> full -> queue request.
        private static int AddFriend(NativeRelationWriteContext c)
        {
            if (!c.TargetOnline) return FriendTargetOffline; // -1
            if (c.IsSelf) return FriendSelf;                 // -2
            if (c.RelationExists) return FriendAlready;      // -3
            if (c.ListFull) return FriendListFull;           // -4
            return Success;                                  // sub_6F39B4(2) queues the request; no reply
        }

        // 4435 sub_6F4E58 / 4436 sub_6F4FE8: name -> target -> self -> full -> already.
        private static int AddDirected(NativeRelationWriteContext c)
        {
            if (c.NameEmpty) return AddEmptyName;      // -1
            if (!c.TargetOnline) return AddTargetOffline; // -2
            if (c.IsSelf) return AddSelf;              // -3
            if (c.ListFull) return AddListFull;        // -4 (checked before "already")
            if (c.RelationExists) return AddAlready;   // -5
            return Success;
        }

        // 4437 sub_6F4C58: self -> not-listed -> remove.
        private static int DelFriend(NativeRelationWriteContext c)
        {
            if (c.IsSelf) return DelFriendSelf;              // -1
            if (!c.RelationExists) return DelFriendNotListed; // -2
            return Success;
        }

        // 4438 sub_6F4F18 / 4439 sub_6F50A4: not-listed -> remove.
        private static int DelDirected(NativeRelationWriteContext c)
        {
            if (!c.RelationExists) return DelNotListed; // -1
            return Success;
        }

        // 4440 sub_6F4F78: not-listed -> colour-apply-failed -> success.
        private static int UpdateColor(NativeRelationWriteContext c)
        {
            if (!c.RelationExists) return ColorNotListed;   // -1
            if (!c.ColorApplied) return ColorApplyFailed;   // -2
            return Success;
        }
    }
}

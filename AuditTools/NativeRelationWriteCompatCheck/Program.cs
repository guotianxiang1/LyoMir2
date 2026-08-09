using GameSvr.Services;

// Contract check for the dormant native Relation write model (4433-4440), locked against sub_6F4B94
// (add_friend), sub_6F4E58 (add_attention), sub_6F4FE8 (add_blacklist), sub_6F4C58 (del_friend),
// sub_6F4F18 (del_attention), sub_6F50A4 (del_blacklist) and sub_6F4F78 (update_attention_colour).

try
{
    VerifyConstants();
    VerifyAddFriend();
    VerifyAddDirected();
    VerifyDelFriend();
    VerifyDelDirected();
    VerifyUpdateColor();
    VerifyReplySemantics();

    Console.WriteLine(
        "PASS NativeRelationWriteCompatCheck ops=4433/4435/4436/4437/4438/4439/4440 " +
        "add_friend(-1/-2/-3/-4/0,fail-sm=4434,no-success-reply) add_directed(-1/-2/-3/-4/-5/0) " +
        "del_friend(-1/-2/0) del_directed(-1/0) color(-1/-2/0) cap=200 dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeRelationWriteCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(NativeRelationWriteOp op, NativeRelationWriteContext c) =>
    NativeRelationWriteTransaction.Evaluate(op, c);

static void VerifyConstants()
{
    Assert(NativeRelationWriteTransaction.ListCap == 200, "cap 200");
    Assert(NativeRelationWriteTransaction.FriendRequestKind == 2, "friend kind 2");
    Assert(NativeRelationWriteTransaction.DefaultAttentionColor == 255, "colour 255");
    Assert(NativeRelationWriteTransaction.VtblSendDefMessage == 0x250, "reply 0x250");
    Assert(NativeRelationWriteTransaction.AddFriendFailSm == 4434, "friend fail sm 4434");
    Assert(NativeRelationWriteTransaction.AddFriendOkSm == 4433, "friend ok sm 4433");
    Assert((int)NativeRelationWriteOp.AddFriend == 4433 && (int)NativeRelationWriteOp.AddAttention == 4435
        && (int)NativeRelationWriteOp.AddBlacklist == 4436 && (int)NativeRelationWriteOp.DelFriend == 4437
        && (int)NativeRelationWriteOp.DelAttention == 4438 && (int)NativeRelationWriteOp.DelBlacklist == 4439
        && (int)NativeRelationWriteOp.UpdateAttentionColor == 4440, "op idents");
}

static void VerifyAddFriend()
{
    // Order: target-offline -1, self -2, already -3, full -4, else 0 (request queued, no reply).
    Assert(Eval(NativeRelationWriteOp.AddFriend,
        new NativeRelationWriteContext { TargetOnline = false }) == -1, "friend offline -> -1");
    Assert(Eval(NativeRelationWriteOp.AddFriend,
        new NativeRelationWriteContext { TargetOnline = true, IsSelf = true }) == -2, "friend self -> -2");
    Assert(Eval(NativeRelationWriteOp.AddFriend,
        new NativeRelationWriteContext { TargetOnline = true, RelationExists = true }) == -3, "friend already -> -3");
    // exists is checked before full: already + full still yields -3.
    Assert(Eval(NativeRelationWriteOp.AddFriend,
        new NativeRelationWriteContext { TargetOnline = true, RelationExists = true, ListFull = true }) == -3,
        "friend already-before-full -> -3");
    Assert(Eval(NativeRelationWriteOp.AddFriend,
        new NativeRelationWriteContext { TargetOnline = true, ListFull = true }) == -4, "friend full -> -4");
    Assert(Eval(NativeRelationWriteOp.AddFriend,
        new NativeRelationWriteContext { TargetOnline = true }) == 0, "friend ok -> 0");
}

static void VerifyAddDirected()
{
    // 4435 attention and 4436 blacklist share the ladder: name -1, target -2, self -3, full -4, already -5.
    foreach (var op in new[] { NativeRelationWriteOp.AddAttention, NativeRelationWriteOp.AddBlacklist })
    {
        Assert(Eval(op, new NativeRelationWriteContext { NameEmpty = true }) == -1, $"{op} empty -> -1");
        Assert(Eval(op, new NativeRelationWriteContext { TargetOnline = false }) == -2, $"{op} offline -> -2");
        Assert(Eval(op, new NativeRelationWriteContext { TargetOnline = true, IsSelf = true }) == -3, $"{op} self -> -3");
        // full is checked before already: full + already yields -4.
        Assert(Eval(op, new NativeRelationWriteContext { TargetOnline = true, ListFull = true, RelationExists = true }) == -4,
            $"{op} full-before-already -> -4");
        Assert(Eval(op, new NativeRelationWriteContext { TargetOnline = true, RelationExists = true }) == -5,
            $"{op} already -> -5");
        Assert(Eval(op, new NativeRelationWriteContext { TargetOnline = true }) == 0, $"{op} ok -> 0");
    }
}

static void VerifyDelFriend()
{
    // self -1, not-listed -2, else 0.
    Assert(Eval(NativeRelationWriteOp.DelFriend,
        new NativeRelationWriteContext { IsSelf = true }) == -1, "del friend self -> -1");
    Assert(Eval(NativeRelationWriteOp.DelFriend,
        new NativeRelationWriteContext { RelationExists = false }) == -2, "del friend not-listed -> -2");
    Assert(Eval(NativeRelationWriteOp.DelFriend,
        new NativeRelationWriteContext { RelationExists = true }) == 0, "del friend ok -> 0");
}

static void VerifyDelDirected()
{
    // 4438 attention and 4439 blacklist: not-listed -1, else 0.
    foreach (var op in new[] { NativeRelationWriteOp.DelAttention, NativeRelationWriteOp.DelBlacklist })
    {
        Assert(Eval(op, new NativeRelationWriteContext { RelationExists = false }) == -1, $"{op} not-listed -> -1");
        Assert(Eval(op, new NativeRelationWriteContext { RelationExists = true }) == 0, $"{op} ok -> 0");
    }
}

static void VerifyUpdateColor()
{
    // not-listed -1, apply-failed -2, else 0.
    Assert(Eval(NativeRelationWriteOp.UpdateAttentionColor,
        new NativeRelationWriteContext { RelationExists = false }) == -1, "colour not-listed -> -1");
    Assert(Eval(NativeRelationWriteOp.UpdateAttentionColor,
        new NativeRelationWriteContext { RelationExists = true, ColorApplied = false }) == -2, "colour apply-fail -> -2");
    Assert(Eval(NativeRelationWriteOp.UpdateAttentionColor,
        new NativeRelationWriteContext { RelationExists = true, ColorApplied = true }) == 0, "colour ok -> 0");
}

static void VerifyReplySemantics()
{
    // add_friend is the only op with no success reply; its failure SM is 4434.
    Assert(!NativeRelationWriteTransaction.RepliesOnSuccess(NativeRelationWriteOp.AddFriend),
        "add_friend no success reply");
    Assert(NativeRelationWriteTransaction.ReplySmId(NativeRelationWriteOp.AddFriend) == 4434,
        "add_friend reply sm 4434");
    foreach (var op in new[] { NativeRelationWriteOp.AddAttention, NativeRelationWriteOp.AddBlacklist,
                               NativeRelationWriteOp.DelFriend, NativeRelationWriteOp.DelAttention,
                               NativeRelationWriteOp.DelBlacklist, NativeRelationWriteOp.UpdateAttentionColor })
    {
        Assert(NativeRelationWriteTransaction.RepliesOnSuccess(op), $"{op} replies on success");
        Assert(NativeRelationWriteTransaction.ReplySmId(op) == (int)op, $"{op} reply sm == op");
    }
}

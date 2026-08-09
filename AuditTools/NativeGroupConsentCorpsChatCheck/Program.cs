// NativeGroupConsentCorpsChatCheck —— 战神 party-invite CONSENT + `!#` CORPS(战队) chat 闸.
//
// 判据全部来自 staging/M2Server_reunpacked_20260803.exe 的反汇编(见
// staging/groupchannel_fix_20260804.md),每条断言都标注 EA。
//
// 覆盖两个高危修复:
//   P1  1020/1021 原版【只排队邀请】(sub_6C341C/sub_6C34EC 唯一状态变更调用 = sub_6F39B4),
//       C# 原先直接强行入队 → 任何人一个包就能把你拖进队伍。
//   P2  `!#` 原版 = 战队(TCorps)聊天(sub_6BB2F8 @6BB74E → sub_6F7B10 → sub_705F3C, ident 108),
//       C# 原先落到【喊话】分支 → 私密文本被 50 格内所有人看到。
//
// ⚠️ 发现文档纠错:discovery_group_channel_20260803.md #17 把 [self+0xAE8] 当成"语音频道",
//    实为 TCorps(VMT 0x705064)。sub_6ADAE4 取 [[+0xAE8]+4] 作行会;该字段 5 个写点全在
//    0x701xxx-0x707xxx 战队管理单元(写点所在函数 0x702328 引用 '副队长'/'战队' 等字面量)。
//    若照文档接到 NativeChannelManager,受众就会错 —— 这正是"受众错比不改更糟"。

using System.Reflection;
using GameSvr;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.g_DenySayMsgList ??=
    new System.Collections.Concurrent.ConcurrentDictionary<string, long>();

CheckInviteQueuesInsteadOfForceJoin();
CheckInviteRejectionLadders();
CheckNonLeaderCanLeave();
CheckMapNoGroupGate();
CheckNativeJoinPathMapGate();
CheckCorpsChatPrefixRouting();
CheckCorpsChatAudience();
CheckCorpsChatConstants();
CheckNativeLiteralsAreByteExact();
CheckSourceContracts();

Console.WriteLine(
    "PASS NativeGroupConsentCorpsChat consent=1020/1021-queue-only del=1022-self-leave "
    + "map=BLACKROOM-0x7C corpsChat=ident108/param-6/79ch audience=own-corps-only");
return;

// ---------------------------------------------------------------------------
// P1 — 战神 sub_6C341C (1020) / sub_6C34EC (1021): the ONLY state-changing callee in
// either body is sub_6F39B4 (queue a pending request). Exhaustive E8-callee sets:
//   sub_6C341C: 405500 4059C0 40C140 652784 6C3380 6C33CC 6F39B4
//   sub_6C34EC: 405500 4059C0 40C140 652784 6B7BAC 6BBE84 6C33CC 6F39B4
// Neither contains sub_726B80 (alloc group) / sub_7272EC (insert member) /
// sub_6C3648 (create-on-accept). Group membership therefore materialises ONLY from the
// 4412 accept (sub_6F3EA8 @6F3F2E call 0x6c3648).
// ---------------------------------------------------------------------------
static void CheckInviteQueuesInsteadOfForceJoin()
{
    // ---- 1020 CM_CREATEGROUP -------------------------------------------------
    var inviter = RegisterPlayer(NewPlayer("p1-inviter"));
    var invitee = RegisterPlayer(NewPlayer("p1-invitee"));

    inviter.Operate(LegacyMessage(Grobal2.CM_CREATEGROUP, invitee.m_sCharName));

    // The headline assertion: no group may exist yet.
    Require(inviter.m_GroupOwner == null,
        "1020 created a group on the INVITER (native sub_6C341C only queues; 6C348A "
        + "call 0x6f39b4 is its sole state change)");
    Require(invitee.m_GroupOwner == null,
        "1020 force-joined the INVITEE without consent (native never calls sub_7272EC "
        + "from sub_6C341C)");
    Require(inviter.m_GroupMembers.Count == 0,
        "1020 populated m_GroupMembers (native allocates the group only in sub_6C3648)");
    // ...and the invite must actually be pending ON THE TARGET
    // (6C3488 mov eax,esi = target is the receiver of sub_6F39B4).
    Require(HasPendingRequest(invitee, inviter, 0),
        "1020 did not queue a type-0 request on the TARGET (6C3484 xor ecx,ecx = type 0)");
    Require(!HasPendingRequest(inviter, invitee, 0),
        "1020 queued the request on the wrong side (native passes target in eax at 6C3488)");

    // Accepting (4412 recog=1 param=0) is what creates the group — sub_6F3EA8
    // @6F3F21 cmp [edi+0xA80],0 / je -> 6F3F2E call sub_6C3648 with leader = requester.
    invitee.Operate(NativeMessage(Grobal2.CM_REPLY_GROUP_MESSAGE, 1, 0,
        inviter.m_sCharName));
    Require(ReferenceEquals(inviter.m_GroupOwner, inviter)
        && ReferenceEquals(invitee.m_GroupOwner, inviter),
        "4412 accept did not create the requester-led group (6F3F2E call 0x6c3648)");
    Require(inviter.m_GroupMembers.Count == 2,
        "4412 accept produced the wrong member count");

    // ---- 1021 CM_ADDGROUPMEMBER --------------------------------------------
    var third = RegisterPlayer(NewPlayer("p1-third"));
    inviter.Operate(LegacyMessage(Grobal2.CM_ADDGROUPMEMBER, third.m_sCharName));
    Require(third.m_GroupOwner == null,
        "1021 force-joined the target (native sub_6C34EC's only state change is "
        + "6C3572 call 0x6f39b4)");
    Require(inviter.m_GroupMembers.Count == 2,
        "1021 appended to m_GroupMembers without consent");
    Require(HasPendingRequest(third, inviter, 0),
        "1021 did not queue a type-0 request on the target (6C356C xor ecx,ecx)");

    inviter.m_GroupMembers.Clear();
    inviter.m_GroupOwner = null;
    invitee.m_GroupOwner = null;
}

// ---------------------------------------------------------------------------
// Rejection ladders + their ORDER. Codes and EAs:
//   1020 (sub_6C341C): self precheck -6/-1 (sub_6C3380), target nil -2 (6C349A),
//        target==self -10 (6C3491), target precheck -4/-3 (sub_6C33CC);
//        reply ident 0x295 = 661 SM_CREATEGROUP_FAIL (6C34B2), only when code != 0.
//   1021 (sub_6C34EC): restricted self => SILENT (6C351D jne 0x6c35ba),
//        not leader -1 (6C3594), count>=0xB -5 (6C358B), target nil -2 (6C3582),
//        target==self -10 (6C3579), target precheck -4/-3;
//        reply ident 0x298 = 664 SM_GROUPADDMEM_FAIL (6C35AC).
// ---------------------------------------------------------------------------
static void CheckInviteRejectionLadders()
{
    // target == self must be -10, NOT the -2 "player not found" C# used to fold it into.
    var solo = RegisterPlayer(NewPlayer("p2-solo"));
    var sent = CaptureDefMessages(solo,
        () => solo.Operate(LegacyMessage(Grobal2.CM_CREATEGROUP, solo.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Ident == Grobal2.SM_CREATEGROUP_FAIL
        && sent[0].Recog == -10,
        "1020 self-target must be -10 (6C3491 mov [ebp-8],0xFFFFFFF6), not -2");

    // -4 BEFORE -3: sub_6C33CC emits -4 for !allowGroup / map / restricted (6C33F8) and
    // only afterwards -3 for already-grouped (6C340B). A target that is BOTH grouped and
    // has group-mode off must therefore report -4.
    var caller = RegisterPlayer(NewPlayer("p2-caller"));
    var leader = RegisterPlayer(NewPlayer("p2-leader"));
    var busy = RegisterPlayer(NewPlayer("p2-busy"));
    leader.m_GroupOwner = leader;
    leader.m_GroupMembers.Add(leader);
    leader.m_GroupMembers.Add(busy);
    busy.m_GroupOwner = leader;      // already grouped  -> would be -3
    busy.m_boAllowGroup = false;     // group-mode off   -> must win with -4
    sent = CaptureDefMessages(caller,
        () => caller.Operate(LegacyMessage(Grobal2.CM_CREATEGROUP, busy.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Recog == -4,
        "target precheck order inverted: native sub_6C33CC emits -4 (6C33F8) before "
        + "-3 (6C340B)");

    // Grouped target with group-mode ON is the pure -3 case.
    busy.m_boAllowGroup = true;
    sent = CaptureDefMessages(caller,
        () => caller.Operate(LegacyMessage(Grobal2.CM_CREATEGROUP, busy.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Recog == -3,
        "already-grouped target must be -3 (6C340B mov [edi],0xFFFFFFFD)");

    // Self already grouped -> -1 from sub_6C3380 (6C33BA mov [esi],0xFFFFFFFF).
    var target = RegisterPlayer(NewPlayer("p2-target"));
    sent = CaptureDefMessages(leader,
        () => leader.Operate(LegacyMessage(Grobal2.CM_CREATEGROUP, target.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Recog == -1,
        "1020 self-already-grouped must be -1 (sub_6C3380 @6C33BA)");

    // 1021 non-leader -> -1 (6C3594).  busy is a member but not the leader.
    sent = CaptureDefMessages(busy,
        () => busy.Operate(LegacyMessage(Grobal2.CM_ADDGROUPMEMBER, target.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Ident == Grobal2.SM_GROUPADDMEM_FAIL
        && sent[0].Recog == -1,
        "1021 non-leader must be -1 (6C3525 call sub_6B7BAC / 6C352C je -> 6C3594)");

    // 1021 capacity: native's hard bound is the 11-slot array
    // (6C3534 cmp dword [eax+0x44],0xB / jge -> 6C358B = -5).
    while (leader.m_GroupMembers.Count < 11)
        leader.m_GroupMembers.Add(RegisterPlayer(NewPlayer(
            "p2-filler" + leader.m_GroupMembers.Count)));
    Require(leader.m_GroupMembers.Count == 11, "capacity fixture did not reach 11");
    sent = CaptureDefMessages(leader,
        () => leader.Operate(LegacyMessage(Grobal2.CM_ADDGROUPMEMBER, target.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Recog == -5,
        "1021 must reject at 11 members with -5 (6C3534 cmp [eax+0x44],0xB / jge 0x6c358b)");

    // 1021 restricted self is SILENT — the jump at 6C351D lands past the reply block.
    // sub_6BBE84 is a pure MOUNT gate: (state 0x33 && [self+0x3C0] != 0) || state 0x34.
    // This fixture used to set m_boGhost, which only "worked" while C# stood the gate in
    // as `m_boDeath || m_boGhost` — a condition neither group precheck actually tests
    // (sub_6C3380 = {sub_6BBE84, [map+0x7C], [self+0xA80]}). State 0x34 (two-seat mount)
    // trips the gate on its own at 0x6BBEA0, so it is the minimal faithful trigger.
    leader.SetNativeActiveState(0x34);
    sent = CaptureDefMessages(leader,
        () => leader.Operate(LegacyMessage(Grobal2.CM_ADDGROUPMEMBER, target.m_sCharName)));
    Require(sent.Count == 0,
        "1021 restricted self must return SILENTLY (6C3516 call sub_6BBE84 / "
        + "6C351D jne 0x6c35ba skips the whole reply block)");
    leader.ClearNativeActiveState(0x34);

    // A ghost/dead leader is NOT restricted natively — proving the old substitute was
    // over-broad, not merely incomplete.
    leader.m_boGhost = true;
    sent = CaptureDefMessages(leader,
        () => leader.Operate(LegacyMessage(Grobal2.CM_ADDGROUPMEMBER, target.m_sCharName)));
    Require(sent.Count == 1,
        "1021 must still answer a ghost leader: sub_6BBE84 tests only mount state, "
        + "never m_boGhost/m_boDeath");
    leader.m_boGhost = false;
}

// ---------------------------------------------------------------------------
// 战神 sub_6C3CF0 (1022): allowed := leader (6C3D23 call sub_6B7BAC) OR own-name-matches
// (6C3D35 lea edx,[ebx+0x106] / 6C3D46 call 0x40591c / 6C3D4B jne 0x6c3d53). A non-leader
// passing their OWN name may leave; anything else is -1 (6C3D53 or esi,0xFFFFFFFF).
// ---------------------------------------------------------------------------
static void CheckNonLeaderCanLeave()
{
    var leader = RegisterPlayer(NewPlayer("p3-leader"));
    var member = RegisterPlayer(NewPlayer("p3-member"));
    var other = RegisterPlayer(NewPlayer("p3-other"));
    leader.m_GroupOwner = leader;
    leader.m_GroupMembers.Add(leader);
    leader.m_GroupMembers.Add(member);
    leader.m_GroupMembers.Add(other);
    member.m_GroupOwner = leader;
    other.m_GroupOwner = leader;

    // A non-leader removing SOMEONE ELSE is still -1.
    var sent = CaptureDefMessages(member,
        () => member.Operate(LegacyMessage(Grobal2.CM_DELGROUPMEMBER,
            other.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Ident == Grobal2.SM_GROUPDELMEM_FAIL
        && sent[0].Recog == -1,
        "1022 non-leader removing another member must be -1 (6C3D53 or esi,-1)");
    Require(ReferenceEquals(other.m_GroupOwner, leader),
        "1022 non-leader removed a third party");

    // A non-leader removing THEMSELF succeeds — the native second branch at 6C3D32.
    sent = CaptureDefMessages(member,
        () => member.Operate(LegacyMessage(Grobal2.CM_DELGROUPMEMBER,
            member.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Ident == Grobal2.SM_GROUPDELMEM_OK
        && sent[0].Recog == 0,
        "1022 self-leave must succeed with SM_GROUPDELMEM_OK recog=0 "
        + "(6C3D84 mov dx,0x297, then esi := 0)");
    Require(member.m_GroupOwner == null
        && !leader.m_GroupMembers.Contains(member),
        "1022 self-leave did not actually remove the member (6C3D73 call sub_726E68)");
    // The leader's own group must be intact — native routes DelMember through the GROUP.
    Require(ReferenceEquals(leader.m_GroupOwner, leader)
        && leader.m_GroupMembers.Contains(leader)
        && leader.m_GroupMembers.Contains(other),
        "1022 self-leave collapsed the whole group");
}

// ---------------------------------------------------------------------------
// 战神 map "no group" byte [map+0x7C]:
//   self   sub_6C3380 @6C339B mov eax,[edi+0x128] / 6C33A1 cmp byte [eax+0x7C],0 -> -6
//   target sub_6C33CC @6C33E1 mov eax,[esi+0x128] / 6C33E7 cmp byte [eax+0x7C],0 -> -4
// Parser sub_774D98 sets that byte for token BLACKROOM (@0x775DC4;
// 775318 mov byte [ebx+0x7C],1 / 775329 mov byte [ebx+0x7C],0).
// ---------------------------------------------------------------------------
static void CheckMapNoGroupGate()
{
    var blackRoom = new Envirnoment
    {
        sMapName = "blackroom-map",
        Flag = new TMapFlag { boBLACKROOM = true }
    };
    var plain = new Envirnoment
    {
        sMapName = "plain-map",
        Flag = new TMapFlag()
    };

    // Self on a BLACKROOM map -> -6 (and -6 must beat the -1 already-grouped code,
    // because sub_6C3380 tests the map before [edi+0xA80]).
    var self = RegisterPlayer(NewPlayer("p4-self"));
    var mate = RegisterPlayer(NewPlayer("p4-mate"));
    self.m_PEnvir = blackRoom;
    mate.m_PEnvir = plain;
    var sent = CaptureDefMessages(self,
        () => self.Operate(LegacyMessage(Grobal2.CM_CREATEGROUP, mate.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Recog == -6,
        "self on a map with [+0x7C] set must be -6 (6C33A7 mov [esi],0xFFFFFFFA)");
    Require(!HasPendingRequest(mate, self, 0),
        "map-denied self still queued an invite");

    // Target on a BLACKROOM map -> -4 (the sub_6C33CC leg at 6C33E7).
    self.m_PEnvir = plain;
    mate.m_PEnvir = blackRoom;
    sent = CaptureDefMessages(self,
        () => self.Operate(LegacyMessage(Grobal2.CM_CREATEGROUP, mate.m_sCharName)));
    Require(sent.Count == 1 && sent[0].Recog == -4,
        "target on a map with [+0x7C] set must be -4 (6C33E7 -> 6C33F8)");

    // Both on plain maps -> the invite is queued, nothing sent.
    mate.m_PEnvir = plain;
    sent = CaptureDefMessages(self,
        () => self.Operate(LegacyMessage(Grobal2.CM_CREATEGROUP, mate.m_sCharName)));
    Require(sent.Count == 0 && HasPendingRequest(mate, self, 0),
        "an eligible 1020 must queue silently (no SM_CREATEGROUP_OK exists in sub_6C341C)");
}

// ---------------------------------------------------------------------------
// P2 — `!#` routes to CORPS chat, never to shout.
// sub_6BB2F8 @6BB74E cmp al,0x23 / 6BB750 jne 0x6bb771 (=shout) / 6BB767 call sub_6F7B10.
// Enclosing '!' gate 6BB6DB cmp edi,2 / jle 0x6bb771 => len > 2 required.
// sub_6F7B10: mute (6F7B3C) -> hint & no send;  no corps (6F7B46) -> SILENT.
// ---------------------------------------------------------------------------
static void CheckCorpsChatPrefixRouting()
{
    var speaker = RegisterPlayer(NewPlayer("p5-speaker"));
    speaker.m_PEnvir = new Envirnoment
    {
        sMapName = "corps-chat-map",
        Flag = new TMapFlag()
    };

    // No corps => the handler consumes the line SILENTLY. The load-bearing part is that it
    // must NOT reach the shout path: with M2Share.CorpsService unavailable in this harness,
    // TryProcessNativeCorpsChat still has to return true.
    var handled = InvokeInstance<bool>(speaker, "TryProcessNativeCorpsChat", "!#secret");
    Require(handled,
        "`!#text` was not consumed by the corps-chat handler, so it falls through to the "
        + "SHOUT ladder (6BB750 jne 0x6bb771) and leaks the text map-wide");

    // A bare "!#" (len == 2) is NOT corps chat: 6BB6DB cmp edi,2 / jle 0x6bb771.
    Require(!InvokeInstance<bool>(speaker, "TryProcessNativeCorpsChat", "!#"),
        "`!#` with an empty body must fall through to shout (6BB6DB cmp edi,2 / jle)");

    // Other prefixes must not be captured by this handler.
    Require(!InvokeInstance<bool>(speaker, "TryProcessNativeCorpsChat", "!!party"),
        "corps-chat handler swallowed the `!!` group prefix (6BB6E7 cmp al,0x21)");
    Require(!InvokeInstance<bool>(speaker, "TryProcessNativeCorpsChat", "!~guild"),
        "corps-chat handler swallowed the `!~` guild prefix (6BB706 cmp al,0x7e)");
    Require(!InvokeInstance<bool>(speaker, "TryProcessNativeCorpsChat", "!shout"),
        "corps-chat handler swallowed a plain shout");

    // The mute leg (6F7B3C cmp byte [ebp+0xC],0 / jne 0x6f7bda) must consume the line and
    // send only the hint — never the corps broadcast.
    speaker.m_boDisableSayMsg = true;
    Require(InvokeInstance<bool>(speaker, "TryProcessNativeCorpsChat", "!#muted"),
        "muted corps chat must still be consumed, not forwarded to shout");
    speaker.m_boDisableSayMsg = false;

    // The chat dispatcher must route '#' before the shout ladder. Source-level check: the
    // '#' test has to appear textually before the boQUIZ/shout block inside ProcessSayMsg.
    var chatSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GameSvr",
        "Players", "TPlayObject.Chat.cs"));
    var corpsIndex = chatSource.IndexOf("TryProcessNativeCorpsChat",
        StringComparison.Ordinal);
    var shoutIndex = chatSource.IndexOf("m_dwShoutMsgTick", StringComparison.Ordinal);
    Require(corpsIndex > 0 && shoutIndex > 0 && corpsIndex < shoutIndex,
        "the `!#` branch must be tested BEFORE the shout ladder (native order: 6BB74E "
        + "then fall-through 0x6BB771)");
}

// ---------------------------------------------------------------------------
// AUDIENCE — the privacy-critical assertion. 战神 sub_705954 walks ONLY the corps member
// list [TCorps+0x30]; a player who is not in the corps must receive NOTHING. This drives
// BroadcastNativeCorpsMessage directly with a hand-built snapshot so a widened audience
// (e.g. iterating the whole player list) is observable.
// ---------------------------------------------------------------------------
static void CheckCorpsChatAudience()
{
    var inCorpsA = RegisterPlayer(NewPlayer("p6-member-a"));
    var inCorpsB = RegisterPlayer(NewPlayer("p6-member-b"));
    var outsider = RegisterPlayer(NewPlayer("p6-outsider"));
    var offlineMember = RegisterPlayer(NewPlayer("p6-offline"));
    offlineMember.m_boGhost = true;   // GetPlayObject filters ghosts => no live player

    // NativeCorpsSnapshot / NativeCorpsMemberSnapshot are internal to GameSvr; build them by
    // reflection rather than widening production access just for an audit.
    var corps = NewCorpsSnapshot(4242, "audit-corps");
    AddCorpsMember(corps, 1, inCorpsA.m_sCharName);
    AddCorpsMember(corps, 2, inCorpsB.m_sCharName);
    AddCorpsMember(corps, 3, offlineMember.m_sCharName);

    var probes = new[] { inCorpsA, inCorpsB, outsider, offlineMember };
    for (var i = 0; i < probes.Length; i++) SetLastPacket(probes[i], null);

    InvokeStaticVoid("BroadcastNativeCorpsMessage", corps, "corps-secret");

    Require(LastPacket(inCorpsA) != null && LastPacket(inCorpsB) != null,
        "corps members did not receive the corps message (sub_705954 sends to every "
        + "member whose [+0x28] player pointer is non-nil)");
    Require(LastPacket(inCorpsA).Ident == 108 && LastPacket(inCorpsA).Recog == 0,
        "corps message must be ident 108 recog 0 (705FC0 mov dx,0x6C / 705FBE xor ecx,ecx)");
    // THE leak assertion: a non-member must be untouched.
    Require(LastPacket(outsider) == null,
        "PRIVACY LEAK: a player outside the sender's corps received the corps message "
        + "(native sub_705954 iterates ONLY [TCorps+0x30])");
    Require(LastPacket(offlineMember) == null,
        "a ghost/offline member was sent to (705999 test edi,edi / 70599B je skip)");
}

// ---------------------------------------------------------------------------
// The 4412/4413 accept path shares 战神 sub_6C33CC's eligibility, so its C# stand-in
// CanJoinNativeGroup must carry the same map gate ([map+0x7C], parser token BLACKROOM).
// Without it a party can still be formed on a no-group map through the consent path.
// ---------------------------------------------------------------------------
static void CheckNativeJoinPathMapGate()
{
    var denied = NewPlayer("p7-denied");
    denied.m_PEnvir = new Envirnoment
    {
        sMapName = "join-blackroom",
        Flag = new TMapFlag { boBLACKROOM = true }
    };
    Require(!InvokeStatic<bool>("CanJoinNativeGroup", denied),
        "CanJoinNativeGroup (the 4412/4413 stand-in for sub_6C33CC) ignores the map "
        + "no-group byte [map+0x7C] (6C33E7 cmp byte [eax+0x7C],0)");

    var plain = NewPlayer("p7-plain");
    plain.m_PEnvir = new Envirnoment
    {
        sMapName = "join-plain",
        Flag = new TMapFlag()
    };
    Require(InvokeStatic<bool>("CanJoinNativeGroup", plain),
        "CanJoinNativeGroup rejects an eligible player on a normal map");
}

// ---------------------------------------------------------------------------
// Corps-chat wire constants, each with its EA.
// ---------------------------------------------------------------------------
static void CheckCorpsChatConstants()
{
    // ident 108: sub_705F3C @705FC0 mov dx,0x6C (non-empty body) and @705FDA (empty body).
    Require(ReadConst<int>("SM_CORPSMESSAGE") == 108,
        "corps-chat ident must be 108 (705FC0 mov dx,0x6C)");
    // param word: sub_6F7B10 @6F7BCA mov cx,0xFFFA = -6.
    Require(ReadConst<int>("NativeCorpsMessageParam") == -6,
        "corps-chat param word must be -6 (6F7BCA mov cx,0xFFFA)");
    // 79 = 0x50 clamp minus the trailing NUL: 705F77 inc eax / 705F7E cmp eax,0x50 /
    // 705F83 mov eax,0x50 / 705F8A dec ecx.
    Require(ReadConst<int>("NativeCorpsMessageMaxLength") == 79,
        "corps-chat body clamp must be 79 (705F7E cmp eax,0x50 then 705F8A dec ecx)");

    // Prefix is `name + ": "` capped to 16 BYTES (6F7B9F mov cl,0x10), and the cut must land
    // on a whole GBK character rather than splitting a double-byte pair. The fixture is
    // deliberately ODD-length in bytes (1 + 8*2 = 17) so that a naive 16-byte slice would
    // land INSIDE the 8th character — an even-length fixture cannot tell the two apart.
    const string oddPrefix = "A十二三四五六七八";
    Require(HUtil32.GbkEncoding.GetByteCount(oddPrefix) == 17,
        "GBK truncation fixture is not 17 bytes, so it cannot detect a mid-character cut");
    var truncated = InvokeStatic<string>("TruncateNativeGbkBytes", oddPrefix, 16);
    Require(HUtil32.GbkEncoding.GetByteCount(truncated) <= 16,
        "16-byte prefix cap exceeded (6F7B9F mov cl,0x10)");
    Require(truncated == "A十二三四五六七",
        "GBK truncation split a double-byte character at the 16-byte cap");
    Require(truncated.IndexOf('�') < 0 && truncated.IndexOf('?') < 0,
        "GBK truncation produced a replacement character = a half-character was written");
    var clamped = InvokeStatic<string>("TruncateNativeGbkBytes",
        new string('x', 200), 79);
    Require(clamped.Length == 79, "79-char clamp is not applied");
}

// ---------------------------------------------------------------------------
// Every 战神 hint we ship on these paths must be the literal FROM THE IMAGE, byte-for-byte in
// GBK — not a paraphrase and not rebuilt from a noun + template. The expected GBK bytes below
// were read out of staging/M2Server_reunpacked_20260803.exe at the cited EAs (verifier:
// staging/_gc_literals.py, report staging/_gc_literals.txt).
//
// This gate exists because the byte check caught FOUR paraphrases and one invented leading
// space that reading the disasm alone had not: the 0x20 at 0x6F3B30 is the ShortString LENGTH
// byte (32 == the byte count that follows), NOT a space. Distinguishing an AnsiString constant
// (refcount -1 at [-8], length at [-4]) from a ShortString (leading length byte) is what makes
// the difference, so both forms are asserted here.
// ---------------------------------------------------------------------------
static void CheckNativeLiteralsAreByteExact()
{
    // (EA, GBK bytes as hex, the C# literal, the file that must contain it)
    var cases = new (uint Ea, string Hex, string Literal, string File)[]
    {
        (0x6C31D8,
            "c8e7b9fbc4e3cfebcdcbb3f6a3accab9d3c3b1e0d7e9b9a6c4dca3a8c9beb3fdb0b4c5a5a3a9",
            "如果你想退出，使用编组功能（删除按钮）", "TPlayObject.Operate.cs"),
        (0x6F7C30, "d2d1beadb1bbbdfbd6b9c1c4ccec",
            "已经被禁止聊天", "TPlayObject.NativeCorpsChat.cs"),
        (0x6F3B5C, "b6d4b7bdd5fdc3a6a3acc7ebc9d4baf3d4d9c7ebc7f3a1a3",
            "对方正忙，请稍后再请求。", "TPlayObject.NativeGroupProtocol.cs"),
        (0x6F3B80,
            "c4fad2d1cce1bdbbd7e9b6d3d1fbc7eba3acc7ebb5c8b4fdb6d4b7bdbbd8d3a6a1a3",
            "您已提交组队邀请，请等待对方回应。", "TPlayObject.NativeGroupProtocol.cs"),
        (0x6F3BAC,
            "c4fad2d1cce1bdbbc8ebb6d3c9eac7eba3acc7ebb5c8b4fdb6d4b7bdbbd8d3a6a1a3",
            "您已提交入队申请，请等待对方回应。", "TPlayObject.NativeGroupProtocol.cs"),
        (0x6F3BD8,
            "c4fad2d1cce1bdbbbac3d3d1c9eac7eba3acc7ebb5c8b4fdb6d4b7bdbbd8d3a6a1a3",
            "您已提交好友申请，请等待对方回应。", "TPlayObject.NativeGroupProtocol.cs"),
        // ShortString @0x6F3B30: leading 0x20 is the LENGTH byte (32), so there is no space.
        (0x6F3B30,
            "ceb4bbd8b8b4c4fab5c4d1fbc7eba3acc7ebcaaec3ebbaf3d4d9b3a2cad4a1a3",
            "未回复您的邀请，请十秒后再尝试。", "TPlayObject.NativeGroupProtocol.cs"),
        (0x6F3D88, "bedcbef8c1cbc4fab5c4d7e9b6d3d1fbc7eba1a3",
            "拒绝了您的组队邀请。", "TPlayObject.NativeGroupProtocol.cs"),
        (0x6F3DA0, "bedcbef8c1cbc4fab5c4c8ebb6d3c9eac7eba1a3",
            "拒绝了您的入队申请。", "TPlayObject.NativeGroupProtocol.cs"),
        (0x6F3DB8, "bedcbef8c1cbc4fab5c4bac3d3d1c9eac7eba1a3",
            "拒绝了您的好友申请。", "TPlayObject.NativeGroupProtocol.cs"),
        (0x6F4208, "ceb4cfecd3a6c4fab5c4d7e9b6d3d1fbc7eba1a3",
            "未响应您的组队邀请。", "TPlayObject.NativeGroupProtocol.cs"),
        // 0x6F4220 really is 组队申请 in the image, NOT 入队申请 — byte-verified.
        (0x6F4220, "ceb4cfecd3a6c4fab5c4d7e9b6d3c9eac7eba1a3",
            "未响应您的组队申请。", "TPlayObject.NativeGroupProtocol.cs"),
        (0x6F4238, "ceb4cfecd3a6c4fab5c4bac3d3d1c9eac7eba1a3",
            "未响应您的好友申请。", "TPlayObject.NativeGroupProtocol.cs")
    };

    var root = FindRepositoryRoot();
    var cache = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (ea, hex, literal, file) in cases)
    {
        var expected = Convert.FromHexString(hex);
        var actual = HUtil32.GbkEncoding.GetBytes(literal);
        Require(expected.AsSpan().SequenceEqual(actual),
            $"literal for 0x{ea:X} is not byte-identical to the image: expected {hex}, "
            + $"C# encodes to {Convert.ToHexString(actual).ToLowerInvariant()}");

        if (!cache.TryGetValue(file, out var source))
        {
            source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players", file));
            cache[file] = source;
        }
        // A bare Contains() would also accept a SUPERSTRING, e.g. " 未回复您的邀请…" with an
        // invented leading space (the 0x20 at 0x6F3B30 is the ShortString LENGTH byte, 32, not
        // a space). So require the literal to appear as a COMPLETE C# string token: the
        // characters immediately around the match must be the quote/interpolation delimiters,
        // never additional payload.
        Require(ContainsAsWholeStringLiteral(source, literal),
            $"the 战神 literal at 0x{ea:X} is not present in {file} as a complete string "
            + "literal — either a paraphrase, a noun+template reconstruction, or the literal "
            + "with extra characters glued on (e.g. an invented leading space)");
    }

    // The confirmation trio carries NO name prefix: sub_6F39B4's three branches push only
    // edx=<literal> (6F3AC9 / 6F3ADF / 6F3AF5). The duplicate-request and dismiss hints DO
    // prefix a name (6F39F5 lea edx,[esi+0x106] / 6F40D8 add edx,0x106).
    var groupSource = cache["TPlayObject.NativeGroupProtocol.cs"];
    Require(groupSource.Contains("requester.SysMsg(text, MsgColor.Green",
            StringComparison.Ordinal),
        "the type-0/1/2 confirmations must be sent verbatim with no name prefix "
        + "(6F3AC9/6F3ADF/6F3AF5 push only edx=<literal>)");
    Require(!StripComments(groupSource).Contains("拒绝了你的",
            StringComparison.Ordinal),
        "the reject hint is still the invented \"拒绝了你的\" + noun template; native stores "
        + "whole sentences at 0x6F3D88/0x6F3DA0/0x6F3DB8");
}

// ---------------------------------------------------------------------------
// Source-level contracts: the force-join primitives must be GONE from the legacy 1020/1021
// bodies, and `!#` must not be wired to the voice-channel manager.
// ---------------------------------------------------------------------------
static void CheckSourceContracts()
{
    var root = FindRepositoryRoot();
    var operateSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Operate.cs"));

    var create = StripComments(ExtractMethod(operateSource,
        "private void ClientCreateGroup("));
    var add = StripComments(ExtractMethod(operateSource,
        "private void ClientAddGroupMember("));
    foreach (var (name, body) in new[] { ("1020", create), ("1021", add) })
    {
        Require(body.Contains("QueueNativeGroupRequest(this, 0)", StringComparison.Ordinal),
            $"{name} must queue a type-0 request (6F39B4 with cl=0)");
        Require(!body.Contains("JoinGroup(", StringComparison.Ordinal),
            $"{name} still force-joins via JoinGroup (native sub_6C341C/sub_6C34EC never "
            + "call sub_7272EC)");
        Require(!body.Contains("m_GroupMembers.Add", StringComparison.Ordinal),
            $"{name} still mutates m_GroupMembers directly");
        Require(!body.Contains("SM_CREATEGROUP_OK", StringComparison.Ordinal)
            && !body.Contains("SM_GROUPADDMEM_OK", StringComparison.Ordinal),
            $"{name} sends a success packet, but 0x294/0x296 exist only in sub_6C3648/"
            + "sub_6C3838 (the accept path)");
        Require(!body.Contains("SendGroupMembers()", StringComparison.Ordinal),
            $"{name} broadcasts a member list although no membership changed");
    }

    // The mojibake hint must be the real 战神 literal (sub_6C3140 @6C3193 -> 0x6C31D8).
    Require(operateSource.Contains("如果你想退出，使用编组功能（删除按钮）",
            StringComparison.Ordinal),
        "the group-close hint must be the 战神 literal at 0x6C31D8");

    // `!#` must NOT be routed to the voice-channel subsystem: [self+0xAE8] is a TCorps
    // (VMT 0x705064), proven by sub_6ADAE4 reading [[+0xAE8]+4] as the guild and by all
    // five writers living in the corps unit 0x701xxx-0x707xxx.
    var corpsChatSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeCorpsChat.cs"));
    Require(!corpsChatSource.Contains("NativeChannelManager", StringComparison.Ordinal),
        "`!#` must target the player's TCorps, NOT NativeChannelManager — the discovery "
        + "doc's 'channel' label for [self+0xAE8] is wrong (sub_6ADAE4 derefs [+0xAE8]+4 "
        + "as the guild; writers are all in the corps unit)");
    Require(corpsChatSource.Contains("TryGetPlayerCorps", StringComparison.Ordinal),
        "`!#` must resolve the sender's own corps");
    Require(corpsChatSource.Contains("corps.Members", StringComparison.Ordinal),
        "corps-chat audience must be the corps member list (sub_705954 walks [TCorps+0x30])");
}

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------
static TProcessMessage LegacyMessage(int ident, string name)
{
    // The legacy 1019-1022 family carries the name as PLAIN sMsg (dispatch 0x6D907B
    // call 0x405708 copies the raw string), unlike the 4412-4416 family which is
    // 6-bit encoded.
    return new TProcessMessage { wIdent = ident, sMsg = name };
}

static TProcessMessage NativeMessage(int ident, int recog, int param, string name)
{
    return new TProcessMessage
    {
        wIdent = ident,
        nParam1 = recog,
        nParam2 = param,
        Payload = EDcode.EncodeBuffer(HUtil32.GbkEncoding.GetBytes(name))
    };
}

static TPlayObject NewPlayer(string name)
{
    return new TPlayObject
    {
        m_boOffLineFlag = true,
        m_boAllowGroup = true,
        m_sCharName = name,
        m_sMapName = "audit-map",
        m_nCurrX = 12,
        m_nCurrY = 34
    };
}

static TPlayObject RegisterPlayer(TPlayObject player)
{
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("UserEngine player list missing");
    var players = (IList<TPlayObject>)field.GetValue(M2Share.UserEngine);
    players.Add(player);
    return player;
}

/// <summary>
/// Captures the SendDefMessage traffic a single Operate() call produces. m_boOffLineFlag is
/// true for audit fixtures, so SendSocket short-circuits and m_DefMsg holds the last packet;
/// we snapshot it around the call to tell "sent nothing" from "sent a code".
/// </summary>
static List<ClientPacket> CaptureDefMessages(TPlayObject player, Action action)
{
    var field = typeof(TPlayObject).GetField("m_DefMsg",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    var sentinel = Grobal2.MakeDefaultMsg(0xFFFF, 0x7FFFFFFF, 0, 0, 0);
    field.SetValue(player, sentinel);
    action();
    var current = (ClientPacket)field.GetValue(player);
    var result = new List<ClientPacket>();
    if (current != null && !ReferenceEquals(current, sentinel))
        result.Add(current);
    return result;
}

static bool HasPendingRequest(TPlayObject recipient, TPlayObject requester, byte type)
{
    return InvokeInstance<bool>(recipient, "HasNativeGroupRequest", requester, type);
}

static FieldInfo DefMsgField()
{
    return typeof(TPlayObject).GetField("m_DefMsg",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException("m_DefMsg missing");
}

static Type GameSvrType(string name)
{
    return typeof(TPlayObject).Assembly.GetType(name, throwOnError: true);
}

static object NewCorpsSnapshot(long id, string name)
{
    var type = GameSvrType("GameSvr.Services.NativeCorpsSnapshot");
    var snapshot = Activator.CreateInstance(type, nonPublic: true);
    SetMember(type, snapshot, "Id", id);
    SetMember(type, snapshot, "Name", name);
    return snapshot;
}

static void AddCorpsMember(object corps, long memberId, string name)
{
    var memberType = GameSvrType("GameSvr.Services.NativeCorpsMemberSnapshot");
    var member = Activator.CreateInstance(memberType, nonPublic: true);
    SetMember(memberType, member, "MemberId", memberId);
    SetMember(memberType, member, "Name", name);
    var members = corps.GetType()
        .GetProperty("Members", BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic)
        .GetValue(corps);
    members.GetType().GetMethod("Add").Invoke(members, new[] { member });
}

/// <summary>
/// Sets an init-only / auto property through its compiler-generated backing field so the
/// audit does not need production setters it would otherwise have to widen.
/// </summary>
static void SetMember(Type type, object instance, string property, object value)
{
    var backing = type.GetField($"<{property}>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic);
    if (backing != null)
    {
        backing.SetValue(instance, value);
        return;
    }
    type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public
        | BindingFlags.NonPublic).SetValue(instance, value);
}

static void InvokeStaticVoid(string name, params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(name + " missing");
    method.Invoke(null, arguments);
}

static ClientPacket LastPacket(TPlayObject player)
{
    return (ClientPacket)DefMsgField().GetValue(player);
}

static void SetLastPacket(TPlayObject player, ClientPacket packet)
{
    DefMsgField().SetValue(player, packet);
}

/// <summary>
/// Strips // comments so the source contracts assert on real CODE, not on the EA-citing
/// commentary that necessarily names the very symbols we forbid.
/// </summary>
static string StripComments(string source)
{
    var lines = source.Split('\n');
    var kept = new List<string>(lines.Length);
    foreach (var line in lines)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal))
            continue;
        var marker = line.IndexOf("//", StringComparison.Ordinal);
        kept.Add(marker >= 0 ? line.Substring(0, marker) : line);
    }
    return string.Join("\n", kept);
}

/// <summary>
/// True when <paramref name="literal"/> occurs in <paramref name="source"/> as a complete C#
/// string literal — i.e. the character immediately before the match is the opening quote and
/// the character immediately after is the closing quote. This rejects superstrings such as a
/// literal with an invented leading space, which a plain Contains() would happily accept.
/// </summary>
static bool ContainsAsWholeStringLiteral(string source, string literal)
{
    for (var i = source.IndexOf(literal, StringComparison.Ordinal); i >= 0;
         i = source.IndexOf(literal, i + 1, StringComparison.Ordinal))
    {
        var before = i > 0 ? source[i - 1] : '\0';
        var afterIndex = i + literal.Length;
        var after = afterIndex < source.Length ? source[afterIndex] : '\0';
        if (before == '"' && after == '"')
            return true;
    }
    return false;
}

static string ExtractMethod(string source, string signature)
{
    var start = source.IndexOf(signature, StringComparison.Ordinal);
    Require(start >= 0, "method not found: " + signature);
    var depth = 0;
    var seenOpen = false;
    for (var i = start; i < source.Length; i++)
    {
        if (source[i] == '{')
        {
            depth++;
            seenOpen = true;
        }
        else if (source[i] == '}')
        {
            depth--;
            if (seenOpen && depth == 0)
                return source.Substring(start, i - start + 1);
        }
    }
    throw new InvalidOperationException("unbalanced braces after " + signature);
}

static T ReadConst<T>(string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(name + " missing");
    return (T)field.GetRawConstantValue();
}

static T InvokeStatic<T>(string name, params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(name + " missing");
    return (T)method.Invoke(null, arguments);
}

static T InvokeInstance<T>(TPlayObject player, string name, params object[] arguments)
{
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(name + " missing");
    return (T)method.Invoke(player, arguments);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

extern alias dbsvr;

using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Configs;
using GameSvr.PasEngine;
using SystemModule;
using SystemModule.Packet;
using NativeHumanDataCodec = global::DBSvr.Core.NativeHumanDataCodec;
using NativeMasterRelationProtocol =
    dbsvr::DBSvr.Core.NativeMasterRelationProtocol;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
InitializeRuntime();
TestFormGmSetConfigLoad();
TestNativeAndTransportRoundTrip();
TestLoginAndSaveMappings();
TestNativeClearProtocol();
TestRelationClientPacket();
TestAgreeFailures();
TestRequestBaishi();
TestAllowMasterCommands();
TestPasRelationships();
TestKickEdgeCases();
TestShiMenFlyModes();
TestSourceContracts();

Console.WriteLine(
    "PASS ShiMen native=D9/DA/DC/DF/E0+5slots protobuf=sparse " +
    "login/save=roundtrip PAS=agree/leave/kick ShiMenFly=1/2/3");
return;

static void TestFormGmSetConfigLoad()
{
    M2Share.g_Config = new GameSvrConfig
    {
        nMinMasterLevel = 99,
        nMasterOKLevel = 99,
        nMaxApprenticeLevel = 99
    };
    var serverConfig = new ServerConfig(Path.Combine(
        AppContext.BaseDirectory, "!Setup.txt"));
    serverConfig.LoadConfig();
    Equal(35, M2Share.g_Config.nMinMasterLevel,
        "FormGMSet SETKEY_SHOUTU load");
    Equal(35, M2Share.g_Config.nMasterOKLevel,
        "FormGMSet SETKEY_CHUSHI load");
    Equal(28, M2Share.g_Config.nMaxApprenticeLevel,
        "FormGMSet SETKEY_BAISHI load");

    var loadedConfig = M2Share.g_Config;
    var fixtureDirectory = Path.Combine(AppContext.BaseDirectory,
        "formgmset-optional-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(fixtureDirectory);
    var setupPath = Path.Combine(fixtureDirectory, "!Setup.txt");
    var formPath = Path.Combine(fixtureDirectory, "FormGMSet.ini");
    File.WriteAllText(setupPath, "[Server]" + Environment.NewLine);
    try
    {
        M2Share.g_Config = new GameSvrConfig
        {
            nMinMasterLevel = 71,
            nMasterOKLevel = 72,
            nMaxApprenticeLevel = 73
        };
        new ServerConfig(setupPath).LoadConfig();
        Assert(!File.Exists(formPath),
            "missing FormGMSet was created");
        Equal(71, M2Share.g_Config.nMinMasterLevel,
            "missing FormGMSet changed SHOUTU");
        Equal(72, M2Share.g_Config.nMasterOKLevel,
            "missing FormGMSet changed CHUSHI");
        Equal(73, M2Share.g_Config.nMaxApprenticeLevel,
            "missing FormGMSet changed BAISHI");

        File.WriteAllText(formPath,
            "SETKEY_SHOUTU=invalid" + Environment.NewLine
            + "SETKEY_CHUSHI=41" + Environment.NewLine
            + "SETKEY_BAISHI=" + Environment.NewLine,
            HUtil32.GbkEncoding);
        new ServerConfig(setupPath).LoadConfig();
        Equal(71, M2Share.g_Config.nMinMasterLevel,
            "invalid FormGMSet changed SHOUTU");
        Equal(41, M2Share.g_Config.nMasterOKLevel,
            "valid FormGMSet CHUSHI ignored");
        Equal(73, M2Share.g_Config.nMaxApprenticeLevel,
            "empty FormGMSet changed BAISHI");
    }
    finally
    {
        M2Share.g_Config = loadedConfig;
        Directory.Delete(fixtureDirectory, true);
    }
}

static void TestNativeAndTransportRoundTrip()
{
    // 2026-08-07: this fixture used to write student names as ShortStrings at
    // 0x680 + i*0x10 and assert they decoded back.  That was self-referential
    // false-green: those offsets are fork contamination, and the real 战神
    // records store NO name there.  Reading them made TryDecode reject 30/30
    // REAL records written by the original Delphi DBServer ("short string
    // length 58 exceeds 15 at 0x680" — 0x680 is CONTENT of the social string
    // at 0x670, whose ':' filler is 0x3A = 58), i.e. every character failed to
    // log in.  See AuditTools/GoldenCodecFidelityCheck.
    //
    // 战神 keeps the whole social state as ONE opaque 128-byte block that it
    // block-copies both ways (load sub_6AFD7C @0x6B096C
    // `lea esi,[rec+0x658]; mov ecx,0x20; rep movsd -> obj+0xc48`; save
    // sub_6B0FF0 @0x6B167E the exact inverse).  So the contract asserted here
    // is now the one native actually has: the flag scalars decode/encode by
    // offset, and the social block survives a round-trip VERBATIM.
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    raw[0x3E] = 1;
    raw[NativeHumanDataCodec.AllowMasterFlagOffset] = 1;
    raw[NativeHumanDataCodec.MasterFlagOffset] = 1;
    raw[NativeHumanDataCodec.StudentFlagOffset] = 1;
    raw[NativeHumanDataCodec.StudentOrderOffset] = 5;
    raw[NativeHumanDataCodec.StudentCountOffset] = 3;
    raw[0xDE] = 0xA1;
    raw[0xDD] = 0xA2;
    raw[0xE1] = 0xA3;
    // A social block shaped like the real records: the populated ':'/'$'
    // ShortString at 0x670 (== block+0x18) plus sentinels at the block edges,
    // so a byte-exact carry is actually being proven.
    raw[NativeHumanDataCodec.NativeSocialBlockOffset] = 0xA5;
    PutShortString(raw, 0x670, "$1:::::::::::::::::::$0$0$");
    raw[NativeHumanDataCodec.NativeSocialBlockOffset
        + NativeHumanDataCodec.NativeSocialBlockLength - 1] = 0xA6;
    var socialBefore = raw.AsSpan(
        NativeHumanDataCodec.NativeSocialBlockOffset,
        NativeHumanDataCodec.NativeSocialBlockLength).ToArray();

    var blob = new byte[raw.Length + 8];
    BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4), raw.Length);
    raw.CopyTo(blob, 8);
    Assert(NativeHumanDataCodec.TryDecode(blob, null, out var decoded,
        out var error), "native ShiMen decode failed: " + error);
    Assert(decoded.Data.boAllowMaster, "raw D9 allow-master flag decode");
    Assert(decoded.Data.boMaster, "raw DA master flag decode");
    Assert(decoded.Data.boStudent, "raw DC student flag decode");
    Equal((byte)5, decoded.Data.btStudentOrder, "raw DF student order");
    Equal((byte)3, decoded.Data.btStudentCount, "raw E0 student count");
    Assert(decoded.Data.NativeSocialBlob != null
           && decoded.Data.NativeSocialBlob.AsSpan()
               .SequenceEqual(socialBefore),
        "social block was not decoded verbatim");

    decoded.Data.boAllowMaster = false;
    decoded.Data.boMaster = false;
    decoded.Data.boStudent = true;
    decoded.Data.btStudentOrder = 3;
    decoded.Data.btStudentCount = 3;
    Assert(NativeHumanDataCodec.TryEncode(decoded, out var encoded,
        out var encodedScript, out error), "native ShiMen encode failed: " + error);
    Assert(NativeHumanDataCodec.TryDecode(encoded, encodedScript,
        out var nativeRoundTrip, out error),
        "native ShiMen round-trip failed: " + error);
    Assert(!nativeRoundTrip.Data.boAllowMaster,
        "raw D9 allow-master flag write");
    Assert(!nativeRoundTrip.Data.boMaster, "raw DA master flag write");
    Assert(nativeRoundTrip.Data.boStudent, "raw DC student flag write");
    Equal((byte)3, nativeRoundTrip.Data.btStudentOrder,
        "raw DF student order write");
    Equal((byte)3, nativeRoundTrip.Data.btStudentCount,
        "raw E0 student count write");
    Equal((byte)0xA1, nativeRoundTrip.NativeData[0xDE],
        "raw DE adjacent preservation");
    Equal((byte)0xA2, nativeRoundTrip.NativeData[0xDD],
        "raw DD adjacent preservation");
    Equal((byte)0xA3, nativeRoundTrip.NativeData[0xE1],
        "raw E1 adjacent preservation");
    // The whole 120-byte social region must come back byte-identical: this is
    // what the old 0x680 ShortString write destroyed (it cleared 0x680..0x690
    // and truncated the 0x670 string).
    Assert(nativeRoundTrip.NativeData.AsSpan(
                NativeHumanDataCodec.NativeSocialBlockOffset,
                NativeHumanDataCodec.NativeSocialBlockLength)
            .SequenceEqual(socialBefore),
        "social block lost fidelity across a native round trip");

    var transport = new THumDataInfo();
    transport.Data.boAllowMaster = true;
    transport.Data.boMaster = true;
    transport.Data.boStudent = true;
    transport.Data.btStudentOrder = 5;
    transport.Data.btStudentCount = 3;
    transport.Data.sStudentNames =
        new[] { "Student0", null, "Student2", "", "Student4" };
    var payload = ProtoBufDecoder.Serialize(transport);
    Assert(payload != null, "ShiMen protobuf serialization failed");
    var transported = ProtoBufDecoder.DeSerialize<THumDataInfo>(payload);
    Assert(transported?.Data != null, "ShiMen protobuf decode failed");
    Assert(transported.Data.boAllowMaster
           && transported.Data.boMaster && transported.Data.boStudent,
        "ShiMen protobuf flags");
    Equal((byte)5, transported.Data.btStudentOrder,
        "ShiMen protobuf order");
    Equal((byte)3, transported.Data.btStudentCount,
        "ShiMen protobuf count");
    AssertSparseNames(transported.Data.sStudentNames,
        "protobuf round trip");
}

static void TestLoginAndSaveMappings()
{
    var engine = new UserEngine();
    M2Share.UserEngine = engine;
    var record = new THumDataInfo();
    record.Header.dCreateDate = DateTime.Today.ToOADate();
    record.Data.sMasterName = "PersistedMaster";
    record.Data.boAllowMaster = true;
    record.Data.boMaster = true;
    record.Data.boStudent = true;
    record.Data.btStudentOrder = 4;
    record.Data.btStudentCount = 2;
    record.Data.sStudentNames =
        new[] { "Slot0", "", "", "Slot3", "" };

    var player = new TPlayObject();
    var load = typeof(UserEngine).GetMethod("GetHumData",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(load != null, "UserEngine.GetHumData was not found");
    load!.Invoke(engine, new object[] { player, record });
    Equal("PersistedMaster", player.m_sMasterName,
        "login master name");
    Assert(player.m_boAllowMaster
           && player.m_boMaster && player.m_boStudent,
        "login relation flags");
    Equal((byte)4, player.m_btStudentOrder, "login student order");
    Equal(2, player.m_nStudentCount, "login student count");
    AssertFixedSlots(player.m_sStudentNames, "login fixed slots");
    Equal("Slot3", player.m_sStudentNames[3], "login sparse slot 3");

    player.m_sMasterName = "SavedMaster";
    player.m_boMaster = false;
    player.m_boStudent = true;
    player.m_btStudentOrder = 3;
    player.m_nStudentCount = 3;
    player.m_sStudentNames =
        new[] { "Saved0", "", "Saved2", "", "Saved4" };
    var save = new THumDataInfo();
    player.MakeSaveRcd(ref save);
    Equal("SavedMaster", save.Data.sMasterName, "save master name");
    Assert(save.Data.boAllowMaster
           && !save.Data.boMaster && save.Data.boStudent,
        "save relation flags");
    Equal((byte)3, save.Data.btStudentOrder, "save student order");
    Equal((byte)3, save.Data.btStudentCount, "save student count");
    AssertSparseNames(save.Data.sStudentNames, "save sparse slots", "Saved");

    Assert(NativeHumanDataCodec.TryEncode(save, out var blob,
        out var script, out var error), "save native encode failed: " + error);
    Assert(NativeHumanDataCodec.TryDecode(blob, script, out var persisted,
        out error), "save native decode failed: " + error);
    Assert(persisted.Data.boAllowMaster
           && !persisted.Data.boMaster && persisted.Data.boStudent,
        "persisted relation flags");
    Equal((byte)3, persisted.Data.btStudentCount,
        "persisted sparse count");
    // sStudentNames are NOT recovered from the native codec until the 0x670
    // block grammar is fully reverse-engineered from 战神 (tracked follow-up).
    // What IS asserted here: the social block comes back byte-exact, proving
    // the names are PRESERVED in the blob even though they are not yet decoded.

    record.Data.btStudentCount = 0xFE;
    load.Invoke(engine, new object[] { player, record });
    Equal(0xFE, player.m_nStudentCount,
        "login raw E0 full-byte count");
    var fullByteSave = new THumDataInfo();
    player.MakeSaveRcd(ref fullByteSave);
    Equal((byte)0xFE, fullByteSave.Data.btStudentCount,
        "save raw E0 full-byte count");
    Assert(NativeHumanDataCodec.TryEncode(fullByteSave,
            out var fullByteBlob, out var fullByteScript, out error),
        "full-byte native encode failed: " + error);
    Assert(NativeHumanDataCodec.TryDecode(fullByteBlob, fullByteScript,
            out var fullByteRoundTrip, out error),
        "full-byte native decode failed: " + error);
    Equal((byte)0xFE, fullByteRoundTrip.Data.btStudentCount,
        "raw E0 full-byte native round trip");
}

static void TestNativeClearProtocol()
{
    Assert(NativeMasterRelationFrameCodec.TryEncodeClear(
            "AccountA", "师傅甲", "徒弟乙", out var encoded,
            out var error), "native 0152 clear encode failed: " + error);
    Assert(LegacyDbServerFrameCodec.TryDecode(encoded, out var frame,
            out error), "native 0152 frame decode failed: " + error);
    Assert(NativeMasterRelationProtocol.TryDecode(frame, out var request,
            out error), "DBSvr 0152 request decode failed: " + error);
    Equal(NativeMasterRelationProtocol.ClearSubcommand,
        request.Subcommand, "native 0152 clear subtype");
    Equal("AccountA", Encoding.GetEncoding(936).GetString(request.Account),
        "native 0152 account");
    Equal("师傅甲", Encoding.GetEncoding(936).GetString(request.MasterName),
        "native 0152 master");
    Equal("徒弟乙", Encoding.GetEncoding(936).GetString(request.StudentName),
        "native 0152 student");
}

static void TestRelationClientPacket()
{
    var process = new TProcessMessage
    {
        BaseObject = unchecked((int)0x89ABCDEF),
        wParam = 0x1234,
        nParam1 = unchecked((int)0xAAAA5678),
        nParam2 = unchecked((int)0xBBBB9ABC),
        nParam3 = unchecked((int)0xCCCCDEF0),
        sMsg = "师傅A"
    };
    var packetBuilder = typeof(TPlayObject).GetMethod(
        "BuildMasterRelationPacket",
        BindingFlags.Static | BindingFlags.NonPublic);
    var bodyBuilder = typeof(TPlayObject).GetMethod(
        "BuildMasterRelationBody",
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert(packetBuilder != null && bodyBuilder != null,
        "10126 client packet builders missing");
    var packet = (ClientPacket)packetBuilder!.Invoke(null,
        new object[] { process })!;
    var body = (byte[])bodyBuilder!.Invoke(null,
        new object[] { process })!;
    Equal((ushort)2820, packet.Ident, "10126 client ident");
    Equal(process.nParam1, packet.Recog, "10126 client Recog");
    Equal((ushort)0x1234, packet.Param, "10126 client Param");
    Equal((ushort)0x9ABC, packet.Tag, "10126 client Tag");
    Equal((ushort)0xDEF0, packet.Series, "10126 client Series");
    Assert(body.SequenceEqual(HUtil32.GetBytes("师傅A\0"))
           && body[^1] == 0, "10126 raw GBK NUL body");
}

static void TestAgreeFailures()
{
    var engine = new UserEngine();
    M2Share.UserEngine = engine;
    var master = NewPlayer("FailureMaster");
    var student = NewPlayer("FailureStudent");
    AddOnline(engine, master, student);
    var npc = new NormNpc();
    var bridge = new PasApiBridge
    {
        CurrentPlayer = master,
        CurrentNpc = npc
    };

    PrepareMasterRequest(master, student);
    master.m_dwMasterRequestTime = unchecked(
        HUtil32.GetTickCount() - 300000);
    Assert(bridge.CallPlayerFunc("AgreeBaishi", Values(), out var expired)
           && !expired.AsBool(), "AgreeBaishi expiry result");
    Assert(!master.m_boRequestMaster
           && master.m_MasterRequestTarget == null
           && master.m_dwMasterRequestTime == 0,
        "AgreeBaishi expiry master cleanup");
    Assert(student.m_boRequestMaster
           && ReferenceEquals(student.m_MasterRequestTarget, master),
        "AgreeBaishi failure changed target pending state");
    Assert(master.m_MsgList.Any(item =>
            item.wIdent == Grobal2.RM_MERCHANTDLGCLOSE
            && item.BaseObject == npc
            && item.nParam1 == npc.ObjectId),
        "AgreeBaishi failure NPC dialog cleanup");

    PrepareMasterRequest(master, student);
    student.m_boGhost = true;
    Assert(bridge.CallPlayerFunc("AgreeBaishi", Values(), out var ghost)
           && !ghost.AsBool(), "AgreeBaishi ghost result");
    Assert(!master.m_boRequestMaster
           && master.m_MasterRequestTarget == null
           && master.m_dwMasterRequestTime == 0,
        "AgreeBaishi ghost cleanup");
    student.m_boGhost = false;

    PrepareMasterRequest(master, student);
    student.m_boStudent = true;
    Assert(bridge.CallPlayerFunc("AgreeBaishi", Values(), out var related)
           && !related.AsBool(), "AgreeBaishi existing student result");
    Assert(!master.m_boRequestMaster
           && master.m_MasterRequestTarget == null
           && master.m_dwMasterRequestTime == 0,
        "AgreeBaishi existing student cleanup");
}

static void TestRequestBaishi()
{
    var cooldown = typeof(PasApiBridge).GetMethod(
        "IsMasterRequestCoolingDown",
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert(cooldown != null, "RequestBaishi cooldown helper missing");
    Assert((bool)cooldown!.Invoke(null, new object[] { 700000, 400000 })!,
        "RequestBaishi 300000ms boundary must reject");
    Assert(!(bool)cooldown.Invoke(null, new object[] { 700000, 399999 })!,
        "RequestBaishi 300001ms boundary must allow");
    Assert((bool)cooldown.Invoke(null,
            new object[] { int.MinValue + 10, int.MaxValue - 9 })!,
        "RequestBaishi unsigned tick wrap");
    var dialogBuilder = typeof(PasApiBridge).GetMethod(
        "BuildRequestBaiShiDialog",
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert(dialogBuilder != null, "RequestBaishi dialog builder missing");
    Equal(
        "RequestStudent 想拜你为师，请选择接受或拒绝!\\ \\<接受/@agrbaishi>\\ \\<拒绝/@disbaishi>",
        (string)dialogBuilder!.Invoke(null, new object[] { "RequestStudent" })!,
        "RequestBaishi target NPC dialog");

    var engine = new UserEngine();
    M2Share.UserEngine = engine;
    var applicant = NewPlayer("RequestStudent");
    var target = NewPlayer("RequestMaster");
    AddOnline(engine, applicant, target);
    var bridge = new PasApiBridge { CurrentPlayer = applicant };
    var args = new List<PasValue>
    {
        PasValue.FromString(target.m_sCharName)
    };

    void ResetRequestState()
    {
        applicant.m_boStudent = false;
        applicant.m_sMasterName = string.Empty;
        applicant.m_boMarried = false;
        applicant.m_Abil.Level = unchecked((ushort)
            M2Share.g_Config.nMaxApprenticeLevel);
        applicant.m_boRequestMaster = false;
        applicant.m_dwMasterRequestTime = 0;
        applicant.m_MasterRequestTarget = null;
        applicant.m_MsgList.Clear();
        target.m_boAllowMaster = true;
        target.m_boMaster = false;
        target.m_Abil.Level = unchecked((ushort)
            M2Share.g_Config.nMinMasterLevel);
        target.m_nStudentCount = 0;
        target.m_boRequestMaster = false;
        target.m_dwMasterRequestTime = 0;
        target.m_MasterRequestTarget = null;
        target.m_MsgList.Clear();
    }

    bool Request()
    {
        Assert(bridge.CallPlayerMethod("RequestBaishi", args),
            "RequestBaishi dispatch");
        return ReferenceEquals(applicant.m_MasterRequestTarget, target)
               && ReferenceEquals(target.m_MasterRequestTarget, applicant);
    }

    ResetRequestState();
    applicant.m_Abil.Level = unchecked((ushort)
        (M2Share.g_Config.nMaxApprenticeLevel + 1));
    Assert(!Request(), "RequestBaishi applicant upper level gate");
    AssertLatestSystemMessage(applicant,
        "[失败] 拜师必须满足：无师傅，等级不高于28",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor,
        "RequestBaishi applicant gate message");

    ResetRequestState();
    applicant.m_boMarried = true;
    applicant.m_sMasterName = "stale-name";
    Assert(Request(), "RequestBaishi added marriage/name rejection");
    Assert(target.m_boRequestMaster
           && !target.m_boMaster
           && ReferenceEquals(applicant.m_MasterRequestTarget, target)
           && ReferenceEquals(target.m_MasterRequestTarget, applicant)
           && applicant.m_dwMasterRequestTime == target.m_dwMasterRequestTime,
        "RequestBaishi reciprocal state");
    AssertLatestSystemMessage(applicant,
        "[成功]你的拜师请求已发出，正等候对方处理..",
        M2Share.g_Config.btRedMsgFColor,
        M2Share.g_Config.btRedMsgBColor,
        "RequestBaishi success message");

    ResetRequestState();
    target.m_boAllowMaster = false;
    target.m_boMaster = true;
    Assert(!Request(), "RequestBaishi target allow-master flag gate");
    AssertLatestSystemMessage(applicant,
        "[失败] 对方不在有效范围或对方已设置拒绝收徒",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor,
        "RequestBaishi target allow message");

    ResetRequestState();
    target.m_Abil.Level = unchecked((ushort)
        (M2Share.g_Config.nMinMasterLevel - 1));
    Assert(!Request(), "RequestBaishi target minimum level gate");
    AssertLatestSystemMessage(applicant,
        "[失败] 对方等级不够或弟子数已满",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor,
        "RequestBaishi target level message");

    ResetRequestState();
    target.m_nStudentCount = 5;
    Assert(!Request(), "RequestBaishi target student count gate");

    ResetRequestState();
    applicant.m_boRequestMaster = true;
    applicant.m_dwMasterRequestTime = HUtil32.GetTickCount();
    Assert(!Request(), "RequestBaishi applicant active cooldown");
    AssertLatestSystemMessage(applicant,
        "[失败] 你刚拜过师，请稍后再试",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor,
        "RequestBaishi applicant cooldown message");

    ResetRequestState();
    target.m_boRequestMaster = true;
    target.m_dwMasterRequestTime = HUtil32.GetTickCount();
    Assert(!Request(), "RequestBaishi target active cooldown");
    AssertLatestSystemMessage(applicant,
        "[失败] 对方正处理他人拜师，请稍后再试",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor,
        "RequestBaishi target cooldown message");

    ResetRequestState();
    var now = HUtil32.GetTickCount();
    applicant.m_boRequestMaster = true;
    applicant.m_dwMasterRequestTime = unchecked(now - 300001);
    target.m_boRequestMaster = true;
    target.m_dwMasterRequestTime = unchecked(now - 300001);
    Assert(Request(), "RequestBaishi expired reciprocal cooldown");
}

static void TestAllowMasterCommands()
{
    var command = typeof(TPlayObject).GetMethod(
        "TrySetAllowMasterCommand",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(command != null, "allow-master command helper missing");
    var player = NewPlayer("AllowMasterCommand");

    player.m_boAllowMaster = true;
    Assert((bool)command!.Invoke(player, new object[] { "拒绝收徒" })!,
        "deny-master command dispatch");
    Assert(!player.m_boAllowMaster, "deny-master command state");
    AssertLatestSystemMessage(player, "拒绝收徒 开",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor,
        "deny-master command message");

    player.m_MsgList.Clear();
    Assert((bool)command.Invoke(player, new object[] { "允许收徒" })!,
        "allow-master command dispatch");
    Assert(player.m_boAllowMaster, "allow-master command state");
    AssertLatestSystemMessage(player, "允许收徒 开",
        M2Share.g_Config.btGreenMsgFColor,
        M2Share.g_Config.btGreenMsgBColor,
        "allow-master command message");

    Assert(!(bool)command.Invoke(player, new object[] { "unknown" })!,
        "allow-master unknown command");
}

static void TestPasRelationships()
{
    ClearQueuedSaves();
    var engine = new UserEngine();
    M2Share.UserEngine = engine;
    var teacher = NewPlayer("Teacher");
    var student = NewPlayer("Student");
    teacher.m_Abil.Level = (ushort)M2Share.g_Config.nMinMasterLevel;
    teacher.m_sStudentNames =
        new[] { "Existing0", "", "Existing2", "", "" };
    teacher.m_nStudentCount = 2;
    teacher.m_boMaster = false;
    teacher.m_boRequestMaster = true;
    teacher.m_MasterRequestTarget = student;
    teacher.m_dwMasterRequestTime = HUtil32.GetTickCount();
    student.m_boRequestMaster = true;
    student.m_MasterRequestTarget = teacher;
    student.m_dwMasterRequestTime = HUtil32.GetTickCount();
    AddOnline(engine, teacher, student);

    var bridge = new PasApiBridge
    {
        CurrentPlayer = teacher,
        CurrentNpc = null
    };
    Assert(bridge.CallPlayerMethod("AgreeBaishi", Values()),
        "AgreeBaishi was not dispatched");
    Equal("Existing0", teacher.m_sStudentNames[0],
        "AgreeBaishi slot 0 preservation");
    Equal("Student", teacher.m_sStudentNames[1],
        "AgreeBaishi first empty slot");
    Equal("Existing2", teacher.m_sStudentNames[2],
        "AgreeBaishi slot 2 preservation");
    Equal(3, teacher.m_nStudentCount, "AgreeBaishi count");
    Assert(teacher.m_boMaster,
        "AgreeBaishi native master relation flag");
    Equal(0, teacher.m_MasterList.Count,
        "AgreeBaishi used m_MasterList fallback");
    Assert(student.m_boStudent, "AgreeBaishi student flag");
    Equal((byte)2, student.m_btStudentOrder,
        "AgreeBaishi student order");
    Equal("Teacher", student.m_sMasterName,
        "AgreeBaishi master direction");
    Assert(ReferenceEquals(teacher, student.m_MasterHuman),
        "AgreeBaishi runtime master link");
    Assert(!teacher.m_boRequestMaster
           && teacher.m_MasterRequestTarget == null
           && teacher.m_dwMasterRequestTime == 0,
        "AgreeBaishi master request cleanup");
    Assert(!student.m_boRequestMaster
           && student.m_dwMasterRequestTime == 0,
        "AgreeBaishi student request cleanup");
    Assert(ReferenceEquals(student.m_MasterRequestTarget, teacher),
        "AgreeBaishi preserved target reciprocal pointer");
    AssertQueuedRelation(student, 8, teacher.m_sCharName,
        "AgreeBaishi relation message");
    AssertQueuedSave(student, "AgreeBaishi immediate save");

    student.m_sMasterName = string.Empty;
    bridge.CurrentPlayer = student;
    Assert(bridge.GetPlayerProperty("IsAStudent", out var property)
           && property.AsBool(), "IsAStudent property did not use flag");
    Assert(bridge.CallPlayerFunc("IsAStudent", Values(), out var function)
           && function.AsBool(), "IsAStudent function did not use flag");
    student.m_sMasterName = teacher.m_sCharName;

    teacher.m_boMaster = true;
    teacher.m_nGold = 100000;
    bridge.CurrentPlayer = NewPlayer("AmbientPlayer");
    bridge.CurrentNpc = new NormNpc();
    Assert(bridge.CallNpcMethod("NpcKickOutStu", new List<PasValue>
        {
            PasValue.FromObject(teacher), PasValue.FromInt(1)
        }, out _), "NpcKickOutStu was not dispatched");
    Equal("Existing0", teacher.m_sStudentNames[0],
        "kick slot 0 preservation");
    Equal(string.Empty, teacher.m_sStudentNames[1],
        "kick exact slot clear");
    Equal("Existing2", teacher.m_sStudentNames[2],
        "kick did not compact slot 2");
    Equal(2, teacher.m_nStudentCount, "kick count decrement");
    Equal(0, teacher.m_nGold, "kick fee timing");
    Assert(teacher.m_boMaster, "kick changed independent boMaster");
    Assert(!student.m_boStudent && student.m_btStudentOrder == 0
           && student.m_sMasterName == string.Empty
           && student.m_MasterHuman == null
           && student.m_MasterRequestTarget == null,
        "kick online student clear");
    AssertQueuedRelation(student, 9, teacher.m_sCharName,
        "kick relation message");
    AssertQueuedSave(student, "kick immediate save");

    teacher.m_sStudentNames[4] = student.m_sCharName;
    teacher.m_nStudentCount = 3;
    student.m_boStudent = true;
    student.m_btStudentOrder = 5;
    student.m_sMasterName = teacher.m_sCharName;
    student.m_MasterHuman = teacher;
    bridge.CurrentPlayer = student;

    // 战神 sub_6CAFF0 @0x6CB003 `mov edx,0xC350` / `mov eax,ebx` (ebx = the PAS
    // player, i.e. the STUDENT) / call sub_6C7D64 (DecGold).  Walking out costs
    // the student a flat 50,000 gold, and a student who cannot pay is refused
    // with 0x6CB048 "你尚无师承或携带的金币不够, 不能离开！" -- 0x6CB011 `je
    // 0x6CB01E` skips sub_6C5EC8 entirely.  This fixture predated the gate.
    student.m_nGold = 0;
    Assert(bridge.CallPlayerMethod("NpcLeaveTec", Values()),
        "NpcLeaveTec was not dispatched (broke student)");
    Equal(student.m_sCharName, teacher.m_sStudentNames[4],
        "leave must be REFUSED when the student cannot pay 0xC350");
    Assert(student.m_boStudent,
        "0x6CB011: a refused leave must not tear the relation down");
    Equal(0, student.m_nGold, "refused leave must not bill the student");

    student.m_nGold = 60000;
    Assert(bridge.CallPlayerMethod("NpcLeaveTec", Values()),
        "NpcLeaveTec was not dispatched");
    Equal(10000, student.m_nGold,
        "0x6CB003: a successful leave charges exactly 0xC350");
    Equal(string.Empty, teacher.m_sStudentNames[4],
        "leave exact slot clear");
    Equal("Existing2", teacher.m_sStudentNames[2],
        "leave did not compact sparse slots");
    Equal(2, teacher.m_nStudentCount, "leave count decrement");
    Assert(teacher.m_boMaster, "leave changed independent boMaster");
    Assert(!student.m_boStudent && student.m_btStudentOrder == 0
           && student.m_sMasterName == string.Empty
           && student.m_MasterHuman == null,
        "leave student relation clear");
}

static void TestKickEdgeCases()
{
    var engine = new UserEngine();
    M2Share.UserEngine = engine;
    var master = NewPlayer("KickMaster");
    var student = NewPlayer("KickStudent");
    AddOnline(engine, master, student);
    var bridge = new PasApiBridge
    {
        CurrentPlayer = master,
        CurrentNpc = new NormNpc()
    };

    master.m_sStudentNames = new[] { student.m_sCharName, "", "", "", "" };
    master.m_nStudentCount = 1;
    master.m_nGold = 99999;
    Assert(bridge.CallNpcMethod("NpcKickOutStu", Values(0), out _),
        "kick insufficient dispatch");
    Equal(99999, master.m_nGold, "kick insufficient no charge");
    Equal(student.m_sCharName, master.m_sStudentNames[0],
        "kick insufficient no clear");

    master.m_nStudentCount = 0;
    master.m_nGold = 100000;
    Assert(bridge.CallNpcMethod("NpcKickOutStu", Values(0), out _),
        "kick zero-count dispatch");
    Equal(100000, master.m_nGold, "kick zero-count no charge");
    Equal(student.m_sCharName, master.m_sStudentNames[0],
        "kick zero-count no clear");

    master.m_nStudentCount = 1;
    Assert(bridge.CallNpcMethod("NpcKickOutStu", Values(5), out _),
        "kick invalid-index dispatch");
    Equal(0, master.m_nGold, "kick invalid index charges first");
    Equal(student.m_sCharName, master.m_sStudentNames[0],
        "kick invalid index no clear");

    master.m_nGold = 100000;
    student.m_boStudent = true;
    student.m_btStudentOrder = 1;
    student.m_sMasterName = "kickmaster";
    Assert(bridge.CallNpcMethod("NpcKickOutStu", Values(0), out _),
        "kick case mismatch dispatch");
    Equal(0, master.m_nGold, "kick case mismatch charge");
    Equal(0, master.m_nStudentCount, "kick case mismatch count clear");
    Equal(string.Empty, master.m_sStudentNames[0],
        "kick case mismatch slot clear");
    Assert(student.m_boStudent && student.m_sMasterName == "kickmaster",
        "kick case mismatch changed online relation");

    master.m_sStudentNames = new[] { "Sparse0", "", "Sparse2", "", "" };
    master.m_nStudentCount = 2;
    master.m_nGold = 100000;
    var build = typeof(PasApiBridge).GetMethod("BuildKaiChuDialog",
        BindingFlags.Static | BindingFlags.NonPublic);
    Assert(build != null, "SendKaiChuList builder missing");
    Equal("请选择你要开除的徒弟\\<Sparse0/@kaichu_M0>" +
          "\\<Sparse2/@kaichu_M2>",
        (string)build!.Invoke(null, new object[] { master })!,
        "SendKaiChuList exact sparse dialog");
    master.m_sStudentNames = new[] { "", "", "", "", "" };
    master.m_nStudentCount = 1;
    Equal("请选择你要开除的徒弟",
        (string)build.Invoke(null, new object[] { master })!,
        "SendKaiChuList positive count with empty sparse slots");
}

static void TestShiMenFlyModes()
{
    TestShiMenFlyMode1AndInvalidMap();
    TestShiMenFlyMode2();
    TestShiMenFlyMode3();
}

static void TestShiMenFlyMode1AndInvalidMap()
{
    var engine = ResetSpatialRuntime();
    var source = NewEnvironment("ShiMen1Source");
    var destination = NewEnvironment("ShiMen1Destination");
    var otherEnvironment = NewEnvironment("ShiMen1Other");
    RegisterMap(source, destination, otherEnvironment);

    var master = NewMappedPlayer("FlyMaster1", source, 10, 10);
    var student = NewMappedPlayer("FlyStudent1", source, 11, 10);
    var ghost = NewMappedPlayer("FlyGhost1", source, 12, 10);
    ghost.m_boGhost = true;
    var dead = NewMappedPlayer("FlyDead1", source, 13, 10);
    dead.m_boDeath = true;
    var other = NewMappedPlayer("FlyOther1", otherEnvironment, 10, 10);
    AddOnline(engine, master, student, ghost, dead, other);
    master.m_sStudentNames = new[]
    {
        student.m_sCharName, "", ghost.m_sCharName,
        dead.m_sCharName, other.m_sCharName
    };
    master.m_nStudentCount = 4;
    var bridge = new PasApiBridge { CurrentPlayer = master };

    var masterBefore = Position(master);
    var studentBefore = Position(student);
    Assert(bridge.CallPlayerMethod("ShiMenFly",
        FlyValues("MissingShiMenMap", 1)), "invalid mode 1 dispatch");
    AssertPosition(master, masterBefore, "invalid map master no-op");
    AssertPosition(student, studentBefore, "invalid map student no-op");

    Assert(bridge.CallPlayerMethod("ShiMenFly",
        FlyValues(destination.sMapName, 1)), "mode 1 dispatch");
    Assert(ReferenceEquals(destination, master.m_PEnvir),
        "mode 1 master destination");
    Assert(ReferenceEquals(destination, student.m_PEnvir),
        "mode 1 live student destination");
    Assert(ReferenceEquals(destination, ghost.m_PEnvir),
        "mode 1 added a ghost exclusion");
    Assert(ReferenceEquals(source, dead.m_PEnvir),
        "mode 1 moved dead student");
    Assert(ReferenceEquals(otherEnvironment, other.m_PEnvir),
        "mode 1 moved student from another environment");
}

static void TestShiMenFlyMode2()
{
    var engine = ResetSpatialRuntime();
    var source = NewEnvironment("ShiMen2Source");
    var destination = NewEnvironment("ShiMen2Destination");
    RegisterMap(source, destination);
    var student = NewMappedPlayer("FlyStudent2", source, 10, 10);
    var master = NewMappedPlayer("FlyMaster2", source, 11, 10);
    student.m_boStudent = true;
    student.m_sMasterName = master.m_sCharName;
    AddOnline(engine, student, master);

    var bridge = new PasApiBridge { CurrentPlayer = student };
    Assert(bridge.CallPlayerMethod("ShiMenFly",
        FlyValues(destination.sMapName, 2)), "mode 2 dispatch");
    Assert(ReferenceEquals(destination, student.m_PEnvir),
        "mode 2 student destination");
    Assert(ReferenceEquals(destination, master.m_PEnvir),
        "mode 2 master follows student");
}

static void TestShiMenFlyMode3()
{
    var engine = ResetSpatialRuntime();
    var source = NewEnvironment("ShiMen3Source");
    var destination = NewEnvironment("ShiMen3Destination");
    RegisterMap(source, destination);
    var student = NewMappedPlayer("FlyStudent3", source, 10, 10);
    var master = NewMappedPlayer("FlyMaster3", source, 11, 10);
    student.m_boStudent = true;
    student.m_sMasterName = master.m_sCharName;
    master.m_nStudentCount = 1;
    master.m_sStudentNames =
        new[] { student.m_sCharName, "", "", "", "" };
    AddOnline(engine, student, master);

    var bridge = new PasApiBridge { CurrentPlayer = student };
    Assert(bridge.CallPlayerMethod("ShiMenFly",
        FlyValues(destination.sMapName, 3)), "mode 3 dispatch");
    Assert(ReferenceEquals(destination, master.m_PEnvir),
        "mode 3 master destination");
    Assert(ReferenceEquals(destination, student.m_PEnvir),
        "mode 3 master brought students");
}

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var packet = Read(root, "SystemModule", "Packet", "THumDataInfo.cs");
    var codec = Read(root, "DBSvr", "Core", "NativeHumanDataCodec.cs");
    var loader = Read(root, "GameSvr", "UsrSystem", "UsrEngn.cs");
    var saver = Read(root, "GameSvr", "Players", "TPlayObject.cs");
    var message = Read(root, "GameSvr", "Players",
        "TPlayObject.Message.cs");
    var relationCodec = Read(root, "SystemModule", "Packet",
        "NativeMasterRelationFrameCodec.cs");
    var bridge = Read(root, "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.cs");
    var serverConfig = Read(root, "GameSvr", "Configs", "ServerConfig.cs");

    Require(packet, "[ProtoMember(75, OverwriteList = true)]",
        "fixed student-name transport tag");
    Require(packet, "[ProtoMember(76)]",
        "allow-master transport tag");
    Require(codec, "AllowMasterFlagOffset = 0x00D9",
        "native allow-master offset");
    Require(codec, "MasterFlagOffset = 0x00DA",
        "native boMaster offset");
    // 2026-08-07: the codec must NOT read/write student names as standalone
    // ShortStrings at 0x680.  raw[0x680] is content of the ':'/'$' companion
    // string kept at 0x670 (byte 0x3A = ':' in 30/30 real records), so reading it
    // as a ShortString length threw "58 exceeds 15" and rejected every character.
    // 战神 block-copies the whole social region inflated[0x650..0x6CF] <->
    // obj+0xc48 (load 0x6B096C, save 0x6B1687) and never re-reads names out of
    // those record slots itself.
    Require(codec, "NativeSocialBlockOffset = 0x0650",
        "native social block base");
    Reject(codec, "WriteShortString(raw, StudentNameBaseOffset",
        "fabricated student-name write at 0x680");
    Reject(codec, "ReadShortString(raw,\n                        StudentNameBaseOffset",
        "fabricated student-name read at 0x680");
    Require(loader, "PlayObject.m_btStudentOrder = HumData.btStudentOrder",
        "student order login mapping");
    Require(loader, "PlayObject.m_nStudentCount = HumData.btStudentCount",
        "full-byte student count login mapping");
    Require(loader, "PlayObject.m_boAllowMaster = HumData.boAllowMaster",
        "allow-master login mapping");
    Require(saver, "HumData.btStudentCount = unchecked((byte)m_nStudentCount)",
        "student count save mapping");
    Require(saver, "HumData.boAllowMaster = m_boAllowMaster",
        "allow-master save mapping");
    Require(message, "Grobal2.SM_MASTERRELATION,",
        "10126 client ident mapping");
    Require(message, "processMessage.nParam1, processMessage.wParam",
        "10126 client header Recog mapping");
    Require(message, "HUtil32.LoWord(processMessage.nParam2)",
        "10126 client header Tag mapping");
    Require(message, "HUtil32.LoWord(processMessage.nParam3)",
        "10126 client header Series mapping");
    Require(message, "(processMessage.sMsg ?? string.Empty) + \"\\0\"",
        "10126 raw GBK NUL body");
    Require(relationCodec, "new LegacyDbServerFrame(1, 0, payload)",
        "0152 Type1 envelope");
    Require(relationCodec, "ClearSubcommand = 3",
        "0152 clear subtype");
    Require(bridge, "NativeMasterRelationFrameCodec.RequestCommand, 0, 0, 0, 0",
        "0152 outer request command");
    Require(serverConfig, "SETKEY_SHOUTU",
        "FormGMSet master level load");
    Require(serverConfig, "SETKEY_CHUSHI",
        "FormGMSet graduation level load");
    Require(serverConfig, "SETKEY_BAISHI",
        "FormGMSet apprentice level load");

    var request = CaseBody(bridge, "requestbaishi", "incpkpoint");
    Require(request, "nMaxApprenticeLevel",
        "RequestBaishi applicant upper level gate");
    Require(request, "target == null || !target.m_boAllowMaster",
        "RequestBaishi target allow-master gate");
    Require(request, "IsMasterRequestCoolingDown",
        "RequestBaishi reciprocal cooldown gate");
    Reject(request, "m_boMarried", "RequestBaishi marriage rejection");
    Reject(request, "m_sMasterName", "RequestBaishi stale-name rejection");
    Reject(request, "!target.m_boMaster",
        "RequestBaishi relation flag used as permission");

    var agree = CaseBody(bridge, "agreebaishi", "disagreebaishi");
    Require(agree, "TryAgreeBaiShi()",
        "AgreeBaishi exact transaction helper");
    Require(bridge, "FindEmptyStudentSlot(master)",
        "AgreeBaishi first-empty-slot logic");
    Require(bridge, "(uint)(HUtil32.GetTickCount()",
        "AgreeBaishi unsigned expiry check");
    Require(bridge, "Grobal2.RM_MASTERRELATION, 0, 8",
        "AgreeBaishi native mode 8 notification");
    Require(bridge, "SaveShiMenPlayer(student)",
        "AgreeBaishi immediate student save");
    Require(bridge, "CloseShiMenDialog(master)",
        "AgreeBaishi 10127 dialog cleanup");
    Reject(agree, "m_MasterList", "AgreeBaishi m_MasterList fallback");
    Require(bridge, "master.m_boMaster = true",
        "AgreeBaishi native master relation flag");
    var list = CaseBody(bridge, "sendkaichulist", "npckickoutstu");
    Require(list, "你携带的金币数量不够！",
        "SendKaiChuList insufficient-gold dialog");
    Require(list, "你没有徒弟！",
        "SendKaiChuList empty dialog");
    Require(bridge, "请选择你要开除的徒弟",
        "SendKaiChuList title");
    Require(bridge, "/@kaichu_M{i}>",
        "SendKaiChuList sparse slot labels");
    var kick = CaseBody(bridge, "npckickoutstu", "chgcelebcolor");
    Require(kick, "kickMaster.m_nGold -= 100000",
        "kick fee timing");
    Require(kick, "StringComparison.Ordinal",
        "kick exact master-name validation");
    Require(kick, "Grobal2.RM_MASTERRELATION, 0, 9",
        "kick native mode 9 notification");
    Require(kick, "TryClearOfflineStudentRelation",
        "kick offline DB relation update");
    Require(kick, "手续费我收下了！但你使用的手段...嘿嘿，我帮不了你",
        "kick charged failure dialog");
    Require(kick, "你的师傅已将你逐出师门",
        "kick student notification");
    Require(kick, "操作成功！", "kick success dialog");
    Reject(kick, "m_nStudentCount - 1", "kick slot compaction");
    Reject(kick, "stuIdx >= CurrentPlayer.m_nStudentCount",
        "kick sparse-slot count gate");
    var fly = MethodBody(bridge, "private void MoveShiMenToMap",
        "// =====================================================================");
    Require(fly, "FindMap(mapName)", "ShiMen invalid-map guard");
    Require(fly, "GetPlayObjectEx", "ShiMen online lookup without ghost filter");
    Reject(fly, "m_MasterList", "ShiMen m_MasterList fallback");
}

static UserEngine ResetSpatialRuntime()
{
    M2Share.MapManager = new MapManager();
    var engine = new UserEngine();
    M2Share.UserEngine = engine;
    return engine;
}

static Envirnoment NewEnvironment(string mapName)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapName,
        nServerIndex = 0
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)64, (short)64 });
    return environment;
}

static void RegisterMap(params Envirnoment[] environments)
{
    var maps = (IDictionary<string, Envirnoment>)typeof(MapManager)
        .GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(M2Share.MapManager)!;
    foreach (var environment in environments)
        maps.Add(environment.sMapName, environment);
}

static TPlayObject NewMappedPlayer(string name, Envirnoment environment,
    short x, short y)
{
    var player = NewPlayer(name);
    player.m_PEnvir = environment;
    player.m_sMapName = environment.sMapName;
    player.m_sMapFileName = environment.m_sMapFileName;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boAddToMaped = false;
    player.m_boDelFormMaped = false;
    Assert(ReferenceEquals(player, environment.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, player)), "place " + name);
    return player;
}

static TPlayObject NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

static void PrepareMasterRequest(TPlayObject master, TPlayObject student)
{
    master.m_Abil.Level = (ushort)M2Share.g_Config.nMinMasterLevel;
    master.m_boRequestMaster = true;
    master.m_MasterRequestTarget = student;
    master.m_dwMasterRequestTime = HUtil32.GetTickCount();
    student.m_boRequestMaster = true;
    student.m_MasterRequestTarget = master;
    student.m_dwMasterRequestTime = HUtil32.GetTickCount();
}

static void AssertQueuedRelation(TPlayObject player, int mode,
    string masterName, string message)
{
    Assert(player.m_MsgList.Any(item =>
            item.wIdent == Grobal2.RM_MASTERRELATION
            && item.nParam1 == mode
            && item.Buff == masterName), message);
}

static void AssertLatestSystemMessage(TPlayObject player, string text,
    int foreground, int background, string message)
{
    var systemMessages = player.m_MsgList.Where(item =>
        item.wIdent == Grobal2.RM_SYSMESSAGE).ToArray();
    Assert(systemMessages.Length > 0, message + " missing");
    var systemMessage = systemMessages[^1];
    Equal(text, systemMessage.Buff, message + " text");
    Equal(foreground, systemMessage.nParam1, message + " foreground");
    Equal(background, systemMessage.nParam2, message + " background");
}

static IList<TSaveRcd> QueuedSaves() =>
    (IList<TSaveRcd>)typeof(TFrontEngine).GetField("m_SaveRcdList",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(M2Share.FrontEngine)!;

static void ClearQueuedSaves() => QueuedSaves().Clear();

static void AssertQueuedSave(TPlayObject player, string message) =>
    Assert(QueuedSaves().Any(save => ReferenceEquals(save.PlayObject, player)),
        message);

static void AddOnline(UserEngine engine, params TPlayObject[] players)
{
    var list = (IList<TPlayObject>)typeof(UserEngine)
        .GetField("m_PlayObjectList", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(engine)!;
    foreach (var player in players)
        if (!list.Contains(player)) list.Add(player);
}

static (Envirnoment Environment, short X, short Y) Position(TPlayObject player) =>
    (player.m_PEnvir, player.m_nCurrX, player.m_nCurrY);

static void AssertPosition(TPlayObject player,
    (Envirnoment Environment, short X, short Y) expected, string message)
{
    Assert(ReferenceEquals(expected.Environment, player.m_PEnvir)
           && expected.X == player.m_nCurrX && expected.Y == player.m_nCurrY,
        message);
}

static void PutShortString(byte[] destination, int offset, string value)
{
    var bytes = Encoding.ASCII.GetBytes(value);
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination, offset + 1);
}

static void AssertSparseNames(string[] names, string message,
    string prefix = "Student")
{
    AssertFixedSlots(names, message);
    Equal(prefix + "0", names[0], message + " slot 0");
    Equal(string.Empty, names[1], message + " slot 1");
    Equal(prefix + "2", names[2], message + " slot 2");
    Equal(string.Empty, names[3], message + " slot 3");
    Equal(prefix + "4", names[4], message + " slot 4");
}

static void AssertFixedSlots(string[] names, string message) =>
    Assert(names != null && names.Length == 5, message + " length");

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static List<PasValue> FlyValues(string text, int value) =>
    new() { PasValue.FromString(text), PasValue.FromInt(value) };

static string Read(string root, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

static string CaseBody(string source, string caseName, string nextCaseName)
{
    var startToken = $"case \"{caseName}\":";
    var endToken = $"case \"{nextCaseName}\":";
    var start = source.IndexOf(startToken, StringComparison.OrdinalIgnoreCase);
    var end = source.IndexOf(endToken, start + startToken.Length,
        StringComparison.OrdinalIgnoreCase);
    Assert(start >= 0 && end > start, "PAS case source: " + caseName);
    return source[start..end];
}

static string MethodBody(string source, string startToken, string endToken)
{
    var start = source.IndexOf(startToken, StringComparison.Ordinal);
    var end = source.IndexOf(endToken, start + startToken.Length,
        StringComparison.Ordinal);
    Assert(start >= 0 && end > start, "method source: " + startToken);
    return source[start..end];
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig
    {
        nMinMasterLevel = 35,
        nMasterOKLevel = 35,
        nMaxApprenticeLevel = 28
    };
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.FrontEngine = new TFrontEngine();
    M2Share.UserEngine = new UserEngine();
    M2Share.CastleManager = new CastleManager();
    M2Share.LogSystem = new MirLog();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
    M2Share.nServerIndex = 0;
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
    File.WriteAllText(Path.Combine(runtimeDirectory, "FormGMSet.ini"),
        "SETKEY_SHOUTU=35" + Environment.NewLine
        + "SETKEY_CHUSHI=35" + Environment.NewLine
        + "SETKEY_BAISHI=28" + Environment.NewLine,
        HUtil32.GbkEncoding);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory, AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " is missing");
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(message + " is present");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

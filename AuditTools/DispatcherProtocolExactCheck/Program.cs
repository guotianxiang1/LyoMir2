using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();

CheckAbilityPackets();
CheckFeatureChanged();
CheckCry();
CheckWhisper();
CheckGoldChanged();
CheckChangeLight();
CheckGroupCancel();
CheckUsersRepair();
CheckThirdBatchDispatcher();
CheckMerchantPayloadProducers();
CheckExactSequenceSource();

Console.WriteLine(
    "DispatcherProtocolExactCheck PASS ability=52/184 feature=41/10 cry=102 whisper=103/param=0xFFFC/tag=level/series=nParam1/recog=speaker gold=653 light=654 group=666,659 repair=10152 third-batch=645,652,1103,1104,1105");

static void CheckAbilityPackets()
{
    var player = NewPlayer();
    player.m_nGold = 123456;
    player.m_nGameGold = unchecked((int)0x76543210);
    player.m_btJob = 2;

    Assert(player.Operate(new TProcessMessage { wIdent = Grobal2.RM_ABILITY }),
        "RM_ABILITY dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_ABILITY, player.m_nGold, 2, 0, 0,
        "RM_ABILITY");
    Equal(184, player.AbilityBody().Length, "SM_ABILITY body size");

    player.m_Abil.Level = 77;
    player.m_Abil.Exp = 987654;
    Assert(player.Operate(new TProcessMessage { wIdent = Grobal2.RM_LEVELUP }),
        "RM_LEVELUP dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_ABILITY, player.m_nGold, 2, 0, 0,
        "RM_LEVELUP trailing ability");
}

static void CheckFeatureChanged()
{
    var actor = new ProbeActor
    {
        m_boObMode = true,
        m_PEnvir = new Envirnoment(),
        m_btRaceServer = Grobal2.RC_ANIMAL,
        m_btRaceImg = 0x7A,
        m_btGender = PlayGender.WoMan,
        m_btHair = 0x22,
        m_btMonsterWeapon = 0x34,
        m_wAppr = 0x5678,
        m_boOnHorse = true,
        m_btHorseType = 0x9A
    };
    var expectedFeature = actor.GetFeatureToLong();
    using var stream = new MemoryStream(10);
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
    {
        writer.Write((ushort)actor.m_btRaceImg);
        writer.Write((byte)1);
        writer.Write(actor.m_btHair);
        writer.Write((ushort)actor.m_btMonsterWeapon);
        writer.Write(actor.m_wAppr);
        writer.Write((ushort)actor.m_btHorseType);
    }
    var expectedBody = stream.ToArray();

    actor.FeatureChanged();
    TProcessMessage queued = null;
    Assert(actor.TryTake(ref queued), "RM_FEATURECHANGED was not queued");
    Equal(Grobal2.RM_FEATURECHANGED, queued.wIdent, "queued feature ident");
    Equal(0, queued.wParam, "queued feature wParam");
    Equal(expectedFeature, queued.nParam1, "queued feature nParam1");
    var actualBody = queued.Payload as byte[];
    Assert(actualBody != null, "queued feature body type");
    Equal(10, actualBody.Length, "queued feature body size");
    Assert(expectedBody.SequenceEqual(actualBody), "queued non-player feature bytes");

    actor.m_wAppr = 0;
    Assert(expectedBody.SequenceEqual(actualBody), "queued feature snapshot mutated");

    var player = NewPlayer();
    Assert(player.Operate(queued), "RM_FEATURECHANGED dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_FEATURECHANGED, actor.ObjectId,
        HUtil32.LoWord(expectedFeature), HUtil32.HiWord(expectedFeature), 0,
        "RM_FEATURECHANGED");
}

static void CheckCry()
{
    var player = NewPlayer();
    const int sourceObjectId = 0x12345678;
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_CRY,
        BaseObject = sourceObjectId,
        nParam1 = 0x12,
        nParam2 = 0x34,
        sMsg = "cry"
    }), "RM_CRY dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_CRY, sourceObjectId, 0x9700, 0, 1,
        "RM_CRY");
}

static void CheckWhisper()
{
    // Ident 103 has exactly one send point in the whole image, the RM 10031 arm:
    //   0x6B4AE4 68 FC FF 00 00     push 0xFFFC        -> Param  (literal)
    //   0x6B4AE9 66 8B 43 02        mov ax,[ebx+2]     -> Tag    = wParam
    //   0x6B4AEE 66 8B 43 04        mov ax,[ebx+4]     -> Series = nParam1
    //   0x6B4AFC 8B 4B 24           mov ecx,[ebx+0x24] -> Recog  = BaseObject
    //   0x6B4AFF 66 BA 67 00        mov dx,0x67
    //   0x6B4B08 FF 93 54 02 00 00  call [VMT+0x254]
    // wParam is the speaker's level: the producer at 0x6C95F6 loads
    // word[speaker+0x278], and +0x278 is the level because 0x68790C
    // (66 FF 83 78 02 00 00 inc word [ebx+0x278]) is gated on
    // 0x6878FB cmp word [ebx+0x278],0xC8 - the level cap.
    var player = NewPlayer();
    const int speakerObjectId = 0x0BADF00D;

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_WHISPER,
        BaseObject = speakerObjectId,
        wParam = 42,
        nParam1 = 0,
        sMsg = "whisper"
    }), "RM_WHISPER dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_WHISPER, speakerObjectId, 0xFFFC, 42, 0,
        "RM_WHISPER");

    // Param is an immediate on the only arm, so no config colour may reach it.
    // The GM and purple tiers this tree used to carry had no native counterpart;
    // 0x38FF (btGMWhisperMsg*) is the colour of the "聆听私聊" monitor SysMsg at
    // 0x6B4AD8 / 0x6C963C, not of the whisper packet.
    var whisperF = M2Share.g_Config.btWhisperMsgFColor;
    var whisperB = M2Share.g_Config.btWhisperMsgBColor;
    var gmF = M2Share.g_Config.btGMWhisperMsgFColor;
    var gmB = M2Share.g_Config.btGMWhisperMsgBColor;
    var purpleF = M2Share.g_Config.btPurpleMsgFColor;
    var purpleB = M2Share.g_Config.btPurpleMsgBColor;
    try
    {
        M2Share.g_Config.btWhisperMsgFColor = 0x11;
        M2Share.g_Config.btWhisperMsgBColor = 0x22;
        M2Share.g_Config.btGMWhisperMsgFColor = 0x33;
        M2Share.g_Config.btGMWhisperMsgBColor = 0x44;
        M2Share.g_Config.btPurpleMsgFColor = 0x55;
        M2Share.g_Config.btPurpleMsgBColor = 0x66;

        Assert(player.Operate(new TProcessMessage
        {
            wIdent = Grobal2.RM_WHISPER,
            BaseObject = speakerObjectId,
            wParam = 200,
            nParam1 = 0,
            sMsg = "whisper"
        }), "RM_WHISPER dispatcher result (recoloured config)");
        Packet(player.m_DefMsg, Grobal2.SM_WHISPER, speakerObjectId, 0xFFFC, 200, 0,
            "RM_WHISPER colour-independent");
    }
    finally
    {
        M2Share.g_Config.btWhisperMsgFColor = whisperF;
        M2Share.g_Config.btWhisperMsgBColor = whisperB;
        M2Share.g_Config.btGMWhisperMsgFColor = gmF;
        M2Share.g_Config.btGMWhisperMsgBColor = gmB;
        M2Share.g_Config.btPurpleMsgFColor = purpleF;
        M2Share.g_Config.btPurpleMsgBColor = purpleB;
    }

    // Series tracks nParam1 rather than the constant 1 this tree used to send.
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_WHISPER,
        BaseObject = speakerObjectId,
        wParam = 7,
        nParam1 = 5,
        sMsg = "whisper"
    }), "RM_WHISPER dispatcher result (series probe)");
    Packet(player.m_DefMsg, Grobal2.SM_WHISPER, speakerObjectId, 0xFFFC, 7, 5,
        "RM_WHISPER series follows nParam1");
}

static void CheckGoldChanged()
{
    var player = NewPlayer();
    player.m_nGold = 345678;
    player.m_nGameGold = unchecked((int)0x76543210);

    Assert(player.Operate(new TProcessMessage { wIdent = Grobal2.RM_GOLDCHANGED }),
        "RM_GOLDCHANGED dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_GOLDCHANGED, player.m_nGold, 0, 0, 0,
        "RM_GOLDCHANGED");
}

static void CheckChangeLight()
{
    var player = NewPlayer();
    const int unknownObjectId = 0x12345678;

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_CHANGELIGHT,
        BaseObject = unknownObjectId
    }), "RM_CHANGELIGHT dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_CHANGELIGHT, unknownObjectId, 4, 0, 0,
        "RM_CHANGELIGHT");
}

static void CheckGroupCancel()
{
    var player = NewPlayer();
    player.m_boAllowGroup = true;

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_GROUPCANCEL,
        nParam1 = 0x1234
    }), "RM_GROUPCANCEL dispatcher result");
    Assert(!player.m_boAllowGroup, "RM_GROUPCANCEL allow-group reset");
    Packet(player.m_DefMsg, Grobal2.SM_GROUPMODECHANGED, 0, 0, 0, 0,
        "RM_GROUPCANCEL trailing group-mode packet");
}

static void CheckUsersRepair()
{
    Equal(10152, Grobal2.RM_SENDUSERSREPAIR,
        "RM_SENDUSERSREPAIR native ident");
    var player = NewPlayer();

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = 10152,
        nParam1 = 0x12345678,
        nParam2 = 0x23456
    }), "RM_SENDUSERSREPAIR dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_SENDUSERREPAIR, 0x12345678, 0x3456, 0, 0,
        "RM_SENDUSERSREPAIR");
}

static void CheckThirdBatchDispatcher()
{
    var player = NewPlayer();

    Equal(1105, Grobal2.SM_PASSWORD, "SM_PASSWORD native ident");
    Assert(player.Operate(new TProcessMessage { wIdent = Grobal2.RM_PASSWORD }),
        "RM_PASSWORD dispatcher result");
    Packet(player.m_DefMsg, 1105, 0, 0, 0, 0, "RM_PASSWORD");

    player.m_DefMsg = null;
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_CHANGEFACE,
        nParam1 = 0,
        nParam2 = 0x10203040
    }), "RM_CHANGEFACE zero guard result");
    Assert(player.m_DefMsg == null, "RM_CHANGEFACE zero guard packet");

    const int packedFace = unchecked((int)0x89ABCDEF);
    const int faceObjectId = 0x10203040;
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_CHANGEFACE,
        nParam1 = packedFace,
        nParam2 = faceObjectId
    }), "RM_CHANGEFACE dispatcher result");
    Packet(player.m_DefMsg, Grobal2.SM_CHANGEFACE, faceObjectId,
        0xCDEF, 0x89AB, 0, "RM_CHANGEFACE");

    var gaugeActor = new ProbeActor();
    gaugeActor.m_WAbil.HP = unchecked((int)0x89ABCDEF);
    gaugeActor.m_WAbil.MaxHP = 0x76543210;
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_10414,
        BaseObject = gaugeActor.ObjectId
    }), "RM_10414 dispatcher result");
    // Delphi's register convention pushes the stack tail LEFT-to-right, which the sibling
    // SM_OPENHEALTH arm pins: it pushes HP.lo, MaxHP.lo, 0, 0 into (wParam, wTag, wSeries,
    // sMsg) — read right-to-left the string parameter would receive an HP value. The RM
    // 10414 arm at 0x6B5C23 therefore reads Param=HP.lo / Tag=MaxHP.lo / Series=1:
    //   0x6B5C38 66 8B 86 AC 02 00 00  mov ax,[esi+0x2AC] ; HP.lo
    //   0x6B5C3F 50                    push eax           ; wParam
    //   0x6B5C40 66 8B 86 B0 02 00 00  mov ax,[esi+0x2B0] ; MaxHP.lo
    //   0x6B5C47 50                    push eax           ; wTag
    //   0x6B5C48 6A 01                 push 1             ; wSeries
    //   0x6B5C4A 8D 45 F0 / 50         lea eax,[ebp-0x10] ; Buf (HP,MaxHP dwords)
    //   0x6B5C4E 6A 08                 push 8             ; Len
    //   0x6B5C53 66 BA 4F 04           mov dx,0x44F       ; 1103
    Packet(player.m_DefMsg, Grobal2.SM_INSTANCEHEALGUAGE,
        gaugeActor.ObjectId, 0xCDEF, 0x3210, 1, "RM_10414");

    var gaugeBodyMethod = typeof(TPlayObject).GetMethod(
        "BuildInstanceHealGaugeBody", BindingFlags.Static | BindingFlags.NonPublic);
    Assert(gaugeBodyMethod != null, "RM_10414 body encoder reflection");
    var gaugeBody = (byte[])gaugeBodyMethod.Invoke(null, new object[]
    {
        gaugeActor.m_WAbil.HP, gaugeActor.m_WAbil.MaxHP
    });
    Equal(8, gaugeBody.Length, "RM_10414 body size");
    Equal(gaugeActor.m_WAbil.HP,
        BinaryPrimitives.ReadInt32LittleEndian(gaugeBody.AsSpan(0, 4)),
        "RM_10414 body HP");
    Equal(gaugeActor.m_WAbil.MaxHP,
        BinaryPrimitives.ReadInt32LittleEndian(gaugeBody.AsSpan(4, 4)),
        "RM_10414 body MaxHP");

    var goodsBody = new byte[] { 0x11, 0x22, 0x33 };
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_SENDGOODSLIST,
        nParam1 = 0x10203040,
        nParam2 = unchecked((int)0x89ABCDEF),
        Payload = goodsBody
    }), "RM_SENDGOODSLIST dispatcher result");
    // Same left-to-right (Param, Tag, Series, Buf, Len) tail as RM_10414:
    //   0x6B5277 66 8B 43 08 mov ax,[ebx+8] / 0x6B527B 50 push eax  ; Param = LoWord(nParam2)
    //   0x6B527C 6A 00       push 0                                 ; Tag
    //   0x6B527E 6A 01       push 1                                 ; Series
    //   0x6B5280 8B 43 10 / 50  push [ebx+0x10]                     ; Buf
    //   0x6B5284 0F B7 C6 / 50  push si                             ; Len
    //   0x6B528B 66 BA 85 02    mov dx,0x285                        ; 645
    Packet(player.m_DefMsg, Grobal2.SM_SENDGOODSLIST, 0x10203040,
        0xCDEF, 0, 1, "RM_SENDGOODSLIST");

    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_SENDDETAILGOODSLIST,
        nParam1 = 0x12345678,
        nParam2 = unchecked((int)0xA1B2C3D4),
        nParam3 = unchecked((int)0xE5F60718),
        Payload = goodsBody
    }), "RM_SENDDETAILGOODSLIST dispatcher result");
    //   0x6B538F 66 8B 43 08 mov ax,[ebx+8]    / push  ; Param  = LoWord(nParam2)
    //   0x6B5394 66 8B 43 0C mov ax,[ebx+0x0C] / push  ; Tag    = LoWord(nParam3)
    //   0x6B5399 6A 00       push 0                    ; Series
    //   0x6B53A6 66 BA 8C 02 mov dx,0x28C              ; 652
    Packet(player.m_DefMsg, Grobal2.SM_SENDDETAILGOODSLIST, 0x12345678,
        0xC3D4, 0x0718, 0, "RM_SENDDETAILGOODSLIST");

    var queuedBodyMethod = typeof(TPlayObject).GetMethod(
        "GetQueuedPayloadBytes", BindingFlags.Static | BindingFlags.NonPublic);
    Assert(queuedBodyMethod != null, "queued body helper reflection");
    var forwarded = (byte[])queuedBodyMethod.Invoke(null, new object[]
    {
        new TProcessMessage { Payload = goodsBody }
    });
    Assert(ReferenceEquals(goodsBody, forwarded), "queued body was reconstructed");
    var empty = (byte[])queuedBodyMethod.Invoke(null, new object[]
    {
        new TProcessMessage()
    });
    Equal(0, empty.Length, "queued empty body");
}

static void CheckMerchantPayloadProducers()
{
    M2Share.UserEngine.StdItemList.Clear();
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "wire-item",
        ItemType = GoodType.ITEM_WEAPON,
        StdMode = 5,
        Price = 1234,
        DuraMax = 1000
    });

    var merchant = new Merchant();
    merchant.m_ItemTypeList.Add(5);
    var goodsField = typeof(Merchant).GetField("m_GoodsList",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(goodsField != null, "merchant goods reflection");
    var goods = (IList<IList<TUserItem>>)goodsField.GetValue(merchant);
    goods.Add(new List<TUserItem>
    {
        new()
        {
            MakeIndex = 0x11223344,
            wIndex = 1,
            Dura = 500,
            DuraMax = 1000
        }
    });

    var player = NewPlayer();
    merchant.UserSelect_BuyItem(player, 0);
    TProcessMessage queued = null;
    Assert(player.TryTake(ref queued), "goods producer queue");
    Equal(Grobal2.RM_SENDGOODSLIST, queued.wIdent, "goods producer ident");
    Equal(merchant.ObjectId, queued.nParam1, "goods producer Recog");
    Equal(1, queued.nParam2, "goods producer count");
    Equal(string.Empty, queued.sMsg, "goods producer string body");
    var goodsBody = queued.Payload as byte[];
    Assert(goodsBody != null, "goods producer byte payload");
    Equal(32, goodsBody.Length, "goods producer body size");
    Equal(1, BinaryPrimitives.ReadInt32LittleEndian(goodsBody.AsSpan(16, 4)),
        "goods producer submenu");
    Equal(1, BinaryPrimitives.ReadInt32LittleEndian(goodsBody.AsSpan(24, 4)),
        "goods producer stock");
    Equal(1, BinaryPrimitives.ReadInt32LittleEndian(goodsBody.AsSpan(28, 4)),
        "goods producer item index");

    goods.Clear();
    merchant.UserSelect_BuyItem(player, 0);
    queued = null;
    Assert(player.TryTake(ref queued), "empty goods producer queue");
    Equal(string.Empty, queued.sMsg, "empty goods producer string body");
    var emptyGoodsBody = queued.Payload as byte[];
    Assert(emptyGoodsBody != null, "empty goods producer byte payload");
    Equal(0, emptyGoodsBody.Length, "empty goods producer body size");

    goods.Add(new List<TUserItem>
    {
        new()
        {
            MakeIndex = 0x55667788,
            wIndex = 1,
            Dura = 600,
            DuraMax = 1000
        }
    });
    player.m_nSoftVersionDateEx = 0;
    merchant.ClientGetDetailGoodsList(player, "wire-item", 0);
    queued = null;
    Assert(player.TryTake(ref queued), "legacy detail producer queue");
    Equal(Grobal2.RM_SENDDETAILGOODSLIST, queued.wIdent,
        "legacy detail producer ident");
    Equal(string.Empty, queued.sMsg, "legacy detail producer string body");
    Assert(queued.Payload is byte[] legacyDetailBody && legacyDetailBody.Length > 0,
        "legacy detail producer byte payload");

    player.m_nSoftVersionDateEx = 1;
    merchant.ClientGetDetailGoodsList(player, "wire-item", 0);
    queued = null;
    Assert(player.TryTake(ref queued), "modern detail producer queue");
    Equal(string.Empty, queued.sMsg, "modern detail producer string body");
    Assert(queued.Payload is byte[] modernDetailBody && modernDetailBody.Length > 0,
        "modern detail producer byte payload");
}

static void CheckExactSequenceSource()
{
    var root = FindRepoRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));

    var goodsBlock = CaseBlock(source, "RM_SENDGOODSLIST", "RM_SENDUSERSELL");
    // 0x6B5277 push word[rec+8] (Param) / 0x6B527C push 0 (Tag) / 0x6B527E push 1 (Series)
    Contains(goodsBlock,
        "ProcessMsg.nParam1, HUtil32.LoWord(ProcessMsg.nParam2), 0, 1)",
        "RM_SENDGOODSLIST Param/Tag/Series");
    Contains(goodsBlock, "GetQueuedPayloadBytes(ProcessMsg)",
        "RM_SENDGOODSLIST queued body");
    NotContains(goodsBlock, "ProcessMsg.sMsg", "RM_SENDGOODSLIST string body");
    NotContains(goodsBlock, "Base64", "RM_SENDGOODSLIST Base64 path");

    var detailGoodsBlock = CaseBlock(source, "RM_SENDDETAILGOODSLIST",
        "RM_GOLDCHANGED");
    // 0x6B538F push word[rec+8] (Param) / 0x6B5394 push word[rec+0xC] (Tag) /
    // 0x6B5399 push 0 (Series)
    Contains(detailGoodsBlock,
        "ProcessMsg.nParam1, HUtil32.LoWord(ProcessMsg.nParam2)",
        "RM_SENDDETAILGOODSLIST Param");
    Contains(detailGoodsBlock, "HUtil32.LoWord(ProcessMsg.nParam3), 0)",
        "RM_SENDDETAILGOODSLIST Tag/Series");
    Contains(detailGoodsBlock, "GetQueuedPayloadBytes(ProcessMsg)",
        "RM_SENDDETAILGOODSLIST queued body");
    NotContains(detailGoodsBlock, "ProcessMsg.sMsg",
        "RM_SENDDETAILGOODSLIST string fallback");

    var gaugeBlock = CaseBlock(source, "RM_10414", "RM_CHANGEFACE");
    // 0x6B5C38 push HP.lo (Param) / 0x6B5C40 push MaxHP.lo (Tag) / 0x6B5C48 push 1 (Series)
    Contains(gaugeBlock, "ProcessMsg.BaseObject, HUtil32.LoWord(gaugeHp)",
        "RM_10414 Param");
    Contains(gaugeBlock, "HUtil32.LoWord(gaugeMaxHp), 1)", "RM_10414 Tag/Series");
    Contains(gaugeBlock, "BuildInstanceHealGaugeBody(gaugeHp, gaugeMaxHp)",
        "RM_10414 8-byte body");

    var changeFaceBlock = CaseBlock(source, "RM_CHANGEFACE", "RM_PASSWORD");
    Contains(changeFaceBlock, "ProcessMsg.nParam1 != 0 && ProcessMsg.nParam2 != 0",
        "RM_CHANGEFACE zero guards");
    Contains(changeFaceBlock, "Grobal2.SM_CHANGEFACE, ProcessMsg.nParam2",
        "RM_CHANGEFACE Recog");
    Contains(changeFaceBlock, "HUtil32.LoWord(ProcessMsg.nParam1)",
        "RM_CHANGEFACE Param");
    Contains(changeFaceBlock, "HUtil32.HiWord(ProcessMsg.nParam1), 0, \"\")",
        "RM_CHANGEFACE Tag/Series/body");
    NotContains(changeFaceBlock, "ObjectManager", "RM_CHANGEFACE object lookup");
    NotContains(changeFaceBlock, "BuildMobileActorStateBody",
        "RM_CHANGEFACE actor body");

    var passwordBlock = CaseBlock(source, "RM_PASSWORD", "RM_PLAYDICE");
    Contains(passwordBlock, "Grobal2.SM_PASSWORD, 0, 0, 0, 0, \"\"",
        "RM_PASSWORD zero packet");

    var constantsSource = File.ReadAllText(Path.Combine(root, "SystemModule",
        "Grobal2.cs"));
    Contains(constantsSource, "SM_PASSWORD = 1105;", "SM_PASSWORD constant");

    var merchantSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Npcs",
        "Merchant.cs"));
    var buyProducer = Between(merchantSource, "public void UserSelect_BuyItem",
        "private void UserSelect_SellItem");
    Contains(buyProducer, "string.Empty, goodsStream.ToArray()",
        "goods producer byte payload");
    NotContains(buyProducer, "Base64", "goods producer Base64 path");
    var detailProducer = Between(merchantSource,
        "public void ClientGetDetailGoodsList", "public void ClientQuerySellPrice");
    Contains(detailProducer, "HUtil32.GetBytes(sSendMsg)",
        "legacy detail producer byte payload");
    Contains(detailProducer, "detailBody.ToArray()",
        "modern detail producer byte payload");
    Equal(2, Count(detailProducer, "Grobal2.RM_SENDDETAILGOODSLIST"),
        "detail byte producer branch count");

    var levelBlock = CaseBlock(source, "RM_LEVELUP", "RM_CHANGENAMECOLOR");
    Ordered(levelBlock, "SM_LEVELUP", "SendNativeAbilityPacket();",
        "RM_LEVELUP packet sequence");
    NotContains(levelBlock, "SM_SUBABILITY", "RM_LEVELUP extra subability");

    var groupBlock = CaseBlock(source, "RM_GROUPCANCEL", "RM_SENDUSERREPAIR");
    Ordered(groupBlock, "SM_GROUPCANCEL", "m_boAllowGroup = false",
        "RM_GROUPCANCEL first packet then state reset");
    Ordered(groupBlock, "m_boAllowGroup = false", "SM_GROUPMODECHANGED",
        "RM_GROUPCANCEL state reset then second packet");
    Equal(2, Count(groupBlock, "SendDefMessage("),
        "RM_GROUPCANCEL packet count");

    var featureBlock = CaseBlock(source, "RM_FEATURECHANGED", "RM_CLEAROBJECTS");
    Contains(featureBlock, "HUtil32.HiWord(ProcessMsg.nParam1), 0)",
        "RM_FEATURECHANGED fixed Series");
    Contains(featureBlock, "ProcessMsg.Payload as byte[] ?? Array.Empty<byte>()",
        "RM_FEATURECHANGED original body");
    Contains(featureBlock, "SendSocket(m_DefMsg, featureBody)",
        "RM_FEATURECHANGED body forwarding");
    NotContains(featureBlock, "GetMobileFeature()",
        "RM_FEATURECHANGED late body reconstruction");

    Contains(source, "Grobal2.MakeDefaultMsg(Grobal2.SM_CRY,",
        "RM_CRY SM_CRY opcode");
    Contains(source, "ProcessMsg.BaseObject, 0x9700, 0, 1)",
        "RM_CRY fixed header");
    Contains(source, "SendSocket(m_DefMsg, ProcessMsg.sMsg)",
        "RM_CRY text forwarding");

    var baseSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.cs"));
    Contains(baseSource,
        "SendRefMsg(Grobal2.RM_FEATURECHANGED, 0, GetFeatureToLong(), 0, 0, \"\"",
        "RM_FEATURECHANGED producer tuple");
    Contains(baseSource, "GetMobileFeature());",
        "RM_FEATURECHANGED producer body");

    foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "GameSvr"),
                 "*.cs", SearchOption.AllDirectories))
    {
        var gameSource = File.ReadAllText(path);
        NotContains(gameSource, "Grobal2.RM_SUBABILITY",
            "active RM_SUBABILITY source " + path);
        NotContains(gameSource, "Grobal2.SM_SUBABILITY",
            "active SM_SUBABILITY source " + path);
    }
}

static ProbePlayer NewPlayer() => new() { m_boOffLineFlag = true };

static string CaseBlock(string source, string startCase, string endCase)
{
    var start = source.IndexOf("case Grobal2." + startCase + ":",
        StringComparison.Ordinal);
    var end = source.IndexOf("case Grobal2." + endCase + ":", start,
        StringComparison.Ordinal);
    Assert(start >= 0 && end > start, startCase + " source block");
    return source[start..end];
}

static string Between(string source, string startText, string endText)
{
    var start = source.IndexOf(startText, StringComparison.Ordinal);
    var end = source.IndexOf(endText, start + Math.Max(startText.Length, 0),
        StringComparison.Ordinal);
    Assert(start >= 0 && end > start, startText + " source block");
    return source[start..end];
}

static void Ordered(string source, string first, string second, string label)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Assert(firstIndex >= 0 && secondIndex > firstIndex, label);
}

static int Count(string source, string value)
{
    var count = 0;
    var index = 0;
    while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }
    return count;
}

static void NotContains(string source, string value, string label)
{
    Assert(!source.Contains(value, StringComparison.Ordinal), label);
}

static void Contains(string source, string value, string label)
{
    Assert(source.Contains(value, StringComparison.Ordinal), label);
}

static void Packet(ClientPacket packet, int ident, int recog, int param, int tag,
    int series, string label)
{
    Assert(packet != null, label + " packet");
    Equal((ushort)ident, packet.Ident, label + " ident");
    Equal(recog, packet.Recog, label + " recog");
    Equal(unchecked((ushort)param), packet.Param, label + " param");
    Equal(unchecked((ushort)tag), packet.Tag, label + " tag");
    Equal(unchecked((ushort)series), packet.Series, label + " series");
}

// The sweep harness runs the exe out of a shared Build tree that sits OUTSIDE the checkout
// (OutputPath ..\..\..\Build\AuditTools\...), so neither the CWD nor the base directory has
// the solution above it. Fall back to where this file was compiled from.
static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "")
{
    foreach (var startPath in new[]
             {
                 Directory.GetCurrentDirectory(),
                 AppContext.BaseDirectory,
                 string.IsNullOrEmpty(sourcePath) ? null : Path.GetDirectoryName(sourcePath)
             })
    {
        if (string.IsNullOrEmpty(startPath)) continue;
        for (var directory = new DirectoryInfo(startPath);
             directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyoMir2.sln")))
                return directory.FullName;
        }
    }

    throw new InvalidOperationException("Repository root not found");
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

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    public byte[] AbilityBody() => BuildNativeAbilityPacket();

    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}

sealed class ProbeActor : TBaseObject
{
    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}

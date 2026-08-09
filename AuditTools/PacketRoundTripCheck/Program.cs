using System.Text;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

CheckAbilityRoundTrip();
CheckOldAbilityRoundTrip();
CheckNativeAbilityPacket();
CheckClientConfRoundTrip();
CheckMessageBodyRoundTrip();
CheckCharDescRoundTrip();
CheckNakedAbilityRoundTrip();
CheckMagicRoundTrip();
CheckClientMagicRoundTrip();
CheckNewClientMagicRoundTrip();
CheckNewClientMagicRejectsMalformedName();
CheckPlayerMagicEncoding();
CheckShowEventBodies();
CheckUserItemCopyIdentity();
CheckActionProtocolConstants();
CheckCastleAttackDateParser();

Console.WriteLine("PacketRoundTripCheck PASS ability=58 oldAbility=40 nativeAbility=184 nakedAbility=20 clientConf=24 messageBody=8/16 charDesc=8 magic=76/84/46 showEvent=12/64 itemCopy=client-id actions=exact GBK=strict castleDate=strict");

static void CheckAbilityRoundTrip()
{
    var source = new TAbility
    {
        Level = ushort.MaxValue,
        AC = int.MinValue,
        MAC = int.MaxValue,
        DC = -1,
        MC = 0x10203040,
        SC = unchecked((int)0x89ABCDEF),
        HP = -123456789,
        MP = 123456789,
        MaxHP = -2,
        MaxMP = 2,
        Exp = int.MinValue + 1,
        MaxExp = int.MaxValue - 1,
        Weight = 1,
        MaxWeight = 0x1234,
        WearWeight = 0x5678,
        MaxWearWeight = 0x9ABC,
        HandWeight = 0xDEF0,
        MaxHandWeight = ushort.MaxValue
    };

    var encoded = source.GetBuffer();
    Equal(58, encoded.Length, "TAbility record size");
    var decoded = RequirePacket<TAbility>(encoded, "TAbility decode");
    BytesEqual(encoded, decoded.GetBuffer(), "TAbility byte round trip");
}

static void CheckOldAbilityRoundTrip()
{
    var source = new TOAbility
    {
        Level = 0x1020,
        AC = 0x3040,
        MAC = 0x5060,
        DC = 0x7080,
        MC = 0x90A0,
        SC = 0xB0C0,
        HP = 0x1234,
        MP = 0x5678,
        MaxHP = 0x9ABC,
        MaxMP = 0xDEF0,
        dw1AC = unchecked((int)0x89ABCDEF),
        Exp = 0x10203040,
        MaxExp = unchecked((int)0xF0E0D0C0),
        Weight = 0x1357,
        MaxWeight = 0x2468,
        WearWeight = 0x11,
        MaxWearWeight = 0x22,
        HandWeight = 0x33,
        MaxHandWeight = 0x44
    };

    var encoded = source.GetBuffer();
    Equal(40, encoded.Length, "TOAbility record size");
    Equal((ushort)0x1234, BitConverter.ToUInt16(encoded, 12),
        "TOAbility HP offset");
    Equal(unchecked((int)0x89ABCDEF), BitConverter.ToInt32(encoded, 20),
        "TOAbility reserved dword offset");
    Equal(0x10203040, BitConverter.ToInt32(encoded, 24),
        "TOAbility exp offset");
    Equal((byte)0x44, encoded[39], "TOAbility final byte");

    var decoded = RequirePacket<TOAbility>(encoded, "TOAbility decode");
    Equal(source.HP, decoded.HP, "TOAbility HP decode");
    Equal(source.dw1AC, decoded.dw1AC, "TOAbility reserved dword decode");
    Equal(source.MaxExp, decoded.MaxExp, "TOAbility max exp decode");
    BytesEqual(encoded, decoded.GetBuffer(), "TOAbility byte round trip");
}

static void CheckClientConfRoundTrip()
{
    var source = new TClientConf
    {
        boClientCanSet = true,
        boRunHuman = false,
        boRunMon = true,
        boRunNpc = false,
        boWarRunAll = true,
        btDieColor = 0xAB,
        wSpellTime = 0x1234,
        wHitIime = 0x5678,
        wItemFlashTime = 0x9ABC,
        btItemSpeed = 0xDE,
        boCanStartRun = true,
        boParalyCanRun = false,
        boParalyCanWalk = true,
        boParalyCanHit = false,
        boParalyCanSpell = true,
        boShowRedHPLable = false,
        boShowHPNumber = true,
        boShowJobLevel = false,
        boDuraAlert = true,
        boMagicLock = false,
        boAutoPuckUpItem = true
    };

    var encoded = source.GetBuffer();
    Equal(TClientConf.RecordSize, encoded.Length, "TClientConf record size");
    Equal((byte)1, encoded[0], "TClientConf first bool offset");
    Equal((byte)0xAB, encoded[5], "TClientConf die color offset");
    Equal((ushort)0x1234, BitConverter.ToUInt16(encoded, 6),
        "TClientConf spell offset");
    Equal((ushort)0x5678, BitConverter.ToUInt16(encoded, 8),
        "TClientConf hit offset");
    Equal((ushort)0x9ABC, BitConverter.ToUInt16(encoded, 10),
        "TClientConf item flash offset");
    Equal((byte)0xDE, encoded[12], "TClientConf item speed offset");
    Equal((byte)1, encoded[23], "TClientConf last bool offset");

    var decoded = RequirePacket<TClientConf>(encoded, "TClientConf decode");
    BytesEqual(encoded, decoded.GetBuffer(), "TClientConf byte round trip");
    if (Packets.ToPacket<TClientConf>(encoded[..^1]) != null)
        throw new InvalidOperationException("TClientConf accepted a truncated record");

    var copy = source.Copy();
    True(!ReferenceEquals(source, copy), "TClientConf copy identity");
    copy.boRunHuman = true;
    copy.wSpellTime = 1;
    True(!source.boRunHuman && source.wSpellTime == 0x1234,
        "TClientConf copy isolation");
}

static void CheckMessageBodyRoundTrip()
{
    var words = new TMessageBodyW
    {
        Param1 = 0x1234,
        Param2 = 0x5678,
        Tag1 = 0x9ABC,
        Tag2 = 0xDEF0
    };
    var wordBytes = words.GetBuffer();
    Equal(TMessageBodyW.RecordSize, wordBytes.Length, "TMessageBodyW size");
    Equal((ushort)0x9ABC, BitConverter.ToUInt16(wordBytes, 4),
        "TMessageBodyW tag offset");
    BytesEqual(wordBytes,
        RequirePacket<TMessageBodyW>(wordBytes, "TMessageBodyW decode").GetBuffer(),
        "TMessageBodyW byte round trip");

    var longs = new TMessageBodyWL
    {
        lParam1 = int.MinValue,
        lParam2 = 0x12345678,
        lTag1 = unchecked((int)0x89ABCDEF),
        lTag2 = int.MaxValue
    };
    var longBytes = longs.GetBuffer();
    Equal(TMessageBodyWL.RecordSize, longBytes.Length, "TMessageBodyWL size");
    Equal(unchecked((int)0x89ABCDEF), BitConverter.ToInt32(longBytes, 8),
        "TMessageBodyWL tag offset");
    BytesEqual(longBytes,
        RequirePacket<TMessageBodyWL>(longBytes, "TMessageBodyWL decode").GetBuffer(),
        "TMessageBodyWL byte round trip");
}

static void CheckCharDescRoundTrip()
{
    var source = new TCharDesc
    {
        Feature = unchecked((int)0x89ABCDEF),
        Status = 0x10203040
    };
    var encoded = source.GetBuffer();
    Equal(TCharDesc.RecordSize, encoded.Length, "TCharDesc size");
    Equal(source.Feature, BitConverter.ToInt32(encoded, 0),
        "TCharDesc feature offset");
    Equal(source.Status, BitConverter.ToInt32(encoded, 4),
        "TCharDesc status offset");
    BytesEqual(encoded,
        RequirePacket<TCharDesc>(encoded, "TCharDesc decode").GetBuffer(),
        "TCharDesc byte round trip");
}

static void CheckNakedAbilityRoundTrip()
{
    var source = new TNakedAbility
    {
        DC = 0x0102,
        MC = 0x0304,
        SC = 0x0506,
        AC = 0x0708,
        MAC = 0x090A,
        HP = 0x0B0C,
        MP = 0x0D0E,
        Hit = 0x0F10,
        Speed = 0x1112,
        X2 = 0x1314
    };
    var encoded = source.GetBuffer();
    Equal(TNakedAbility.RecordSize, encoded.Length, "TNakedAbility size");
    Equal(source.Hit, BitConverter.ToUInt16(encoded, 14),
        "TNakedAbility hit offset");
    Equal(source.Speed, BitConverter.ToUInt16(encoded, 16),
        "TNakedAbility speed offset");
    Equal(source.X2, BitConverter.ToUInt16(encoded, 18),
        "TNakedAbility X2 offset");
    BytesEqual(encoded,
        RequirePacket<TNakedAbility>(encoded, "TNakedAbility decode").GetBuffer(),
        "TNakedAbility byte round trip");
}

static void CheckNativeAbilityPacket()
{
    var actor = (TBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(TBaseObject));
    actor.m_WAbil = new TAbility
    {
        Level = 0xABCD,
        AC = unchecked((int)0x33441122),
        MAC = unchecked((int)0x77885566),
        DC = unchecked((int)0xBBCC99AA),
        MC = unchecked((int)0xFFEECCDD),
        SC = 0x24681357,
        HP = 0x10203040,
        MaxHP = 0x50607080,
        MP = unchecked((int)0x90A0B0C0),
        MaxMP = unchecked((int)0xD0E0F000),
        Exp = 0x10203040,
        MaxExp = unchecked((int)0xF0E0D0C0),
        Weight = 0x1234,
        MaxWeight = 0x2345,
        WearWeight = 0x3456,
        MaxWearWeight = 0x4567,
        HandWeight = 0x5678,
        MaxHandWeight = 0x6789
    };
    actor.m_btHitPoint = 0x12;
    actor.m_wSpeedPoint = 0x3456;
    actor.m_nHealthRecover = 0x5678;
    actor.m_nSpellRecover = 0x789A;
    actor.m_wEffectResistance = 0x9ABC;
    actor.m_nPoisonRecover = 0xBCDE;
    actor.m_nAntiMagic = 0xDEF0;
    actor.m_wEffectStrength = 0x1357;

    typeof(TBaseObject).GetField("m_nHitSpeed",
        BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(actor, (ushort)0x2468);
    var coreField = typeof(TBaseObject).GetField("m_NativeCoreWorkingAbility",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var coreAbility = coreField.GetValue(actor)!;
    coreAbility.GetType().GetField("CCLow",
        BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coreAbility,
        unchecked((int)0x89ABCDEF));
    coreAbility.GetType().GetField("CCHigh",
        BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coreAbility,
        0x12345678);
    coreField.SetValue(actor, coreAbility);
    var builder = typeof(TBaseObject).GetMethod("BuildNativeAbilityPacket",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var encoded = (byte[])builder.Invoke(actor, null)!;

    Equal(184, encoded.Length, "native ability record size");
    Equal((ushort)0xABCD, BitConverter.ToUInt16(encoded, 0), "native ability level");
    Equal((ushort)0x0012, BitConverter.ToUInt16(encoded, 2), "native ability hit");
    Equal((ushort)0x9ABC, BitConverter.ToUInt16(encoded, 0x0A),
        "native ability type-62 slot");
    Equal((ushort)0x2468, BitConverter.ToUInt16(encoded, 0x12),
        "native ability hit speed");
    Equal((byte)0xAB, encoded[0x14], "native ability level high byte");
    Equal(0x1122, BitConverter.ToInt32(encoded, 0x18),
        "native ability AC low");
    Equal(0x3344, BitConverter.ToInt32(encoded, 0x1C),
        "native ability AC high");
    Equal(0x10203040, BitConverter.ToInt32(encoded, 0x40),
        "native ability HP");
    Equal(unchecked((int)0xF0E0D0C0), BitConverter.ToInt32(encoded, 0x54),
        "native ability max exp");
    Equal(0x1357u, BitConverter.ToUInt32(encoded, 0x80),
        "native ability type-61 slot");
    Equal(unchecked((int)0x89ABCDEF), BitConverter.ToInt32(encoded, 0xB0),
        "native ability CC low");
    Equal(0x12345678, BitConverter.ToInt32(encoded, 0xB4),
        "native ability CC high");
}

static void CheckMagicRoundTrip()
{
    var source = new TMagic
    {
        wMagicID = 0x1234,
        sMagicName = "烈火剑法",
        btEffectType = 0x56,
        btEffect = 0x78,
        wSpell = 0x9ABC,
        wPower = 0xDEF0,
        TrainLevel = new byte[] { 1, 2, 3, 4 },
        MaxTrain = new[] { 0x10203040, -2, int.MaxValue, int.MinValue },
        btTrainLv = 0x11,
        btJob = 0x22,
        dwDelayTime = unchecked((int)0xA1B2C3D4),
        btDefSpell = 0x33,
        btDefPower = 0x44,
        wMaxPower = 0x5566,
        btDefMaxPower = 0x77,
        sDescr = "magic-desc"
    };

    var encoded = source.GetBuffer();
    Equal(TMagic.RecordSize, encoded.Length, "TMagic record size");
    Equal((ushort)0x1234, BitConverter.ToUInt16(encoded, 0), "TMagic id offset");
    Equal((byte)8, encoded[2], "TMagic GBK name length");
    Equal((byte)0x56, encoded[15], "TMagic effect type offset");
    Equal((byte)0, encoded[17], "TMagic reserved byte");
    Equal((ushort)0x9ABC, BitConverter.ToUInt16(encoded, 18), "TMagic spell offset");
    Equal(0x10203040, BitConverter.ToInt32(encoded, 28), "TMagic train offset");
    Equal((ushort)0, BitConverter.ToUInt16(encoded, 46), "TMagic reserved word");
    Equal(unchecked((int)0xA1B2C3D4), BitConverter.ToInt32(encoded, 48),
        "TMagic delay offset");
    Equal((byte)10, encoded[57], "TMagic description length");

    var decoded = RequirePacket<TMagic>(encoded, "TMagic decode");
    Equal(source.sMagicName, decoded.sMagicName, "TMagic name decode");
    Equal(source.MaxTrain[3], decoded.MaxTrain[3], "TMagic train decode");
    Equal(source.sDescr, decoded.sDescr, "TMagic description decode");
    BytesEqual(encoded, decoded.GetBuffer(), "TMagic byte round trip");

    var malformed = (byte[])encoded.Clone();
    malformed[2] = 13;
    if (Packets.ToPacket<TMagic>(malformed) != null)
        throw new InvalidOperationException("TMagic accepted an oversized short string");
}

static void CheckClientMagicRoundTrip()
{
    var source = new TClientMagic
    {
        Key = (char)0xE9,
        Level = 0x7F,
        CurTrain = unchecked((int)0x89ABCDEF),
        Def = new TMagic
        {
            wMagicID = 0x1357,
            sMagicName = "火球术",
            TrainLevel = new byte[] { 4, 3, 2, 1 },
            MaxTrain = new[] { 1, 2, 3, 4 },
            sDescr = "client-magic"
        }
    };

    var encoded = source.GetBuffer();
    Equal(TClientMagic.RecordSize, encoded.Length, "TClientMagic record size");
    Equal((byte)0xE9, encoded[0], "TClientMagic ANSI key");
    Equal((byte)0x7F, encoded[1], "TClientMagic level offset");
    Equal((ushort)0, BitConverter.ToUInt16(encoded, 2), "TClientMagic padding");
    Equal(unchecked((int)0x89ABCDEF), BitConverter.ToInt32(encoded, 4),
        "TClientMagic train offset");
    Equal((ushort)0x1357, BitConverter.ToUInt16(encoded, 8),
        "TClientMagic definition offset");

    var decoded = RequirePacket<TClientMagic>(encoded, "TClientMagic decode");
    Equal(source.Key, decoded.Key, "TClientMagic key decode");
    Equal(source.Def.sMagicName, decoded.Def.sMagicName,
        "TClientMagic definition decode");
    BytesEqual(encoded, decoded.GetBuffer(), "TClientMagic byte round trip");
}

static void CheckNewClientMagicRoundTrip()
{
    var source = new TNewClientMagic
    {
        MagicName = "烈火剑法甲乙",
        MagicType = byte.MaxValue,
        EffectType = 0x80,
        Effect = 0x7F,
        MagicId = ushort.MaxValue,
        Level = short.MinValue,
        Key = short.MaxValue,
        NeedMp = -12345,
        SpellTick = 23456,
        NextNeedLv = -1,
        ColdTick = int.MinValue,
        CurTrain = int.MaxValue,
        MaxTrain = unchecked((int)0x89ABCDEF),
        DelayTime = 0x10203040
    };

    var encoded = source.GetBuffer();
    Equal(TNewClientMagic.RecordSize, encoded.Length,
        "TNewClientMagic record size");
    var decoded = RequirePacket<TNewClientMagic>(encoded,
        "TNewClientMagic decode");
    Equal("烈火剑法甲乙", decoded.MagicName,
        "TNewClientMagic GBK name");
    BytesEqual(encoded, decoded.GetBuffer(),
        "TNewClientMagic byte round trip");
}

static void CheckNewClientMagicRejectsMalformedName()
{
    var malformed = new byte[TNewClientMagic.RecordSize];
    malformed[0] = 15;
    if (Packets.ToPacket<TNewClientMagic>(malformed) != null)
        throw new InvalidOperationException(
            "TNewClientMagic accepted a name longer than its 14-byte slot");
}

static void CheckPlayerMagicEncoding()
{
    var userMagic = new TUserMagic
    {
        btLevel = 2,
        btKey = 7,
        nTranPoint = 0x10203040,
        MagicInfo = new TMagic
        {
            wMagicID = 62,
            sMagicName = "烈火剑法",
            btEffectType = 0x56,
            btEffect = 0x78,
            wSpell = 100,
            btTrainLv = 1,
            btDefSpell = 3,
            MaxTrain = new[] { 100, 200, 300, 400 },
            dwDelayTime = unchecked((int)0x89ABCDEF)
        }
    };

    var playerEncoder = typeof(TPlayObject).GetMethod("EncodeClientMagic",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var encoded = (byte[])playerEncoder.Invoke(null, new object[] { userMagic })!;
    Equal(TNewClientMagic.RecordSize, encoded.Length,
        "player client magic record size");
    Equal((byte)8, encoded[0], "player client magic name length");
    Equal((byte)1, encoded[15], "player client magic type");
    Equal((byte)0x56, encoded[16], "player client magic effect type");
    Equal((byte)0x78, encoded[17], "player client magic effect");
    Equal((ushort)62, BitConverter.ToUInt16(encoded, 18),
        "player client magic id");
    Equal((short)2, BitConverter.ToInt16(encoded, 20),
        "player client magic level");
    Equal((short)7, BitConverter.ToInt16(encoded, 22),
        "player client magic key");
    // Native MP cost = sub_4C8888 (@0x4C8888..0x4C88C5), which the native client-magic
    // encoder fn 0x4C8498 writes straight into this field
    // (@0x4C850C call 0x4C8888 / @0x4C8511 mov word ptr [ebx+0x18],ax).
    //   Round((wSpell / 4.0f) * (btLevel + 1)) + btDefSpell
    // with the divisor being the float32 4.0 at [0x4C88C8] (raw 00 00 80 40, `D8 /6`
    // fixes the operand at 4 bytes) and btDefSpell (+0x17) added INSIDE the function
    // (@0x4C88BA mov dl,[esi+0x17] / @0x4C88BD add ax,dx). btTrainLv (+0x1A) is NEVER
    // read in the body — it is only the level cap (sub_4C88EC / sub_4C896C).
    // This fixture deliberately uses btTrainLv = 1, i.e. NOT 3, because the discarded
    // `(btTrainLv + 1)` divisor coincides with 4.0 exactly when btTrainLv == 3 — which
    // is why the defect stayed hidden (CommonDB.cs hardcodes btTrainLv = 3).
    //   native : Round((100 / 4.0) * (2 + 1)) + 3 = Round(75) + 3 = 78
    //   old C# : (100 / (1 + 1)) * (2 + 1) + 3   = 153   <- ALSO integer-divided,
    //            truncating before the multiply, where native rounds once at the end
    //            via sub_403574 (fistp qword, round-half-to-even).
    // See staging/heromagic_mpcost_fix_20260804.md §B.
    Equal((short)78, BitConverter.ToInt16(encoded, 24),
        "player client magic need MP");
    Equal((short)0, BitConverter.ToInt16(encoded, 26),
        "player client magic spell tick");
    Equal((short)0, BitConverter.ToInt16(encoded, 28),
        "player client magic next level");
    Equal(0, BitConverter.ToInt32(encoded, 30),
        "player client magic cold tick");
    Equal(0x10203040, BitConverter.ToInt32(encoded, 34),
        "player client magic train point");
    Equal(300, BitConverter.ToInt32(encoded, 38),
        "player client magic max train");
    Equal(unchecked((int)0x89ABCDEF), BitConverter.ToInt32(encoded, 42),
        "player client magic delay");

    var heroEncoder = typeof(HeroObject).GetMethod("EncodeHeroMagic",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var heroEncoded = (byte[])heroEncoder.Invoke(null, new object[] { userMagic })!;
    BytesEqual(encoded, heroEncoded, "player/hero magic encoder parity");
}

static void CheckShowEventBodies()
{
    var builder = typeof(TPlayObject).GetMethod("BuildShowEventBody",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    using var normal = new Event(null, 0, 0, 5, 1000, false)
    {
        m_nEventParam = 0x3344,
        m_sEventOwnerName = "abcd"
    };
    var normalBody = (byte[])builder.Invoke(null,
        new object[] { normal, unchecked((int)0x11223344) })!;
    Equal(12, normalBody.Length, "normal SHOWEVENT body size");
    Equal((ushort)0x1122, BitConverter.ToUInt16(normalBody, 0),
        "normal SHOWEVENT packed parameter");
    Equal((ushort)0x3344, BitConverter.ToUInt16(normalBody, 2),
        "normal SHOWEVENT event parameter");
    Equal((byte)3, normalBody[8], "normal SHOWEVENT owner length");
    BytesEqual(Encoding.ASCII.GetBytes("abc"), normalBody[9..12],
        "normal SHOWEVENT owner truncation");

    using var stall = new Event(null, 0, 0, 41, 1000, false)
    {
        m_nEventParam = 0x5566,
        m_sEventOwnerName = "123456789012345",
        m_sEventStallName = "stall-name",
        m_lEventOwnerId = unchecked((long)0x8877665544332211)
    };
    var stallBody = (byte[])builder.Invoke(null,
        new object[] { stall, unchecked((int)0x77885566) })!;
    Equal(64, stallBody.Length, "stall SHOWEVENT body size");
    Equal((ushort)0x7788, BitConverter.ToUInt16(stallBody, 0),
        "stall SHOWEVENT packed parameter");
    Equal((ushort)0x5566, BitConverter.ToUInt16(stallBody, 2),
        "stall SHOWEVENT event parameter");
    Equal((byte)14, stallBody[8], "stall SHOWEVENT owner length");
    Equal((byte)10, stallBody[23], "stall SHOWEVENT name length");
    Equal((byte)0, stallBody[54], "stall SHOWEVENT padding byte 0");
    Equal((byte)0, stallBody[55], "stall SHOWEVENT padding byte 1");
    Equal(unchecked((long)0x8877665544332211), BitConverter.ToInt64(stallBody, 56),
        "stall SHOWEVENT owner id");
}

static void CheckUserItemCopyIdentity()
{
    var source = new TUserItem
    {
        MakeIndex = 123,
        ClientItemID = unchecked((int)0x89ABCDEF),
        wIndex = 7,
        btValue = new byte[] { 1, 2, 3 }
    };
    var copy = new TUserItem(source);
    Equal(source.ClientItemID, copy.ClientItemID,
        "TUserItem copy client item id");
    True(!ReferenceEquals(source.btValue, copy.btValue),
        "TUserItem copy value isolation");
}

static void CheckActionProtocolConstants()
{
    Equal(1200, Grobal2.SM_PLAYDICE, "SM_PLAYDICE ident");
    Equal(10005, Grobal2.RM_HEAVYHIT, "RM_HEAVYHIT ident");
    Equal(10007, Grobal2.RM_SPELL, "RM_SPELL ident");
    Equal(10008, Grobal2.RM_POWERHIT, "RM_POWERHIT ident");
    Equal(10009, Grobal2.RM_SPELL2, "RM_SPELL2 ident");
    Equal(10013, Grobal2.RM_PUSH, "RM_PUSH ident");
    Equal(10015, Grobal2.RM_RUSH, "RM_RUSH ident");
    Equal(10016, Grobal2.RM_RUSHKUNG, "RM_RUSHKUNG ident");
    Equal(10020, Grobal2.RM_STRUCK, "RM_STRUCK ident");
    Equal(10022, Grobal2.RM_DISAPPEAR, "RM_DISAPPEAR ident");
    Equal(10027, Grobal2.RM_STRUCK_MAG, "RM_STRUCK_MAG ident");
    Equal(10037, Grobal2.RM_CRSHIT, "RM_CRSHIT ident");
    Equal(10038, Grobal2.RM_TWINHIT, "RM_TWINHIT ident");
    Equal(10043, Grobal2.RM_USERNAME, "RM_USERNAME ident");
    Equal(10044, Grobal2.RM_WINEXP, "RM_WINEXP ident");
    Equal(10045, Grobal2.RM_LEVELUP, "RM_LEVELUP ident");
    Equal(10046, Grobal2.RM_CHANGENAMECOLOR, "RM_CHANGENAMECOLOR ident");
    Equal(10330, Grobal2.RM_SPACEMOVE_FIRE, "RM_SPACEMOVE_FIRE ident");
    Equal(10331, Grobal2.RM_SPACEMOVE_SHOW, "RM_SPACEMOVE_SHOW ident");
    Equal(10335, Grobal2.RM_SPACEMOVE_FIRE2, "RM_SPACEMOVE_FIRE2 ident");
    Equal(10336, Grobal2.RM_SPACEMOVE_SHOW2, "RM_SPACEMOVE_SHOW2 ident");
}

static void CheckCastleAttackDateParser()
{
    var parser = typeof(TUserCastle).GetMethod("TryParseAttackDate",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    var valid = ParseAttackDate(parser, "2026-07-23");
    True(valid.Parsed, "castle date valid parse");
    Equal(new DateTime(2026, 7, 23), valid.Value, "castle date valid value");

    var defaults = ParseAttackDate(parser, "not-a-date");
    True(defaults.Parsed, "castle date default parse");
    Equal(new DateTime(1999, 1, 1), defaults.Value,
        "castle date non-numeric defaults");

    var missing = ParseAttackDate(parser, "2026--23");
    True(!missing.Parsed, "castle date missing segment rejected");

    try
    {
        ParseAttackDate(parser, "2026-13-01");
        throw new InvalidOperationException("castle date invalid month was accepted");
    }
    catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException)
    {
    }
}

static (bool Parsed, DateTime Value) ParseAttackDate(MethodInfo parser, string value)
{
    var args = new object[] { value, default(DateTime) };
    var parsed = (bool)parser.Invoke(null, args)!;
    return (parsed, (DateTime)args[1]);
}

static T RequirePacket<T>(byte[] encoded, string name) where T : Packets, new()
{
    var packet = Packets.ToPacket<T>(encoded);
    return packet ?? throw new InvalidOperationException(name + " returned null");
}

static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual,
    string name)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(name + ": bytes differ");
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
}

static void True(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
}

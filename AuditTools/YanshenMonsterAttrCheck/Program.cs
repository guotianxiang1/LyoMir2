using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Plugins;
using SystemModule;

try
{
    Run();
    Console.WriteLine(
        "PASS YanshenMonsterAttrCheck " +
        "imm8=sbyte-not-byte names=2hanzi setpetv=slot+gt0+ret-1 " +
        "npc-payload=19-opcode0 apply=>0-dc-split");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"YanshenMonsterAttrCheck FAIL: {exception}");
    return 1;
}

static void Run()
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    VerifySourceContracts();
    VerifyImm8Domain();
    VerifyApplyGates();
    VerifySetPetVTargeting();
    VerifyNpcCreatMonsPayload();
    VerifyNameAndCountWithPlugin();
}

static void VerifySourceContracts()
{
    var src = File.ReadAllText(Path.Combine(FindRepoRoot(),
        "GameSvr", "Plugins", "YanshenApi.cs"));
    Assert(src.Contains("internal static int NativeSlaveCountImm8(int v) => (sbyte)(v > 0x7F ? 0x7F : v);",
            StringComparison.Ordinal),
        "NativeSlaveCountImm8 must keep (sbyte) after the 0x7F clamp; (byte) turns -1 into 255");
    Assert(!src.Contains("(byte)(v > 0x7F", StringComparison.Ordinal),
        "quantity patches must not recast the imm8 as unsigned byte");
    Assert(src.Contains("1 => \"月灵\"", StringComparison.Ordinal)
           && src.Contains("2 => \"白虎\"", StringComparison.Ordinal)
           && src.Contains("_ => \"神兽\"", StringComparison.Ordinal),
        "神兽 name table must stay the three two-character GBK literals");
    Assert(src.Contains("ApplySetPetV(_player?.m_SlaveList", StringComparison.Ordinal),
        "SetPetAttr must index the host slave list, not invent a side table");
    var commands = File.ReadAllText(Path.Combine(FindRepoRoot(),
        "GameSvr", "Plugins", "YanshenCommands.cs"));
    Assert(!commands.Contains("case 23:", StringComparison.Ordinal),
        "cmd 23 must not take a special dual-feature gate; gs/ys consumption is a separate key");
}

static void VerifyImm8Domain()
{
    Equal(-1, YanshenApi.NativeSlaveCountImm8(-1),
        "神兽_数量=-1 must stay -1 (0x76EE98 6A xx sign-extends; callee reads dword)");
    Equal(1, YanshenApi.NativeSlaveCountImm8(1), "imm8 identity 1");
    Equal(127, YanshenApi.NativeSlaveCountImm8(127), "imm8 upper bound 0x7F");
    Equal(127, YanshenApi.NativeSlaveCountImm8(128),
        "0x100A9DE9 83 F8 7F / jle / mov eax,0x7F clamps 128 to 127");
    Equal(127, YanshenApi.NativeSlaveCountImm8(255),
        "255 is above 0x7F so it clamps to 127, not wrapping to -1");
    Equal(-128, YanshenApi.NativeSlaveCountImm8(-128), "al of -128 is 0x80 → sbyte -128");
}

static void VerifyApplyGates()
{
    var mon = NewSlave("gate");
    mon.m_Abil.AC = HUtil32.MakeLong(3, 4);
    mon.m_WAbil.AC = HUtil32.MakeLong(3, 4);
    mon.m_Abil.DC = HUtil32.MakeLong(5, 6);
    mon.m_WAbil.DC = HUtil32.MakeLong(5, 6);
    mon.m_nNextHitTime = 2000;
    mon.m_nWalkSpeed = 1400;

    YanshenApi.ApplyYanshenMonsterAttrs(mon,
        ac: 0, mac: 0, dc: 0, dcMax: 0, mc: 0, sc: 0,
        speed: 0, hit: 0, hp: 0, maxHp: 0, attackSpd: 0, walkSpd: 0);
    Equal(HUtil32.MakeLong(3, 4), mon.m_WAbil.AC, "<=0 must not write AC");
    Equal(HUtil32.MakeLong(5, 6), mon.m_WAbil.DC, "<=0 must not write DC");
    Equal(2000, mon.m_nNextHitTime, "<=0 must not write gs");
    Equal(1400, mon.m_nWalkSpeed, "<=0 must not write ys");

    YanshenApi.ApplyYanshenMonsterAttrs(mon,
        ac: 11, mac: 0, dc: 7, dcMax: 9, mc: 0, sc: 0,
        speed: 0, hit: 0, hp: 100, maxHp: 200, attackSpd: 400, walkSpd: 300);
    Equal(HUtil32.MakeLong(11, 11), mon.m_Abil.AC, "AC min=max into m_Abil");
    Equal(HUtil32.MakeLong(11, 11), mon.m_WAbil.AC, "AC min=max into m_WAbil");
    Equal(HUtil32.MakeLong(7, 9), mon.m_WAbil.DC, "Dc/DcMax must split lo/hi");
    Equal(100, mon.m_WAbil.HP, "hp >0 writes current HP");
    Equal(200, mon.m_WAbil.MaxHP, "Maxhp >0 writes MaxHP");
    Equal(400, mon.m_nNextHitTime, "gs → +0x320 m_nNextHitTime");
    Equal(300, mon.m_nWalkSpeed, "ys → +0x324 m_nWalkSpeed");
}

static void VerifySetPetVTargeting()
{
    var a = NewSlave("狗");
    a.m_WAbil.AC = HUtil32.MakeLong(1, 1);
    var b = NewSlave("猪");
    b.m_WAbil.AC = HUtil32.MakeLong(2, 2);
    var c = NewSlave("狗");
    c.m_WAbil.AC = HUtil32.MakeLong(3, 3);
    var slaves = new List<TBaseObject> { a, b, c };

    Equal(-1, YanshenApi.ApplySetPetV(slaves, "", 2, 40, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        "empty name + id=2 must return -1 (0x10073936 or eax,-1)");
    Equal(HUtil32.MakeLong(1, 1), a.m_WAbil.AC, "id=2 must not touch slot 1");
    Equal(HUtil32.MakeLong(40, 40), b.m_WAbil.AC, "id=2 is 1-based slot 2");
    Equal(HUtil32.MakeLong(3, 3), c.m_WAbil.AC, "id=2 must not touch slot 3");

    Equal(-1, YanshenApi.ApplySetPetV(slaves, "", 0, 99, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        "id<=0 fails closed");
    Equal(HUtil32.MakeLong(1, 1), a.m_WAbil.AC, "id=0 must not spray onto every slave");
    Equal(-1, YanshenApi.ApplySetPetV(slaves, "", 9, 99, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        "id>count fails closed");

    Equal(-1, YanshenApi.ApplySetPetV(slaves, "狗", 0, 50, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        "name scan still returns -1");
    Equal(HUtil32.MakeLong(50, 50), a.m_WAbil.AC, "name 狗 must rewrite slot 1");
    Equal(HUtil32.MakeLong(40, 40), b.m_WAbil.AC, "name 狗 must skip 猪");
    Equal(HUtil32.MakeLong(50, 50), c.m_WAbil.AC, "name 狗 must rewrite every match");
}

static void VerifyNpcCreatMonsPayload()
{
    Assert(!YanshenApi.TryParseNpcCreatMonsPayload("", out _, out _),
        "empty payload must fail");
    Assert(!YanshenApi.TryParseNpcCreatMonsPayload("1^0^0^1^0^0^0^0^0^0^0^0^0^0^0^0^0^鸡^0", out _, out _),
        "opcode must be 0");
    var payload = "0^12^34^2^5^10^20^30^40^50^60^1^2^100^200^300^400^鸡^3";
    Assert(YanshenApi.TryParseNpcCreatMonsPayload(payload, out var spec, out var error),
        "19-field NpcFuc payload must parse: " + error);
    Equal(12, spec.X, "x");
    Equal(34, spec.Y, "y");
    Equal(2, spec.Num, "num");
    Equal(5, spec.Round, "round");
    Equal(10, spec.Ac, "Ac");
    Equal(20, spec.Mac, "Mac");
    Equal(30, spec.Dc, "Dc");
    Equal(40, spec.DcMax, "DcMax");
    Equal(50, spec.Mc, "Mc");
    Equal(60, spec.Sc, "Sc");
    Equal(1, spec.Speed, "Speed");
    Equal(2, spec.Hit, "Hit");
    Equal(100, spec.Hp, "hp");
    Equal(200, spec.MaxHp, "Maxhp");
    Equal(300, spec.AttackSpd, "AttackSpd");
    Equal(400, spec.WalkSpd, "WalkSpd");
    Equal("鸡", spec.MonName, "MonName");
    Equal("3", spec.Map, "Map");
    Equal(19, YanshenApi.NpcCreatMonsFieldCount, "NpcFuc serializes 19 segments");
    Equal("yanshen2.0.7", YanshenApi.NpcCreatMonsSentinel,
        "Pascal wrapper smuggles the payload through CheckMapMonByName");
}

static void VerifyNameAndCountWithPlugin()
{
    var root = Path.Combine(Path.GetTempPath(),
        "loym2-ys-mons-" + Guid.NewGuid().ToString("N"));
    try
    {
        var envir = Directory.CreateDirectory(Path.Combine(root, "Mir200", "Envir")).FullName;
        var runtime = Directory.CreateDirectory(Path.Combine(root, "Mir200", "GS1")).FullName;
        var configPath = Path.Combine(runtime, "config.json");
        WriteGbk(configPath,
            "{\r\n" +
            "  \"召唤神兽\": 1,\r\n" +
            "  \"召唤骷髅\": 1,\r\n" +
            "  \"神兽_数量\": \"-1\",\r\n" +
            "  \"召唤骷髅_数量\": \"3\",\r\n" +
            "  \"神兽_序号\": \"2\"\r\n" +
            "}\r\n");

        var manager = new PluginManager(envir, runtime);
        manager.RegisterBuiltinPlugins();
        Assert(manager.LoadPlugin("YanshenCompat"), "YanshenCompat did not load");
        var plugin = manager.GetPlugin("YanshenCompat")
            ?? throw new InvalidOperationException("YanshenCompat missing");
        var apiOff = new YanshenApi(null, null, manager);
        Equal("神兽", apiOff.ShenShouName(),
            "uninitialized plugin must leave the host name 神兽");
        Equal(1, apiOff.ShenShouSlaveCount(),
            "uninitialized plugin must leave the host imm8 1");
        Equal(1, apiOff.KuLouSlaveCount(),
            "uninitialized 召唤骷髅 must leave the host imm8 1");

        plugin.IsInitialized = true;
        var api = new YanshenApi(null, null, manager);
        var gbk = Encoding.GetEncoding(936);
        Equal("白虎", api.ShenShouName(), "神兽_序号=2 → 白虎");
        Equal(2, api.ShenShouName().Length, "name patch has no Delphi length prefix");
        Equal(4, gbk.GetByteCount(api.ShenShouName()), "host write is 4 GBK bytes");
        Equal(-1, api.ShenShouSlaveCount(),
            "神兽_数量=-1 must not become 255");
        Equal(3, api.KuLouSlaveCount(), "召唤骷髅_数量=3");

        manager.SetNativeConfigValue("神兽_序号", 1L);
        Equal("月灵", api.ShenShouName(), "神兽_序号=1 → 月灵");
        Equal(4, gbk.GetByteCount(api.ShenShouName()), "月灵 is also two hanzi");
        manager.SetNativeConfigValue("神兽_序号", 99L);
        Equal("神兽", api.ShenShouName(), "unknown idx falls back to 神兽");

        manager.SetNativeConfigValue("召唤神兽", 0L);
        Equal("神兽", api.ShenShouName(), "disabled 召唤神兽 restores 神兽");
        Equal(1, api.ShenShouSlaveCount(), "disabled 召唤神兽 restores imm8 1");
        manager.SetNativeConfigValue("召唤骷髅", 0L);
        Equal(1, api.KuLouSlaveCount(), "disabled 召唤骷髅 restores imm8 1");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static Monster NewSlave(string name)
{
    var mon = new Monster { m_sCharName = name };
    mon.m_Abil.AC = HUtil32.MakeLong(1, 1);
    mon.m_WAbil.AC = HUtil32.MakeLong(1, 1);
    mon.m_Abil.DC = HUtil32.MakeLong(1, 1);
    mon.m_WAbil.DC = HUtil32.MakeLong(1, 1);
    return mon;
}

static string FindRepoRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj not found");
}

static void WriteGbk(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.GetEncoding(936));
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
    M2Share.RandomNumber ??= RandomNumber.GetInstance();
    // TBaseObject 构造函数末尾 (TBaseObject.cs:907) 调用
    // M2Share.ObjectManager.RegisterConstructed(this) 登记新对象；本工具在
    // VerifyApplyGates/VerifySetPetVTargeting 里 new Monster()，缺了它会空引用。
    // 与其他能跑的 harness（如 YanshenTriggerDispatchCheck）一致，补一个空实例即可。
    M2Share.ObjectManager ??= new ObjectManager();
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

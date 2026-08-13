using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Plugins;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var failures = new List<string>();

Check("numeric AllFuc tunnel", CheckNumericTunnel);
Check("caret AllFuc tunnel", CheckCaretTunnel);
Check("Chinese AllFuc tunnel", CheckChineseTunnel);
Check("!!!! selector miss falls back to the host API", CheckSelectorFallback);
Check("five Give format classification", CheckGiveClassification);
Check("dynamic TClientItem wire", CheckDynamicClientItemWire);
Check("owned equipment slots 13..15 wire", CheckOwnedUseItemsWire);
Check("SM_DEALREMOTEADDITEM native header", CheckDealAddWire);
Check("CM_DEALDELITEM cancels whole transaction", CheckDealDeleteCancelsTransaction);
Check("SM_STORAGE_OK carries stored item", CheckStorageItemOkWire);
Check("item producer native packet exactness", CheckItemProducerExactness);
Check("SM_SAVEITEMLIST native capacity header", CheckStorageListWire);
Check("item source fields 55..108", CheckItemSourceWire);
Check("hero dynamic TClientEquip wire", CheckHeroDynamicEquipWire);
Check("hero ability header and empty list wire", CheckHeroProtocolHeadersAndEmptyLists);
Check("SM_SENDUSERSTATE 16 dynamic slots", CheckUserStateWire);
Check("white-pig 16-slot equipment rules", CheckWhitePigEquipmentSlots);
Check("equipment slots 13..15 contribute abilities", CheckExtendedEquipmentAbilities);
Check("command 16 direct MSG semantics", CheckDirectMessageSemantics);
Check("command 22 ranged MSG semantics", CheckGroundMessageSemantics);
Check("five Give format behavior", CheckGiveBehavior);
Check("GiveBindItem/BindGive bind propagation", CheckBindPropagation);

if (failures.Count != 0)
{
    Console.Error.WriteLine($"FAIL yanshen-2.07 protocol checks={failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine(
    "PASS yanshen-2.07 numeric=40 caret=15 chinese=6 give=5 bind=GiveBindItem+BindGive");

void Check(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex}");
    }
}

static void CheckNumericTunnel()
{
    var ids = Enumerable.Range(1, 41).Where(id => id != 6).ToArray();
    foreach (var id in ids)
    {
        var command = PluginManager.ParseTunnelCommand(
            $"!!!!\u96c6\u6210\u51fd\u6570,{id},11,\u53c2\u6570,33$");
        Require(command != null, $"ID {id} was not recognized");
        Equal(TunnelFormat.NumericId, command.Format, $"ID {id} format");
        Equal(id, command.CommandId, $"ID {id} command id");
        SequenceEqual(new[] { "11", "\u53c2\u6570", "33" }, command.Parameters,
            $"ID {id} parameters");
    }
}

static void CheckCaretTunnel()
{
    int[] ids = { 1, 2, 3, 10, 13, 20, 29, 30, 31, 32, 33, 34, 35, 36, 37 };
    foreach (var id in ids)
    {
        var command = PluginManager.ParseTunnelCommand(
            $"!!!!\u7231\u5fc3\u5206\u5272^{id}^\u53c2\u6570\u7532^\u53c2\u6570\u4e59$");
        Require(command != null, $"caret ID {id} was not recognized");
        Equal(TunnelFormat.CaretSeparated, command.Format, $"caret ID {id} format");
        Equal(id, command.CommandId, $"caret ID {id} command id");
        SequenceEqual(new[] { "\u53c2\u6570\u7532", "\u53c2\u6570\u4e59" }, command.Parameters,
            $"caret ID {id} parameters");
    }
}

static void CheckChineseTunnel()
{
    var cases = new[]
    {
        new ChineseCase("!!!!\u7ed9\u4e0e\u5143\u7d201:2:3:", "\u7ed9\u4e0e\u5143\u7d20", new[] { "1", "2", "3" }),
        new ChineseCase("!!!!\u83b7\u53d6\u5143\u7d201:2$", "\u83b7\u53d6\u5143\u7d20", new[] { "1", "2" }),
        new ChineseCase("!!!!\u5b9a\u4e49\u4f24\u5bb3100:200:", "\u5b9a\u4e49\u4f24\u5bb3", new[] { "100", "200" }),
        new ChineseCase("!!!!\u82f1\u96c4\u6781\u54c13:4:", "\u82f1\u96c4\u6781\u54c1", new[] { "3", "4" }),
        new ChineseCase("!!!!hq\u53d6sj\u6233", "hq\u53d6sj\u6233", Array.Empty<string>()),
        new ChineseCase("!!!!zd\u4e49\u56de\u6536", "zd\u4e49\u56de\u6536", Array.Empty<string>())
    };

    foreach (var item in cases)
    {
        var command = PluginManager.ParseTunnelCommand(item.Input);
        Require(command != null, $"{item.Name} was not recognized");
        Equal(TunnelFormat.ChineseName, command.Format, $"{item.Name} format");
        Equal(item.Name, command.ChineseCommand, $"{item.Name} command name");
        SequenceEqual(item.Parameters, command.Parameters, $"{item.Name} parameters");
    }

    // 原生只有 6 个中文命令名。两份运行期转储（2.0.7 0x102AA8E8.. /
    // 2.0.8 0x102BE81C..）全镜像里 NUL 结尾的 `!!!!` 串各只有 8 条 ——
    // 6 个中文名加 `!!!!集成函数`、`!!!!爱心分割`。下面这五个名字五编码
    // （ascii/GBK/UTF-16LE/UTF-8/Big5）在两版上都 0 命中，`攻击伤害` 唯一的
    // 两处 GBK 命中（2.0.8 0x102BC84E/0x102BCA36、2.0.7 0x102A8CDE/0x102A8EC6）
    // 都在 GUI 帮助文案正文里，没有任何 push 引用它做前缀比对。
    // 守卫它们不再被当成原生命令名切分。
    string[] fabricated =
    {
        "plus\u4f24\u5bb3",              // plus伤害
        "\u653b\u51fb\u4f24\u5bb3",      // 攻击伤害
        "hq\u53d6sj\u95f4",              // hq取sj间
        "zd\u56de\u6536",                // zd回收
        "\u7ed9\u4e88\u5143\u7d20",      // 给予元素
    };
    foreach (var name in fabricated)
    {
        var command = PluginManager.ParseTunnelCommand($"!!!!{name}1:2:");
        Require(command != null, $"{name} payload was not parsed at all");
        Require(command.ChineseCommand != name,
            $"{name} is not a native command name but was split out as one");
    }
}

// 入口选择器 sub_1005E4D0 的前缀链一条也比不中时，原生链尾 0x1005F1D6 →
// 0x1005F20F `mov eax,0xFFFFF988` 返回 -1656；挂在宿主
// TPlayObject.GetBagItemCount 0x007447C0 上的钩子 0x58A05264
// `cmp eax,0xFFFFF988` / 0x58BBAAF5 `je 0x58DBA7B2` 就去跑原函数体
// （0x58DBA7B2 重放 `55 8B EC 33 C9` 再 `push [0x1031B9A4]` / `ret`）。
// 该钩子在 2.0.7 运行期转储里被直接观测到（HOOK source=0x007447C0）。
// 守住「哪些串算命中」这条线：多一条都会把宿主行为吃掉，少一条会把已坐实的
// 隧道命令误判成物品名。
static void CheckSelectorFallback()
{
    string[] hits =
    {
        "!!!!\u96c6\u6210\u51fd\u6570,36,0$",              // !!!!集成函数
        "!!!!\u7231\u5fc3\u5206\u5272^13^0$",              // !!!!爱心分割
        "!!!!hq\u53d6sj\u6233",                            // !!!!hq取sj戳（全等）
        "!!!!zd\u4e49\u56de\u6536",                        // !!!!zd义回收（全等）
        "!!!!\u7ed9\u4e0e\u5143\u7d201:2:3:",              // !!!!给与元素
        "!!!!\u83b7\u53d6\u5143\u7d201:2$",                // !!!!获取元素
        "!!!!\u5b9a\u4e49\u4f24\u5bb3100:200:",            // !!!!定义伤害
        "!!!!\u82f1\u96c4\u6781\u54c13:4:",                // !!!!英雄极品
    };
    foreach (var input in hits)
        Require(PluginManager.IsNativeSelectorHit(input),
            $"'{input}' must be taken by the native selector");

    string[] misses =
    {
        // A.3 判定的五个自造名：两版运行期转储五编码全 0 命中。
        "!!!!plus\u4f24\u5bb31:2:",                        // plus伤害
        "!!!!\u653b\u51fb\u4f24\u5bb31:2:",                // 攻击伤害
        "!!!!hq\u53d6sj\u95f4",                            // hq取sj间
        "!!!!zd\u56de\u6536",                              // zd回收
        "!!!!\u7ed9\u4e88\u5143\u7d201:2:3:",              // 给予元素
        // 全等比对的两条多一个字符就比不中（sub_10043E20 = compare(const string&)）。
        "!!!!hq\u53d6sj\u62331",
        "!!!!zd\u4e49\u56de\u6536$",
        // 0x1005E737 砍掉最后 1 字节后，串正好等于 12 字节前缀的反而比不中。
        "!!!!\u7ed9\u4e0e\u5143\u7d20",
        "!!!!\u82f1\u96c4\u6781\u54c1",
        // 没有 `!!!!集成函数` 打头的裸数字串不在 8 条字面量里。
        "!!!!36,0$",
        "!!!!#ys,1,2$",
    };
    foreach (var input in misses)
        Require(!PluginManager.IsNativeSelectorHit(input),
            $"'{input}' must fall back to the host GetBagItemCount");
}

static void CheckGiveClassification()
{
    var values17 = string.Join(',', Enumerable.Range(1, 17));
    var jp6 = string.Join(',', Enumerable.Range(21, 6));
    var cases = new[]
    {
        new GiveCase("\u6d4b\u8bd5\u5251!!!!1|2|3|4|5|", "1|2|3|4|5|"),
        new GiveCase($"\u6d4b\u8bd5\u5251!!!!#ys,{values17}$", $"#ys,{values17}$"),
        new GiveCase($"\u6d4b\u8bd5\u5251!!!!#ys,{values17},{jp6}$jp2ys",
            $"#ys,{values17},{jp6}$jp2ys"),
        new GiveCase("\u6d4b\u8bd5\u5251!!!!#ys\u2026\u2026\u6765\u6e90\u7532\u2026\u2026\u63cf\u8ff0\u4e00\u2026\u2026\u63cf\u8ff0\u4e8c$zdyly",
            "#ys\u2026\u2026\u6765\u6e90\u7532\u2026\u2026\u63cf\u8ff0\u4e00\u2026\u2026\u63cf\u8ff0\u4e8c$zdyly"),
        new GiveCase("\u6d4b\u8bd5\u5251!!!!#ys,opaque:item:data$data",
            "#ys,opaque:item:data$data")
    };

    foreach (var item in cases)
    {
        var command = PluginManager.ParseTunnelCommand(item.Input);
        Require(command != null, $"Give payload '{item.Payload}' was not recognized");
        Require(command.Format is TunnelFormat.ItemGiveOld or TunnelFormat.ItemGiveNew
                or TunnelFormat.ItemGiveExt,
            $"Give payload '{item.Payload}' format: actual={command.Format}");
        Equal("\u6d4b\u8bd5\u5251", command.ItemName, $"Give payload '{item.Payload}' item name");
        Equal(item.Payload, command.RawPayload, $"Give payload '{item.Payload}' raw payload");
    }
}

static void CheckGiveBehavior()
{
    InitializeGameState();
    using var runtime = new InitializedYanshenRuntime("自定义元素", "眼神特殊函数");
    var player = NewPlayer();
    var npc = new NormNpc();
    var engine = new YanshenCommandEngine(player, npc, runtime.Manager);
    var values17 = string.Join(',', Enumerable.Range(1, 17));
    var jp6 = string.Join(',', Enumerable.Range(21, 6));

    player.m_ItemList.Clear();
    Require(engine.HandleGiveWithElements("\u6d4b\u8bd5\u5251!!!!1|2|3|4|5|", 1, false),
        "old five-element Give was not handled");
    var item = SingleItem(player, "old five-element Give");
    Equal(1, item.ys1, "old ys1");
    SequenceEqual(new byte[] { 2, 3, 4, 5 }, new[] { item.ys2, item.ys3, item.ys4, item.ys5 },
        "old ys2..ys5");

    player.m_ItemList.Clear();
    Require(engine.HandleGiveWithElements($"\u6d4b\u8bd5\u5251!!!!#ys,{values17}$", 1, false),
        "17-element Give was not handled");
    item = SingleItem(player, "17-element Give");
    SequenceEqual(Enumerable.Range(1, 17).ToArray(), Elements(item), "ys1..ys17");

    player.m_ItemList.Clear();
    Require(engine.HandleGiveWithElements(
            $"\u6d4b\u8bd5\u5251!!!!#ys,{values17},{jp6}$jp2ys", 1, false),
        "17-element plus JP Give was not handled");
    item = SingleItem(player, "17-element plus JP Give");
    SequenceEqual(Enumerable.Range(1, 17).ToArray(), Elements(item), "JP format ys1..ys17");
    SequenceEqual(Enumerable.Range(21, 6).Select(value => (byte)value).ToArray(),
        new[] { item.jp1, item.jp2, item.jp3, item.jp4, item.jp5, item.jp6 }, "jp1..jp6");

    player.m_ItemList.Clear();
    Require(engine.HandleGiveWithElements(
            "\u6d4b\u8bd5\u5251!!!!#ys\u2026\u2026\u6765\u6e90\u7532\u2026\u2026\u63cf\u8ff0\u4e00\u2026\u2026\u63cf\u8ff0\u4e8c$zdyly", 1, true),
        "description Give was not handled");
    item = SingleItem(player, "description Give");
    Equal("\u6765\u6e90\u7532", item.pname, "description pname");
    Equal("\u63cf\u8ff0\u4e00", item.desc1, "description line 1");
    Equal("\u63cf\u8ff0\u4e8c", item.desc2, "description line 2");
    Equal((byte)1, item.Bind, "description bind flag");

    player.m_ItemList.Clear();
    var source = new TUserItem
    {
        MakeIndex = 9001,
        wIndex = 1,
        Dura = 77,
        DuraMax = 123,
        ys1 = 123456789,
        ys2 = 22,
        ys17 = 177,
        jp1 = 31,
        jp6 = 36,
        Bind = 1,
        pname = "\u6765\u6e90\u4e59",
        desc1 = "\u6570\u636e\u4e00",
        desc2 = "\u6570\u636e\u4e8c"
    };
    source.btValue[0] = 9;
    player.m_ItemList.Add(source);
    var api = new YanshenApi(player, npc, runtime.Manager);
    var data = api.GetItemDataByMakeIndex(source.MakeIndex);
    Require(!string.IsNullOrEmpty(data), "GetItemDataByMakeIndex returned no data");
    player.m_ItemList.Clear();
    Require(engine.HandleGiveWithElements($"\u6d4b\u8bd5\u5251!!!!#ys,{data}$data", 1, false),
        "data Give was not handled");
    item = SingleItem(player, "data Give");
    Equal(source.wIndex, item.wIndex, "data wIndex");
    Equal(source.Dura, item.Dura, "data dura");
    Equal(source.DuraMax, item.DuraMax, "data duraMax");
    Equal(source.ys1, item.ys1, "data ys1");
    Equal(source.ys2, item.ys2, "data ys2");
    Equal(source.ys17, item.ys17, "data ys17");
    Equal(source.jp1, item.jp1, "data jp1");
    Equal(source.jp6, item.jp6, "data jp6");
    Equal(source.Bind, item.Bind, "data bind");
    Equal(source.pname, item.pname, "data pname");
    Equal(source.desc1, item.desc1, "data desc1");
    Equal(source.desc2, item.desc2, "data desc2");
    Equal(source.btValue[0], item.btValue[0], "data btValue[0]");
}

static void CheckDynamicClientItemWire()
{
    var item = PrepareWireItem();
    var encoded = InvokeClientItemEncoder(item);
    var record = DecodeClientItem(encoded);

    Equal(unchecked((uint)item.MakeIndex), record.MakeIndex, "TClientItem MakeIndex");
    Equal(item.wIndex, record.Index, "TClientItem Index");
    Equal(item.Dura, record.Dura, "TClientItem dura");
    Equal(item.DuraMax, record.DuraMax, "TClientItem duraMax");
    Equal(29, record.Fields.Count, "TClientItem KeyValueSize");
    Equal(16 + record.Fields.Count * 4, encoded.Length, "TClientItem total length");
    Equal(0u, record.Reserved, "TClientItem reserved");

    Equal((short)2, Field(record, 12), "Bind normalStateSet");
    short[] ys1Bytes = { 0x12, 0x34, 0x56, 0x78 };
    for (var i = 0; i < ys1Bytes.Length; i++)
        Equal(ys1Bytes[i], Field(record, 110 + i), $"ys1 byte valueType {110 + i}");
    for (var i = 0; i < 16; i++)
        Equal((short)(i + 2), Field(record, 114 + i), $"ys{i + 2} valueType {114 + i}");

    var expectedAttributes = new Dictionary<short, short>
    {
        [0] = 14,
        [1] = 51,
        [2] = 35,
        [3] = 73,
        [5] = 83,
        [7] = 106,
        [9] = 126,
        [19] = 41
    };
    foreach (var expected in expectedAttributes)
        Equal(expected.Value, Field(record, expected.Key),
            $"random/JP valueType {expected.Key}");
}

static void CheckHeroDynamicEquipWire()
{
    var item = PrepareWireItem();
    var expectedItem = InvokeClientItemEncoder(item);
    var useItems = new TUserItem[16];
    const int slot = 6;
    useItems[slot] = item;

    var method = typeof(HeroObject).GetMethod("EncodeHeroUseItems",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(method != null, "HeroObject.EncodeHeroUseItems reflection target missing");
    object[] arguments = { useItems, 0 };
    var encoded = (byte[])method.Invoke(null, arguments)!;

    Equal(1, (int)arguments[1], "hero equipped item count");
    Equal(slot, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0, 4)),
        "TClientEquip slot");
    Equal(4 + expectedItem.Length, encoded.Length, "TClientEquip total length");
    SequenceEqual(expectedItem, encoded.AsSpan(4).ToArray(),
        "TClientEquip dynamic TClientItem body");

    var record = DecodeClientItem(encoded.AsSpan(4).ToArray());
    Equal(29, record.Fields.Count, "hero TClientItem KeyValueSize");
    Equal((short)2, Field(record, 12), "hero Bind normalStateSet");
    Equal((short)0x12, Field(record, 110), "hero ys1 high byte");
    Equal((short)17, Field(record, 129), "hero ys17");
}

static void CheckHeroProtocolHeadersAndEmptyLists()
{
    const int currentExp = 0x12345678;
    const int job = 2;
    var headerMethod = typeof(HeroObject).GetMethod("BuildHeroAbilityHeader",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(headerMethod != null, "HeroObject.BuildHeroAbilityHeader reflection target missing");
    var header = (ClientPacket)headerMethod.Invoke(null,
        new object[] { currentExp, job })!;

    Equal(Grobal2.SM_HERO_ABILITY, header.Ident, "SM_HERO_ABILITY ident");
    Equal(currentExp, header.Recog, "SM_HERO_ABILITY current experience");
    Equal(job, header.Param, "SM_HERO_ABILITY job");
    Equal(0, header.Tag, "SM_HERO_ABILITY tag");
    Equal(0, header.Series, "SM_HERO_ABILITY series");

    var bagMethod = typeof(HeroObject).GetMethod("EncodeHeroBagItems",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(bagMethod != null, "HeroObject.EncodeHeroBagItems reflection target missing");
    object[] bagArguments = { Array.Empty<TUserItem>(), 0 };
    var bagBody = (byte[])bagMethod.Invoke(null, bagArguments)!;
    Equal(0, (int)bagArguments[1], "empty hero bag count");
    Equal(0, bagBody.Length, "empty hero bag body");

    var useItemsMethod = typeof(HeroObject).GetMethod("EncodeHeroUseItems",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(useItemsMethod != null, "HeroObject.EncodeHeroUseItems reflection target missing");
    object[] useItemsArguments = { new TUserItem[16], 0 };
    var useItemsBody = (byte[])useItemsMethod.Invoke(null, useItemsArguments)!;
    Equal(0, (int)useItemsArguments[1], "empty hero equipped-item count");
    Equal(0, useItemsBody.Length, "empty hero equipped-item body");

    var previousUserEngine = M2Share.UserEngine;
    try
    {
        const ushort magicId = 17;
        var magic = new TMagic { wMagicID = magicId, sMagicName = "hero-delete-wire" };
        var userEngine = new UserEngine();
        userEngine.m_MagicList.Add(magic);
        M2Share.UserEngine = userEngine;

        var hero = new HeroObject();
        var userMagic = new TUserMagic { MagicInfo = magic, wMagIdx = magicId };
        hero.m_HeroMagicList.Add(userMagic);
        hero.m_MagicArr[magicId] = userMagic;

        Require(hero.DeleteHeroMagic(magic.sMagicName), "hero magic delete rejected");
        Equal(0, hero.m_HeroMagicList.Count, "deleted hero magic list count");
        Require(hero.m_MagicArr[magicId] == null,
            "deleted hero magic remained in the indexed magic array");
    }
    finally
    {
        M2Share.UserEngine = previousUserEngine;
    }
}

static void CheckOwnedUseItemsWire()
{
    var source = PrepareWireItem();
    var player = NewPlayer();
    for (var slot = Grobal2.U_MASK; slot <= Grobal2.U_HORSE; slot++)
    {
        var item = new TUserItem(source)
        {
            MakeIndex = source.MakeIndex + slot
        };
        player.m_UseItems[slot] = item;
    }

    var method = typeof(TPlayObject).GetMethod("EncodeClientUseItems",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(method != null, "TPlayObject.EncodeClientUseItems reflection target missing");
    var encoded = (byte[])method.Invoke(player, null)!;

    var offset = 0;
    for (var expectedSlot = Grobal2.U_MASK;
         expectedSlot <= Grobal2.U_HORSE;
         expectedSlot++)
    {
        Require(offset + 20 <= encoded.Length,
            $"owned equipment slot {expectedSlot} record is short");
        Equal(expectedSlot,
            BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(offset, 4)),
            $"owned TClientEquip slot {expectedSlot}");
        var keyCount = BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(offset + 14, 2));
        var itemLength = 16 + keyCount * 4;
        Require(itemLength >= 16 && offset + 4 + itemLength <= encoded.Length,
            $"owned equipment slot {expectedSlot} has invalid length {itemLength}");
        var record = DecodeClientItem(encoded.AsSpan(offset + 4, itemLength).ToArray());
        Equal(unchecked((uint)player.m_UseItems[expectedSlot].ClientItemID), record.MakeIndex,
            $"owned equipment slot {expectedSlot} MakeIndex");
        Equal((short)2, Field(record, 12),
            $"owned equipment slot {expectedSlot} Bind");
        Equal((short)17, Field(record, 129),
            $"owned equipment slot {expectedSlot} ys17");
        offset += 4 + itemLength;
    }
    Equal(encoded.Length, offset, "owned equipment slots 13..15 body length");

    var sendMethod = typeof(TPlayObject).GetMethod("SendUseitems",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(sendMethod != null, "TPlayObject.SendUseitems reflection target missing");
    sendMethod.Invoke(player, null);
    Equal(Grobal2.SM_SENDUSEITEMS, player.m_DefMsg.Ident,
        "SM_SENDUSEITEMS header ident");
    Equal(0, player.m_DefMsg.Param, "SM_SENDUSEITEMS header param");
    Equal(3, player.m_DefMsg.Tag, "SM_SENDUSEITEMS header item count");
    Equal(0, player.m_DefMsg.Series, "SM_SENDUSEITEMS header series");
}

static void CheckDealDeleteCancelsTransaction()
{
    var method = typeof(TPlayObject).GetMethod("ClientDelDealItem",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(method != null, "TPlayObject.ClientDelDealItem reflection target missing");
    Require(typeof(TPlayObject).GetMethod("SendDelDealItem",
                BindingFlags.Instance | BindingFlags.NonPublic) == null,
        "obsolete single-item trade response path still exists");

    var local = NewPlayer();
    var remote = NewPlayer();
    var localItem = new TUserItem
    {
        MakeIndex = 0x210001,
        ClientItemID = 0x310001,
        wIndex = 1
    };
    var remoteItem = new TUserItem
    {
        MakeIndex = 0x210002,
        ClientItemID = 0x310002,
        wIndex = 1
    };
    local.m_DealItemList.Add(localItem);
    remote.m_DealItemList.Add(remoteItem);
    local.m_DealCreat = remote;
    remote.m_DealCreat = local;
    local.m_boDealing = true;
    remote.m_boDealing = true;
    local.m_nDealGolds = 17;
    remote.m_nDealGolds = 23;

    const int sentinelIdent = 32000;
    local.m_DefMsg = Grobal2.MakeDefaultMsg(sentinelIdent, 0, 0, 0, 0);
    remote.m_DefMsg = Grobal2.MakeDefaultMsg(sentinelIdent, 0, 0, 0, 0);
    var originalCannotGetBack = M2Share.g_Config.boCanNotGetBackDeal;
    try
    {
        M2Share.g_Config.boCanNotGetBackDeal = true;

        method.Invoke(local, new object[] { 0x7F000001, "ignored-name" });
        Require(local.m_boDealing && remote.m_boDealing,
            "invalid trade item id changed transaction state");
        Equal(1, local.m_DealItemList.Count,
            "invalid trade item id changed local trade list");
        Equal(sentinelIdent, local.m_DefMsg.Ident,
            "invalid trade item id emitted a response");
        Equal(sentinelIdent, remote.m_DefMsg.Ident,
            "invalid trade item id emitted a remote response");

        remote.m_boDealOK = true;
        method.Invoke(local, new object[] { localItem.ClientItemID, "ignored-name" });
        Require(local.m_boDealing && remote.m_boDealing,
            "confirmed partner did not block trade-item deletion");
        Equal(1, local.m_DealItemList.Count,
            "confirmed partner changed local trade list");
        Equal(sentinelIdent, local.m_DefMsg.Ident,
            "confirmed partner emitted a local response");
        Equal(sentinelIdent, remote.m_DefMsg.Ident,
            "confirmed partner emitted a remote response");

        remote.m_boDealOK = false;
        method.Invoke(local, new object[] { localItem.ClientItemID, "wrong-name" });
    }
    finally
    {
        M2Share.g_Config.boCanNotGetBackDeal = originalCannotGetBack;
    }

    Require(!local.m_boDealing && !remote.m_boDealing,
        "matched trade item id did not cancel both transaction sides");
    Require(local.m_DealCreat == null && remote.m_DealCreat == null,
        "cancelled transaction retained a partner reference");
    Equal(Grobal2.SM_DEALCANCEL, local.m_DefMsg.Ident,
        "local whole-transaction cancellation response");
    Equal(Grobal2.SM_DEALCANCEL, remote.m_DefMsg.Ident,
        "remote whole-transaction cancellation response");
    Equal(0, local.m_DealItemList.Count, "local trade list after cancellation");
    Equal(0, remote.m_DealItemList.Count, "remote trade list after cancellation");
    Require(local.m_ItemList.Contains(localItem),
        "local trade item was not rolled back to the bag");
    Require(remote.m_ItemList.Contains(remoteItem),
        "remote trade item was not rolled back to the bag");
    Equal(17, local.m_nGold, "local trade gold rollback");
    Equal(23, remote.m_nGold, "remote trade gold rollback");
}

static void CheckDealAddWire()
{
    var local = NewPlayer();
    var remote = NewPlayer();
    var item = PrepareWireItem();
    local.m_DealCreat = remote;

    var method = typeof(TPlayObject).GetMethod("SendAddDealItem",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(method != null, "TPlayObject.SendAddDealItem reflection target missing");
    method.Invoke(local, new object[] { item });

    Equal(Grobal2.SM_DEALREMOTEADDITEM, local.m_DefMsg.Ident,
        "SM_DEALREMOTEADDITEM header ident");
    Equal(local.ObjectId, local.m_DefMsg.Recog,
        "SM_DEALREMOTEADDITEM owner id");
    Equal(0, local.m_DefMsg.Param, "SM_DEALREMOTEADDITEM header param");
    Equal(0, local.m_DefMsg.Tag, "SM_DEALREMOTEADDITEM header tag");
    Equal(1, local.m_DefMsg.Series, "SM_DEALREMOTEADDITEM item count");

    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "Players",
        "TPlayObject.cs"));
    var start = source.IndexOf("private void SendAddDealItem(",
        StringComparison.Ordinal);
    var end = source.IndexOf("private void OpenDealDlg(",
        start, StringComparison.Ordinal);
    Require(start >= 0 && end > start, "SendAddDealItem source boundary missing");
    var body = source[start..end];
    Require(body.Contains(
            "SendDefMessage(Grobal2.SM_DEALADDITEM_OK, 0, 0, 0, 0",
            StringComparison.Ordinal),
        "SM_DEALADDITEM_OK is not emitted with the native zero header");
}

static void CheckStorageItemOkWire()
{
    var player = NewPlayer();
    var item = PrepareWireItem();
    item.ClientItemID = 0x310003;
    var method = typeof(TPlayObject).GetMethod("SendStorageItemOk",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(method != null, "TPlayObject.SendStorageItemOk reflection target missing");

    method.Invoke(player, new object[] { item });

    Equal(Grobal2.SM_STORAGE_OK, player.m_DefMsg.Ident,
        "SM_STORAGE_OK header ident");
    Equal(item.ClientItemID, player.m_DefMsg.Recog,
        "SM_STORAGE_OK stored item id");
    Equal(0, player.m_DefMsg.Param, "SM_STORAGE_OK storage type");
    Equal(0, player.m_DefMsg.Tag, "SM_STORAGE_OK header tag");
    Equal(0, player.m_DefMsg.Series, "SM_STORAGE_OK header series");

    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "Players",
        "TPlayObject.Operate.cs"));
    var start = source.IndexOf("internal void ClientStorageItem(",
        StringComparison.Ordinal);
    var end = source.IndexOf("internal void ClientTakeBackStorageItem(",
        start, StringComparison.Ordinal);
    Require(start >= 0 && end > start, "ClientStorageItem source boundary missing");
    var body = source[start..end];
    Require(body.Contains("SendStorageItemOk(UserItem);", StringComparison.Ordinal),
        "ClientStorageItem does not emit the native item-bearing success packet");
    Require(!body.Contains("SendSaveItemList(", StringComparison.Ordinal),
        "ClientStorageItem still sends a non-native full-list refresh");
    Require(!body.Contains("SendDefMessage(Grobal2.SM_STORAGE_OK",
            StringComparison.Ordinal),
        "ClientStorageItem still sends the obsolete empty success packet");

    source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    start = source.IndexOf("case \"storageitem\":", StringComparison.Ordinal);
    end = source.IndexOf("case \"getbackitem\":", start,
        StringComparison.Ordinal);
    Require(start >= 0 && end > start, "PAS StorageItem source boundary missing");
    body = source[start..end];
    Require(body.Contains("CurrentPlayer.SendStorageItemOk(item);",
            StringComparison.Ordinal),
        "PAS StorageItem does not emit the native item-bearing success packet");
    Require(!body.Contains("SendSaveItemList(", StringComparison.Ordinal),
        "PAS StorageItem still sends a non-native full-list refresh");
    Require(!body.Contains("SendDefMessage(Grobal2.SM_STORAGE_OK",
            StringComparison.Ordinal),
        "PAS StorageItem still sends the obsolete empty success packet");
    Require(body.Contains("int movedCount = 0;", StringComparison.Ordinal) &&
            body.Contains("movedCount++;", StringComparison.Ordinal),
        "PAS StorageItem does not track successful moves");
    var movedGuard = body.IndexOf("if (movedCount > 0)", StringComparison.Ordinal);
    var weightChanged = body.IndexOf("CurrentPlayer.WeightChanged();",
        StringComparison.Ordinal);
    var saveHuman = body.IndexOf("M2Share.UserEngine.SaveHumanRcd(CurrentPlayer);",
        StringComparison.Ordinal);
    Require(movedGuard >= 0 && movedGuard < weightChanged && weightChanged < saveHuman,
        "PAS StorageItem weight/save are not guarded by a successful move");
}

static void CheckItemProducerExactness()
{
    var player = NewPlayer();
    var item = PrepareWireItem();
    player.SendUpdateItem(item);

    Equal(Grobal2.SM_UPDATEITEM, player.m_DefMsg.Ident,
        "SendUpdateItem final packet ident");
    Equal(player.ObjectId, player.m_DefMsg.Recog,
        "SendUpdateItem owner id");
    Equal(0, player.m_DefMsg.Param, "SendUpdateItem header param");
    Equal(0, player.m_DefMsg.Tag, "SendUpdateItem header tag");
    Equal(1, player.m_DefMsg.Series, "SendUpdateItem item count");

    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "Players",
        "TPlayObject.cs"));
    var start = source.IndexOf("public void SendUpdateItem(",
        StringComparison.Ordinal);
    var end = source.IndexOf("private bool CheckTakeOnItems(", start,
        StringComparison.Ordinal);
    Require(start >= 0 && end > start, "SendUpdateItem source boundary missing");
    var body = source[start..end];
    Require(!body.Contains("SM_ITEMUPDATE", StringComparison.Ordinal),
        "SendUpdateItem still emits the non-native SM1500 refresh");

    source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "Players",
        "TPlayObject.Operate.cs"));
    start = source.IndexOf("private void ClientUseItems(int itemId, int useMode)",
        StringComparison.Ordinal);
    end = source.IndexOf("private void NotifyItemActivePoint(", start,
        StringComparison.Ordinal);
    Require(start >= 0 && end > start, "ClientUseItems source boundary missing");
    body = source[start..end];
    var bagDura = body.IndexOf("SendDefMessage(Grobal2.SM_BAGITEMDURACHG,",
        StringComparison.Ordinal);
    var pileEatFail = body.LastIndexOf("SendDefMessage(Grobal2.SM_EAT_FAIL,",
        bagDura, StringComparison.Ordinal);
    var weightChanged = body.IndexOf("WeightChanged();", bagDura,
        StringComparison.Ordinal);
    Require(pileEatFail >= 0 && pileEatFail < bagDura && bagDura < weightChanged,
        "partial pile consume is not SM636 then SM641 then WeightChanged");
    Require(body.Contains("itemId, item.Dura, item.DuraMax, 0, \"\");",
            StringComparison.Ordinal),
        "partial pile SM641 header is not (itemId,Dura,DuraMax,0)");
    Require(!body.Contains("SendUpdateItem(item);", StringComparison.Ordinal),
        "partial pile consume still emits SM203");

    start = source.IndexOf("private void ClientTakeOnItems(",
        StringComparison.Ordinal);
    end = source.IndexOf("private void ClientTakeOffItems(", start,
        StringComparison.Ordinal);
    Require(start >= 0 && end > start, "ClientTakeOnItems source boundary missing");
    body = source[start..end];
    var takeOnOk = body.IndexOf("Grobal2.SM_TAKEON_OK", StringComparison.Ordinal);
    weightChanged = body.IndexOf("WeightChanged();", takeOnOk,
        StringComparison.Ordinal);
    Require(takeOnOk >= 0 && weightChanged > takeOnOk,
        "successful take-on does not refresh bag weight after SM615");

    source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    start = source.IndexOf("case Grobal2.CM_DROPITEM:", StringComparison.Ordinal);
    end = source.IndexOf("case Grobal2.CM_PICKUP:", start,
        StringComparison.Ordinal);
    Require(start >= 0 && end > start, "CM_DROPITEM source boundary missing");
    body = source[start..end];
    Require(body.Contains(
            "SendDefMessage(Grobal2.SM_DROPITEM_SUCCESS, ProcessMsg.nParam1, 0, 0, 0, \"\");",
            StringComparison.Ordinal),
        "SM600 drop success is not header-only");
    Require(body.Contains(
            "SendDefMessage(Grobal2.SM_DROPITEM_FAIL, ProcessMsg.nParam1, 0, 0, 0, \"\");",
            StringComparison.Ordinal),
        "SM601 drop failure is not header-only");

    source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    start = source.IndexOf("case \"delbagitemofall\":", StringComparison.Ordinal);
    end = source.IndexOf("case \"setplayerlevel\":", start,
        StringComparison.Ordinal);
    Require(start >= 0 && end > start, "PAS DelBagItemOfAll source boundary missing");
    body = source[start..end];
    Require(!body.Contains("SM_DELITEM", StringComparison.Ordinal) &&
            !body.Contains("SendUpdateItem(", StringComparison.Ordinal) &&
            !body.Contains("SM_ITEMUPDATE", StringComparison.Ordinal),
        "PAS DelBagItemOfAll still emits non-native item packets");
}

static void CheckStorageListWire()
{
    var player = NewPlayer();
    var template = PrepareWireItem();
    for (var i = 0; i <= TPlayObject.STORAGE_PAGE_SIZE; i++)
    {
        var item = new TUserItem(template)
        {
            MakeIndex = template.MakeIndex + i + 1,
            ClientItemID = template.MakeIndex + 0x10000 + i,
            wIndex = 1
        };
        player.m_StorageItemList.Add(item);
    }

    const int merchantId = 0x123456;
    var method = typeof(TPlayObject).GetMethod("SendSaveItemList",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(method != null, "TPlayObject.SendSaveItemList reflection target missing");
    player.m_nStoragePage = 0;
    method.Invoke(player, new object[] { merchantId });
    Equal(Grobal2.SM_SAVEITEMLIST, player.m_DefMsg.Ident,
        "SM_SAVEITEMLIST first-page ident");
    Equal(merchantId, player.m_DefMsg.Recog,
        "SM_SAVEITEMLIST merchant id");
    Equal(TPlayObject.STORAGE_PAGE_SIZE, player.m_DefMsg.Param,
        "SM_SAVEITEMLIST first-page item count");
    Equal(TPlayObject.STORAGE_PAGE_SIZE, player.m_DefMsg.Tag,
        "SM_SAVEITEMLIST native page capacity");
    Equal(0, player.m_DefMsg.Series,
        "SM_SAVEITEMLIST first-page index");

    player.m_nStoragePage = 1;
    method.Invoke(player, new object[] { merchantId });
    Equal(1, player.m_DefMsg.Param,
        "SM_SAVEITEMLIST second-page item count");
    Equal(TPlayObject.STORAGE_PAGE_SIZE, player.m_DefMsg.Tag,
        "SM_SAVEITEMLIST second-page capacity");
    Equal(1, player.m_DefMsg.Series,
        "SM_SAVEITEMLIST second-page index");
}

static void CheckItemSourceWire()
{
    var item = PrepareWireItem();
    var timestamp = new DateTime(2026, 7, 14, 23, 5, 0);
    item.sourceTime = "2026-07-14 23:05:00";
    item.mapName = "盟重土城";
    item.killerName = "白野猪";
    item.pname = "玩家甲";

    var record = DecodeClientItem(InvokeClientItemEncoder(item));
    var day = (int)Math.Floor(timestamp.ToOADate());
    Equal((short)(day & 0xFF), Field(record, 55), "source OLE day low byte");
    Equal((short)((day >> 8) & 0xFF), Field(record, 56), "source OLE day high byte");
    Equal((short)5, Field(record, 57), "source minute");
    Equal((short)23, Field(record, 58), "source hour");
    Equal("盟重土城", DecodeFieldText(record, 59, 16), "source map");
    Equal("白野猪", DecodeFieldText(record, 76, 15), "source monster");
    Equal("玩家甲", DecodeFieldText(record, 92, 15), "source player");
    Require(!record.Fields.ContainsKey(107), "monster source was marked as system");
    Equal((short)1, Field(record, 108), "source sentinel");

    item.mapName = string.Empty;
    item.killerName = "锻造师";
    item.pname = "不下发";
    record = DecodeClientItem(InvokeClientItemEncoder(item));
    Equal("锻造师", DecodeFieldText(record, 76, 15), "system creator");
    Equal((short)1, Field(record, 107), "system source flag");
    Equal((short)1, Field(record, 108), "system source sentinel");
    for (var type = 92; type <= 106; type++)
        Require(!record.Fields.ContainsKey((short)type),
            $"system source unexpectedly contains player field {type}");
}

static void CheckUserStateWire()
{
    var item = PrepareWireItem();
    var useItems = new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT];
    const int slot = Grobal2.U_HORSE;
    useItems[slot] = item;

    var method = typeof(TPlayObject).GetMethod("EncodeClientUserState",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(method != null, "TPlayObject.EncodeClientUserState reflection target missing");
    var encoded = (byte[])method.Invoke(null, new object[]
    {
        0x12345678, "角色甲", (ushort)9, "行会甲", "职位甲", useItems
    })!;

    Equal(0x12345678, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0, 4)),
        "TUserStateInfo feature");
    Equal("角色甲", DecodeShortString(encoded, 4, 15), "TUserStateInfo userName");
    Equal((byte)9, encoded[20], "TUserStateInfo nameColorIndex");
    Equal((byte)0, encoded[21], "TUserStateInfo milrankSW");
    Equal((byte)0, encoded[22], "TUserStateInfo b2");
    Equal((byte)0, encoded[23], "TUserStateInfo vipFlag");
    Equal("行会甲", DecodeShortString(encoded, 24, 15), "TUserStateInfo guildName");
    Equal("职位甲", DecodeShortString(encoded, 40, 15), "TUserStateInfo clanName");

    var offset = 56;
    for (var index = 0; index < 16; index++)
    {
        var keyCount = BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(offset + 10, 2));
        var length = 16 + keyCount * 4;
        Require(length >= 16 && offset + length <= encoded.Length,
            $"TUserStateInfo slot {index} has invalid dynamic length {length}");
        if (index == slot)
        {
            var record = DecodeClientItem(encoded.AsSpan(offset, length).ToArray());
            Equal(unchecked((uint)item.MakeIndex), record.MakeIndex,
                "TUserStateInfo equipped MakeIndex");
            Equal((short)2, Field(record, 12), "TUserStateInfo equipped Bind");
            Equal((short)0x12, Field(record, 110), "TUserStateInfo equipped ys1");
        }
        else
        {
            Require(encoded.AsSpan(offset, length).ToArray().All(value => value == 0),
                $"TUserStateInfo empty slot {index} is nonzero");
        }
        offset += length;
    }
    Equal(encoded.Length, offset, "TUserStateInfo 16-slot body length");
}

static void CheckWhitePigEquipmentSlots()
{
    Equal(16, Grobal2.HUMAN_EQUIPPED_ITEM_COUNT, "human equipment slot count");
    var eyeSlots = new[]
    {
        (Slot: Grobal2.U_MASK, Name: "斗笠", StdMode: (byte)16),
        (Slot: Grobal2.U_YUPEI, Name: "玉佩", StdMode: (byte)29),
        (Slot: Grobal2.U_HORSE, Name: "盾牌", StdMode: (byte)34)
    };
    foreach (var entry in eyeSlots)
    {
        Equal(entry.Slot, M2Share.GetUseItemIdx(entry.Name),
            $"equipment name -> slot {entry.Name}");
        Equal(entry.Name, M2Share.GetUseItemName(entry.Slot),
            $"equipment slot -> name {entry.Slot}");
        Require(M2Share.CheckUserItems(entry.Slot,
                new GoodItem { StdMode = entry.StdMode }),
            $"StdMode {entry.StdMode} rejected for slot {entry.Slot}");
        Require(!M2Share.CheckUserItems(entry.Slot,
                new GoodItem { StdMode = byte.MaxValue }),
            $"invalid StdMode accepted for slot {entry.Slot}");
    }

    // A8 裁决（2026-08-14）：腰带/鞋子/宝石各只收一个 StdMode，扩展号一律拒收。
    //
    // 本文件建树时（d5d00744 基线导入）此处写的是 腰带{27,54,64}/鞋子{28,52,62}/
    // 宝石{7,53,63}，从未给出原生出处，与 M2Share.CheckUserItems 的 DURA-16/17
    // 反演正面冲突。本轮在 flat_image.bin(base 0x400000) 与眼神 2.0.8 转储
    // (base 0x10000000) 上逐字节裁决，判 DURA-16/17 为准，扩展号是无据之谈：
    //
    // 1) 宿主 StdMode -> 物品类 的派发是 byte 表 0x74C374[StdMode] 取臂号、
    //    dword 表 0x74C414[臂号] 取构造臂。实测：
    //      27 -> 11h -> 0074D141  mov eax,[0x75DA68]  TBelt
    //      28 -> 12h -> 0074D157  mov eax,[0x75DB58]  TBoots
    //       7 -> 07h -> 0074CE9E  TCharm 族跳表
    //      51/52/53/54/63/64 -> 00h -> 默认臂 0074D67E mov eax,[0x781BD8] TBasePileItem
    //      62               -> 20h -> 0074D3E2 mov eax,[0x7825C8] TAnimalMascot
    // 2) 槽位资格是 VMT+0x60 谓词。TBelt/TBoots/TCharm 的谓词体分别是
    //      00762D30 80FA0A 0F94C0 C3   cmp dl,0Ah / sete al / ret
    //      007630CC 80FA0B 0F94C0 C3   cmp dl,0Bh
    //      00763390 80FA0C 0F94C0 C3   cmp dl,0Ch
    //    ——各自只对一个槽号返回真，没有第二个受理号。
    //    TBasePileItem VMT 0x781C24 的 +0x60 = 0x781C88 落在类名串 "TBasePileItem"、
    //    TAnimalMascot VMT 0x782614 的 +0x60 = 0x782678 落在类名串 "TAnimalMascot"
    //    ——两者父链都不是 TEquipItem，VMT 里根本没有谓词槽，原生穿不上。
    // 3) 眼神插件没有扩展它：把上述 20 个 VA（两张派发表、四条构造臂、三个谓词体、
    //    四个 classref 全局、四张 VMT、TEquipItem 基类谓词 0x75FE18）在 45 MB 插件
    //    转储里做绝对 dword 引用普查，命中 **0**；三条谓词的字节签名
    //    80FA0A0F94C0C3 / 80FA0B… / 80FA0C… 作为补丁 blob 也 0 命中。
    //    对照组（插件确实要打的挂载点）同一把尺子量各有 4~10 处引用：
    //      0x76C88B=8  0x76C816=6  0x6EDC5E=4  0x6EC111=4  0x6C09B5=4
    //      0x76E2BC=8  0x767BAE=10 0x6BC9E2=4  0x691E2E=5  0x7720FB=4
    //    ——普查方法有效，结论是插件从不碰装备资格。
    var slotOnlyStdMode = new[]
    {
        (Slot: Grobal2.U_BELT,  Name: "belt",  Accept: (byte)27, Reject: new byte[] { 51, 52, 53, 54, 62, 63, 64 }),
        (Slot: Grobal2.U_BOOTS, Name: "boots", Accept: (byte)28, Reject: new byte[] { 51, 52, 53, 54, 62, 63, 64 }),
        (Slot: Grobal2.U_CHARM, Name: "charm", Accept: (byte)7,  Reject: new byte[] { 51, 52, 53, 54, 62, 63, 64 })
    };
    foreach (var entry in slotOnlyStdMode)
    {
        Require(M2Share.CheckUserItems(entry.Slot,
                new GoodItem { StdMode = entry.Accept }),
            $"{entry.Name} must accept its native StdMode {entry.Accept}");
        foreach (var stdMode in entry.Reject)
            Require(!M2Share.CheckUserItems(entry.Slot,
                    new GoodItem { StdMode = stdMode }),
                $"{entry.Name} must reject StdMode {stdMode}: it constructs "
                + "TBasePileItem/TAnimalMascot, whose VMT has no +0x60 predicate");
    }
}

static void CheckExtendedEquipmentAbilities()
{
    InitializeGameState();
    M2Share.UserEngine.StdItemList.Clear();
    var slots = new[]
    {
        (Slot: Grobal2.U_MASK, StdMode: (byte)16, Dc: (ushort)1, Dc2: (ushort)2),
        (Slot: Grobal2.U_YUPEI, StdMode: (byte)29, Dc: (ushort)4, Dc2: (ushort)8),
        (Slot: Grobal2.U_HORSE, StdMode: (byte)34, Dc: (ushort)16, Dc2: (ushort)32)
    };
    var player = NewPlayer();
    foreach (var entry in slots)
    {
        M2Share.UserEngine.StdItemList.Add(new GoodItem
        {
            Name = "extended-slot-" + entry.Slot,
            ItemType = GoodType.ITEM_ACCESSORY,
            StdMode = entry.StdMode,
            Dc = entry.Dc,
            Dc2 = entry.Dc2
        });
        player.m_UseItems[entry.Slot] = new TUserItem
        {
            MakeIndex = 0x4000 + entry.Slot,
            wIndex = (ushort)M2Share.UserEngine.StdItemList.Count,
            Dura = 1,
            DuraMax = 1
        };
    }

    player.RecalcAbilitys();
    Equal(21, HUtil32.LoWord(player.m_AddAbil.wDC),
        "extended equipment DC low total");
    Equal(42, HUtil32.HiWord(player.m_AddAbil.wDC),
        "extended equipment DC high total");
    Equal(22, HUtil32.LoWord(player.m_WAbil.DC),
        "extended equipment effective DC low (including base 1)");
    Equal(47, HUtil32.HiWord(player.m_WAbil.DC),
        "extended equipment effective DC high (including base 4 and naked adjustment 1)");
}

static TUserItem PrepareWireItem()
{
    InitializeGameState();
    M2Share.UserEngine.StdItemList.Clear();
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "wire-item",
        ItemType = GoodType.ITEM_WEAPON,
        StdMode = 5,
        Ac = 10,
        Ac2 = 20,
        Mac = 30,
        Mac2 = 40,
        Dc = 50,
        Dc2 = 60,
        Mc = 70,
        Mc2 = 80,
        Sc = 90,
        Sc2 = 100
    });
    var item = new TUserItem
    {
        MakeIndex = 0x11223344,
        wIndex = 1,
        Dura = 0x5566,
        DuraMax = 0x7788,
        ys1 = 0x12345678,
        ys2 = 2,
        ys3 = 3,
        ys4 = 4,
        ys5 = 5,
        ys6 = 6,
        ys7 = 7,
        ys8 = 8,
        ys9 = 9,
        ys10 = 10,
        ys11 = 11,
        ys12 = 12,
        ys13 = 13,
        ys14 = 14,
        ys15 = 15,
        ys16 = 16,
        ys17 = 17,
        jp1 = 21,
        jp2 = 22,
        jp3 = 23,
        jp4 = 24,
        jp5 = 25,
        jp6 = 26,
        Bind = 1
    };
    for (var i = 0; i < 7; i++)
        item.btValue[i] = (byte)(i + 1);
    return item;
}

static byte[] InvokeClientItemEncoder(TUserItem item)
{
    var method = typeof(TPlayObject).GetMethod("EncodeClientItemRecord",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(method != null, "TPlayObject.EncodeClientItemRecord reflection target missing");
    return (byte[])method.Invoke(null, new object[] { item })!;
}

static DecodedClientItem DecodeClientItem(byte[] encoded)
{
    Require(encoded.Length >= 16, $"TClientItem header is short: {encoded.Length}");
    var keyValueSize = BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(10, 2));
    Require(keyValueSize >= 0, $"negative KeyValueSize: {keyValueSize}");
    Equal(16 + keyValueSize * 4, encoded.Length, "TClientItem encoded length");

    var fields = new Dictionary<short, short>();
    for (var i = 0; i < keyValueSize; i++)
    {
        var offset = 16 + i * 4;
        var type = BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(offset, 2));
        var value = BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(offset + 2, 2));
        Require(fields.TryAdd(type, value), $"duplicate TClientItem valueType: {type}");
    }

    return new DecodedClientItem(
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0, 4)),
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(4, 2)),
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(6, 2)),
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(8, 2)),
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(12, 4)),
        fields);
}

static short Field(DecodedClientItem record, int valueType)
{
    Require(record.Fields.TryGetValue((short)valueType, out var value),
        $"TClientItem valueType {valueType} missing");
    return value;
}

static string DecodeFieldText(DecodedClientItem record, int firstType, int byteCount)
{
    var bytes = new byte[byteCount];
    for (var i = 0; i < byteCount; i++)
        if (record.Fields.TryGetValue((short)(firstType + i), out var value))
            bytes[i] = (byte)value;
    var length = Array.IndexOf(bytes, (byte)0);
    if (length < 0) length = bytes.Length;
    return HUtil32.GbkEncoding.GetString(bytes, 0, length);
}

static string DecodeShortString(byte[] data, int offset, int maxBytes)
{
    var length = Math.Min(data[offset], maxBytes);
    return HUtil32.GbkEncoding.GetString(data, offset + 1, length);
}

static void CheckBindPropagation()
{
    InitializeGameState();
    using var runtime = new InitializedYanshenRuntime("自定义元素");
    var values17 = string.Join(',', Enumerable.Range(1, 17));
    var payload = $"\u6d4b\u8bd5\u5251!!!!#ys,{values17}$";
    foreach (var method in new[] { "GiveBindItem", "BindGive" })
    {
        var player = NewPlayer();
        var bridge = new PasApiBridge();
        using (bridge.PushContext(player, new NormNpc()))
        {
            Require(bridge.CallPlayerMethod(method,
                    new List<PasValue> { PasValue.FromString(payload), PasValue.FromInt(1) }),
                $"{method} was not dispatched");
        }
        var item = SingleItem(player, method);
        Equal((byte)1, item.Bind, $"{method} bind flag");
    }
}

static void CheckDirectMessageSemantics()
{
    var header = MakeDirectHeader(101, 17, 18, 19, 1271);
    Equal(101, header.Recog, "command 16 header Recog");
    Equal((ushort)1271, header.Ident, "command 16 header Ident");
    Equal((ushort)17, header.Param, "command 16 header Param");
    Equal((ushort)18, header.Tag, "command 16 header Tag");
    Equal((ushort)19, header.Series, "command 16 header Series");

    InitializeGameState();
    using var runtime = new InitializedYanshenRuntime("眼神特殊函数");
    var gateServices = GetGateServices();
    gateServices.Clear();

    var callerGate = CreateGateService(11);
    var otherGate = CreateGateService(12);
    gateServices[11] = callerGate;
    gateServices[12] = otherGate;

    try
    {
        var caller = NewPlayer();
        caller.m_sCharName = "direct-caller";
        caller.m_boOffLineFlag = false;
        caller.m_nGateIdx = 11;
        caller.m_nSocket = 1101;
        caller.m_nGSocketIdx = 1;

        var other = NewPlayer();
        other.m_sCharName = "direct-other";
        other.m_boOffLineFlag = false;
        other.m_nGateIdx = 12;
        other.m_nSocket = 1201;
        other.m_nGSocketIdx = 2;

        var api = new YanshenApi(caller, new NormNpc(), runtime.Manager);
        Equal(0, api.SendDirectMessage(101, 17, 18, 19, 1271, "direct-body"),
            "command 16 send result");
        Equal(1, callerGate.GateInfo.nSendCount, "command 16 sender queue count");
        Equal(0, otherGate.GateInfo.nSendCount, "command 16 only caller receives");
    }
    finally
    {
        gateServices.Clear();
        callerGate.GateInfo.Socket?.Dispose();
        otherGate.GateInfo.Socket?.Dispose();
    }
}

static void CheckGroundMessageSemantics()
{
    InitializeGameState();
    var callerMap = NewGroundMap();
    var sourceMap = NewGroundMap();
    var caller = NewGroundPlayer("caller", callerMap, 5, 5);
    var source = NewGroundPlayer("source", sourceMap, 20, 20);
    var lowerEdge = NewGroundPlayer("lower", callerMap, 18, 18);
    var inside = NewGroundPlayer("inside", callerMap, 21, 21);
    var upperX = NewGroundPlayer("upper-x", callerMap, 22, 20);
    var upperY = NewGroundPlayer("upper-y", callerMap, 20, 22);

    PlaceGroundPlayer(callerMap, caller);
    PlaceGroundPlayer(sourceMap, source);
    PlaceGroundPlayer(callerMap, lowerEdge);
    PlaceGroundPlayer(callerMap, inside);
    PlaceGroundPlayer(callerMap, upperX);
    PlaceGroundPlayer(callerMap, upperY);

    caller.m_TargetCret = source;
    Equal(source, ResolveGroundSource(caller, -1), "command 22 Recog=-1 target");
    Equal(caller, ResolveGroundSource(caller, 0), "command 22 Recog=0 caller");
    Equal(source, ResolveGroundSource(caller, source.ObjectId),
        "command 22 positive ObjectId");
    Equal(caller, ResolveGroundSource(caller, int.MaxValue),
        "command 22 invalid positive ObjectId");

    caller.m_TargetCret = null;
    Equal(caller, ResolveGroundSource(caller, -1),
        "command 22 Recog=-1 null-target fallback");
    caller.m_TargetCret = source;

    var recipients = FindGroundRecipients(caller, source, 2);
    SequenceEqual(new[] { lowerEdge.ObjectId, inside.ObjectId }.OrderBy(value => value),
        recipients.Select(player => player.ObjectId).OrderBy(value => value),
        "command 22 caller-map half-open recipients");
    Require(!recipients.Contains(source), "command 22 incorrectly used the source object's map");
    Require(!recipients.Contains(upperX) && !recipients.Contains(upperY),
        "command 22 included center+range boundary");
    Equal(0, FindGroundRecipients(caller, source, 0).Count,
        "command 22 zero range");

    var header = MakeGroundHeader(source, 17, 18, 19, 1314);
    Equal(source.ObjectId, header.Recog, "command 22 normalized header Recog");
    Equal((ushort)1314, header.Ident, "command 22 header Ident");
    Equal((ushort)17, header.Param, "command 22 header Param");
    Equal((ushort)18, header.Tag, "command 22 header Tag");
    Equal((ushort)19, header.Series, "command 22 header Series");
}

static ClientPacket MakeDirectHeader(int recog, int param, int tag, int series, int ident)
{
    var method = typeof(YanshenApi).GetMethod("MakeDirectMessage",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(method != null, "MakeDirectMessage reflection target missing");
    return (ClientPacket)method.Invoke(null, new object[] { recog, param, tag, series, ident });
}

static TBaseObject ResolveGroundSource(TPlayObject caller, int recog)
{
    var method = typeof(YanshenApi).GetMethod("ResolveGroundMessageSource",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(method != null, "ResolveGroundMessageSource reflection target missing");
    return (TBaseObject)method.Invoke(null, new object[] { caller, recog });
}

static List<TPlayObject> FindGroundRecipients(TPlayObject caller, TBaseObject source, int range)
{
    var method = typeof(YanshenApi).GetMethod("FindGroundMessageRecipients",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(method != null, "FindGroundMessageRecipients reflection target missing");
    return (List<TPlayObject>)method.Invoke(null, new object[] { caller, source, range });
}

static ClientPacket MakeGroundHeader(TBaseObject source, int param, int tag, int series, int ident)
{
    var method = typeof(YanshenApi).GetMethod("MakeGroundMessage",
        BindingFlags.Static | BindingFlags.NonPublic);
    Require(method != null, "MakeGroundMessage reflection target missing");
    return (ClientPacket)method.Invoke(null, new object[] { source, param, tag, series, ident });
}

static Envirnoment NewGroundMap()
{
    var map = new Envirnoment();
    typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(map, new object[] { (short)64, (short)64 });
    return map;
}

static TPlayObject NewGroundPlayer(string name, Envirnoment map, short x, short y)
{
    return new TPlayObject
    {
        m_sCharName = name,
        m_PEnvir = map,
        m_nCurrX = x,
        m_nCurrY = y,
        m_boOffLineFlag = true
    };
}

// AddToMap, not MoveToMovingObject: the original's mover sub_7797CC only reports
// success from 0x779A95, which is reached after unlinking the actor from the SOURCE
// cell. Asking it to move an actor out of a cell it was never in walks the empty
// list and falls through to `xor eax,eax` @0x779AAD, i.e. FALSE. A first placement
// has no source cell, so the mover is the wrong primitive for it.
static void PlaceGroundPlayer(Envirnoment map, TPlayObject player)
{
    player.m_boAddToMaped = false;
    player.m_boDelFormMaped = false;
    Require(ReferenceEquals(player, map.AddToMap(player.m_nCurrX, player.m_nCurrY,
        CellType.OS_MOVINGOBJECT, player)), "place command 22 player");
}

static void InitializeGameState()
{
    M2Share.UserEngine = new UserEngine();
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "\u6d4b\u8bd5\u5251",
        DuraMax = 100,
        Weight = 0
    });
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogSystem = new MirLog();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.g_Config.nCheckBlock = 0;
}

static ConcurrentDictionary<int, GateService> GetGateServices()
{
    M2Share.GateManager ??= GateManager.Instance;
    var field = typeof(GateManager).GetField("_gateDataService",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(field != null, "GateManager gate dictionary reflection target missing");
    return (ConcurrentDictionary<int, GateService>)field.GetValue(M2Share.GateManager)!;
}

static GateService CreateGateService(int gateIdx)
{
    return new GateService(gateIdx, new TGateInfo
    {
        boUsed = true,
        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp),
        SocketId = gateIdx,
        UserList = new List<TGateUserInfo>(),
        nUserCount = 0,
        nSendChecked = 0,
        nSendBlockCount = 0
    });
}

static TPlayObject NewPlayer() => new()
{
    m_sCharName = "yanshen-check",
    m_PEnvir = new Envirnoment(),
    m_boObMode = true,
    m_boOffLineFlag = true
};

static TUserItem SingleItem(TPlayObject player, string context)
{
    Equal(1, player.m_ItemList.Count, $"{context} item count");
    return player.m_ItemList[0];
}

static int[] Elements(TUserItem item) =>
[
    item.ys1, item.ys2, item.ys3, item.ys4, item.ys5, item.ys6,
    item.ys7, item.ys8, item.ys9, item.ys10, item.ys11, item.ys12,
    item.ys13, item.ys14, item.ys15, item.ys16, item.ys17
];

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected={expected} actual={actual}");
}

static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();
    if (!expectedArray.SequenceEqual(actualArray))
        throw new InvalidOperationException(
            $"{message}: expected=[{string.Join(',', expectedArray)}] " +
            $"actual=[{string.Join(',', actualArray)}]");
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

internal sealed record ChineseCase(string Input, string Name, string[] Parameters);
internal sealed record GiveCase(string Input, string Payload);
internal sealed record DecodedClientItem(uint MakeIndex, ushort Index, ushort Dura,
    ushort DuraMax, uint Reserved, Dictionary<short, short> Fields);

internal sealed class InitializedYanshenRuntime : IDisposable
{
    private readonly string _root;
    private readonly PluginManager _previousManager;

    public PluginManager Manager { get; }

    public InitializedYanshenRuntime(params string[] enabledSwitches)
    {
        _root = Path.Combine(Path.GetTempPath(),
            "loym2-yanshen207-protocol-" + Guid.NewGuid().ToString("N"));
        var envir = Directory.CreateDirectory(Path.Combine(_root, "Envir")).FullName;
        var runtime = Directory.CreateDirectory(Path.Combine(_root, "GS1")).FullName;
        var config = "{" + string.Join(',', enabledSwitches.Select(name =>
            $"\"{name}\":1")) + "}";
        File.WriteAllText(Path.Combine(runtime, "config.json"), config,
            Encoding.GetEncoding(936));

        Manager = new PluginManager(envir, runtime);
        Manager.RegisterBuiltinPlugins();
        if (!Manager.LoadPlugin("YanshenCompat"))
            throw new InvalidOperationException("YanshenCompat did not enter Running state");

        _previousManager = M2Share.PluginManager;
        M2Share.PluginManager = Manager;
        var initializerPath = Path.Combine(envir, "PsMapQuest", "RunQuest.pas");
        Directory.CreateDirectory(Path.GetDirectoryName(initializerPath)!);
        const string source = "program Init; procedure initys; begin end; begin end.";
        var program = new PasParser(new PasLexer(source, initializerPath), envir).Parse();
        new PasInterpreter(program, new PasApiBridge()).ExecuteProcedure("initys");
        if (!Manager.GetPlugin("YanshenCompat").IsInitialized)
            throw new InvalidOperationException("RunQuest initys did not initialize YanshenCompat");
    }

    public void Dispose()
    {
        M2Share.PluginManager = _previousManager;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}

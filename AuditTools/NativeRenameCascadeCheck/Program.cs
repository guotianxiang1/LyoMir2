// 断言角色改名链的原版契约。证据底本 DBServer_repaired_20260803.exe
// （sha256 70234272f417a07ab61ffafe1ebb255d31422a5ee25840481a5a10d6c6028666），
// ImageBase 0x400000。⚠️ 此处曾写“CODE 未被 VMProtect 虚拟化”，那是错的：
// 该镜像有 .vmp0(0x5E6000..0xB6278B) / .vmp1(0xB63000..0x1205D40) 两个段，
// 且 CODE 段有 688 处控制转移进去（独立复核：E8 call 568 处 + E9 jmp 120 处）。
// 正确说法：**大部分游戏逻辑函数未被虚拟化**，所以下面每个期望值都直读反汇编，
// 不是从伪代码抄的。
//
// 覆盖三层：
//   1. 长度门        fn_5CD2EC 0x5CD34F..0x5CD358
//   2. 字符白名单    fn_5CCDE4 0x5CCDE4..0x5CCF3A（改名链唯一的注入防线）
//   3. 级联结构      fn_5A923C 的 22 条 UPDATE / 19 张表 / 15 个门
using System.Text;
using DBSvr.Core;
using DBSvr;

var failures = new List<string>();
var asserts = 0;

Run("length gate 4..14 (fn_5CD2EC 0x5CD355)", LengthGate);
Run("whitelist: ascii alnum counting (0x5CCE6A/0x5CCE73/0x5CCE84)", AsciiClasses);
Run("whitelist: gbk lead/trail ranges (0x5CCE92/0x5CCEA5)", GbkRanges);
Run("whitelist: three hard-coded blocks (0x5CCED9/0x5CCEE8/0x5CCEFD)", BlockedPoints);
Run("whitelist: final quorum cjk>=1 or alnum>=2 (0x5CCF1E/0x5CCF24)", FinalQuorum);
Run("whitelist: trim-length equality (0x5CCE0B)", TrimEquality);
Run("whitelist: dangling gbk lead rejected (0x5CCF18)", DanglingLead);
Run("cascade shape: 22 statements / 19 tables / 15 gates", CascadeShape);
Run("cascade rows: db/table/column/gate per statement", CascadeRows);
Run("cascade behaviour: sql text / gate cache / fire-and-forget", CascadeBehaviour);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"NativeRenameCascadeCheck PASS asserts={asserts} "
                  + "len=4..14 lead=B0..F7 trail=A1..FE "
                  + "quorum=cjk1|alnum2 cascade=22/19/15 "
                  + "chain=0xFB0->0x5CD2EC->0x5A8DDC->0x5A923C");
return 0;

// ------------------------------------------------- 级联 22 行逐条（覆盖缺口补丁）
//
// 为什么需要这一段：CascadeShape 只断言条数 22 与门数 15。那两条对了，内容
// 仍可以全错 —— 把 U11 的库 `guild` 写成 `gamedata`、把 U5 的列 `Charname`
// 写成 `CharName`、把 G2 拆成 4 个独立门，条数门数都不变，审计照样绿。
//
// 下面每一行的期望值都**独立照原版模板常量抄写**（VA 见行尾注释），不从
// 生产侧的 Cascade 表派生，否则就是拿实现证明实现。
void CascadeRows()
{
    // (gate, db, table, column, tag)  —— 顺序 = 原版 exec VA 升序
    var expected = new (string, string, string, string, string)[]
    {
        (null,        "gamedata", "WeaponUpg",              "CharName",      "U1"),  // 0x5A9C88 exec 0x5A92D0
        ("c2citems",  "gamedata", "c2citems",               "FromChrName",   "U2"),  // 0x5A9D04 exec 0x5A934C
        ("c2citems",  "gamedata", "c2citems",               "ToChrName",     "U3"),  // 0x5A9D44 exec 0x5A939B
        ("Kindling",  "gamedata", "CreditCard",             "CharName",      "U4"),  // 0x5A9D9C exec 0x5A9417
        ("Kindling",  "gamedata", "Kindling",               "Charname",      "U5"),  // 0x5A9DD8 exec 0x5A9466
        ("Kindling",  "gamedata", "M2_HeroPointActor1204",  "Charname",      "U6"),  // 0x5A9E14 exec 0x5A94B5
        ("Kindling",  "gamedata", "GloryPoint",             "Charname",      "U7"),  // 0x5A9E5C exec 0x5A9504
        ("humantitle","gamedata", "humantitle",             "ChrName",       "U8"),  // 0x5A9EB4 exec 0x5A957C
        ("TitleRelation","gamedata","TitleRelation",        "GrantName",     "U9"),  // 0x5A9F10 exec 0x5A95F8
        ("TitleRelation","gamedata","TitleRelation",        "ChrName",       "U10"), // 0x5A9F50 exec 0x5A9647
        ("guild_user","guild",    "guild_user",             "CharName",      "U11"), // 0x5A9FE4 exec 0x5A96BF
        ("FeedPetManager","gamedata","FeedPetManager",      "MasterName",    "U12"), // 0x5AA040 exec 0x5A9737
        ("dominatorpet","Mir3",   "dominatorpet",           "MasterName",    "U13"), // 0x5AA0E0 exec 0x5A97AF
        ("TransferAreaScore","gamedata","TransferAreaScore","CharName",      "U14"), // 0x5AA148 exec 0x5A982D
        ("dominatorvote","gamedata","dominatorvote",        "DominatorName", "U15"), // 0x5AA1AC exec 0x5A98B5
        ("dominatorvote","gamedata","dominatorvote",        "VoterName",     "U16"), // 0x5AA1F4 exec 0x5A990A
        ("m2_yb_deal_setinfo","gamedata","m2_yb_deal_setinfo","CharName",    "U17"), // 0x5AA258 exec 0x5A998E
        ("humanachieve","gamedata","humanachieve",          "ChrName",       "U18"), // 0x5AA2BC exec 0x5A9A12
        ("m2_offirankorders","gamedata","m2_offirankorders","CharName",      "U19"), // 0x5AA31C exec 0x5A9A96
        ("m2_beatdownmonorder","gamedata","m2_beatdownmonorder","CharName",  "U20"), // 0x5AA384 exec 0x5A9B1A
        ("mirmatchgroupapplymemberlist","gamedata","mirmatchgroupapplymemberlist","CharName","U21"), // 0x5AA3F8 exec 0x5A9B9E
        ("mirmatchgroupmemberlist","gamedata","mirmatchgroupmemberlist","CharName","U22"),           // 0x5AA470 exec 0x5A9C22
    };

    var actual = MySqlNativeRenameCascadeService.CascadeRows;
    Equal(expected.Length, actual.Count, "cascade row count");
    for (var i = 0; i < expected.Length && i < actual.Count; i++)
    {
        var e = expected[i];
        var a = actual[i];
        Equal(e.Item5, a.Tag,    $"row {i} tag (order must follow exec VA)");
        Equal(e.Item1, a.Gate,   $"{e.Item5} gate table");
        Equal(e.Item2, a.Db,     $"{e.Item5} database (guild/Mir3 are NOT gamedata)");
        Equal(e.Item3, a.Table,  $"{e.Item5} table");
        Equal(e.Item4, a.Column, $"{e.Item5} column (verbatim case)");
    }

    // U1 无门：first exec 0x5A92D0 早于 first gate 0x5A92F5（机器验证过）。
    Equal((string)null, actual[0].Gate, "U1 WeaponUpg has NO existence gate");
    // G2 一门保 4 表：Kindling 不存在则 CreditCard/M2_HeroPointActor1204/GloryPoint 也不更新。
    var kindling = 0;
    foreach (var r in actual) if (r.Gate == "Kindling") kindling++;
    Equal(4, kindling, "G2 gates 4 tables through one Kindling probe");
    // 三张表各被打两次（不同列），故 22 条打 19 张表。
    var tables = new HashSet<string>(StringComparer.Ordinal);
    foreach (var r in actual) tables.Add(r.Db + "." + r.Table);
    Equal(19, tables.Count, "22 statements hit 19 distinct tables");
    // 三个库逐字：gamedata / guild / Mir3（M 大写）。
    var dbs = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var r in actual) dbs.Add(r.Db);
    Equal("Mir3,gamedata,guild", string.Join(",", dbs), "exactly three schemas, verbatim case");
}

// --------------------------------- 级联行为三条（SQL 文本 / 门缓存 / fire-and-forget）
//
// 为什么需要这一段：上面 115 条只钉住了 Cascade[] 的**内容**，钉不住 RenameCascade
// 的**行为**。经复核确认，以下三种改坏在只有行断言时全绿：
//   (a) 删掉 SQL 里的 IGNORE，或把 WHERE 列写成别的列
//   (b) 删掉 gateCache（每行查一次门；原版是 15 次 show-tables）
//   (c) 把门关的 continue 改成 return，或在 catch 里 throw
// 现在 SQL 组装是 BuildCascadeSql、遍历是 RunCascade（接受探测/执行委托），
// 两者都是调用点**实际走**的路径，故断言它们等于断言行为，不是平行描述。
void CascadeBehaviour()
{
    // ---- (a) SQL 文本逐字。原版 U1 模板 @0x5A9C88：
    //      `Update ignore gamedata.WeaponUpg set CharName="%s" where CharName="%s";`
    var sql = MySqlNativeRenameCascadeService.BuildCascadeSql(
        "gamedata", "WeaponUpg", "CharName");
    Equal("UPDATE IGNORE gamedata.WeaponUpg SET CharName=@n WHERE CharName=@o", sql,
        "U1 SQL text verbatim (IGNORE + schema prefix + same col in SET/WHERE)");
    True(sql.Contains("IGNORE"),
        "IGNORE is mandatory: new name already present in a table must skip that row");
    // SET 列与 WHERE 列同名（22/22，已机器校验）——把 WHERE 换成 Idx 这条会红。
    Equal(2, CountOccurrences(sql, "CharName"),
        "SET column and WHERE column are the same name");
    // 库名带 schema 前缀，guild / Mir3 不是 gamedata。
    True(MySqlNativeRenameCascadeService.BuildCascadeSql("guild", "guild_user", "CharName")
            .Contains("guild.guild_user"),
        "schema prefix is emitted verbatim (U11 is guild, not gamedata)");
    True(MySqlNativeRenameCascadeService.BuildCascadeSql("Mir3", "dominatorpet", "MasterName")
            .Contains("Mir3.dominatorpet"),
        "U13 schema is literal Mir3 (M uppercase)");

    // ---- (b) 门缓存：22 条语句只应产生 15 次门探测（原版 15 个 show-tables）。
    var probes = new List<string>();
    var executed = new List<string>();
    var applied = MySqlNativeRenameCascadeService.RunCascade(
        probeGate: (db, table) => { probes.Add(db + "." + table); return true; },
        execute: executed.Add);
    Equal(15, probes.Count,
        "22 statements must produce exactly 15 gate probes (gate results are cached)");
    Equal(15, new HashSet<string>(probes, StringComparer.Ordinal).Count,
        "each gate probed at most once");
    Equal(22, executed.Count, "all 22 statements run when every gate is open");
    Equal(22, applied, "applied counts every successful statement");

    // ---- (c) 门关只跳过该块，不中止后续。Kindling 关 ⇒ 少 4 条（G2 一门保 4 表）。
    executed.Clear();
    applied = MySqlNativeRenameCascadeService.RunCascade(
        probeGate: (db, table) => table != "Kindling",
        execute: executed.Add);
    Equal(18, executed.Count,
        "closed Kindling gate skips exactly its 4 tables, later blocks still run");
    Equal(18, applied, "applied reflects the skipped block");
    True(executed.Exists(x => x.Contains("mirmatchgroupmemberlist")),
        "statements after a closed gate still run (continue, not return)");
    False(executed.Exists(x => x.Contains("GloryPoint")),
        "GloryPoint is gated by Kindling, not by its own table");

    // U1 无门 ⇒ 即使所有门都关，它照样执行。
    executed.Clear();
    MySqlNativeRenameCascadeService.RunCascade(
        probeGate: (db, table) => false,
        execute: executed.Add);
    Equal(1, executed.Count, "only U1 runs when every gate is closed (U1 has no gate)");
    True(executed[0].Contains("WeaponUpg"), "the ungated statement is WeaponUpg");

    // ---- (c2) fire-and-forget：每条都抛，遍历仍走完 22 条，applied 归零，不冒泡。
    var attempts = 0;
    var errors = new List<string>();
    applied = MySqlNativeRenameCascadeService.RunCascade(
        probeGate: (db, table) => true,
        execute: _ => { attempts++; throw new InvalidOperationException("boom"); },
        onError: (tag, db, table, msg) => errors.Add(tag));
    Equal(22, attempts, "a throwing statement must not abort the remaining ones");
    Equal(22, errors.Count, "every failure is reported through onError");
    Equal(0, applied, "applied stays 0 when every statement fails");

    // 只有中间一条失败时，其余 21 条仍然执行。
    executed.Clear();
    applied = MySqlNativeRenameCascadeService.RunCascade(
        probeGate: (db, table) => true,
        execute: q =>
        {
            if (q.Contains("guild.guild_user")) throw new InvalidOperationException("boom");
            executed.Add(q);
        });
    Equal(21, executed.Count, "one failing statement does not stop the other 21");
    Equal(21, applied, "applied excludes only the failed statement");
}

int CountOccurrences(string haystack, string needle)
{
    var n = 0;
    for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
         i = haystack.IndexOf(needle, i + 1, StringComparison.Ordinal))
        n++;
    return n;
}

// ---------------------------------------------------------------- 长度门

void LengthGate()
{
    // 0x5CD352 add eax,-4 / 0x5CD355 sub eax,0x0b / 0x5CD358 jae -> err=-1
    // 无符号语义 ⇒ 合法区间 [4,14]，两端点都合法。
    Equal(4, NativeCharacterNameValidator.MinimumNameLength, "min length");
    Equal(14, NativeCharacterNameValidator.MaximumNameLength, "max length");
    for (var len = 0; len < 40; len++)
    {
        var expected = len >= 4 && len <= 14;
        Equal(expected, NativeCharacterNameValidator.IsLengthAllowed(len),
            $"length {len} allowed={expected}");
    }
    // Len<4 时 Len-4 下溢成大正数、-11 仍无借位 ⇒ 同样落 -1（不是"太短所以放过"）。
    False(NativeCharacterNameValidator.IsLengthAllowed(3), "len 3 rejected via underflow");
    False(NativeCharacterNameValidator.IsLengthAllowed(15), "len 15 rejected");
}

// ------------------------------------------------------------ ASCII 分类

void AsciiClasses()
{
    // 单个 ASCII 字母/数字不够（quorum 要 >=2），两个就够。
    False(Allow("a"), "single letter fails quorum");
    True(Allow("ab"), "two letters pass");
    True(Allow("AB"), "two uppercase pass");
    True(Allow("12"), "two digits pass");
    True(Allow("a1"), "letter+digit pass");
    True(Allow("Zz09"), "mixed alnum pass");

    // 边界字符逐个验：'a'/'z'/'A'/'Z'/'0'/'9' 都必须被计数。
    foreach (var pair in new[] { "aa", "zz", "AA", "ZZ", "00", "99", "az", "AZ", "09" })
        True(Allow(pair), $"boundary pair '{pair}' accepted");

    // 相邻的非法 ASCII：'`'(0x60) 与 '{'(0x7B) 紧邻 a..z，'@'(0x40)/'['(0x5B) 紧邻 A..Z，
    // '/'(0x2F)/':'(0x3A) 紧邻 0..9。它们不是 GBK 首字节（<0xB0）⇒ 直接 false。
    foreach (var bad in new[] { "``ab", "{{ab", "@@ab", "[[ab", "//ab", "::ab" })
        False(Allow(bad), $"adjacent non-alnum '{bad[0]}' rejected");
}

// -------------------------------------------------------- GBK 首/尾字节区间

void GbkRanges()
{
    // 0x5CCE92 add al,0x50 / sub al,0x48 / jae -> reject
    // 穷举解得首字节合法区 = 0xB0..0xF7；与区码门 (add 0xF0 / sub 0x48) 同区间。
    for (var lead = 0x00; lead <= 0xFF; lead++)
    {
        // 跳过 ASCII 字母数字（走单字节分支，不是首字节判定）
        if ((lead >= 'a' && lead <= 'z') || (lead >= 'A' && lead <= 'Z')
            || (lead >= '0' && lead <= '9')) continue;
        var expected = lead >= 0xB0 && lead <= 0xF7;
        // 配一个合法尾字节 0xA1，且该组合不落在三个屏蔽点上
        var ok = NativeCharacterNameValidator.IsNameAllowed(
            new byte[] { (byte)lead, 0xA1, (byte)'a', (byte)'b' });
        // zone 0x4C + cell 0x41 是屏蔽点（lead 0xEC + trail 0xE1），此处 trail=0xA1 -> cell=0x01，
        // 不会命中，故期望值只由区间决定。
        Equal(expected, ok, $"lead 0x{lead:X2} accepted={expected}");
    }

    // 0x5CCEA5 add al,0x5f / sub al,0x5e / jae -> reject ⇒ 尾字节合法区 0xA1..0xFE
    for (var trail = 0x00; trail <= 0xFF; trail++)
    {
        var expected = trail >= 0xA1 && trail <= 0xFE;
        // lead 0xB0 -> zone 0x10，不在任何屏蔽区，故期望值只由尾字节区间决定
        var ok = NativeCharacterNameValidator.IsNameAllowed(
            new byte[] { 0xB0, (byte)trail, (byte)'a', (byte)'b' });
        Equal(expected, ok, $"trail 0x{trail:X2} accepted={expected}");
    }
}

// ------------------------------------------------------------ 三组定点屏蔽

void BlockedPoints()
{
    // 逐条照抄，不合并、不外推 —— 原版就是硬编码的三组。
    // (1) 0x5CCED9 zone==0x37 且 0x5CCEE2 cell+0xA6 < 5 ⇒ cell 0x5A..0x5E
    for (var cell = 0x5A; cell <= 0x5E; cell++)
        False(AllowBytes(Gbk(0x37, cell), A("ab")),
            $"zone 0x37 cell 0x{cell:X2} blocked");
    // 屏蔽区下界外必须放行
    True(AllowBytes(Gbk(0x37, 0x59), A("ab")), "zone 0x37 cell 0x59 allowed");
    // ⚠️ 上界外**不能**用 cell 0x5F 来验：cell 0x5F ⇒ trail = 0x5F+0xA0 = 0xFF，
    // 而尾字节合法区是 0xA1..0xFE（0x5CCEA5 add al,0x5f / sub al,0x5e），
    // 0xFF 会先被尾字节门拒掉，测不到屏蔽区的上界。改用 zone 0x36 的同 cell
    // 证明屏蔽是 zone+cell 联合判定（见本函数末尾），上界另由 cell 0x59 的下界侧覆盖。

    // (2) 0x5CCEE8 zone==0x38 且 cell ∈ {0x0D, 0x0F, 0x1C}
    foreach (var cell in new[] { 0x0D, 0x0F, 0x1C })
        False(AllowBytes(Gbk(0x38, cell), A("ab")),
            $"zone 0x38 cell 0x{cell:X2} blocked");
    foreach (var cell in new[] { 0x0C, 0x0E, 0x10, 0x1B, 0x1D })
        True(AllowBytes(Gbk(0x38, cell), A("ab")),
            $"zone 0x38 cell 0x{cell:X2} allowed");

    // (3) 0x5CCEFD zone==0x4C 且 0x5CCF03 cell==0x41
    False(AllowBytes(Gbk(0x4C, 0x41), A("ab")), "zone 0x4C cell 0x41 blocked");
    True(AllowBytes(Gbk(0x4C, 0x40), A("ab")), "zone 0x4C cell 0x40 allowed");
    True(AllowBytes(Gbk(0x4C, 0x42), A("ab")), "zone 0x4C cell 0x42 allowed");
    // 同 cell 但别的 zone 必须放行（证明屏蔽是 zone+cell 联合判定，不是只看 cell）
    True(AllowBytes(Gbk(0x4D, 0x41), A("ab")), "zone 0x4D cell 0x41 allowed");
    True(AllowBytes(Gbk(0x36, 0x5A), A("ab")), "zone 0x36 cell 0x5A allowed");
}

// ------------------------------------------------------------------ 最终门

void FinalQuorum()
{
    // 0x5CCF1E cmp [ebp-0x14],1 / jge -> true   （汉字数 >= 1）
    // 0x5CCF24 cmp [ebp-0x18],2 / jge -> true   （字母数字数 >= 2）
    // 一个汉字就够（4 字节名 = 2 汉字，也够）
    True(AllowBytes(Gbk(0x10, 0x01), A("ab")), "one cjk + 2 alnum");
    True(AllowBytes(Gbk(0x10, 0x01), Gbk(0x11, 0x02)), "two cjk, no alnum");
    // 汉字 0 个、字母数字只有 1 个 ⇒ 两个门都不过。用 4 字节满足长度门。
    False(Allow("a"), "1 alnum, 0 cjk -> reject");
    // 恰好 2 个字母数字 ⇒ 过
    True(Allow("ab"), "exactly 2 alnum -> accept");
}

void TrimEquality()
{
    // 0x5CCDF1 Trim / 0x5CCDFC Length / 0x5CCE0B cmp / jne -> false
    False(Allow(" abc"), "leading space rejected");
    False(Allow("abc "), "trailing space rejected");
    False(Allow("ab cd"), "inner space rejected");

    // ⚠️ 这三条**都不是** Trim 门拒的，而是逐字符门（0x5CCE92 的 jae）拒的。
    // 我用变异测试证明了这一点：把 TrimmedLengthEquals 整个删掉，本审计**仍然全绿**。
    // 起初把它记为假绿，穷举后发现是原版自身的冗余：
    //
    //   Trim 去除的字节 = 0x00..0x20（33 个）
    //   逐字符门接受的字节 = a-z / A-Z / 0-9 / GBK 首字节 0xB0..0xF7
    //   两集合交集 = **空**
    //   且 GBK 尾字节合法区是 0xA1..0xFE，也不含任何 <= 0x20 的值
    //   ⇒ 不存在"能被 Trim 去掉、却能过逐字符门"的字节，
    //     故不存在任何输入能单独触发 Trim 门。
    //
    // 结论：原版 0x5CCDF1/0x5CCDFC/0x5CCE0B 那道 Trim 门在语义上被逐字符门完全覆盖，
    // 是**死门**。仍然照抄它（忠实优先，且它是廉价的提前退出），
    // 但不能为它写"独立可观测"的断言 —— 那种断言在原版语义下不可能构造。
    True(NativeCharacterNameValidator.IsNameAllowed(A("abcd")),
        "a clean name still passes with both gates in place");
}

void DanglingLead()
{
    // 0x5CCF18 cmp byte [ebp-0x0d],0 / jne -> false
    // 结尾停在 GBK 首字节（后面没有尾字节）⇒ 拒。
    False(NativeCharacterNameValidator.IsNameAllowed(
        new byte[] { (byte)'a', (byte)'b', 0xB0 }), "dangling lead byte rejected");
}

// -------------------------------------------------------------- 级联结构

void CascadeShape()
{
    // fn_5A923C：22 条 UPDATE，打 19 张表（c2citems / TitleRelation / dominatorvote
    // 各被打 2 次 ⇒ 22 - 3 = 19），15 个 show-tables 门。
    Equal(22, MySqlNativeRenameCascadeService.CascadeStatementCount,
        "22 cascade UPDATEs (brief said 21; bytes say 22)");
    Equal(15, MySqlNativeRenameCascadeService.GateCount,
        "15 distinct show-tables gates");
}

// ------------------------------------------------------------------ helpers

bool Allow(string ascii)
    => NativeCharacterNameValidator.IsNameAllowed(
        Encoding.ASCII.GetBytes(ascii));

// 本地函数不支持重载，故统一收 byte[]；ASCII 尾巴用 A() 转。
bool AllowBytes(byte[] head, byte[] tail)
{
    var all = new byte[head.Length + tail.Length];
    head.CopyTo(all, 0);
    tail.CopyTo(all, head.Length);
    return NativeCharacterNameValidator.IsNameAllowed(all);
}

byte[] A(string ascii) => Encoding.ASCII.GetBytes(ascii);

// zone/cell 是原版 [ebp-0x19]/[ebp-0x1a]，即 lead-0xA0 / trail-0xA0
byte[] Gbk(int zone, int cell)
    => new[] { (byte)(zone + 0xA0), (byte)(cell + 0xA0) };

void Run(string name, Action body)
{
    try { body(); Console.WriteLine("PASS " + name); }
    catch (Exception ex)
    {
        failures.Add($"FAIL [{name}] {ex.Message}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

void Equal<T>(T expected, T actual, string what)
{
    asserts++;
    if (!Equals(expected, actual))
        throw new Exception($"{what}: expected {expected}, got {actual}");
}

void True(bool value, string what)
{
    asserts++;
    if (!value) throw new Exception(what + ": expected true");
}

void False(bool value, string what)
{
    asserts++;
    if (value) throw new Exception(what + ": expected false");
}

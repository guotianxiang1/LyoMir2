// 断言角色改名链的原版契约。证据底本 DBServer_repaired_20260803.exe
// （sha256 70234272f417a07ab61ffafe1ebb255d31422a5ee25840481a5a10d6c6028666），
// ImageBase 0x400000，CODE 段未被 VMProtect 虚拟化，以下每个期望值都直读反汇编，
// 不是从伪代码抄的。
//
// 覆盖三层：
//   1. 长度门        fn_5CD2EC 0x5CD34F..0x5CD358
//   2. 字符白名单    fn_5CCDE4 0x5CCDE4..0x5CCF3A（改名链唯一的注入防线）
//   3. 级联结构      fn_5A923C 的 22 条 UPDATE / 19 张表 / 15 个门
using System.Text;
using DBSvr.Core;
using DBSvr.DB.impl;

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

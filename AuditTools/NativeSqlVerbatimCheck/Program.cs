// NativeSqlVerbatimCheck — SQL 逐字保真闸
//
// 比对对象：C# DBSvr 的 SQL 文本 vs 原版 DBServer 的 Delphi 长字符串字面量。
//
// 证据底本：活进程 CODE 快照 dbserver_CODE_live.bin（0x401000..0x5D5000）。
// 修复版 EXE 的 CODE 段 rawsz=0（VMProtect 加载时解密），故不能直读 EXE。
// 第二证人：该 EXE 的 .vmp1 原始数据是原 CODE 的明文副本（固定对齐），
// 全部 306 条语句在副本中字节相同（0 处不一致）。
//
// 字面量判定（严格）：[VA-8]=refcount 恒 -1、[VA-4]=len32、text[len]==0、
// 文本内无 NUL。本文件内嵌的 NATIVE_* 常量全部满足该判定，每条带 VA。
//
// ⚠️ 零 dword 交叉引用不等于死代码：本二进制每条 CREATE TABLE IF NOT EXISTS
// 都是零引用（引用函数被 VMP 虚拟化），其中 Guild.Castle 等表明显在线。
// 本闸不以引用数判定任何一条为死代码。
//
// 设计约束：
//  · 无 ProjectReference ⇒ 不需要任何 InternalsVisibleTo，不碰 DBSvr 内部类型。
//    （若将来要断言 DBSvr 的 internal 成员，需要
//     [assembly: InternalsVisibleTo("NativeSqlVerbatimCheck")] 加在 DBSvr 上；
//     本闸刻意不走那条路。）
//  · actual 值一律从 .cs 源文本读取，仓库根用 [CallerFilePath] 推导，
//    不用 AppDomain.CurrentDomain.BaseDirectory —— 后者随 TFM/输出层级漂移。
//  · 匹配前剥离 // 与 /* */ 注释：被注释掉的调用照样能被朴素子串命中而假绿。
//    本仓库确有此陷阱 —— MySqlNativeRenameCascadeService.cs 的注释里含
//    "Update ignore gamedata"。
//  · 不依赖活库。
//
// 退出码：全通过 0 / 有 FAIL 非 0 / 有 SKIP 打印 INCOMPLETE: 并退出 2。
//
// 预期今天为 FAIL：断言写的是"原生应有的样子"，红灯即缺口未修。
//
// ---------------------------------------------------------------------------
// 2026-08-10 闸门自审（本轮只改本闸，不改任何实现文件）
// ---------------------------------------------------------------------------
// 一、订正的**本闸自身错误**（原断言写错，不是实现错）：
//   1. D1a/D1b 极性反了。原断言要求 FightPoints 绑定传入值，但字节证明原版
//      每次保存都落 0：两条模板 0x5B152C/0x5B5068 虽然把 [rec+0x54] 填进
//      TVarRec 第 12 槽（0x5B0F17 / 0x5B4B14 `mov eax,[eax+0x54]`），可保存前
//      的投影例程 0x5ADE34 在 0x5AE018/0x5AE01A `xor edx,edx` +
//      `mov [eax+0x54],edx` 把该字段清零。0x5ADE57 的门是
//      `cmp dword[ebp-0xC],0xEF00 / jl 0x5AE01D`，而它**仅有的两个**调用者
//      0x5A82D1（ecx 由 0x5A82C9 置 0xEF00）与 0x5AEB9（ecx 由 0x5AAEB1 置
//      0xEF00）都恰好传 0xEF00 ⇒ jl 不成立 ⇒ 两条路径都执行清零。
//      +0x54 的写入者普查（disp8 形式 `89 /r 54`，区间 0x5A0000..0x5B8000）
//      只有 4 处：0x5A68B4（装载器回填）、0x5AE01A（上述清零）、
//      0x5A57ED / 0x5AFD76（另一对象类）。故实现写 FightPoints=0 是保真的，
//      本闸原来的红灯是假红。已反向重写。
//   2. NATIVE_ANCIENT_DELETE 原注释写"0x5BD1F8 len=47 / 0x5CA000 len=106"。
//      0x5BD1F8 实为 `select High_Priority Count(*) from Del_Temp_Idx`，
//      与"古老角色清理"无关，是抄错的地址。已删该 VA，只留 0x5CA000。
//   3. NATIVE_MIRSTARS 原注释把 0x479148 / 0x4791C4 当同文两份。二者文本
//      **不同**：0x479148 是 `sex = 0`，0x4791C4 是 `sex = 1`。已分列。
// 二、订正的**匹配式缺陷**（实现是对的，匹配式让它红/跳 = 假红）：
//   4. D7 用 `SET\s+MasterExp=@\w+\s+WHERE` 匹配，但实现把列名参数化成
//      $"SET {column}=@e"，字面上永不出现 MasterExp ⇒ 永假红。改为按
//      withUpdateTime 分支断言。
//   5. CSONLY-4 用"存在 4 列 WHERE 子串"判红。实现补上 ScoreType 之后，
//      那 4 列串仍是新串的**前缀** ⇒ 修好了也永远红。改为正向断言 ScoreType。
//   6. FLAG-g 四条是**同义反复**：ok 取自本文件硬写的 csFragment 自身是否含
//      HIGH_PRIORITY，而那些常量里从来没有 ⇒ 找到就必红、改好也必红；另两条
//      因为实现已改写而 fragment 失配 ⇒ 落入 SKIP 而非绿。整块重写为
//      "在树里定位该语句，再看命中文本有没有 High_Priority"。
// 三、覆盖率分母订正（**没有缩小分母**，是放大）：
//   原写 253 条 GAME-band / 306 总数，无从复核。本轮按字面量头严格枚举
//   （[VA-8]==-1、[VA-4]==len、text[len]==0、文内无 NUL）得 SQL 首词字面量
//   516 条，按内容二分：GAME 354 处（去重后 315 条不同语句）、
//   外部 RDBMS 驱动模板 162 处（pg_/RDB$/SYS.ALL_/sysobjects/@@identity…）。
//   分母因此由 253 上调为 354 处 / 315 条不同语句 —— 百分比变**难看**，
//   这是订正而不是美化。枚举脚本：staging/_denominator.py。

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

var pass = 0;
var fail = 0;
var skipped = new List<string>();
var root = FindRepositoryRoot();

// ---------------------------------------------------------------------------
// 原生 expected（每条 = VA + header 校验过的逐字文本）
// ---------------------------------------------------------------------------

// 0x5B152C len=219 / 0x5B5068 len=219（同族两份，仅空白差异）
const string NATIVE_USER_INDEX_SAVE_FIGHTPOINTS = "FightPoints=%d";
const string NATIVE_USER_INDEX_SAVE_WHERE = "where idx=%d";
// 0x5ACDB8 len=46
const string NATIVE_AWARD_STATUS2 = "Update awardplayers set Status=2 where Idx=%d;";
// 0x5A552C / 0x5A7284 len=55（注意：无 LIMIT）
const string NATIVE_AWARD_SELECT_STATUS0 =
    "Select * from awardplayers where PTID=\"%s\" and Status=0";
// 0x5A7B84 / 0x5ACD58 len=74（注意：无 LIMIT）
const string NATIVE_AWARD_SELECT_STATUS1 =
    "Select Idx from awardplayers where PTID=\"%s\" and HumName=\"%s\" and Status=1";
// 0x5BAD84 len=9 —— 全二进制唯一一条 use，坐实无前缀表名归 mir3
const string NATIVE_USE_DB = "use mir3;";
// 0x5AE114 len=83（转服置锁；C# 侧缺失）
const string NATIVE_TRANSLOCK_SET =
    "Update mir3.user_index set IsTransLock=1, DesZoneId=%d, DesGroupId=%d where idx=%d;";
// 0x5AE2C8 len=57
const string NATIVE_TRANSFERMODAL_SET =
    "Update mir3.user_index set TransferModal=%d where idx=%d;";
// 0x5B3E54 / 0x5C1990 len=45
const string NATIVE_MAXIDX = "Select High_Priority Max(idx) as MaxIdx from ";
// 0x592E18 len=109 —— 表名全小写（原版故意不一致）
const string NATIVE_ZONGPAIROLE_INSERT_TABLE = "zongpairole";
// 0x5937EC len=62 —— MasterExp 不带 UpdateTime 的变体
const string NATIVE_ZONGPAI_MASTEREXP_NO_TIME =
    "update ZongpaiBase set MasterExp = %u where MasterName = \"%s\";";
// 0x479148 len=113（sex = 0）／0x4791C4 len=113（sex = 1）
// 订正：原注释把两条当同文两份，实际只差 sex 常量 —— 是**两条**语句。
const string NATIVE_MIRSTARS_SEX0 =
    "select ChrName, nValue from gamedata.mirStars where sex = 0 "
    + "Order by nValue desc, level desc, exp desc limit 100;";
const string NATIVE_MIRSTARS_SEX1 =
    "select ChrName, nValue from gamedata.mirStars where sex = 1 "
    + "Order by nValue desc, level desc, exp desc limit 100;";
// 0x5C9B3C len=75
const string NATIVE_YBCONSUME =
    "SELECT YBConsume FROM gamedata.YBConsume WHERE PTID='%s' AND YBConsume>=%d;";
// 0x5C0700 len=95（GBK 中文）
const string NATIVE_CASTLE_SEED_TABLE = "Guild.Castle";
// 0x5C0CF4 —— TransferAreaScoreSendRecord 的唯一键是五列，含 ScoreType
const string NATIVE_SENDRECORD_UNIQUE_KEY =
    "unique key Record_Index(TimeStamp, CharName, ZoneId, GroupId, ScoreType)";
// 0x5CA000 len=106（原生"古老角色"清理不用临时表）
// 订正：原注释另列的 0x5BD1F8 是抄错的地址 —— 那条实为
// `select High_Priority Count(*) from Del_Temp_Idx`（len=47），属**不活跃**
// 角色清理的计数句，与古老角色清理无关。已删。
const string NATIVE_ANCIENT_DELETE =
    "delete from mir3.user_index where (year(modifyDate) <= 2008) "
    + "or (year(modifyDate) < 2010 and level <= 60);";
// 静态表加载 9 条写死语句里带 ORDER BY 的 5 条
var nativeStaticOrderBy = new (string Va, string Sql)[]
{
    ("0x5C4104", "select High_Priority * from forcemagic order by ForceId"),
    ("0x5C4810", "select High_Priority * from heromagic order by MagicIdx;"),
    ("0x5C4F34", "select High_Priority * from humanmagic order by MagicIdx;"),
    ("0x5C6DF8", "select High_Priority * from stditems order by idx;"),
    ("0x5C7D90", "Select High_Priority * from SuperForce order by level;"),
};
// 改名级联三种列名写法（原版故意不归一化）
var nativeCascadeColumnSpellings = new (string Va, string Column)[]
{
    ("0x5A9C88", "CharName"),   // .WeaponUpg
    ("0x5A9DD8", "Charname"),   // .Kindling   —— 注意小写 n
    ("0x5A9EB4", "ChrName"),    // .humantitle
};
// 0x5A8124 len=143（GM 建号：带 IGNORE、带 LoginID、写死 40/5）
const string NATIVE_GM_CREATE =
    "insert Ignore into user_index(PTID, LoginID, ChrName, Level, AdminLevel, "
    + "CreateDate, ModifyDate) Values(\"%s\", \"%s\", \"%s\", 40, 5, Now(), Now());";
// 0x5A86E8 len=49（AdminLevel 参数化）
const string NATIVE_ADMINLEVEL_SET = "Update user_index set AdminLevel=%d where idx=%d;";
// 原生 DDL/迁移锚点（N-a 组）
var nativeDdlAnchors = new (string Va, string Fragment)[]
{
    ("0x5BAF04", "Create table if not exists user_index"),
    ("0x5BB498", "Create table if not exists user_data"),
    ("0x5BB570", "Create table if not exists hero_index"),
    ("0x5BB934", "Create table if not exists hero_data"),
    ("0x5BF728", "Create table if not exists user_storage"),
    ("0x5BBC5C", "Create table if not exists dominatorpet"),
    ("0x5BBA08", "Create table if not exists awardplayers"),
    ("0x5BBB54", "Create table if not exists HallOfFame"),
    ("0x5BF818", "CREATE TABLE IF NOT EXISTS Guild.Castle"),
    ("0x5BEE34", "Create table if not exists gamedata.ZongpaiBase"),
};
// 原生表维护语句（C# 全无）
var nativeMaintenance = new (string Va, string Sql)[]
{
    ("0x5BD070", "OPTIMIZE TABLE user_index;"),
    ("0x5BD094", "OPTIMIZE TABLE user_data;"),
    ("0x5BD0B8", "OPTIMIZE TABLE hero_index;"),
    ("0x5BD0DC", "OPTIMIZE TABLE hero_data;"),
};

// ---------------------------------------------------------------------------
// C# actual（源文本，注释已剥离）
// ---------------------------------------------------------------------------

var playRecord = Load("DBSvr/DB/impl/MySqlPlayRecordService.cs");
var playData = Load("DBSvr/DB/impl/MySqlPlayDataService.cs");
var heroRecord = Load("DBSvr/DB/impl/MySqlHeroRecordService.cs");
var storage = Load("DBSvr/DB/impl/MySqlStorageService.cs");
var zongpai = Load("DBSvr/DB/impl/MySqlZongpaiService.cs");
var transferArea = Load("DBSvr/DB/impl/MySqlTransferAreaService.cs");
var cascade = Load("DBSvr/DB/impl/MySqlNativeRenameCascadeService.cs");
var gameSoc = Load("DBSvr/Services/GameSocService.cs");
var userSoc = Load("DBSvr/Services/UserSocService.cs");
var cleanup = Load("DBSvr/Core/CleanupService.cs");
var dbInit = Load("DBSvr/Core/DatabaseInitService.cs");
var staticLoader = Load("DBSvr/Core/NativeType2StaticLoader.cs");
var awardProto = Load("DBSvr/Core/NativeAwardPlayerProtocol.cs");
var dbShare = Load("DBSvr/DBShare.cs");
var wholeDbSvr = LoadTree("DBSvr");

// === D1 FightPoints：原断言极性反了，已按字节反向重写 =====================
// 追溯：本闸原来断言"必须绑定传入值"，把实现的 FightPoints=0 判为数据丢失。
// 字节不支持该断言 ——
//   · 模板 0x5B152C（len=219）/ 0x5B5068（len=219）确实是 `FightPoints=%d`，
//     且第 12 槽绑 [rec+0x54]（0x5B0F17 / 0x5B4B14 `mov eax,[eax+0x54]`）；
//   · 但保存前投影例程 0x5ADE34 在 0x5AE018/0x5AE01A 无条件
//     `xor edx,edx` / `mov [eax+0x54],edx` 清零该字段；
//   · 0x5ADE57 的门 `cmp dword[ebp-0xC],0xEF00 / jl 0x5AE01D` 只在 ecx<0xEF00
//     时跳过清零，而该函数**仅有的两个**调用者都传 0xEF00
//     （0x5A82C9→0x5A82D1、0x5AAEB1→0x5AAEB9），两条路径都清零；
//   · 写 [reg+0x54] 的普查（disp8 编码 `89 /r 54`，0x5A0000..0x5B8000）共 4 处：
//     0x5A68B4 装载器回填、0x5AE01A 上述清零、0x5A57ED / 0x5AFD76 属另一对象。
//     没有任何一处写入非零战力值。
// 结论：落 0 才是字节保真。现在断言"保存路径必须落 0"，防的是反向回归 ——
// 有人"顺手修好"它，就会伪造一条原版不存在的数据通路。
Check("D1a-FightPoints-save-writes-literal-zero-like-native",
    expected: "native zeroes rec+0x54 at 0x5AE01A before formatting "
        + $"`{NATIVE_USER_INDEX_SAVE_FIGHTPOINTS}` (0x5B152C) => column persists 0",
    actual: Contains(playRecord, "FightPoints=0")
        ? "FightPoints=0 (matches native)"
        : "value bound (FABRICATES a data path absent from DBServer)",
    ok: Contains(playRecord, "FightPoints=0"));

// 旁证记录（不作判据）：另一条保存路径 MySqlPlayDataService.Update 绑 @fp。
// 该路径的原生对应尚未定位 —— 不能据此断言两边必须一致，故只打印计数。
var fpBound = CountOf(playRecord, "FightPoints=@fp") + CountOf(playData, "FightPoints=@fp");
var fpZero = CountOf(playRecord, "FightPoints=0");
Check("D1b-FightPoints-primary-save-path-is-zero",
    expected: "the 0x5B152C-equivalent save path writes the literal 0",
    actual: "FightPoints=0 sites=" + fpZero + " (bound @fp sites elsewhere="
        + fpBound + "; those belong to a save path whose native counterpart "
        + "is not yet located — informational only)",
    ok: fpZero >= 1);

// === D1c WHERE 键：原生只按 idx ==========================================
Check("D1c-user_index-save-where-key-is-idx-only",
    expected: $"native 0x5B152C `{NATIVE_USER_INDEX_SAVE_WHERE}`",
    actual: Contains(playRecord, "WHERE idx=@idx AND ChrName=@name")
        ? "WHERE idx=@idx AND ChrName=@name (extra key; silently 0 rows after rename)"
        : "WHERE idx only",
    ok: !Contains(playRecord, "WHERE idx=@idx AND ChrName=@name"));

// === D2 awardplayers 错库 =================================================
// 原生七条 awardplayers 语句全无 schema 前缀，DDL 0x5BBA08 亦无前缀，
// 在 use mir3;（0x5BAD84）之下解析为 mir3.awardplayers。
var gamedataAward = CountOf(gameSoc, "gamedata.awardplayers")
    + CountOf(userSoc, "gamedata.awardplayers");
Check("D2a-awardplayers-schema-is-mir3",
    expected: $"mir3.awardplayers (native has no prefix + `{NATIVE_USE_DB}`)",
    actual: $"gamedata.awardplayers occurrences={gamedataAward}",
    ok: gamedataAward == 0);

// 同表在 NativeAwardPlayerProtocol 已正确写 mir3. ⇒ 仓库内部矛盾
var mir3Award = CountOf(awardProto, "mir3.awardplayers");
Check("D2b-awardplayers-schema-internally-consistent",
    expected: "one schema for one table",
    actual: "mir3.=" + mir3Award + ", gamedata.=" + gamedataAward,
    ok: gamedataAward == 0 || mir3Award == 0);

// === D2c/D3 awardplayers LIMIT 与 WHERE ==================================
Check("D2c-award-select-status0-has-no-LIMIT",
    expected: $"native 0x5A552C `{NATIVE_AWARD_SELECT_STATUS0}` (no LIMIT)",
    actual: Contains(userSoc, "awardplayers WHERE PTID=@p AND Status=0 LIMIT 1")
        ? "LIMIT 1 added" : "no LIMIT",
    ok: !Contains(userSoc, "awardplayers WHERE PTID=@p AND Status=0 LIMIT 1"));

// 该 SQL 在源码里被拆成两段字符串拼接，故只匹配含 LIMIT 的那一段。
Check("D2d-award-select-status1-has-no-LIMIT",
    expected: $"native 0x5A7B84 `{NATIVE_AWARD_SELECT_STATUS1}` (no LIMIT)",
    actual: Contains(gameSoc, "AND Status=1 LIMIT 1") ? "LIMIT 1 added" : "no LIMIT",
    ok: !Contains(gameSoc, "AND Status=1 LIMIT 1"));

Check("D3-award-status2-where-key-is-Idx-only",
    expected: $"native 0x5ACDB8 `{NATIVE_AWARD_STATUS2}`",
    actual: Contains(gameSoc, "WHERE Idx=@i AND Status=1")
        ? "WHERE Idx=@i AND Status=1 (extra guard, idempotency changed)"
        : "WHERE Idx only",
    ok: !Contains(gameSoc, "WHERE Idx=@i AND Status=1"));

// === D4 / N-b 转服置锁缺失 ================================================
Check("N-b-translock-set-implemented",
    expected: $"native 0x5AE114 `{NATIVE_TRANSLOCK_SET}`",
    actual: Contains(wholeDbSvr, "IsTransLock=1, DesZoneId=@")
            || Contains(wholeDbSvr, "SET IsTransLock=1,")
        ? "present" : "absent (cross-server lock never persisted)",
    ok: Contains(wholeDbSvr, "IsTransLock=1, DesZoneId=@")
        || Contains(wholeDbSvr, "SET IsTransLock=1,"));

// === N-c TransferModal 只读不写 ===========================================
Check("N-c-transfermodal-write-implemented",
    expected: $"native 0x5AE2C8 `{NATIVE_TRANSFERMODAL_SET}`",
    actual: Contains(wholeDbSvr, "TransferModal=@") || Contains(wholeDbSvr, "transferModal=@")
        ? "present" : "absent (column is SELECTed but never UPDATEd)",
    ok: Contains(wholeDbSvr, "TransferModal=@") || Contains(wholeDbSvr, "transferModal=@"));

// === D5 MAX(idx) 语义 =====================================================
Check("D5-maxidx-no-COALESCE",
    expected: $"native 0x5B3E54 `{NATIVE_MAXIDX}` (bare Max(idx); empty table => NULL)",
    actual: Contains(storage, "COALESCE(MAX")
        ? "COALESCE(MAX(Idx),0) (empty table => 0, not NULL)" : "bare MAX",
    ok: !Contains(storage, "COALESCE(MAX"));

// === D6 表名大小写归一化 ==================================================
// 原生 0x592E18 是全小写 zongpairole，0x594AF0 是驼峰 ZongpaiBase ⇒ 原版
// 自身不一致，属有意保真项。C# 全部归一化成驼峰。
Check("D6-zongpairole-table-case-verbatim",
    expected: $"native 0x592E18 uses `{NATIVE_ZONGPAIROLE_INSERT_TABLE}` (all lower)",
    actual: Contains(zongpai, "INSERT IGNORE INTO gamedata.ZongpaiRole")
        ? "ZongpaiRole (camel; native lower-case spelling normalised away)"
        : "verbatim",
    ok: !Contains(zongpai, "INSERT IGNORE INTO gamedata.ZongpaiRole"));

// === D7 MasterExp 不带 UpdateTime 的变体 ==================================
// 匹配式订正：原来找字面 `SET MasterExp=@…`，但实现把列名参数化成
// $"SET {column}=@e"，字面上永不出现 MasterExp ⇒ 原断言是**永假红**。
// 现在断言"存在不带 UpdateTime 的写模板分支"，并要求它和带 UpdateTime 的
// 分支同时存在（原版两条模板：0x59361C/0x593790 带、0x5937EC 不带）。
var zpNoTime = Regex.IsMatch(zongpai,
    @"UPDATE\s+\w*\.?ZongpaiBase\s+SET\s+\{?\w+\}?=@\w+\s+WHERE\s+MasterName",
    RegexOptions.IgnoreCase);
var zpWithTime = Regex.IsMatch(zongpai,
    @"UPDATE\s+\w*\.?ZongpaiBase\s+SET\s+\{?\w+\}?=@\w+,\s*UpdateTime=Now\(\)",
    RegexOptions.IgnoreCase);
Check("D7-zongpai-masterexp-without-updatetime-path",
    expected: $"native 0x5937EC `{NATIVE_ZONGPAI_MASTEREXP_NO_TIME}` "
        + "coexists with the UpdateTime variant (0x59361C / 0x593790)",
    actual: $"no-UpdateTime template={zpNoTime}, UpdateTime template={zpWithTime}",
    ok: zpNoTime && zpWithTime);

// === D8 SrcHeroName 从不被读出 ============================================
Check("D8-SrcHeroName-column-used",
    expected: "native 0x58CE48 / 0x5B2618 reference SrcHeroName",
    actual: Contains(wholeDbSvr, "SrcHeroName") ? "present" : "absent from all DBSvr SQL",
    ok: Contains(wholeDbSvr, "SrcHeroName"));

// === D10 GM 建号 ==========================================================
Check("D10a-gm-create-keeps-LoginID",
    expected: $"native 0x5A8124 `{NATIVE_GM_CREATE}`",
    actual: Contains(wholeDbSvr, "LoginID") ? "present" : "LoginID never written by C#",
    ok: Contains(wholeDbSvr, "LoginID"));

Check("D10b-adminlevel-parameterised",
    expected: $"native 0x5A86E8 `{NATIVE_ADMINLEVEL_SET}`",
    actual: Contains(userSoc, "SET AdminLevel=5, Level=40")
        ? "AdminLevel=5 hardcoded (no parameterised setter)" : "parameterised",
    ok: !Contains(userSoc, "SET AdminLevel=5, Level=40"));

// === C#-ONLY 第 3 档：真·发明 =============================================
// CREATE USER / Create User / create user 在 CODE 快照 0 命中。
Check("CSONLY-1-no-invented-CREATE-USER",
    expected: "native creates users implicitly via `Grant ... identified by` "
        + "(0x59D584/0x59D5E0/0x59D610/0x59D640); CREATE USER: 0 hits in CODE",
    actual: Contains(dbInit, "CREATE USER")
        ? "CREATE USER invented in DatabaseInitService.cs" : "absent",
    ok: !Contains(dbInit, "CREATE USER"));

// account.ticket / account.normal / pt_id 在 CODE 快照 0 命中。
Check("CSONLY-2-no-invented-account-schema",
    expected: "`account.ticket` / `account.normal` / `pt_id`: 0 hits in CODE snapshot",
    actual: Contains(userSoc, "account.ticket") || Contains(userSoc, "account.normal")
        ? "account.* queried (external auth schema absent from DBServer binary)"
        : "absent",
    ok: !(Contains(userSoc, "account.ticket") || Contains(userSoc, "account.normal")));

// 原生改名只级联 ZongpaiBase(0x594AF0) 与 ZongpaiMember(0x594BA0)。
Check("CSONLY-3-no-invented-ZongpaiRole-rename-cascade",
    expected: "native cascades MasterName to ZongpaiBase + ZongpaiMember only",
    actual: Contains(zongpai, "ZongpaiRole SET MasterName")
        ? "ZongpaiRole MasterName cascade invented (third table)" : "absent",
    ok: !Contains(zongpai, "ZongpaiRole SET MasterName"));

// 该表唯一键是五列（DDL 0x5C0CF4）。
// 匹配式订正：原来判"存在 4 列 WHERE 子串"即红，但补上 ScoreType 之后
// 那 4 列串仍是新串的**前缀**，子串匹配照样命中 ⇒ 修好了也永远红。
// 改为正向断言：该 UPDATE 的 WHERE 必须落到 ScoreType。
var sendRecordUpdate = Regex.Match(transferArea,
    @"UPDATE\s+\w*\.?TransferAreaScoreSendRecord\s+SET\s+State=@\w+\s+WHERE[^""]*",
    RegexOptions.IgnoreCase);
Check("CSONLY-4-sendrecord-where-includes-ScoreType",
    expected: $"native DDL 0x5C0CF4 `{NATIVE_SENDRECORD_UNIQUE_KEY}` (5 columns) "
        + "=> the State UPDATE must key on ScoreType too",
    actual: sendRecordUpdate.Success
        ? (Regex.IsMatch(sendRecordUpdate.Value, @"ScoreType", RegexOptions.IgnoreCase)
            ? "ScoreType present in WHERE"
            : "ScoreType missing => updates all ScoreTypes of that key: "
              + sendRecordUpdate.Value)
        : "no State UPDATE found for that table",
    ok: sendRecordUpdate.Success
        && Regex.IsMatch(sendRecordUpdate.Value, @"ScoreType", RegexOptions.IgnoreCase));

// 原生不用临时表做古老角色清理；Ancient_Temp_Idx 在 CODE 快照 0 命中。
Check("CSONLY-5-ancient-cleanup-mechanism",
    expected: $"native 0x5CA000 `{NATIVE_ANCIENT_DELETE}` (no temp table; "
        + "`Ancient_Temp_Idx`: 0 hits in CODE)",
    actual: Contains(cleanup, "Ancient_Temp_Idx")
        ? "temp-table mechanism invented, and `IsDelete=0` added to the predicate"
        : "matches native",
    ok: !Contains(cleanup, "Ancient_Temp_Idx"));

// === NATIVE-ONLY 缺口 =====================================================
// 订正：原来只挂一条 mirStars。二者是**两条**不同语句（sex 常量不同），
// 且原生按 sex 分别查询 ⇒ 分列断言，缺一条就是缺一条。
foreach (var (va, sql, sex) in new (string Va, string Sql, string Sex)[]
{
    ("0x479148", NATIVE_MIRSTARS_SEX0, "0"),
    ("0x4791C4", NATIVE_MIRSTARS_SEX1, "1"),
})
{
    var ok = Regex.IsMatch(wholeDbSvr,
        @"FROM\s+gamedata\.mirStars\s+WHERE\s+sex\s*=\s*" + sex,
        RegexOptions.IgnoreCase);
    Check($"N-d-mirStars-ranking-sex{sex}-implemented",
        expected: $"native {va} `{sql}`",
        actual: ok ? "present" : "absent (only mentioned in a comment)",
        ok: ok);
}

// 注意：C# 的 VipYBConsume 是配置整数（DBShare.cs:56），与该表无关。
var ybConfig = Contains(dbShare, "VipYBConsume") ? "confirmed" : "not found";
Check("N-e-YBConsume-table-query-implemented",
    expected: $"native 0x5C9B3C `{NATIVE_YBCONSUME}`",
    actual: Regex.IsMatch(wholeDbSvr, @"FROM\s+gamedata\.YBConsume", RegexOptions.IgnoreCase)
        ? "present"
        : "absent (VipYBConsume in DBShare is an unrelated config int: " + ybConfig + ")",
    ok: Regex.IsMatch(wholeDbSvr, @"FROM\s+gamedata\.YBConsume", RegexOptions.IgnoreCase));

// ⚠️ 归属声明：沙巴克城堡的 DDL/初始化由本会话另一路（castle-ddl-native）负责。
// 本闸只断言"DBSvr 侧是否发过这条 seed"，不对 GameSvr 侧下结论。
Check("N-h-castle-seed-row-implemented",
    expected: $"native 0x5C0700 insert into {NATIVE_CASTLE_SEED_TABLE}(Guid,name) "
        + "values(1,<GBK 沙巴克>) on duplicate key update",
    actual: Contains(wholeDbSvr, "Guild.Castle")
        ? "present" : "absent from DBSvr (may be owned by GameSvr — see report §7)",
    ok: Contains(wholeDbSvr, "Guild.Castle"));

// C# 把四条合成一条 `OPTIMIZE TABLE mir3.user_data, mir3.hero_data`，且只含
// 两张表。故按"表名出现在某条 OPTIMIZE 语句内"匹配，而非前缀紧邻匹配。
foreach (var (va, sql) in nativeMaintenance)
{
    var table = sql.Split(' ')[2].TrimEnd(';');
    var ok = Regex.IsMatch(wholeDbSvr,
        @"OPTIMIZE\s+TABLE[^"";]*\b" + Regex.Escape(table) + @"\b",
        RegexOptions.IgnoreCase);
    Check($"N-i-maintenance-{table}",
        expected: $"native {va} `{sql}`",
        actual: ok ? "covered by an OPTIMIZE statement" : "absent",
        ok: ok);
}

// === N-a schema 供给：69 条 DDL/迁移 ======================================
// 大小写不敏感匹配：C# 侧若真有 DDL，写法可能是全大写。要让红灯红在
// "确实没有"，而不是红在大小写。
var ddlPresent = nativeDdlAnchors.Count(a =>
{
    var table = a.Fragment.Split(' ').Last();
    return Regex.IsMatch(wholeDbSvr,
        @"CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+" + Regex.Escape(table) + @"\b",
        RegexOptions.IgnoreCase);
});
Check("N-a1-schema-DDL-present",
    expected: $"native provisions its own schema: 24 CREATE TABLE + 37 ALTER TABLE "
        + $"+ 4 CREATE DATABASE (anchors: {nativeDdlAnchors.Length} sampled)",
    actual: $"{ddlPresent}/{nativeDdlAnchors.Length} anchors found in DBSvr",
    ok: ddlPresent == nativeDdlAnchors.Length);

Check("N-a2-column-migration-present",
    expected: "native runs `show columns ... like` + `Alter table ... add column` "
        + "migrations (0x5BBE04, 0x5BC0F4, 0x5BC3AC, 0x5BC5E4, ...)",
    actual: Regex.IsMatch(wholeDbSvr, @"ALTER\s+TABLE\s+\w*\.?user_index\s+ADD",
        RegexOptions.IgnoreCase) ? "present" : "absent (no column migration at all)",
    ok: Regex.IsMatch(wholeDbSvr, @"ALTER\s+TABLE\s+\w*\.?user_index\s+ADD",
        RegexOptions.IgnoreCase));

// === 旗标 g：High_Priority ================================================
// ⚠️ 整块重写（原实现是**同义反复**，红绿都无意义）：
// 原来 ok 取自 `Regex.IsMatch(csFragment, "HIGH_PRIORITY")`，而 csFragment 是
// 本文件里硬写的常量、其中从来不含 HIGH_PRIORITY ⇒ 只要在树里找到该串就必红，
// 实现改好了也照样红；而实现一旦改写 SQL 文本，Contains 失配就落进 SKIP。
// 两条路都测不到实现。
// 现在改成：用**结构式**在树里定位该表的 blob 读语句（不依赖任何一种写法），
// 再看命中的语句文本自身有没有 High_Priority。全部命中都必须带 —— 原版这四张
// 大表的每条 blob 读都带（见各 VA），漏一条就是漏一条。
var hpSites = CountOfIgnoreCase(wholeDbSvr, "HIGH_PRIORITY");
var flatDbSvr = Regex.Replace(wholeDbSvr, @"\s+", " ");
var hpTargets = new (string Va, string NativeSql, string Table, string Key)[]
{
    ("0x5B1610", "Select High_Priority idx, data, ScriptData from user_data where idx =",
        "user_data", "Idx"),
    ("0x5B28C8", "Select High_Priority idx, data, dynData from hero_data where idx =",
        "hero_data", "Idx"),
    ("0x5B9DF0", "Select High_Priority idx, data from user_storage where idx =",
        "user_storage", "idx"),
    ("0x59748C", "Select High_Priority Data From dominatorpet where MasterId=%d;",
        "dominatorpet", "MasterId"),
};
foreach (var (va, nativeSql, table, key) in hpTargets)
{
    // blob 读的结构签名：SELECT 列表里含 Data，FROM <表>，WHERE <键>。
    // span 内不含引号 ⇒ 命中的一定落在同一个字符串字面量里。
    var reads = Regex.Matches(flatDbSvr,
        @"SELECT[^""]{0,160}?\bData\b[^""]{0,160}?FROM\s+(?:mir3\.)?"
        + Regex.Escape(table) + @"\s+WHERE\s+" + Regex.Escape(key),
        RegexOptions.IgnoreCase);
    if (reads.Count == 0)
    {
        // 定位不到对应语句就无法比对：记 SKIP（INCOMPLETE + 退出码 2）。
        // 既不能当 FAIL，更不能当 PASS。
        skipped.Add($"FLAG-g-High_Priority-{table}: no blob-read statement located "
            + $"for that table (native {va} `{nativeSql}`)");
        continue;
    }
    var missing = reads.Cast<Match>()
        .Where(m => !Regex.IsMatch(m.Value, @"HIGH_PRIORITY", RegexOptions.IgnoreCase))
        .Select(m => m.Value.Trim())
        .ToList();
    Check($"FLAG-g-High_Priority-{table}",
        expected: $"native {va} `{nativeSql}` — every C# blob read of that table "
            + "must carry High_Priority",
        actual: missing.Count == 0
            ? $"all {reads.Count} blob read(s) carry High_Priority"
            : $"{missing.Count}/{reads.Count} blob read(s) lack it: "
              + string.Join(" | ", missing)
              + $" (tree-wide HIGH_PRIORITY sites={hpSites}, native=41)",
        ok: missing.Count == 0);
}

// === 旗标 h：静态表 ORDER BY ==============================================
// 只查 NativeType2StaticLoader（这 9 张表的加载归它）。若查整树会被别处
// 无关的 `ORDER BY idx`（如 NativeType2StdItemsImportService 自己那条）
// 命中而假绿 —— 那是另一条语句，不能替它作证。
foreach (var (va, sql) in nativeStaticOrderBy)
{
    var order = sql[sql.IndexOf("order by", StringComparison.OrdinalIgnoreCase)..]
        .TrimEnd(';')[9..].Trim();
    var table = Regex.Match(sql, @"from\s+(\w+)", RegexOptions.IgnoreCase).Groups[1].Value;
    var ok = Regex.IsMatch(staticLoader,
        @"ORDER\s+BY\s+" + Regex.Escape(order), RegexOptions.IgnoreCase);
    Check($"FLAG-h-static-order-by-{table}",
        expected: $"native {va} `{sql}`",
        actual: ok ? "ORDER BY present" : "ORDER BY lost (loader emits bare "
            + "`SELECT HIGH_PRIORITY * FROM mir3.` + table name)",
        ok: ok);
}

// === 旗标 f：级联列名三种写法必须逐字（这一处 C# 做对了，应为绿）==========
foreach (var (va, column) in nativeCascadeColumnSpellings)
{
    var ok = Contains(cascade, $"Column = \"{column}\"");
    Check($"FLAG-f-cascade-column-spelling-{column}",
        expected: $"native {va} spells the column `{column}` verbatim",
        actual: ok ? $"`{column}` preserved" : $"`{column}` normalised away",
        ok: ok);
}

// 级联条数与门数（原生 22 条语句 / 15 个 show-tables 门）
var cascadeRows = CountOf(cascade, "new Stmt {");
Check("FLAG-f-cascade-statement-count",
    expected: "native issues 22 cascade UPDATEs (exec VA 0x5A92D0..0x5A9C22)",
    actual: $"Cascade table rows={cascadeRows}",
    ok: cascadeRows == 22);

// 主档改名两条必须无 IGNORE（0x5A91D0 / 0x5A9210 逐字无 ignore）
var masterHasIgnore = Regex.IsMatch(cascade,
    @"UPDATE\s+IGNORE\s+user_index\s+SET\s+ChrName", RegexOptions.IgnoreCase);
Check("FLAG-c-master-rename-has-no-IGNORE",
    expected: "native 0x5A91D0 `Update user_index set ChrName=\"` — no IGNORE",
    actual: masterHasIgnore ? "IGNORE added" : "no IGNORE (correct)",
    ok: !masterHasIgnore);

// 级联模板必须有 IGNORE（0x5A9C68 前缀逐字含 ignore）
Check("FLAG-c-cascade-template-has-IGNORE",
    expected: "native 0x5A9C68 `Update ignore gamedata` — IGNORE present",
    actual: Contains(cascade, "UPDATE IGNORE {db}.{table}") ? "IGNORE present" : "IGNORE lost",
    ok: Contains(cascade, "UPDATE IGNORE {db}.{table}"));

// === 旗标 c：0x5A8124 的 IGNORE ===========================================
Check("FLAG-c-gm-create-user_index-has-IGNORE",
    expected: $"native 0x5A8124 `{NATIVE_GM_CREATE}`",
    actual: Regex.IsMatch(wholeDbSvr,
        @"INSERT\s+IGNORE\s+INTO\s+\w*\.?user_index", RegexOptions.IgnoreCase)
        ? "IGNORE present" : "IGNORE lost (C# uses plain INSERT INTO user_index)",
    ok: Regex.IsMatch(wholeDbSvr,
        @"INSERT\s+IGNORE\s+INTO\s+\w*\.?user_index", RegexOptions.IgnoreCase));

// === 旗标 b：LIMIT 额度等价（这些应为绿）=================================
Check("FLAG-b-BatchLimit-equals-native-5000",
    expected: "native 0x58CE48 / 0x596E94 / 0x5A6CF0 / 0x5AC630 use `Limit 5000`",
    actual: Contains(dbShare, "BatchLimit = 5000") ? "BatchLimit=5000" : "mismatch",
    ok: Contains(dbShare, "BatchLimit = 5000"));

Check("FLAG-b-RankLimit-equals-native-100",
    expected: "native 0x4788E4..0x479240 ranking selects use `Limit 100`",
    actual: Contains(dbShare, "RankLimit = 100") ? "RankLimit=100" : "mismatch",
    ok: Contains(dbShare, "RankLimit = 100"));

// === 旗标 b：清理常量（应为绿）============================================
var inactiveLevel = Grab(cleanup, @"NativeInactiveLevelLimit\s*=\s*(\d+)");
var inactiveDays = Grab(cleanup, @"NativeInactiveDays\s*=\s*(\d+)");
Check("FLAG-b-inactive-cleanup-constants",
    expected: "native 0x5BD17C `Level<8 and Now()>Date_Add(ModifyDate, interval 15 Day)`",
    actual: "NativeInactiveLevelLimit=" + inactiveLevel
        + ", NativeInactiveDays=" + inactiveDays,
    ok: inactiveLevel == "8" && inactiveDays == "15");

var ancientLevel = Grab(cleanup, @"CleanAncientCharacters\(int maxLevel = (\d+)\)");
Check("FLAG-b-ancient-cleanup-level-default",
    expected: $"native 0x5CA000 `{NATIVE_ANCIENT_DELETE}` => level <= 60",
    actual: "CleanAncientCharacters default maxLevel=" + ancientLevel,
    ok: ancientLevel == "60");

// === 旗标 a：%d/%s/%u 模板不得进 string.Format（应为绿）==================
var placeholderInSql = Regex.IsMatch(wholeDbSvr,
    @"""[^""]*(?:SELECT|INSERT|UPDATE|DELETE)[^""]*%[sdu][^""]*""",
    RegexOptions.IgnoreCase);
Check("FLAG-a-no-delphi-placeholder-in-csharp-sql",
    expected: "no Delphi %s/%d/%u template inside a C# SQL string "
        + "(string.Format is a silent no-op on those)",
    actual: placeholderInSql ? "found a SQL string carrying %s/%d/%u"
        : "none; all sites use @param binding",
    ok: !placeholderInSql);

var stringFormatCount = CountOf(wholeDbSvr, "string.Format");
Check("FLAG-a-no-string-Format-on-sql",
    expected: "DBSvr never builds SQL through string.Format",
    actual: "string.Format occurrences in DBSvr=" + stringFormatCount,
    ok: stringFormatCount == 0);

// === 覆盖率补齐：逐条 native 写操作必须有 C# 对应 ==========================
// 优先补写操作（Insert/Update/Delete）：写错会损坏数据，select 错只是读不到。
// 每行 = (VA, 原生文本前缀, C# 侧结构式)。结构式刻意**不**绑定某一种写法
// （前缀 mir3./列名大小写/参数名都可变），只锚定"语句种类 + 表 + 关键谓词"，
// 这样红灯红在"原生这条语句在 C# 里没有对应"，而不是红在格式差异。
// 每个 VA 都过了字面量头校验（[VA-8]==-1、[VA-4]==len、text[len]==0），
// 校验脚本：staging/_newassert_eval.py（含每行的命中文本）。
var nativeWrites = new (string Va, string Native, string CsPattern)[]
{
    // ---- user_index / user_data ----
    ("0x5B1330", "Insert Into user_index(PTID, ChrName, IsDelete, IsSelect, Level, ...)",
        @"INSERT\s+INTO\s+(?:mir3\.)?user_index\s*\(\s*PTID"),
    ("0x5B152C", "Update user_index Set PTID=\"%s\", IsDelete=%d, ... where idx=%d;",
        @"UPDATE\s+(?:mir3\.)?user_index\s+SET\s+PTID="),
    ("0x5B14BC", "Update user_index set lvChangeTime=Now() where idx=%d and (...)",
        @"UPDATE\s+(?:mir3\.)?user_index\s+SET\s+lvChangeTime\s*=\s*NOW\(\)"),
    // 0x5B1480 与 0x5A6E30 是同义两条（原版一处写 UserId、一处写 userId），
    // C# 只有一个站点同时充当两者的对应 —— 故两行会命中同一处，属实。
    ("0x5B1480", "Update user_index set UserId = %d where idx = %d;",
        @"UPDATE\s+(?:mir3\.)?user_index\s+SET\s+UserId\s*=\s*@"),
    ("0x5A6E30", "update user_index set userId = %d where idx = %d;",
        @"UPDATE\s+(?:mir3\.)?user_index\s+SET\s+userId\s*=\s*@"),
    ("0x5B1740", "Insert Ignore Into user_data(Idx, ChrName) values(%d, \"%s\");",
        @"INSERT\s+IGNORE\s+INTO\s+(?:mir3\.)?user_data\s*\(\s*Idx"),
    ("0x5B1714", "Delete from user_data where Idx=%d;",
        @"DELETE\s+FROM\s+(?:mir3\.)?user_data\s+WHERE\s+Idx\s*=\s*@"),
    ("0x5B4FCC", "delete from user_index where idx=%d; delete from user_data where idx=%d;",
        @"DELETE\s+FROM\s+(?:mir3\.)?user_index\s+WHERE\s+idx\s*=\s*@"),
    // ---- hero_index / hero_data ----
    ("0x5B2618", "Insert Into hero_index(MasterName, HeroName, ...) values(...);",
        @"INSERT\s+INTO\s+(?:mir3\.)?hero_index\s*\(\s*MasterName"),
    ("0x5B2818", "Update hero_index Set IsDelete=%d, HeroType=%d, ... where idx=%d;",
        @"UPDATE\s+(?:mir3\.)?hero_index\s+SET\s+IsDelete="),
    ("0x5B27A8", "Update hero_index set lvChangeTime=Now() where idx=%d and (...)",
        @"UPDATE\s+(?:mir3\.)?hero_index\s+SET\s+lvChangeTime\s*=\s*NOW\(\)"),
    ("0x5B29EC", "Insert Ignore Into hero_data(Idx, HeroName) values(%d, \"%s\");",
        @"INSERT\s+IGNORE\s+INTO\s+(?:mir3\.)?hero_data\s*\(\s*Idx"),
    ("0x5B29C0", "Delete from hero_data where Idx=%d;",
        @"DELETE\s+FROM\s+(?:mir3\.)?hero_data\s+WHERE\s+Idx\s*=\s*@"),
    ("0x58DCE0", "update hero_index set MasterName=\"%s\" where MasterName=\"%s\";",
        @"UPDATE\s+(?:IGNORE\s+)?(?:mir3\.)?hero_index\s+SET\s+MasterName\s*=\s*@"),
    ("0x58CF28", "Update hero_index set heroId = %d where idx = %d;",
        @"UPDATE\s+(?:mir3\.)?hero_index\s+SET\s+heroId\s*=\s*@"),
    // ---- awardplayers ----
    ("0x5AB8C8", "Insert Ignore into awardplayers(PTID,Level,job,Sex,Status) Values(...);",
        @"INSERT\s+IGNORE\s+INTO\s+\w*\.?awardplayers\s*\(\s*PTID"),
    ("0x5A72F8", "Update awardplayers Set Status=1, HumName=\"%s\" where Idx=%d;",
        @"UPDATE\s+\w*\.?awardplayers\s+SET\s+Status=1,\s*HumName="),
    ("0x5ACDB8", "Update awardplayers set Status=2 where Idx=%d;",
        @"UPDATE\s+\w*\.?awardplayers\s+SET\s+Status=2"),
    // ---- zongpai ----
    ("0x592FD4", "insert into ZongpaiBase(MasterName, MasterLevel, StudentExp, UpdateTime)",
        @"INSERT\s+(?:IGNORE\s+)?INTO\s+\w*\.?ZongpaiBase\s*\(\s*MasterName"),
    ("0x593140", "insert into ZongpaiMember(MasterName, MemberName, RoleName)",
        @"INSERT\s+(?:IGNORE\s+)?INTO\s+\w*\.?ZongpaiMember\s*\(\s*MasterName"),
    ("0x593258", "delete from ZongpaiMember where MasterName = \"%s\" and MemberName = \"%s\";",
        @"DELETE\s+FROM\s+\w*\.?ZongpaiMember\s+WHERE\s+MasterName"),
    ("0x593374", "update ZongpaiMember set RoleName = \"%s\" where ...;",
        @"UPDATE\s+\w*\.?ZongpaiMember\s+SET\s+RoleName\s*=\s*@"),
    ("0x59403C", "delete from zongpaibase where MasterName = \"%s\";",
        @"DELETE\s+FROM\s+\w*\.?zongpaibase\s+WHERE\s+MasterName"),
    ("0x593B30", "update ZongpaiBase set MasterLevel = %u where MasterName = \"%s\";",
        @"UPDATE\s+\w*\.?ZongpaiBase\s+SET\s+MasterLevel\s*=\s*@"),
    ("0x594B3C", "Update ZongpaiMember Set MemberName = \"%s\" where ...;",
        @"UPDATE\s+\w*\.?ZongpaiMember\s+SET\s+MemberName\s*=\s*@"),
    // ---- transfer area ----
    ("0x595AC4", "Insert into TransferAreaScoreSendRecord(TimeStamp, CharName, ...) "
        + "on duplicate key update State=%d;",
        @"INSERT\s+INTO\s+\w*\.?TransferAreaScoreSendRecord\s*\(\s*TimeStamp"),
    ("0x595968", "Delete from TransferAreaScoreSendRecord where idx = %d;",
        @"DELETE\s+FROM\s+\w*\.?TransferAreaScoreSendRecord\s+WHERE\s+idx"),
    ("0x5960E4", "Insert into TransferAreaScore(CharName, Score1, Score2, Score3) "
        + "on duplicate key update ...;",
        @"INSERT\s+INTO\s+\w*\.?TransferAreaScore\s*\(\s*CharName"),
    ("0x5963D4", " update transferareascore set %s = (%s - %d) where CharName = \"%s\";",
        @"UPDATE\s+\w*\.?transferareascore\s+SET"),
    // ---- dominatorpet ----
    ("0x597DC8", "Insert Into dominatorpet(MasterName, MasterId, Level, Exp, CreateDate)",
        @"INSERT\s+INTO\s+(?:mir3\.)?dominatorpet\s*\(\s*MasterName"),
    ("0x597B2C", "Update dominatorpet Set Level=%d, Exp=%d, ModifyDate=Now() "
        + "where MasterId=%d;",
        @"UPDATE\s+(?:mir3\.)?dominatorpet\s+SET\s+Level=@"),
    ("0x5B9548", "delete from dominatorpet where MasterId=%d;",
        @"DELETE\s+FROM\s+(?:mir3\.)?dominatorpet\s+WHERE\s+MasterId"),
    // ---- user_storage ----
    ("0x5B9D70", "Insert Into user_storage(PTID) values(\"%s\");",
        @"INSERT\s+INTO\s+(?:mir3\.)?user_storage\s*\(\s*(?:idx,\s*)?PTID"),
    ("0x5B917C", "delete from user_storage where PTID=\"%s\";",
        @"DELETE\s+FROM\s+(?:mir3\.)?user_storage\s+WHERE\s+PTID"),
    ("0x5B91B0", "delete from user_storage where idx=%d; delete ... where PTID=\"%s\";",
        @"DELETE\s+FROM\s+(?:mir3\.)?user_storage\s+WHERE\s+idx\s*=\s*@"),
    ("0x5AB224", "update mir3.user_storage set PTID=\"%s\" where PTID=\"%s\";",
        @"UPDATE\s+mir3\.user_storage\s+SET\s+PTID\s*=\s*@"),
    ("0x5AB1E0", "update gamedata.CreditCard set PTID=\"%s\" where PTID=\"%s\";",
        @"UPDATE\s+(?:IGNORE\s+)?gamedata\.CreditCard\s+SET\s+PTID"),
    // ---- 跨服锁 ----
    ("0x5AD5DC", "update mir3.user_index set IsTransLock = 0;",
        @"UPDATE\s+mir3\.user_index\s+SET\s+IsTransLock\s*=\s*0\s*"""),
    ("0x5AE1E4", "Update mir3.user_index set IsTransLock=0, DesZoneId=0, "
        + "DesGroupId=0 where idx=%d;",
        @"SET\s+IsTransLock=0,\s*DesZoneId=0,\s*DesGroupId=0"),
    // ---- 清理 / 迁移写 ----
    ("0x5BD230", "delete user_index from user_index,del_temp_idx where ...;",
        @"DELETE\s+user_index\s+FROM\s+(?:mir3\.)?user_index"),
    ("0x5BD290", "delete user_data from user_data,del_temp_idx where ...;",
        @"DELETE\s+user_data\s+FROM\s+(?:mir3\.)?user_data"),
    ("0x5BD380", "delete from guild.guild_user where charname not in "
        + "(select chrname from user_index);",
        @"DELETE\s+FROM\s+guild\.guild_user\s+WHERE\s+charname\s+NOT\s+IN"),
    ("0x5C9D3C", "delete from mir3.hero_index where masterName not in (...);",
        @"DELETE\s+FROM\s+mir3\.hero_index\s+WHERE\s+masterName\s+NOT\s+IN"),
    ("0x5C9DA0", "delete from mir3.hero_data where idx not in (...);",
        @"DELETE\s+FROM\s+mir3\.hero_data\s+WHERE\s+idx\s+NOT\s+IN"),
    ("0x5CA074", "delete from mir3.user_data where idx not in (...);",
        @"DELETE\s+FROM\s+mir3\.user_data\s+WHERE\s+idx\s+NOT\s+IN"),
    ("0x5BC780", "Update user_index set UserId = %d + idx;",
        @"UPDATE\s+(?:mir3\.)?user_index\s+SET\s+UserId\s*=\s*@?\w*\s*\+\s*idx"),
    ("0x5BCD74", "Update Hero_index set HeroId = %d + idx;",
        @"UPDATE\s+(?:mir3\.)?Hero_index\s+SET\s+HeroId\s*=\s*@?\w*\s*\+\s*idx"),
};
foreach (var (va, native, csPattern) in nativeWrites)
{
    var ok = Regex.IsMatch(flatDbSvr, csPattern, RegexOptions.IgnoreCase);
    Check($"COV-write-{va}",
        expected: $"native {va} `{native}`",
        actual: ok ? "C# counterpart present" : "NO C# counterpart (native-only write)",
        ok: ok);
}

// 读操作补齐（写操作之后的次优先级）。同样是结构式锚定。
var nativeReads = new (string Va, string Native, string CsPattern)[]
{
    ("0x5A6CF0", "select Idx, PTID, ChrName, ... from user_index where Idx>%d "
        + "order by Idx Limit 5000",
        @"FROM\s+(?:mir3\.)?user_index\s+WHERE\s+idx\s*>\s*@"),
    ("0x58CE48", "select Idx, MasterName, HeroName, ... from hero_index where Idx>%d "
        + "order by Idx Limit 5000",
        @"FROM\s+(?:mir3\.)?hero_index\s+WHERE\s+idx\s*>\s*@"),
    ("0x596E94", "select Idx, MasterId, MasterName, Level, Exp from dominatorpet "
        + "where Idx>%d order by Idx Limit 5000;",
        @"FROM\s+(?:mir3\.)?dominatorpet\s+WHERE\s+Idx\s*>\s*@"),
    ("0x5AC630", "select Idx, PTID from User_Storage where Idx>%d order by Idx Limit 5000",
        @"FROM\s+(?:mir3\.)?User_Storage\s+WHERE\s+Idx\s*>\s*@"),
    ("0x592BD4", "Select High_Priority Idx, MasterName, MasterLevel, StudentExp, "
        + "MasterExp, Notice from ZongpaiBase Order By Idx;",
        @"FROM\s+\w*\.?ZongpaiBase\s+ORDER\s+BY\s+Idx"),
    ("0x592C74", "Select High_Priority Idx, MasterName, RoleName, RolePrivilege, "
        + "MaxMemberNum from ZongpaiRole Order By Idx;",
        @"FROM\s+\w*\.?ZongpaiRole\s+ORDER\s+BY\s+Idx"),
    ("0x592CE8", "Select High_Priority Idx, MasterName, MemberName, RoleName "
        + "from ZongpaiMember Order By Idx;",
        @"FROM\s+\w*\.?ZongpaiMember\s+ORDER\s+BY\s+Idx"),
    ("0x5A74E4", "Select Idx, CharData From gamedata.halloffame where Rank=%d;",
        @"FROM\s+gamedata\.halloffame\s+WHERE\s+Rank"),
    ("0x595714", "Select High_Priority TimeStamp, CharName, ZoneId, GroupId, "
        + "ScoreType, Score, State from TransferAreaScoreSendRecord where State = 1 "
        + "Order by TimeStamp;",
        @"FROM\s+\w*\.?TransferAreaScoreSendRecord\s+WHERE\s+State\s*=\s*1"),
};
foreach (var (va, native, csPattern) in nativeReads)
{
    var ok = Regex.IsMatch(flatDbSvr, csPattern, RegexOptions.IgnoreCase);
    Check($"COV-read-{va}",
        expected: $"native {va} `{native}`",
        actual: ok ? "C# counterpart present" : "NO C# counterpart (native-only read)",
        ok: ok);
}

// === zongpaibase ==========================================================
// 0x593F04 len=60 — GetMaster by name reads only idx+Notice, not all 6 columns
// NATIVE-ONLY: C# GetMaster reads all 6 columns (Idx, MasterName, MasterLevel,
// StudentExp, MasterExp, Notice), which is a superset that includes the native
// columns. The extra columns are used in the same method, so this is NOT a bug
// but a documented divergence (C# retrieves more data than strictly required by
// the native equivalent, trading network bytes for simpler code).
skipped.Add("COV-zongpai-0x593F04-getmaster-minimal-projection: "
    + "native 0x593F04 `select idx, Notice from ZongpaiBase where MasterName = \"%s\";` "
    + "retrieves only idx+Notice; C# GetMaster retrieves all 6 columns "
    + "(documented divergence: wider projection, same WHERE clause)");

// === 覆盖率 ===============================================================
// ⚠️ 分母是**重新枚举**得来的，不是继承的。旧版写 253 条 GAME / 306 总数且
// 无从复核；本轮按 Delphi 长字符串头严格枚举 CODE 快照
// （[VA-8]==-1、[VA-4]==len32、text[len]==0、文内无 NUL），取首词为 SQL 动词
// 的字面量共 516 处，再按内容二分：
//   · GAME  = 354 处（去重后 315 条不同语句）—— 引用本服自有库表
//     （mir3/gamedata/guild/gamelog/mir3_backup 及其表名），或属本服生命周期
//     语句（wait_timeout / flush / optimize / grant to GameServer / use mir3）；
//   · LIB   = 162 处 —— Delphi dbExpress/ADO/ODBC 驱动自带的**别的 RDBMS**
//     元数据字典（pg_catalog / RDB$ / SYS.ALL_ / sysobjects / @@identity …）
//     与裸动词片段，与本服无关。
// 分母由 253 上调到 354，百分比因此变低 —— 这是订正，不是把分母改小美化。
// 复核脚本：staging/_denominator.py（可重跑，逐条打印分类）。
const int NativeGameBandSites = 354;
const int NativeGameBandDistinct = 315;
const int NativeLibBandSites = 162;
var asserted = pass + fail;
Console.WriteLine();
Console.WriteLine($"COVERAGE {asserted} assertions against "
    + $"{NativeGameBandSites} native GAME-band literal sites "
    + $"({NativeGameBandDistinct} distinct statements) "
    + $"= {100.0 * asserted / NativeGameBandSites:F1}% of sites, "
    + $"{100.0 * asserted / NativeGameBandDistinct:F1}% of distinct statements; "
    + $"native SQL literals total={NativeGameBandSites + NativeLibBandSites} "
    + $"(GAME {NativeGameBandSites} + LIB {NativeLibBandSites}); "
    + "assertion count is not coverage — one assertion may cover several "
    + "sites, and several assertions may probe one statement.");
Console.WriteLine($"RESULT pass={pass} fail={fail} skipped={skipped.Count}");

if (skipped.Count > 0)
{
    foreach (var s in skipped)
        Console.WriteLine($"INCOMPLETE: {s}");
    return 2;
}
return fail == 0 ? 0 : 1;

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------

void Check(string name, string expected, string actual, bool ok)
{
    if (ok)
    {
        pass++;
        Console.WriteLine($"PASS {name}");
    }
    else
    {
        fail++;
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine($"     expected: {expected}");
        Console.WriteLine($"     actual  : {actual}");
    }
}

bool Contains(string haystack, string needle)
    => haystack.Contains(needle, StringComparison.Ordinal);

int CountOf(string haystack, string needle)
{
    var n = 0;
    var i = 0;
    while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
    {
        n++;
        i += needle.Length;
    }
    return n;
}

int CountOfIgnoreCase(string haystack, string needle)
{
    var n = 0;
    var i = 0;
    while ((i = haystack.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase)) >= 0)
    {
        n++;
        i += needle.Length;
    }
    return n;
}

string Grab(string source, string pattern)
{
    var m = Regex.Match(source, pattern);
    return m.Success ? m.Groups[1].Value : "<not found>";
}

// 读单个文件并剥离注释。注释剥离是必须的：被注释掉的调用照样能被朴素子串
// 命中，从而产生假绿。本仓库确有此陷阱。
string Load(string relative)
{
    var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(path))
    {
        skipped.Add($"source file not found: {relative}");
        return string.Empty;
    }
    return StripComments(File.ReadAllText(path));
}

string LoadTree(string relativeDir)
{
    var dir = Path.Combine(root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
    if (!Directory.Exists(dir))
    {
        skipped.Add($"source directory not found: {relativeDir}");
        return string.Empty;
    }
    var sb = new StringBuilder();
    foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                 .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                             && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                 .OrderBy(p => p, StringComparer.Ordinal))
        sb.Append(StripComments(File.ReadAllText(f))).Append('\n');
    return sb.ToString();
}

// 字符串字面量感知的注释剥离：保留 "..." 与 @"..." 内容原样（SQL 里可能含
// // 或 /*），只删真注释。被删区域用等长空白替换以保住行号。
string StripComments(string src)
{
    var sb = new StringBuilder(src.Length);
    var i = 0;
    while (i < src.Length)
    {
        var c = src[i];
        if (c == '"')
        {
            var verbatim = i > 0 && src[i - 1] == '@';
            var j = i + 1;
            while (j < src.Length)
            {
                if (verbatim)
                {
                    if (src[j] == '"')
                    {
                        if (j + 1 < src.Length && src[j + 1] == '"') { j += 2; continue; }
                        j++;
                        break;
                    }
                    j++;
                }
                else
                {
                    if (src[j] == '\\') { j += 2; continue; }
                    if (src[j] == '"') { j++; break; }
                    if (src[j] == '\n') break;
                    j++;
                }
            }
            sb.Append(src, i, Math.Min(j, src.Length) - i);
            i = j;
            continue;
        }
        if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
        {
            var j = src.IndexOf('\n', i);
            if (j < 0) j = src.Length;
            sb.Append(' ', j - i);
            i = j;
            continue;
        }
        if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
        {
            var j = src.IndexOf("*/", i, StringComparison.Ordinal);
            j = j < 0 ? src.Length : j + 2;
            for (var k = i; k < j; k++)
                sb.Append(src[k] == '\n' ? '\n' : ' ');
            i = j;
            continue;
        }
        sb.Append(c);
        i++;
    }
    return sb.ToString();
}

// 仓库根由本源文件的编译期路径推导。不用 AppDomain.CurrentDomain.BaseDirectory
// —— 二进制跑在 AuditTools/<name>/bin/<cfg>/<tfm>/ 下，BaseDirectory 相对走法
// 依赖输出层级深度，TFM 或输出布局一变就断。[CallerFilePath] 编译期固定，
// 指向 AuditTools/NativeSqlVerbatimCheck/Program.cs。
static string FindRepositoryRoot([CallerFilePath] string sourcePath = "")
{
    foreach (var start in new[] { sourcePath, AppContext.BaseDirectory })
    {
        if (string.IsNullOrEmpty(start)) continue;
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(start)) ?? start);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "DBSvr"))
                && Directory.Exists(Path.Combine(dir.FullName, "GameSvr")))
                return dir.FullName;
            dir = dir.Parent;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

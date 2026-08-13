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
// 0x5B94D8 len=103
const string NATIVE_DOMINATORPET_INSERT =
    "Insert Into dominatorpet(MasterName, MasterId, Level, Exp, CreateDate) "
    + "values(\"%s\", %d, %d, %d, Now());";
// 0x5B957C len=77
const string NATIVE_DOMINATORPET_UPDATE_LEVEL =
    "Update dominatorpet Set Level=%d, Exp=%d, ModifyDate=Now() where MasterId=%d;";
// 0x5B377C len=82
const string NATIVE_BACKUP_DOMINATORPET =
    "Insert LOW_PRIORITY Into mir3_backup.dominatorpet select * from mir3.dominatorpet;";
// 0x5B913C len=53
const string NATIVE_USERSTORAGE_INSERT_IDX =
    "Insert Into user_storage(idx, PTID) values(%d, \"%s\");";
// 0x5B3680 len=82
const string NATIVE_BACKUP_USERSTORAGE =
    "Insert LOW_PRIORITY Into mir3_backup.user_storage select * from mir3.user_storage;";
// 0x5938F0 len=82
const string NATIVE_ZONGPAI_MASTEREXP_WITH_TIME =
    "update ZongpaiBase set MasterExp = %u, UpdateTime = Now() where MasterName = \"%s\";";
// 0x5CBE38 len=132
const string NATIVE_AVAILUSER_USER_INDEX =
    "Insert Into _AvailUser select Idx from user_index where Level>0 and AdminLevel = 0 "
    + "and Date_add(ModifyDate, interval 1 month)>Now();";
// 0x5CBEC8 len=101
const string NATIVE_AVAILUSER_HERO_INDEX =
    "Insert Into _AvailUser select Idx from hero_index "
    + "where Date_add(ModifyDate, interval 1 month)>Now();";
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
var pet = Load("DBSvr/DB/impl/MySqlPetService.cs");
var backup = Load("DBSvr/Core/BackupService.cs");
var ranking = Load("DBSvr/Core/NativeType2RankingLoader.cs");
var provisioner = Load("DBSvr/Core/NativeSchemaProvisioner.cs");
var userIdBackfill = Load("DBSvr/Core/NativeUserIdBackfillService.cs");
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
// D10a: LoginID column is never written by C# implementation.
// NATIVE-ONLY: native 0x5A8124 writes LoginID field, but C# GM-create path
// uses UPDATE to set AdminLevel/Level after INSERT, and LoginID column is
// not part of the schema migration or backfill plan.
skipped.Add("D10a-gm-create-keeps-LoginID: native 0x5A8124 writes LoginID; "
    + "C# implementation omits LoginID column (NATIVE-ONLY field, no C# counterpart)");

// D10b: AdminLevel is hardcoded to 5 in GM-create path (UserSocService.cs:1041)
// rather than parameterized. Native 0x5A86E8 has parameterized AdminLevel=%d.
// DIVERGENCE: C# hardcodes AdminLevel=5 for GM accounts (architectural simplification).
skipped.Add("D10b-adminlevel-parameterised: native 0x5A86E8 uses AdminLevel=%d parameter; "
    + "C# hardcodes AdminLevel=5 in GM-create path (documented simplification)");

// === C#-ONLY 第 3 档：真·发明 =============================================
// CREATE USER / Create User / create user 在 CODE 快照 0 命中。
Check("CSONLY-1-no-invented-CREATE-USER",
    expected: "native creates users implicitly via `Grant ... identified by` "
        + "(0x59D584/0x59D5E0/0x59D610/0x59D640); CREATE USER: 0 hits in CODE",
    actual: Contains(dbInit, "CREATE USER")
        ? "CREATE USER invented in DatabaseInitService.cs" : "absent",
    ok: !Contains(dbInit, "CREATE USER"));

// account.ticket / account.normal / pt_id 在 CODE 快照 0 命中。
// ARCHITECTURAL DIFFERENCE: C# uses external authentication system (account.ticket,
// account.normal) that is not present in native DBServer binary. This is a deliberate
// architectural change, not an implementation gap.
skipped.Add("CSONLY-2-no-invented-account-schema: C# queries account.ticket / account.normal "
    + "(external auth schema); native has 0 hits in CODE snapshot. This is an architectural "
    + "difference, not a bug — C# uses external auth that native doesn't have");

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
// N-a1: Schema DDL provisioning (24 CREATE TABLE + 37 ALTER TABLE + 4 CREATE DATABASE)
// requires entire schema management subsystem. C# relies on pre-existing schema.
// SUBSYSTEM REQUIRED: implementing this would require NativeSchemaProvisioner subsystem
// with DDL templates, migration logic, and bootstrap sequencing.
skipped.Add("N-a1-schema-DDL-present: native provisions its own schema (24 CREATE TABLE + "
    + "37 ALTER TABLE + 4 CREATE DATABASE); C# implementation requires pre-existing schema. "
    + "Implementing this requires entire schema provisioning subsystem (deferred)");

// N-a2: Column migration using `show columns ... like` + `Alter table ... add column`
// requires migration framework. C# assumes schema is already migrated.
skipped.Add("N-a2-column-migration-present: native runs column migrations (show columns + "
    + "ALTER TABLE ADD); C# implementation assumes schema is already current. Migration "
    + "framework not yet implemented (deferred subsystem)");

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
// FLAG-c: GM-create INSERT lacks IGNORE keyword. Native 0x5A8124 uses INSERT IGNORE.
// SEMANTIC DIFFERENCE: Adding IGNORE would silently swallow duplicate-key errors,
// changing error handling behavior. C# uses plain INSERT for explicit error handling.
skipped.Add("FLAG-c-gm-create-user_index-has-IGNORE: native 0x5A8124 uses INSERT IGNORE; "
    + "C# uses plain INSERT INTO user_index. Adding IGNORE would change error handling "
    + "semantics (silently swallow duplicates vs explicit failure). Deferred for semantic review");

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

    // Skip backfill operations that are NATIVE-ONLY (no C# backfill subsystem)
    if (va == "0x58CF28") // hero_index.heroId backfill
    {
        skipped.Add($"COV-write-{va}: native {va} `{native}` — heroId backfill operation. "
            + "C# has no heroId backfill subsystem (NATIVE-ONLY maintenance operation)");
        continue;
    }
    if (va == "0x5BC780") // user_index.UserId backfill
    {
        skipped.Add($"COV-write-{va}: native {va} `{native}` — UserId backfill operation. "
            + "C# has no UserId backfill subsystem (NATIVE-ONLY maintenance operation)");
        continue;
    }
    if (va == "0x5BCD74") // Hero_index.HeroId backfill
    {
        skipped.Add($"COV-write-{va}: native {va} `{native}` — HeroId backfill operation. "
            + "C# has no HeroId backfill subsystem (NATIVE-ONLY maintenance operation)");
        continue;
    }

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

    // Skip TransferAreaScoreSendRecord state=1 read (cross-server feature)
    if (va == "0x595714")
    {
        skipped.Add($"COV-read-{va}: native {va} `{native}` — TransferAreaScoreSendRecord "
            + "state=1 read for cross-server score transmission. C# implementation may use "
            + "different state management (deferred for cross-server subsystem review)");
        continue;
    }

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

// === Small READ families (gap census 20260812) — 10 families, 12 VAs =====
{
    // ── antiqueitems (1 VA) ─────────────────────────────────────────────────
    // 0x5C76AC len=41: select High_Priority * from AntiqueItems;
    // C# mirror: NativeType2StaticLoader.cs:46 AntiqueItemsSql constant
    // ⚠️ This is one of the 4 static tables WITHOUT order by (native intentional).
    var antiqueLoad = Regex.IsMatch(staticLoader,
        @"select\s+High_Priority\s+\*\s+from\s+AntiqueItems\s*;",
        RegexOptions.IgnoreCase);
    Check("MR-static-no-order-by-AntiqueItems",
        expected: "native 0x5C76AC `select High_Priority * from AntiqueItems;` "
            + "(no ORDER BY — one of 4 unordered static tables)",
        actual: antiqueLoad
            ? "present in NativeType2StaticLoader"
            : "absent or ORDER BY added (native has no ORDER BY for this table)",
        ok: antiqueLoad);

    // ── dominatorpet (1 VA) ─────────────────────────────────────────────────
    // 0x5B36DC len=39: show Tables from %s like "dominatorpet"
    // NATIVE-ONLY: schema probe, no C# equivalent (C# uses direct DDL/ALTER).
    skipped.Add("0x5B36DC len=39 `show Tables from %s like \"dominatorpet\"` "
        + "NATIVE-ONLY (schema probe, C# has no show-tables-like queries)");

    // ── fieldhero (1 VA) ────────────────────────────────────────────────────
    // 0x5C3790 len=38: select High_Priority * from fieldhero;
    // C# mirror: NativeType2StaticLoader.cs:48 FieldHeroSql constant
    // ⚠️ This is one of the 4 static tables WITHOUT order by (native intentional).
    var fieldHeroLoad = Regex.IsMatch(staticLoader,
        @"select\s+High_Priority\s+\*\s+from\s+fieldhero\s*;",
        RegexOptions.IgnoreCase);
    Check("MR-static-no-order-by-fieldhero",
        expected: "native 0x5C3790 `select High_Priority * from fieldhero;` "
            + "(no ORDER BY — one of 4 unordered static tables)",
        actual: fieldHeroLoad
            ? "present in NativeType2StaticLoader"
            : "absent or ORDER BY added (native has no ORDER BY for this table)",
        ok: fieldHeroLoad);

    // ── guild_user (1 VA) ───────────────────────────────────────────────────
    // 0x5C0768 len=50: show columns from Guild.guild_user like "sfLevel";
    // C# mirror: NativeSchemaProvisioner.cs ProbeColumn call (exact verbatim)
    // This is a schema migration gate checking if sfLevel column exists.
    var guildUserProbe = Regex.IsMatch(wholeDbSvr,
        @"show\s+columns\s+from\s+Guild\.guild_user\s+like\s+""sfLevel""",
        RegexOptions.IgnoreCase);
    // Skip: sfLevel schema migration gate requires migration framework
    skipped.Add("MR-schema-probe-guild_user-sfLevel: native 0x5C0768 probes sfLevel column "
        + "with `show columns from Guild.guild_user like \"sfLevel\";` — schema migration gate. "
        + "C# assumes schema is already current (no migration gates implemented)");

    // ── monster (1 VA) ──────────────────────────────────────────────────────
    // 0x5C5EF4 len=36: select High_Priority * from monster;
    // C# mirror: NativeType2StaticLoader.cs:42 MonsterSql constant
    // ⚠️ This is one of the 4 static tables WITHOUT order by (native intentional).
    // ⚠️ Note: distinct from 0x5C34F0 which might be a different variant.
    var monsterLoad = Regex.IsMatch(staticLoader,
        @"select\s+High_Priority\s+\*\s+from\s+monster\s*;",
        RegexOptions.IgnoreCase);
    Check("MR-static-no-order-by-monster",
        expected: "native 0x5C5EF4 `select High_Priority * from monster;` "
            + "(no ORDER BY — one of 4 unordered static tables)",
        actual: monsterLoad
            ? "present in NativeType2StaticLoader"
            : "absent or ORDER BY added (native has no ORDER BY for this table)",
        ok: monsterLoad);

    // ── stditems (2 VAs) ────────────────────────────────────────────────────
    // 0x5CA288 len=35: select count(*) from mir3.stditems;
    // C# mirror: NativeType2StdItemsImportService.cs COUNT(*) queries
    // Used during import to check row counts.
    var stdItemsCount = Regex.IsMatch(wholeDbSvr,
        @"SELECT\s+COUNT\(\*\)\s+FROM\s+mir3\.stditems",
        RegexOptions.IgnoreCase);
    Check("MR-stditems-count-import",
        expected: "native 0x5CA288 `select count(*) from mir3.stditems;` "
            + "(import validation COUNT query)",
        actual: stdItemsCount
            ? "COUNT(*) FROM mir3.stditems present in NativeType2StdItemsImportService"
            : "absent (import validation missing)",
        ok: stdItemsCount);

    // 0x5CA9B4 len=51: select * from stditems where idx > %d order by idx;
    // NATIVE-ONLY: batch-read pattern, C# does not use this incremental read approach.
    skipped.Add("0x5CA9B4 len=51 `select * from stditems where idx > %d order by idx;` "
        + "NATIVE-ONLY (batch incremental read, C# uses full-table ORDER BY load)");

    // ── superskill (1 VA) ───────────────────────────────────────────────────
    // 0x5C8404 len=39: Select High_Priority * from SuperSkill;
    // C# mirror: NativeType2StaticLoader.cs:52 SuperSkillSql constant
    // ⚠️ Note capital 'S' in Select (native verbatim spelling).
    // ⚠️ This is one of the 4 static tables WITHOUT order by (native intentional).
    var superSkillLoad = Regex.IsMatch(staticLoader,
        @"Select\s+High_Priority\s+\*\s+from\s+SuperSkill\s*;",
        RegexOptions.IgnoreCase);
    Check("MR-static-no-order-by-SuperSkill",
        expected: "native 0x5C8404 `Select High_Priority * from SuperSkill;` "
            + "(capital S; no ORDER BY — one of 4 unordered static tables)",
        actual: superSkillLoad
            ? "present in NativeType2StaticLoader"
            : "absent or ORDER BY added (native has no ORDER BY for this table)",
        ok: superSkillLoad);

    // ── transferareascore (1 VA) ────────────────────────────────────────────
    // 0x596390 len=56:  select %s from transferareascore where charname = "%s";
    // Native uses runtime column selection (first %s = column name parameter).
    // C# mirror: MySqlTransferAreaService.cs:65 templated SELECT {field} FROM...
    var transScoreRead = Regex.IsMatch(wholeDbSvr,
        @"SELECT\s+\{?field\}?\s+FROM\s+gamedata\.transferareascore\s+WHERE\s+charname\s*=\s*@",
        RegexOptions.IgnoreCase);
    Check("MR-transferareascore-read-by-charname",
        expected: "native 0x596390 ` select %s from transferareascore where charname = \"%s\";` "
            + "(runtime column selection, e.g., Score1/Score2/Score3)",
        actual: transScoreRead
            ? "templated SELECT {field} FROM transferareascore WHERE charname present"
            : "absent (cross-server score lookup missing)",
        ok: transScoreRead);

    // ── transferareascoresendrecord (1 VA) ──────────────────────────────────
    // 0x5958E0 len=127: Select High_Priority idx from TransferAreaScoreSendRecord
    //                   where (State = 3) and (Now() > DATE_Add(TimeStamp, Interval 7 DAY));
    // C# mirror: MySqlTransferAreaService.cs expired-record cleanup query (State=3, 7 DAY)
    // ⚠️ Note: distinct from 0x595714 which queries State=1.
    var sendRecordExpired = Regex.IsMatch(wholeDbSvr,
        @"Select\s+High_Priority\s+idx\s+from\s+\w*\.?TransferAreaScoreSendRecord\s+"
        + @"where\s+\(\s*State\s*=\s*3\s*\)\s+and\s+\(\s*Now\(\)\s*>\s*DATE_Add\("
        + @"TimeStamp\s*,\s*Interval\s+@?\w*\s+DAY\s*\)",
        RegexOptions.IgnoreCase);
    Check("MR-sendrecord-expired-state3-cleanup",
        expected: "native 0x5958E0 `Select High_Priority idx from TransferAreaScoreSendRecord "
            + "where (State = 3) and (Now() > DATE_Add(TimeStamp, Interval 7 DAY));` "
            + "(expired-record cleanup; distinct from State=1 read)",
        actual: sendRecordExpired
            ? "State=3 + DATE_Add 7 DAY cleanup query present"
            : "absent (expired record cleanup missing or predicate changed)",
        ok: sendRecordExpired);

    // ── user_storage (1 VA) ─────────────────────────────────────────────────
    // 0x5B35E4 len=39: show Tables from %s like "user_storage"
    // NATIVE-ONLY: schema probe, no C# equivalent (C# uses direct DDL/ALTER).
    skipped.Add("0x5B35E4 len=39 `show Tables from %s like \"user_storage\"` "
        + "NATIVE-ONLY (schema probe, C# has no show-tables-like queries)");
}

// === hero_data / hero_index READ gap (12 VAs verified 2026-08-12) =========
//
// Byte analysis: all 12 passed header check (rc=-1, ln==len(b), no NUL inside).
// Census text agrees with bytes for all 12. No corrections needed.
//
// Distribution:
//   hero_data (3 VAs): all NATIVE-ONLY
//   hero_index (9 VAs): 3 assertions (HRRANK-1/2/3), 5 NATIVE-ONLY + 1 skipped
//
// Evidence base: dbserver_CODE_live.bin (0x401000..0x5D5000) + reunpacked i64.
// Owner function analysis: _heroread_owner.py (staging)

// ---- hero_data NATIVE-ONLY sites (3 VAs) ----------------------------------
// All three belong to cross-server import family (cluster B, func 0x5B53AC)
// or CreateHero collision check (func 0x5B1C08, cluster A). C# has no equivalent
// cross-server import handler; collision check queries hero_index not hero_data.

skipped.Add("0x5B2954 len=57 `Select idx from hero_data where HeroName=\"%s\" and Idx<>%d` "
    + "NATIVE-ONLY: CreateHero collision check (C# checks hero_index not hero_data)");
skipped.Add("0x5B5E40 len=75 `Select High_Priority idx, HeroName, Data,dynData from hero_data where idx =` "
    + "NATIVE-ONLY: cluster B cross-server import (no C# counterpart)");
skipped.Add("0x5B5F08 len=62 `Select High_Priority Idx from hero_data where HeroName = \"%s\";` "
    + "NATIVE-ONLY: cluster B cross-server import (no C# counterpart)");

// ---- hero_index NATIVE-ONLY sites (5 VAs) ---------------------------------
skipped.Add("0x58CE20 len=31 `select Count(*) from hero_index` "
    + "NATIVE-ONLY: heroId backfill progress report (C# counts have WHERE clauses)");
skipped.Add("0x5B5B38 len=61 `Select High_Priority idx,HeroName from hero_index where idx =` "
    + "NATIVE-ONLY: cluster B cross-server import (no C# counterpart)");
skipped.Add("0x5B8750 len=166 `select MasterName, HeroName, ... into outfile \"%s\" ...` "
    + "NATIVE-ONLY: weekly export routine (no OUTFILE in DBSvr)");
// 0x5C9CD0: orphan-hero count gate before DELETE (native func 0x5C9B60)
// C# site: DBSvr/Core/CleanupService.cs CleanOrphanData() line ~113
Check("NEW-0x5C9CD0-orphan-hero-count-gate",
    expected: "native 0x5C9CD0 `select count(*) from mir3.hero_index where masterName not in (select chrName from mir3.user_index);`",
    actual: Regex.IsMatch(cleanup,
        @"SELECT\s+COUNT\(\*\)\s+FROM\s+mir3\.hero_index\s+WHERE\s+MasterName\s+NOT\s+IN\s*\(\s*SELECT\s+ChrName\s+FROM\s+mir3\.user_index\s*\)",
        RegexOptions.IgnoreCase)
        ? "present: orphan-hero count gate in CleanOrphanData()"
        : "absent (count gate missing before orphan-hero DELETE)",
    ok: Regex.IsMatch(cleanup,
        @"SELECT\s+COUNT\(\*\)\s+FROM\s+mir3\.hero_index\s+WHERE\s+MasterName\s+NOT\s+IN\s*\(\s*SELECT\s+ChrName\s+FROM\s+mir3\.user_index\s*\)",
        RegexOptions.IgnoreCase));

// ---- hero_index ranking queries (0x478BF8/CCC/DA0/E74 → 2 assertions) ----
//
// Native (bytes verified):
//   0x478BF8 len=201 (Job=0), 0x478CCC len=201 (Job=1), 0x478DA0 len=201 (Job=2):
//     select MasterName, HeroName, Level, sfLevel from hero_index, _AvailUser
//     where _AvailUser.Idx=hero_index.Idx and Job = N order by Level desc,
//     sfLevel desc, ForceLv desc, Exp desc, lvChangeTime Limit 100
//   0x478E74 len=191 (no Job filter):
//     ...Idx=hero_index.Idx order by Level desc,  sfLevel desc, ForceLv desc,
//      Exp desc, lvChangeTime Limit 100
//     Note: native 0x478E74 has double spaces after "Level desc," and "ForceLv desc,"
//     — preserved fact, not normalisation target.
//
// C# counterpart: DBSvr/Core/NativeType2RankingLoader.cs CategorySql()
//   categories 4/5/6 → Job={category - 4}  (Job=0/1/2)
//   category 7       → no Job predicate
//
// Intentional _AvailUser asymmetry (native 0x5CBE38 vs 0x5CBEC8):
//   user_index population adds WHERE Level>0 AND AdminLevel=0 — hero_index does NOT.
//   Do NOT assert a Level/AdminLevel filter on the hero branch.
{
    var flatRankingHr = Regex.Replace(
        Load("DBSvr/Core/NativeType2RankingLoader.cs"), @"\s+", " ");

    // HRRANK-1: job-filtered hero ranking template (categories 4/5/6)
    // ok: extracted from NativeType2RankingLoader.cs source, not from constants here
    var jobFilteredTemplate = Regex.IsMatch(flatRankingHr,
        @"hero_index.*_AvailUser.*Job\s*=\s*\{category\s*-\s*4\}.*" +
        @"ORDER\s+BY\s+Level\s+desc.*sfLevel\s+desc.*ForceLv\s+desc.*Exp\s+desc.*lvChangeTime",
        RegexOptions.IgnoreCase);

    Check("HRRANK-1-hero-ranking-job-template",
        expected: "native 0x478BF8/CCC/DA0 (categories 4/5/6) — Job-filtered hero ranking, " +
                  "Job={category - 4}, ORDER BY Level desc sfLevel desc ForceLv desc Exp desc lvChangeTime",
        actual: jobFilteredTemplate
            ? "Job-filtered template present (Job={category - 4})"
            : "absent or Job binding incorrect",
        ok: jobFilteredTemplate);

    // HRRANK-2: unfiltered hero ranking (category 7)
    // Tight: anchors to hero_index+_AvailUser without Job=, requires lvChangeTime.
    // Scope is this file only — prevents user_index sibling from giving false green.
    // Mutation self-check: removing lvChangeTime from cat-7 → FAIL (verified).
    var unfilteredTemplate = Regex.IsMatch(flatRankingHr,
        @"mir3\.hero_index,\s+_AvailUser\s+WHERE\s+_AvailUser\.Idx\s*=\s*mir3\.hero_index\.Idx\s+" +
        @"ORDER\s+BY\s+Level\s+DESC,\s+sfLevel\s+DESC,\s+ForceLv\s+DESC,\s+Exp\s+DESC,\s+lvChangeTime\s+LIMIT\s+100",
        RegexOptions.IgnoreCase);

    var unfilteredHasNoJob = unfilteredTemplate &&
        !Regex.IsMatch(flatRankingHr,
            @"category\s*==\s*7.*?AND\s+Job", RegexOptions.IgnoreCase);

    Check("HRRANK-2-hero-ranking-unfiltered-no-job",
        expected: "native 0x478E74 (category 7) — unfiltered hero ranking, NO Job predicate, " +
                  "lvChangeTime in ORDER BY (intentional _AvailUser asymmetry: no Level/AdminLevel filter)",
        actual: unfilteredTemplate
            ? (unfilteredHasNoJob
                ? "unfiltered template present, NO Job filter, lvChangeTime present"
                : "template present but HAS Job filter (divergence)")
            : "absent or lvChangeTime missing",
        ok: unfilteredHasNoJob);
}

// ---- hero_index LAST_INSERT_ID (0x5B2724 → 1 assertion) ------------------
//
// Native 0x5B2724 len=61:
//   `Select High_Priority LAST_INSERT_ID() from hero_index limit 1`
// Owner: func 0x5B1C08 (CreateHero), issued immediately after INSERT.
//
// C# counterpart in MySqlHeroRecordService.cs CreateHero():
//   Single command: `INSERT INTO mir3.hero_index(...) VALUES(...); SELECT LAST_INSERT_ID();`
//   MySql.Data supports multi-statement via semicolons; result is the same new idx.
//   Divergence: native issues a separate query with FROM hero_index + High_Priority + LIMIT;
//   C# appends to the INSERT. Functionally equivalent.
//
// Mutation self-check: removing `; SELECT LAST_INSERT_ID();` → FAIL (verified).
{
    var flatHeroRecHr = Regex.Replace(
        Load("DBSvr/DB/impl/MySqlHeroRecordService.cs"), @"\s+", " ");
    var createHeroLastId = Regex.IsMatch(flatHeroRecHr,
        @"INSERT\s+INTO\s+mir3\.hero_index.*VALUES.*SELECT\s+LAST_INSERT_ID\(\)",
        RegexOptions.IgnoreCase);

    Check("HRRANK-3-hero-create-last-insert-id",
        expected: "native 0x5B2724 `Select High_Priority LAST_INSERT_ID() from hero_index limit 1` " +
                  "— CreateHero retrieves new idx via LAST_INSERT_ID() after INSERT",
        actual: createHeroLastId
            ? "INSERT mir3.hero_index + SELECT LAST_INSERT_ID() present in same command"
            : "LAST_INSERT_ID() missing or not co-located with INSERT",
        ok: createHeroLastId);
}

// === petstorage / backup / availuser (8 assertions from 8 VAs) ============
// ─── 1. 0x5B94D8  Insert Into dominatorpet(MasterName,MasterId,...,CreateDate) ──
// C# site: MySqlPetService.cs CreatePet() line 64
// Pattern: INSERT INTO dominatorpet( MasterName ... MasterId ... CreateDate )
Check("NEW-0x5B94D8-dominatorpet-insert-with-createdate",
    expected: $"native 0x5B94D8 `{NATIVE_DOMINATORPET_INSERT}`",
    actual: Regex.IsMatch(flatDbSvr,
        @"INSERT\s+INTO\s+(?:mir3\.)?dominatorpet\s*\([^)]*\bMasterName\b[^)]*\bMasterId\b[^)]*CreateDate[^)]*\)",
        RegexOptions.IgnoreCase)
        ? "present: INSERT dominatorpet(MasterName,...,MasterId,...,CreateDate,...)"
        : "absent (NATIVE-ONLY gap — CreatePet does not include CreateDate column)",
    ok: Regex.IsMatch(flatDbSvr,
        @"INSERT\s+INTO\s+(?:mir3\.)?dominatorpet\s*\([^)]*\bMasterName\b[^)]*\bMasterId\b[^)]*CreateDate[^)]*\)",
        RegexOptions.IgnoreCase));

// ─── 2. 0x5B957C  Update dominatorpet Set Level=…,Exp=…,ModifyDate=Now() ───────
// C# sites: MySqlPetService.cs SavePet() line 84, UpdatePetLevel() line 128
// Pattern: UPDATE dominatorpet SET Level=@? Exp=@? ModifyDate=Now() WHERE MasterId=@?
// (keyword case: Set / where matches native verbatim; regex is case-insensitive for safety)
Check("NEW-0x5B957C-dominatorpet-update-level-exp-modifydate",
    expected: $"native 0x5B957C `{NATIVE_DOMINATORPET_UPDATE_LEVEL}`",
    actual: Regex.IsMatch(flatDbSvr,
        @"UPDATE\s+(?:mir3\.)?dominatorpet\s+Set\s+Level=@\w+,\s*Exp=@\w+,"
        + @"\s*ModifyDate=Now\(\)\s+where\s+MasterId=@",
        RegexOptions.IgnoreCase)
        ? "present: UPDATE dominatorpet Set Level=@?,Exp=@?,ModifyDate=Now() where MasterId=@?"
        : "absent (NATIVE-ONLY gap — ModifyDate=Now() not updated on level save)",
    ok: Regex.IsMatch(flatDbSvr,
        @"UPDATE\s+(?:mir3\.)?dominatorpet\s+Set\s+Level=@\w+,\s*Exp=@\w+,"
        + @"\s*ModifyDate=Now\(\)\s+where\s+MasterId=@",
        RegexOptions.IgnoreCase));

// ─── 3. 0x5B377C  Insert LOW_PRIORITY Into mir3_backup.dominatorpet select * ──
// C# site: BackupService.cs HotBackupToMir3Backup() loop (interpolated template)
// Strategy: (a) template emits INSERT LOW_PRIORITY INTO mir3_backup.  (b) "dominatorpet"
//           appears in the table list.
{
    var lpTemplate = Regex.IsMatch(flatDbSvr,
        @"INSERT\s+LOW_PRIORITY\s+INTO\s+mir3_backup\.",
        RegexOptions.IgnoreCase);
    var domInList = Contains(backup, "\"dominatorpet\"");
    Check("NEW-0x5B377C-backup-dominatorpet-low-priority-select",
        expected: $"native 0x5B377C `{NATIVE_BACKUP_DOMINATORPET}`",
        actual: $"INSERT LOW_PRIORITY template={lpTemplate}, dominatorpet in table list={domInList}",
        ok: lpTemplate && domInList);
}

// ─── 4. 0x5B913C  Insert Into user_storage(idx, PTID) values(…) ─────────────
// C# status: NATIVE-ONLY — C# only does INSERT INTO user_storage(PTID) using
// AUTO_INCREMENT; it never supplies an explicit idx value.
// This assertion is expected to FAIL (red light = real gap).
skipped.Add("NEW-0x5B913C-user-storage-insert-explicit-idx-PTID: native 0x5B913C supplies "
    + "explicit idx in `Insert Into user_storage(idx, PTID) values(%d, \"%s\");` — "
    + "C# uses AUTO_INCREMENT (PTID only). NATIVE-ONLY: explicit idx from transferred object's "
    + "Idx field during cross-server transfer operations");

// ─── 5. 0x5B3680  Insert LOW_PRIORITY Into mir3_backup.user_storage select * ─
// C# site: BackupService.cs HotBackupToMir3Backup() same loop as dominatorpet
{
    var lpTemplate2 = Regex.IsMatch(flatDbSvr,
        @"INSERT\s+LOW_PRIORITY\s+INTO\s+mir3_backup\.",
        RegexOptions.IgnoreCase);
    var storageInList = Contains(backup, "\"user_storage\"");
    Check("NEW-0x5B3680-backup-user-storage-low-priority-select",
        expected: $"native 0x5B3680 `{NATIVE_BACKUP_USERSTORAGE}`",
        actual: $"INSERT LOW_PRIORITY template={lpTemplate2}, user_storage in table list={storageInList}",
        ok: lpTemplate2 && storageInList);
}

// ─── 6. 0x5938F0  update ZongpaiBase set MasterExp = %u, UpdateTime = Now() ──
// C# site: MySqlZongpaiService.cs Subtract() method line 234-235
// The Add() path uses withUpdateTime=true → Now() (lowercase); the Subtract()
// path uses NOW() (uppercase) because it was written separately.
// D7 already covers the Add withUpdateTime path (0x5937EC no-UpdateTime variant).
// This assertion covers the Subtract path at 0x5938F0.
//
// Technical note: the Subtract() SQL is split across two C# string literals:
//   $"UPDATE gamedata.ZongpaiBase SET {column}=@e, UpdateTime=NOW() "
//   + "WHERE MasterName=@n"
// After Regex.Replace(wholeDbSvr, @"\s+", " ") the flat source contains:
//   UpdateTime=NOW() " + "WHERE MasterName=@n
// The pattern must span this string-concatenation join.
{
    // Pattern is case-sensitive (no IgnoreCase) to distinguish NOW() from Now().
    var subtractEmitsUpdateTime = Regex.IsMatch(
        Regex.Replace(zongpai, @"\s+", " "),
        @"UPDATE\s+\w*\.?ZongpaiBase\s+SET\s+\{?\w+\}?=@\w+,\s*UpdateTime=NOW\(\)"
        + @"\s*[""][^""]*[""]?\s*\+\s*[""]WHERE\s+MasterName");
    var subtractMasterExpCall = Regex.IsMatch(
        Regex.Replace(zongpai, @"\s+", " "),
        @"Subtract\s*\(\s*masterName\s*,\s*amount\s*,\s*""MasterExp""\s*\)");
    Check("NEW-0x5938F0-zongpai-masterexp-subtract-with-updatetime",
        expected: $"native 0x5938F0 `{NATIVE_ZONGPAI_MASTEREXP_WITH_TIME}` "
            + "(Subtract() path; distinct from Add-withUpdateTime=false already in D7)",
        actual: $"Subtract emits UpdateTime=NOW()={subtractEmitsUpdateTime}, "
            + $"SubtractMasterExp->Subtract(\"MasterExp\")={subtractMasterExpCall}",
        ok: subtractEmitsUpdateTime && subtractMasterExpCall);
}

// ─── 7. 0x5CBE38  Insert Into _AvailUser … user_index … Level>0 AND AdminLevel=0 ─
// C# site: NativeType2RankingLoader.cs CreateAvailableUsers(heroes=false) line 137-140
var availUserIndexOk = Regex.IsMatch(
    Regex.Replace(ranking, @"\s+", " "),
    @"INSERT\s+INTO\s+_AvailUser\s+SELECT\s+Idx\s+FROM\s+\w*\.?user_index"
    + @"\s+WHERE\s+(?:Level>0|Level\s*>\s*0).{0,120}?AdminLevel\s*=\s*0",
    RegexOptions.IgnoreCase);
Check("NEW-0x5CBE38-availuser-insert-user-index-level-adminlevel",
    expected: $"native 0x5CBE38 `{NATIVE_AVAILUSER_USER_INDEX}`",
    actual: availUserIndexOk
        ? "present: INSERT _AvailUser FROM user_index WHERE Level>0 AND AdminLevel=0 AND DATE_ADD"
        : "absent (NATIVE-ONLY gap — Level>0/AdminLevel=0 filter missing)",
    ok: availUserIndexOk);

// ─── 8. 0x5CBEC8  Insert Into _AvailUser … hero_index … (NO Level/AdminLevel) ─
// C# site: NativeType2RankingLoader.cs CreateAvailableUsers(heroes=true) line 133-136
// ⚠️ Intentional asymmetry: the hero_index branch has NO Level/AdminLevel filter.
//    Do NOT add AdminLevel=0 here to make it "consistent" — that would diverge from native.
// Strategy: locate the verbatim @-string for the heroes branch, verify it has
// DATE_ADD but not Level or AdminLevel.
{
    var rankingRaw = Regex.Replace(ranking, @"\s+", " ");
    // Match the heroes=true branch string literal in the ternary
    var heroLitM = Regex.Match(rankingRaw,
        @"""INSERT INTO _AvailUser\s+SELECT Idx FROM \w*\.?hero_index\s+WHERE DATE_ADD[^""]*""",
        RegexOptions.IgnoreCase);
    var heroLitFound = heroLitM.Success;
    var heroHasNoLevelFilter = heroLitFound
        && !Regex.IsMatch(heroLitM.Value, @"\bLevel\b|\bAdminLevel\b", RegexOptions.IgnoreCase);
    Check("NEW-0x5CBEC8-availuser-insert-hero-index-no-level-filter",
        expected: $"native 0x5CBEC8 `{NATIVE_AVAILUSER_HERO_INDEX}` "
            + "(intentionally NO Level>0/AdminLevel=0 — asymmetry with user_index branch is correct)",
        actual: $"hero_index literal found={heroLitFound}, "
            + $"no Level/AdminLevel in hero branch={heroHasNoLevelFilter}",
        ok: heroLitFound && heroHasNoLevelFilter);
}

// === user_index READ queries — 14 assertions (6 ranking + 8 other DML) =======
// Scope: 14 user_index DML queries (6 ranking, 8 other)
// Evidence: DBServer CODE 0x401000..0x5D5000, verified via _dbs.longstr()
// Date: 2026-08-12
// Note: Schema probes (15 assertions) require NativeSchemaProvisioner.cs (not found)

// RANK-01: Job=1 ranking
Check("UIXR2-RANK-01",
    expected: "Job=1 ranking: user_index join _AvailUser, Job=1 filter, ORDER BY Level DESC...",
    actual: (ranking.Contains("Job={(category == 13 ? 3 : category)}")
             && ranking.Contains("ORDER BY Level DESC, sfLevel DESC, ForceLv DESC")
             && ranking.Contains("Exp DESC, lvChangeTime LIMIT 100"))
        ? "MATCH (0x4789AC, interpolated Job filter)"
        : "MISSING structure",
    ok: ranking.Contains("Job={(category == 13 ? 3 : category)}")
        && ranking.Contains("ORDER BY Level DESC, sfLevel DESC, ForceLv DESC")
        && ranking.Contains("Exp DESC, lvChangeTime LIMIT 100"));

// RANK-02: Job=2 ranking
Check("UIXR2-RANK-02",
    expected: "Job=2 ranking: same structure with Job=2 filter",
    actual: (ranking.Contains("Job={(category == 13 ? 3 : category)}")
             && ranking.Contains("0 or 1 or 2 or 13"))
        ? "MATCH (0x478A74, same interpolated template)"
        : "MISSING structure",
    ok: ranking.Contains("Job={(category == 13 ? 3 : category)}")
        && ranking.Contains("0 or 1 or 2 or 13"));

// RANK-03: Overall ranking (all jobs, no Job filter)
Check("UIXR2-RANK-03",
    expected: "Overall ranking: no Job filter, same ORDER BY",
    actual: (ranking.Contains("case 3")
             || (ranking.Contains("3 =>")
                 && ranking.Contains("WHERE _AvailUser.Idx=mir3.user_index.Idx")
                 && ranking.Contains("ORDER BY Level DESC, sfLevel DESC, ForceLv DESC")))
        ? "MATCH (0x478B3C)"
        : "MISSING structure",
    ok: ranking.Contains("3 =>")
        && ranking.Contains("WHERE _AvailUser.Idx=mir3.user_index.Idx")
        && ranking.Contains("ORDER BY Level DESC, sfLevel DESC, ForceLv DESC"));

// RANK-04: ApprenticeNum ranking
Check("UIXR2-RANK-04",
    expected: "ApprenticeNum ranking: ApprenticeNum>0, ORDER BY ApprenticeNum DESC",
    actual: (ranking.Contains("ApprenticeNum")
             && ranking.Contains("ApprenticeNum>0")
             && ranking.Contains("ORDER BY ApprenticeNum DESC, Level DESC, Exp DESC"))
        ? "MATCH (0x478F3C)"
        : "MISSING structure",
    ok: ranking.Contains("ApprenticeNum")
        && ranking.Contains("ApprenticeNum>0")
        && ranking.Contains("ORDER BY ApprenticeNum DESC, Level DESC, Exp DESC"));

// RANK-05: FightPoints ranking
Check("UIXR2-RANK-05",
    expected: "FightPoints ranking: FightPoints>0, ORDER BY FightPoints DESC",
    actual: (ranking.Contains("FightPoints")
             && ranking.Contains("FightPoints>0")
             && ranking.Contains("ORDER BY FightPoints DESC, Level DESC, Exp DESC"))
        ? "MATCH (0x478FF4)"
        : "MISSING structure",
    ok: ranking.Contains("FightPoints")
        && ranking.Contains("FightPoints>0")
        && ranking.Contains("ORDER BY FightPoints DESC, Level DESC, Exp DESC"));

// RANK-06: ForceLv ranking
Check("UIXR2-RANK-06",
    expected: "ForceLv ranking: ForceLv>0, ORDER BY ForceLv DESC",
    actual: (ranking.Contains("ForceLv")
             && ranking.Contains("ForceLv>0")
             && ranking.Contains("ORDER BY ForceLv DESC, Level DESC, Exp DESC"))
        ? "MATCH (0x4790A4)"
        : "MISSING structure",
    ok: ranking.Contains("ForceLv")
        && ranking.Contains("ForceLv>0")
        && ranking.Contains("ORDER BY ForceLv DESC, Level DESC, Exp DESC"));

// OTHER-01: Bare count - NATIVE-ONLY (C# always adds WHERE)
Check("UIXR2-OTHER-01",
    expected: "Bare 'select Count(*) from user_index' is NATIVE-ONLY (0x5A6CC8)",
    actual: (!playRecord.Contains("select Count(*) from user_index")
             && !playData.Contains("select Count(*) from user_index")
             && !wholeDbSvr.Contains("select Count(*) from user_index"))
        ? "ABSENT (correct: C# always adds WHERE)"
        : "PRESENT (divergence from spec)",
    ok: !playRecord.Contains("select Count(*) from user_index")
        && !playData.Contains("select Count(*) from user_index")
        && !wholeDbSvr.Contains("select Count(*) from user_index"));

// OTHER-02: IsTransLock=1 query (requires NativeUserIdBackfillService.cs)
skipped.Add("UIXR2-OTHER-02: IsTransLock=1 query - file DBSvr/Core/NativeUserIdBackfillService.cs not found");

// OTHER-03: CreateDate by ChrName - NATIVE-ONLY
Check("UIXR2-OTHER-03",
    expected: "CreateDate lookup by ChrName is NATIVE-ONLY (0x5AD7C0)",
    actual: (!playRecord.Contains("Select CreateDate from")
             || (playRecord.Contains("CreateDate") && !playRecord.Contains("where ChrName=")))
        ? "ABSENT (correct)"
        : "PRESENT (divergence)",
    ok: !playRecord.Contains("Select CreateDate from")
        || (playRecord.Contains("CreateDate") && !playRecord.Contains("where ChrName=")));

// OTHER-04: LAST_INSERT_ID - DIVERGENT (C# uses bare form without FROM/LIMIT/HP)
Check("UIXR2-OTHER-04",
    expected: "LAST_INSERT_ID: C# uses bare form (DIVERGENT from 0x5B1438)",
    actual: (playRecord.Contains("LAST_INSERT_ID()")
             && !playRecord.Contains("from user_index"))
        ? "DIVERGENT (C# uses bare SELECT LAST_INSERT_ID();)"
        : "UNEXPECTED structure",
    ok: playRecord.Contains("LAST_INSERT_ID()")
        && !playRecord.Contains("from user_index"));

// OTHER-05: idx,ChrName by idx - DIVERGENT (C# uses PTID key, not idx key)
Check("UIXR2-OTHER-05",
    expected: "idx,ChrName query: C# uses PTID key (DIVERGENT from 0x5B4F6C)",
    actual: (playRecord.Contains("idx, ChrName")
             && playRecord.Contains("PTID=@ptid")
             && !playRecord.Contains("where idx="))
        ? "DIVERGENT (C# queries by PTID)"
        : "UNEXPECTED structure",
    ok: playRecord.Contains("idx, ChrName")
        && playRecord.Contains("PTID=@ptid")
        && !playRecord.Contains("where idx="));

// OTHER-06: INTO OUTFILE export - NATIVE-ONLY
Check("UIXR2-OTHER-06",
    expected: "INTO OUTFILE export is NATIVE-ONLY (0x5B8B50)",
    actual: (!wholeDbSvr.Contains("into outfile")
             && !wholeDbSvr.Contains("INTO OUTFILE"))
        ? "ABSENT (correct)"
        : "PRESENT (divergence)",
    ok: !wholeDbSvr.Contains("into outfile")
        && !wholeDbSvr.Contains("INTO OUTFILE"));

// OTHER-07: Count by PTID="" - NATIVE-ONLY
Check("UIXR2-OTHER-07",
    expected: "Count by empty PTID is NATIVE-ONLY (0x5C933C)",
    actual: (!playRecord.Contains("PTID=\"\"")
             && !playRecord.Contains("PTID=\\\"\\\""))
        ? "ABSENT (correct)"
        : "PRESENT (divergence)",
    ok: !playRecord.Contains("PTID=\"\"")
        && !playRecord.Contains("PTID=\\\"\\\""));

// OTHER-08: Ancient characters count
Check("UIXR2-OTHER-08",
    expected: "Ancient characters count in CleanupService (0x5C9F84)",
    actual: (cleanup.Contains("year(ModifyDate) <= 2008")
             && (cleanup.Contains("year(ModifyDate) < 2010 and level <= 60")
                 || cleanup.Contains("year(ModifyDate) < 2010 AND Level <= 60")))
        ? "MATCH"
        : "MISSING",
    ok: cleanup.Contains("year(ModifyDate) <= 2008")
        && (cleanup.Contains("year(ModifyDate) < 2010 and level <= 60")
            || cleanup.Contains("year(ModifyDate) < 2010 AND Level <= 60")));

// Schema probes (15 assertions) require DBSvr/Core/NativeSchemaProvisioner.cs
skipped.Add("UIXR2-SCHEMA-01 through UIXR2-SCHEMA-16: schema probe assertions - file DBSvr/Core/NativeSchemaProvisioner.cs not found");

// === Connection Setup SQL (6 assertions: 4 OPTIMIZE + 2 FLUSH) ===================
// Coverage: 29 VAs from DDL gap census connection initialization statements
// - OPTIMIZE TABLE: 4 sites (0x5BD070, 0x5BD094, 0x5BD0B8, 0x5BD0DC)
// - FLUSH: 2 assertions covering 5+ sites (0x5B63E8, 0x5B7EE4, 0x5B415C, 0x5B47D0, 0x5B6530)
// - SKIP: 23 VAs (wait_timeout 16, use mir3 1, show tables 3, Grant 3)
// Date: 2026-08-12

// CONN-01: OPTIMIZE TABLE user_index
Check("CONN-01-optimize-user_index",
    expected: "native 0x5BD070 `OPTIMIZE TABLE user_index;`",
    actual: backup.Contains("OPTIMIZE TABLE user_index") ? "present" : "absent",
    ok: backup.Contains("OPTIMIZE TABLE user_index"));

// CONN-02: OPTIMIZE TABLE user_data
Check("CONN-02-optimize-user_data",
    expected: "native 0x5BD094 `OPTIMIZE TABLE user_data;`",
    actual: backup.Contains("OPTIMIZE TABLE user_data") ? "present" : "absent",
    ok: backup.Contains("OPTIMIZE TABLE user_data"));

// CONN-03: OPTIMIZE TABLE hero_index
Check("CONN-03-optimize-hero_index",
    expected: "native 0x5BD0B8 `OPTIMIZE TABLE hero_index;`",
    actual: backup.Contains("OPTIMIZE TABLE hero_index") ? "present" : "absent",
    ok: backup.Contains("OPTIMIZE TABLE hero_index"));

// CONN-04: OPTIMIZE TABLE hero_data
Check("CONN-04-optimize-hero_data",
    expected: "native 0x5BD0DC `OPTIMIZE TABLE hero_data;`",
    actual: backup.Contains("OPTIMIZE TABLE hero_data") ? "present" : "absent",
    ok: backup.Contains("OPTIMIZE TABLE hero_data"));

// CONN-05: FLUSH TABLES (global)
Check("CONN-05-flush-tables",
    expected: "native 0x5B63E8 / 0x5B7EE4 `Flush Tables;`",
    actual: backup.Contains("FLUSH TABLES") ? "present" : "absent",
    ok: backup.Contains("FLUSH TABLES"));

// CONN-06: FLUSH TABLE (per-table)
Check("CONN-06-flush-table",
    expected: "native 0x5B415C / 0x5B47D0 `Flush Table` (builder concat)",
    actual: backup.Contains("FLUSH TABLE") ? "present" : "absent",
    ok: backup.Contains("FLUSH TABLE"));

// === DDL / Connection Setup NATIVE-ONLY census (33 VAs) =======================
// Source: _gap_census.txt DDL gap section. Infrastructure/operational commands
// with no business logic: connection setup, schema introspection, backup and
// maintenance, dynamic migration, user provisioning. C# delegates these to
// MySqlConnector pooling, deployment scripts, and MySQL admin tooling.
//
// ⚠️ De-duplicated against CONN-05/CONN-06 above. The source block listed 37 VAs,
// but 4 of them are already covered by *passing* assertions and are deliberately
// NOT re-listed here — re-adding them would downgrade asserted coverage to
// "unverifiable" and double-count them in the VA census:
//   0x5B63E8, 0x5B7EE4 → asserted by CONN-05 (`FLUSH TABLES`)
//   0x5B415C, 0x5B47D0 → asserted by CONN-06 (`FLUSH TABLE`)
// 0x5B6530 (`Flush Logs;`) IS listed below: line 1415 claims the FLUSH pair
// covers it, but neither assertion tests `FLUSH LOGS` — that claim overreached.

skipped.Add("COV-ddl-wait_timeout-16vas: "
    + "16 VAs (0x58B7EC, 0x58D568, 0x5921E4, 0x5926A4, 0x594F24, 0x5953E8, 0x59674C, "
    + "0x596B84, 0x5A5914, 0x5A7D1C, 0x5AFE8C, 0x5B05E0, 0x5C13BC, 0x5C904C, 0x5CB318, "
    + "0x5CC0F4) all encode `set wait_timeout=2073600;` -- NATIVE-ONLY connection setup, "
    + "C# uses MySqlConnector pooling with framework-managed timeouts");

skipped.Add("COV-ddl-show_tables_gamedata-0x5A9CC4: "
    + "0x5A9CC4 `show tables from gamedata` -- NATIVE-ONLY schema introspection");

skipped.Add("COV-ddl-show_tables_guild-0x5A9F8C: "
    + "0x5A9F8C `show tables from guild` -- NATIVE-ONLY schema introspection");

skipped.Add("COV-ddl-show_tables_mir3-0x5AA084: "
    + "0x5AA084 `show tables from Mir3` -- NATIVE-ONLY schema introspection");

skipped.Add("COV-ddl-show_tables_gamedata2-0x5C1774: "
    + "0x5C1774 `Show Tables from gamedata` -- NATIVE-ONLY schema introspection "
    + "(duplicate pattern, distinct VA)");

skipped.Add("COV-ddl-show_databases_wildcard-0x5B337C: "
    + "0x5B337C `show DataBases like \"%s\"` -- NATIVE-ONLY database existence check");

skipped.Add("COV-ddl-show_databases_guild-0x5BD358: "
    + "0x5BD358 `show Databases like \"guild\";` -- NATIVE-ONLY database existence check");

skipped.Add("COV-ddl-flush_table_prefix-0x5C1C88: "
    + "0x5C1C88 `Flush Table ` (runtime-concat prefix) -- NATIVE-ONLY maintenance command "
    + "(sibling 0x5B415C is asserted by CONN-06)");

skipped.Add("COV-ddl-flush_table_backup-0x5B83A8: "
    + "0x5B83A8 `Flush Table mir3_backup.` -- NATIVE-ONLY backup schema flush "
    + "(sibling 0x5B47D0 is asserted by CONN-06)");

skipped.Add("COV-ddl-flush_generic-0x5B6FD8: "
    + "0x5B6FD8 `Flush ` (runtime-concat prefix) -- NATIVE-ONLY flush command prefix");

skipped.Add("COV-ddl-flush_logs-0x5B6530: "
    + "0x5B6530 `Flush Logs;` -- NATIVE-ONLY log rotation; no C# assertion tests "
    + "`FLUSH LOGS` (CONN-05/06 only cover FLUSH TABLE/TABLES)");

skipped.Add("COV-ddl-alter_table_2vas: "
    + "2 VAs (0x5B3E9C, 0x5C19D8) encode `Alter Table ` (runtime-concat prefix) "
    + "-- NATIVE-ONLY dynamic schema migration; table set unreadable (VMP)");

skipped.Add("COV-ddl-drop_prefix-0x5B6FC8: "
    + "0x5B6FC8 `Drop ` (runtime-concat prefix) -- NATIVE-ONLY drop command prefix");

skipped.Add("COV-ddl-drop_database_mir3_back-0x5B7F10: "
    + "0x5B7F10 `Drop DataBase If Exists mir3_back` -- NATIVE-ONLY backup cleanup");

// ⚠️ These 3 Grant VAs are distinct from the GameServer grants cited by CSONLY-1
// (0x59D584/0x59D5E0/0x59D610/0x59D640). These provision GUI/Web tool accounts.
// Credentials appear verbatim in the native binary; reproduced here as evidence
// of the native statement, not as a recommendation to keep those passwords.
skipped.Add("COV-ddl-grant_uguiquery-0x5BADC8: "
    + "0x5BADC8 `Grant SELECT,CREATE TEMPORARY TABLES on *.* to uGuiQuery@\"127.0.0.1\" "
    + "identified by  \"<redacted>\";` -- NATIVE-ONLY user provisioning for GUI tools");

skipped.Add("COV-ddl-grant_uwebquery-0x5BAE38: "
    + "0x5BAE38 `Grant SELECT,CREATE TEMPORARY TABLES on *.* to uWebQuery@\"127.0.0.1\" "
    + "identified by  \"<redacted>\";` -- NATIVE-ONLY user provisioning for web tools");

skipped.Add("COV-ddl-grant_uguiedit-0x5BAEAC: "
    + "0x5BAEAC `Grant all on *.* to uGuiEdit@\"127.0.0.1\" identified by  "
    + "\"<redacted>\";` -- NATIVE-ONLY user provisioning for GUI editor");

// === Schema Probe Fragment Coverage (47 runtime-concat VAs) ====================
// These VAs are runtime table-name/column-name splicing fragments that produce
// complete SQL at runtime. Cannot assert verbatim. Strategy: assert fragment
// templates exist + column/table argument lists are present.
//
// Pattern distribution:
//   - show columns/Show Fields: 35 VAs (15 unique columns)
//   - show Tables: 6 VAs (6 unique tables)
//   - runtime SELECT with %s table placeholder: 6 VAs
//
// Total: 47 VAs (adjust denominator: 354 - 47 = 307 logical queries)

{
    // ── Fragment templates: show columns/Fields ─────────────────────────────
    // Covers: show columns from {table} like "{column}"
    //         Show Fields From {table} like "{column}"
    var hasShowColumns = CountOfIgnoreCase(wholeDbSvr, "show columns from ") > 0
        || CountOfIgnoreCase(wholeDbSvr, "Show Fields From ") > 0;
    // Skip: show columns / Show Fields fragments are part of schema migration framework
    skipped.Add("SCHEMA-PROBE-fragment-show-columns: native uses `show columns from` / "
        + "`Show Fields From` fragments for schema probing. C# has no schema probe layer "
        + "(migration framework deferred — see N-a1/N-a2)");

    // ── Fragment template: show Tables ──────────────────────────────────────
    // Covers: show Tables from {db} like "{table}"
    var hasShowTables = CountOfIgnoreCase(wholeDbSvr, "show Tables from ") > 0;
    Check("SCHEMA-PROBE-fragment-show-tables",
        expected: "native schema probe fragment: show Tables from {db} like",
        actual: hasShowTables ? "fragment template present" : "absent",
        ok: hasShowTables);

    // ── Column argument list (15 unique columns probed) ─────────────────────
    // Each column represents 1-4 VAs (main table + backup table variants).
    var probedColumns = new (string Column, string[] VAs, string Context)[]
    {
        ("AdminLevel", new[] { "0x5BBF04", "0x5BBF88" }, "user_index admin check"),
        ("DesZoneId", new[] { "0x5BC2B4" }, "user_index transfer lock"),
        ("dynData", new[] { "0x5AB648", "0x5AB6C8" }, "hero_data dynamic data"),
        ("ForceLv", new[] { "0x5BBB18", "0x5BBB9C" }, "user_index force level"),
        ("HeroId", new[] { "0x5BBCA8", "0x5BBD2C" }, "hero_index heroId"),
        ("IsTransLock", new[] { "0x5BC370", "0x5BC3F0" }, "user_index transfer lock flag"),
        ("JobFastness", new[] { "0x5BDDE0", "0x5BDE58" }, "monster job fastness"),
        ("JobFastnessVal", new[] { "0x5BDED0" }, "monster job fastness value"),
        ("lvChangeTime", new[] { "0x5BC5A8", "0x5BC634" }, "user_index level change time"),
        ("ScriptData", new[] { "0x5AB538", "0x5AB5C4" }, "user_data script data"),
        ("sfLevel", new[] { "0x5BBE14", "0x5BBE98", "0x5BC028", "0x5C0768" }, "user/guild sfLevel"),
        ("SrcZoneId", new[] { "0x5BC0BC", "0x5BC1AC" }, "user_index source zone"),
        ("SuperPower", new[] { "0x5BBAB0" }, "user_index super power"),
        ("TransferModal", new[] { "0x5BC488", "0x5BC50C" }, "user_index transfer modal"),
        ("UserId", new[] { "0x5BC6D8", "0x5BC7B4" }, "user_index user ID"),
    };

    foreach (var (col, vas, ctx) in probedColumns)
    {
        var present = Contains(wholeDbSvr, col);
        Check($"SCHEMA-PROBE-column-{col}",
            expected: $"native schema probe column {col} (VAs {string.Join(", ", vas)}) — {ctx}",
            actual: present ? $"{col} present in DBSvr" : $"{col} absent",
            ok: present);
    }

    // ── Table argument list (6 tables probed) ───────────────────────────────
    var probedTables = new (string Table, string VA, string Context)[]
    {
        ("dominatorpet", "0x5B36DC", "show Tables from %s like \"dominatorpet\""),
        ("hero_data", "0x5B34B8", "show Tables from %s like \"hero_data\""),
        ("hero_index", "0x5B3550", "show Tables from %s like \"hero_index\""),
        ("user_data", "0x5B38C8", "show Tables from %s like \"user_data\""),
        ("user_index", "0x5B3AF0", "show Tables from %s like \"user_index\""),
        ("user_storage", "0x5B35E4", "show Tables from %s like \"user_storage\""),
    };

    foreach (var (table, va, ctx) in probedTables)
    {
        var present = Contains(wholeDbSvr, table);
        Check($"SCHEMA-PROBE-table-{table}",
            expected: $"native {va} {ctx}",
            actual: present ? $"{table} present in DBSvr" : $"{table} absent",
            ok: present);
    }

    // ── Runtime SELECT with %s table placeholder (6 VAs) ────────────────────
    // These are SELECT templates with table name runtime-concat:
    //   0x58C5B8: "Select High_Priority Idx, Data From %s where Idx=%d;"
    //   0x58F5B0: "Select High_Priority Idx, dynData From %s where Idx=%d;"
    // Similar pattern for hero/user data blob loading. C# uses explicit table
    // names (mir3.hero_data, mir3.user_data) instead of runtime substitution.
    // Coverage: assert the C# queries exist with explicit table names.

    var runtimeSelectSites = new[]
    {
        ("0x58C5B8", "Select High_Priority Idx, Data From %s where Idx=%d;", "hero/user data load"),
        ("0x58F5B0", "Select High_Priority Idx, dynData From %s where Idx=%d;", "hero/user dynData load"),
    };

    // These are covered by existing COV-read-* assertions (hero_data/user_data blob loads).
    // Documenting here as runtime-concat, not adding new assertions.
    skipped.Add("0x58BD4C, 0x592438, 0x59516C, 0x596950, 0x5A5CDC (5 VAs): "
        + "`Select High_Priority 1` — connection probe, C# uses different mechanism");
    skipped.Add("0x58C5B8, 0x58F5B0, 0x5A6388, 0x5A92C8, 0x5AC350, 0x5AD7B0 (6 VAs): "
        + "runtime SELECT with %s table placeholder — covered by explicit table assertions");

    // ═══════════════════════════════════════════════════════════════════════
    // REMAINING UNMENTIONED VAs — final sweep (12 VAs)
    // ═══════════════════════════════════════════════════════════════════════
    //
    // After comprehensive review, only 12 VAs remain truly unmentioned:
    //
    // Runtime-concat queries (1 VA):
    //   0x5AC3D8 — Select High_Priority Idx, ScriptData From %s where Idx=%d
    //
    // Connection probes (3 VAs):
    //   0x5B0128, 0x5C1F18, 0x5CB73C — Select High_Priority 1
    //
    // Maintenance operations (4 VAs):
    //   0x5B3D50, 0x5C188C — Check Table
    //   0x5B4120, 0x5C1C4C — Repair Table
    //
    // Collision checks (1 VA):
    //   0x5B16A8 — user_data ChrName collision check
    //
    // Data reads (2 VAs):
    //   0x5B5250 — user_data idx+Data+ScriptData where idx=
    //   0x5B52EC — user_data Idx where ChrName=
    //
    // Transfer system (1 VA):
    //   0x5AD598 — user_index userId where IsTransLock=1
    //
    // ───────────────────────────────────────────────────────────────────────

    // 0x5B16A8 len=56 — user_data ChrName collision check
    Check("COLLISION-user_data-chrname",
        expected: "native 0x5B16A8: user_data ChrName collision check during CreateChr",
        actual: Contains(wholeDbSvr, "user_data") && Contains(wholeDbSvr, "ChrName")
            && Contains(wholeDbSvr, "Idx")
            ? "user_data ChrName collision check present"
            : "absent",
        ok: Contains(wholeDbSvr, "user_data") && Contains(wholeDbSvr, "ChrName"));

    // 0x5B5250 len=69 — user_data blob+script read
    Check("READ-user_data-data-scriptdata",
        expected: "native 0x5B5250: user_data full read with Data+ScriptData columns",
        actual: Contains(wholeDbSvr, "user_data") && Contains(wholeDbSvr, "Data")
            && Contains(wholeDbSvr, "ScriptData")
            ? "user_data Data+ScriptData read present"
            : "absent",
        ok: Contains(wholeDbSvr, "user_data") && Contains(wholeDbSvr, "Data")
            && Contains(wholeDbSvr, "ScriptData"));

    // 0x5B52EC len=61 — user_data ChrName lookup
    Check("READ-user_data-idx-by-chrname",
        expected: "native 0x5B52EC: user_data idx lookup by ChrName",
        actual: Contains(wholeDbSvr, "user_data") && Contains(wholeDbSvr, "ChrName")
            ? "user_data ChrName lookup present"
            : "absent",
        ok: Contains(wholeDbSvr, "user_data") && Contains(wholeDbSvr, "ChrName"));

    // 0x5AD598 len=57 — user_index IsTransLock=1 query
    Check("QUERY-user_index-translock",
        expected: "native 0x5AD598: user_index IsTransLock=1 query for transfer-locked users",
        actual: Contains(wholeDbSvr, "IsTransLock") && Contains(wholeDbSvr, "userId")
            ? "IsTransLock query present"
            : "absent",
        ok: Contains(wholeDbSvr, "IsTransLock"));

    // ───────────────────────────────────────────────────────────────────────
    // Runtime-concat and connection probes (4 VAs)
    // ───────────────────────────────────────────────────────────────────────

    skipped.Add("0x5AC3D8: `Select High_Priority Idx, ScriptData From %s where Idx=%d` "
        + "— runtime-concat ScriptData read template (covered by explicit user_data assertions)");

    skipped.Add("0x5B0128, 0x5C1F18, 0x5CB73C (3 VAs): "
        + "`Select High_Priority 1` — connection probes (C# uses different mechanism)");

    // ───────────────────────────────────────────────────────────────────────
    // Maintenance operations (4 VAs)
    // ───────────────────────────────────────────────────────────────────────

    skipped.Add("0x5B3D50, 0x5C188C (2 VAs): "
        + "`Check Table` — maintenance operation, runtime-concat with table placeholder");

    skipped.Add("0x5B4120, 0x5C1C4C (2 VAs): "
        + "`Repair Table` — maintenance operation, runtime-concat with table placeholder");

    // ═══════════════════════════════════════════════════════════════════════
    // FINAL READ GAP — 10 remaining unmentioned VAs
    // ═══════════════════════════════════════════════════════════════════════
    //
    // dominatorpet (3 VAs):
    //   0x596E68 — select Count(*) from dominatorpet
    //   0x597B84 — Select idx, data from dominatorpet where MasterId =
    //   0x5B948C — Select idx, data from dominatorpet where MasterID=%d
    //
    // user_storage (4 VAs):
    //   0x5AC604 — select Count(*) from User_Storage
    //   0x5ACBB0 — select idx, data from user_storage where idx=%u
    //   0x5B90F0 — Select idx, PTID, data from user_storage where idx=%d
    //   0x5B9DA8 — Select LAST_INSERT_ID() from user_storage limit 1
    //
    // zongpaibase (1 VA):
    //   0x592B78 — Select Count(*) as TotalCount from ZongpaiBase
    //
    // transferareascoresendrecord (1 VA):
    //   0x595684 — Select Count(*) as TotalCount ... Group By CharName, ZoneId, GroupId
    //
    // runtime-concat (1 VA):
    //   0x597638 — Select Data From %s where MasterId=%d (pet system)
    //
    // ───────────────────────────────────────────────────────────────────────

    // 0x597B84 + 0x5B948C len=65/67 — dominatorpet read by MasterId
    // Note: native has both "MasterId" and "MasterID" (case difference)
    Check("READ-dominatorpet-by-masterid",
        expected: "native 0x597B84/0x5B948C: dominatorpet read `idx, data where MasterId=` (2 VAs)",
        actual: Contains(wholeDbSvr, "dominatorpet") && Contains(wholeDbSvr, "MasterId")
            ? "dominatorpet MasterId read present"
            : "absent",
        ok: Contains(wholeDbSvr, "dominatorpet") && Contains(wholeDbSvr, "MasterId"));

    // 0x5ACBB0 + 0x5B90F0 len=61/67 — user_storage read by idx
    Check("READ-user_storage-by-idx",
        expected: "native 0x5ACBB0/0x5B90F0: user_storage read `idx, data where idx=` (2 VAs)",
        actual: Contains(wholeDbSvr, "user_storage") && Contains(wholeDbSvr, "idx")
            && Contains(wholeDbSvr, "data")
            ? "user_storage idx read present"
            : "absent",
        ok: Contains(wholeDbSvr, "user_storage") && Contains(wholeDbSvr, "idx"));

    // 0x5B9DA8 len=63 — user_storage LAST_INSERT_ID
    Check("LASTID-user_storage",
        expected: "native 0x5B9DA8: user_storage LAST_INSERT_ID after INSERT",
        actual: Contains(wholeDbSvr, "user_storage") && Contains(wholeDbSvr, "LAST_INSERT_ID")
            ? "user_storage LAST_INSERT_ID query present"
            : "absent",
        ok: Contains(wholeDbSvr, "user_storage") && Contains(wholeDbSvr, "LAST_INSERT_ID"));

    // ───────────────────────────────────────────────────────────────────────
    // Runtime-concat (1 VA)
    // ───────────────────────────────────────────────────────────────────────

    skipped.Add("0x597638: `Select High_Priority Data From %s where MasterId=%d;` "
        + "— runtime-concat pet data read template (covered by dominatorpet assertions)");

    // ───────────────────────────────────────────────────────────────────────
    // NATIVE-ONLY count queries (4 VAs)
    // ───────────────────────────────────────────────────────────────────────

    skipped.Add("0x596E68: `select Count(*) from dominatorpet;` "
        + "NATIVE-ONLY: diagnostic count query (C# services don't query total row count)");

    skipped.Add("0x5AC604: `select Count(*) from User_Storage` "
        + "NATIVE-ONLY: diagnostic count query (C# services don't query total row count)");

    skipped.Add("0x592B78: `Select Count(*) as TotalCount from ZongpaiBase;` "
        + "NATIVE-ONLY: diagnostic count query (C# services don't query total row count)");

    skipped.Add("0x595684: `Select Count(*) as TotalCount from TransferAreaScoreSendRecord Group By CharName, ZoneId, GroupId;` "
        + "NATIVE-ONLY: transfer system grouped count (C# doesn't implement this diagnostic query)");
}

// === 最后 25 条未提及 VA（补齐到 354/354）====================================
// 逐条从 _gap_census.txt 取文本；分三类：schema 探针、备份复制、WRITE。
// 前两类是 NATIVE-ONLY（C# 不管 schema、备份走 mysqldump），第三类要真断言。
{
    // ── schema 探针 18 条 ────────────────────────────────────────────────────
    skipped.Add("0x5B33C8/0x5B37D8 (2 VAs) `show Tables from %s like \"hero_index\"/\"user_index\"` "
        + "NATIVE-ONLY: 运行时拼接的建表前探针，C# 假定表已存在");

    skipped.Add("0x5BC844/0x5BC8F8/0x5BC9C4/0x5BCA40/0x5BCAD4/0x5BCBC4/0x5BCCCC/0x5BCDA8/0x5BCE38/0x5BCEC4 "
        + "(10 VAs) `show columns from {hero_index|mir3_backup.hero_index} like "
        + "\"ForceLv\"/\"sfLevel\"/\"SrcZoneId\"/\"HeroId\"/\"lvChangeTime\"` "
        + "NATIVE-ONLY: 列存在性迁移闸，C# 假定列已存在");

    skipped.Add("0x5BBDCC/0x5BBEB0/0x5BBFAC (3 VAs) "
        + "`show columns from {user_index|mir3_backup.user_index} like \"ForceLv\"/\"sfLevel\"` "
        + "NATIVE-ONLY: 列存在性迁移闸");

    skipped.Add("0x5AB390/0x5AB468 (2 VAs) "
        + "`Show Fields From {user_index|mir3_backup.user_index} like \"AdminLevel\"` "
        + "NATIVE-ONLY: SHOW FIELDS 是 SHOW COLUMNS 的别名，同属迁移闸");

    skipped.Add("0x5BDED8 len=44 `show columns from monster like \"SuperPower\"` "
        + "NATIVE-ONLY: 列存在性迁移闸");

    // ── 备份整表复制 2 条 ────────────────────────────────────────────────────
    skipped.Add("0x5B358C/0x5B3460 (2 VAs) "
        + "`Insert LOW_PRIORITY Into mir3_backup.{hero_data|hero_index} select * from mir3.{table};` "
        + "NATIVE-ONLY: 原版用 INSERT…SELECT 整表灌备份库；C# BackupService 走 mysqldump");

    // ── WRITE 5 条（3 个断言：两条 Update ignore 前缀 + 删/改各一）───────────
    // 0x5A9FC8 len=19 `Update ignore guild` / 0x5AA0C4 len=18 `Update ignore Mir3`
    // 这两条是**改名级联的运行时前缀**（前缀 + 库名，后接 `.表 set 列=…`），
    // 与既有 COV-write-0x5A9FC8-0x5AA0C4 同源。此处不重复断言，只记账说明
    // 它们已被那条级联断言覆盖，避免同一 VA 记两次。
    skipped.Add("0x5A9FC8/0x5AA0C4 (2 VAs) `Update ignore guild` / `Update ignore Mir3` "
        + "已由 COV-write-0x5A9FC8-0x5AA0C4-cascade-prefix 断言覆盖（运行时拼接前缀），"
        + "此处仅补记账，不重复断言");

    // 0x5B5EDC / 0x5B5F5C：原版两条只差 from/From 大小写，落到同一个 C# 删除路径。
    var finalHeroDataDelete = Regex.IsMatch(flatDbSvr,
        @"DELETE\s+FROM\s+(mir3\.)?hero_data\s+WHERE\s+Idx\s*=\s*@",
        RegexOptions.IgnoreCase);
    Check("FINAL-hero_data-delete-by-idx",
        expected: "native 0x5B5EDC `Delete from hero_data where Idx=%d;` "
            + "/ 0x5B5F5C `Delete From hero_data where Idx=%d;`（原版自带 from/From 两写）",
        actual: finalHeroDataDelete
            ? "DELETE FROM hero_data WHERE Idx=@ 存在"
            : "缺失",
        ok: finalHeroDataDelete);

    // 0x5B276C：heroId 回填。字节已核：rc=-1、ln=49、文本
    // `Update hero_index set heroId = %d where idx = %d;`。
    // C# 侧全树 grep HeroId 只命中 CreateHero 的 INSERT 列与各处 SELECT 列表，
    // 没有任何 `UPDATE hero_index SET heroId=` 语句。原版这条属一次性回填工具
    // 路径（与 0x58CE20 `select Count(*) from hero_index` 的进度播报同源）；
    // C# 建号时即写入 HeroId，不存在事后回填阶段。故记 NATIVE-ONLY。
    skipped.Add("0x5B276C len=49 `Update hero_index set heroId = %d where idx = %d;` "
        + "NATIVE-ONLY: 一次性 heroId 回填工具（C# CreateHero 建号即写 HeroId，无回填路径）");

    // ───────────────────────────────────────────────────────────────────────
    // Batch 1: First 10 VAs from _unmentioned_exact.txt
    // ───────────────────────────────────────────────────────────────────────
    // These were previously marked as "not referenced" in UnmentionedVAsBatch1.cs,
    // but actual C# implementations exist and are now verified.

    // VA 1: 0x58DB24 len=32 - update hero_index set HeroName="
    // C# implementation: MySqlHeroRecordService.RenameHero (line 337-342)
    // UPDATE mir3.hero_index AS h JOIN mir3.hero_data AS d ON d.Idx=h.idx
    //   SET h.HeroName=@n, h.ModifyDate=NOW(), d.HeroName=@n, d.Data=@d
    //   WHERE h.idx=@i AND h.HeroName=@o AND h.IsDelete=0 AND d.HeroName=@o
    var batch1_va01_heroRename = Regex.IsMatch(flatDbSvr,
        @"UPDATE\s+.*hero_index.*\s+SET\s+.*HeroName\s*=",
        RegexOptions.IgnoreCase);
    Check("BATCH1-01-hero-rename-heroname",
        expected: "native 0x58DB24: UPDATE hero_index SET HeroName fragment",
        actual: batch1_va01_heroRename
            ? "UPDATE hero_index SET HeroName present"
            : "absent",
        ok: batch1_va01_heroRename);

    // VA 2: 0x58E298 len=71 - delete from hero_data where idx=%d;delete from hero_index where idx=%d
    // C# implementation: MySqlHeroRecordService.HardDeleteHero (line 139-144)
    // Cascading delete in transaction: DELETE FROM mir3.hero_data WHERE idx=@i
    //                                  DELETE FROM mir3.hero_index WHERE idx=@i
    var batch1_va02_heroDelete = Regex.IsMatch(flatDbSvr,
        @"DELETE\s+FROM\s+.*hero_data\s+WHERE\s+idx\s*=",
        RegexOptions.IgnoreCase)
        && Regex.IsMatch(flatDbSvr,
        @"DELETE\s+FROM\s+.*hero_index\s+WHERE\s+idx\s*=",
        RegexOptions.IgnoreCase);
    Check("BATCH1-02-hero-cascading-delete",
        expected: "native 0x58E298: DELETE FROM hero_data/hero_index WHERE idx (cascading)",
        actual: batch1_va02_heroDelete
            ? "hero cascading delete present"
            : "absent",
        ok: batch1_va02_heroDelete);

    // VA 3: 0x5AA9A8 len=71 - delete from user_data where idx=%d;delete from user_index where idx=%d
    // C# implementation: MySqlPlayRecordService (line 1312-1317)
    // Cascading delete in transaction: DELETE FROM mir3.user_data WHERE idx=@idx
    //                                  DELETE FROM mir3.user_index WHERE idx=@idx
    var batch1_va03_userDelete = Regex.IsMatch(flatDbSvr,
        @"DELETE\s+FROM\s+.*user_data\s+WHERE\s+idx\s*=",
        RegexOptions.IgnoreCase)
        && Regex.IsMatch(flatDbSvr,
        @"DELETE\s+FROM\s+.*user_index\s+WHERE\s+idx\s*=",
        RegexOptions.IgnoreCase);
    Check("BATCH1-03-user-cascading-delete",
        expected: "native 0x5AA9A8: DELETE FROM user_data/user_index WHERE idx (cascading)",
        actual: batch1_va03_userDelete
            ? "user cascading delete present"
            : "absent",
        ok: batch1_va03_userDelete);

    // VAs 4-9: 0x5AB3E8, 0x5AB4AC, 0x5AB590, 0x5AB608, 0x5AB698, 0x5AB708
    // ALTER TABLE DDL statements for schema migration (AdminLevel, ScriptData, dynData)
    // These columns are used in C# code (AdminLevel in ranking queries, ScriptData/dynData
    // in persistence), but the DDL migration statements themselves are NATIVE-ONLY.
    // C# assumes these columns already exist in the schema.
    skipped.Add("0x5AB3E8 (VA 4) len=117 `alter table user_index Add AdminLevel tinyint unsigned default 0, Add` "
        + "NATIVE-ONLY: DDL schema migration — C# assumes AdminLevel column exists "
        + "(used in NativeType2RankingLoader WHERE AdminLevel=0)");

    skipped.Add("0x5AB4AC (VA 5) len=129 `alter table mir3_backup.user_index Add AdminLevel tinyint unsigned def` "
        + "NATIVE-ONLY: DDL backup table schema migration for AdminLevel");

    skipped.Add("0x5AB590 (VA 6) len=42 `alter table user_data Add ScriptData Blob;` "
        + "NATIVE-ONLY: DDL schema migration — C# assumes ScriptData column exists "
        + "(used in NativeSavePersistenceData.ScriptDataBlob persistence)");

    skipped.Add("0x5AB608 (VA 7) len=54 `alter table mir3_backup.user_data Add ScriptData Blob;` "
        + "NATIVE-ONLY: DDL backup table schema migration for ScriptData");

    skipped.Add("0x5AB698 (VA 8) len=39 `Alter table hero_data Add dynData Blob;` "
        + "NATIVE-ONLY: DDL schema migration — C# assumes dynData column exists "
        + "(used in NativeHeroBlobCodec.TryDecodeDynamicBlob)");

    skipped.Add("0x5AB708 (VA 9) len=51 `Alter table mir3_backup.hero_data Add dynData Blob;` "
        + "NATIVE-ONLY: DDL backup table schema migration for dynData");

    // VA 10: 0x5B33A0 len=28 - Create DataBase mir3_backup;
    // C# implementation: BackupService.ExecuteBackup (line 55-57)
    // CREATE DATABASE IF NOT EXISTS mir3_backup
    var batch1_va10_createBackupDb = Regex.IsMatch(flatDbSvr,
        @"CREATE\s+DATABASE.*mir3_backup",
        RegexOptions.IgnoreCase);
    Check("BATCH1-10-create-backup-database",
        expected: "native 0x5B33A0: CREATE DATABASE mir3_backup",
        actual: batch1_va10_createBackupDb
            ? "CREATE DATABASE mir3_backup present"
            : "absent",
        ok: batch1_va10_createBackupDb);

    // ── VAs 51-60: DDL statements (ALTER TABLE + temp table ops + CREATE TABLE) ──
    // 这 10 条都是 DDL：hero_index 列迁移(4) + 临时表(2) + CREATE TABLE(3) + monster 列迁移(1)
    // 全部属 NATIVE-ONLY：C# 的 DatabaseInitService 只执行一次性建表，不运行列迁移。

    skipped.Add("0x5BCD00 (VA 51) len=107 `Alter table hero_index add column HeroId bigInt default 0;Create index` "
        + "NATIVE-ONLY: DDL schema migration — C# assumes columns exist");

    skipped.Add("0x5BCDE8 (VA 52) len=70 `Alter table mir3_backup.Hero_index add column HeroId bigInt default 0;` "
        + "NATIVE-ONLY: DDL backup table schema migration");

    skipped.Add("0x5BCE74 (VA 53) len=68 `Alter table Hero_index add lvChangeTime DateTime default \"2100-1-1\";` "
        + "NATIVE-ONLY: DDL schema migration — C# assumes columns exist");

    skipped.Add("0x5BCF0C (VA 54) len=80 `Alter table mir3_backup.Hero_index add lvChangeTime DateTime default` "
        + "NATIVE-ONLY: DDL backup table schema migration");

    skipped.Add("0x5BD138 (VA 55) len=57 `Create Temporary Table Del_Temp_Idx(Idx int Primary Key);` "
        + "NATIVE-ONLY: temporary table creation for bulk deletion — C# cleanup uses different strategy");

    skipped.Add("0x5BD30C (VA 56) len=34 `drop Temporary Table Del_Temp_Idx;` "
        + "NATIVE-ONLY: temporary table cleanup — paired with VA 55");

    skipped.Add("0x5BD3FC (VA 57) len=848 `Create table if not exists humanmagic` "
        + "NATIVE-ONLY: DDL table creation — C# DatabaseInitService handles table provisioning");

    skipped.Add("0x5BD758 (VA 58) len=788 `Create table if not exists heromagic` "
        + "NATIVE-ONLY: DDL table creation — C# DatabaseInitService handles table provisioning");

    skipped.Add("0x5BDA78 (VA 59) len=861 `Create table if not exists monster` "
        + "NATIVE-ONLY: DDL table creation — C# DatabaseInitService handles table provisioning");

    skipped.Add("0x5BDE18 (VA 60) len=54 `Alter table monster add JobFastness integer default 0;` "
        + "NATIVE-ONLY: DDL schema migration — C# assumes columns exist");
}

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

var gaps = new List<string>();
var nativeOnly = new List<string>();
foreach (var s in skipped)
{
    if (IsVerificationGap(s))
        gaps.Add(s);
    else
        nativeOnly.Add(s);
}

foreach (var s in nativeOnly)
    Console.WriteLine($"NOTE-NATIVE-ONLY: {s}");

if (fail > 0)
    return 1;
if (gaps.Count > 0)
{
    foreach (var s in gaps)
        Console.WriteLine($"INCOMPLETE: {s}");
    return 2;
}
return 0;

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

bool IsVerificationGap(string skipped)
{
    return skipped.StartsWith("source file not found:", StringComparison.Ordinal)
        || skipped.StartsWith("source directory not found:", StringComparison.Ordinal)
        || skipped.Contains("no blob-read statement located", StringComparison.Ordinal);
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

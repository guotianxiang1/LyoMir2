using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using DBSvr.Core;
using SystemModule;

namespace DBSvr
{
    /// <summary>
    /// 角色改名三库级联的复刻。证据底本 DBServer_repaired_20260803.exe
    /// （sha256 70234272f417a07ab61ffafe1ebb255d31422a5ee25840481a5a10d6c6028666），
    /// ImageBase 0x400000。⚠️ 此处曾写“CODE 未 VMP 虚拟化”，那是错的：
    /// .vmp0/.vmp1 两段存在，CODE 有 688 处转移进去（E8 568 + E9 120）。
    /// 准确说法：**大部分游戏逻辑函数未虚拟化**，所有引用的 VA 均直读反汇编。
    ///
    /// 级联块的拼接形态（以 U1 为例，0x5A9288..0x5A92D0）：
    ///   0x5A92A2  push 0x5A9C68            ; "Update ignore gamedata"（前缀，含库名）
    ///   0x5A92A7  push [ebp-0x14]          ; 运行时库名变量
    ///   0x5A92AA  push 0x5A9C88            ; ".WeaponUpg set CharName=\"%s\" where CharName=\"%s\";"
    ///   0x5A92B2  mov edx,3 / call 0x404F78 ; 三段拼成 SQL 骨架
    ///   0x5A92C0  mov ecx,1 / call 0x40CF30 ; Format，ecx=1 ⇒ 2 个 %s
    ///   0x5A92D0  call 0x5A5D18             ; 入队（带锁 SQL 队列，与主档同一个）
    ///
    /// ⚠️ 参数化 vs 字面插值：原版 22+2 条全是字面串插值，注入防线只有校验层
    /// fn_5CCDE4 的字符白名单。这里改用参数化 —— 对 MySQL 而言产生的行为等价
    /// （受影响行、ignore 语义、执行顺序都不变），但不继承那条注入面。
    /// 这是**有意的、唯一的**偏离，其余一律逐字。
    ///
    /// 逐字保留的四件事（规格 §6 明确列为"不要自作聪明"）：
    ///   1. `IGNORE` —— 22 条级联全有，主档 2 条全无
    ///   2. fire-and-forget —— 每条 call 之后原版**没有** test al,al，失败照做下一条
    ///   3. 不加事务 —— 原版此处无 BEGIN/COMMIT/ROLLBACK
    ///   4. 列名三种写法不归一化 —— CharName / Charname / ChrName 逐字
    /// </summary>
    public class MySqlNativeRenameCascadeService : INativeRenameCascadeService
    {
        /// <summary>
        /// 一条级联 UPDATE。Gate 为 null 表示该块**没有存在性门**。
        /// </summary>
        private sealed class Stmt
        {
            public string Gate;      // 门查的表名（null = 无门）
            public string Db;        // gamedata / guild / Mir3
            public string Table;
            public string Column;    // set 列 == where 列（22/22 成立，已机器校验）
            public string Tag;       // U#，仅日志
        }

        /// <summary>
        /// 22 条级联，顺序即原版 exec VA 升序（0x5A92D0 .. 0x5A9C22）。
        ///
        /// ⚠️ 两处反直觉结构，必须原样照抄，不要"修"：
        ///  (a) U1 WeaponUpg **没有存在性门** —— 机器验证 first exec 0x5A92D0 早于
        ///      first gate 0x5A92F5。不要给它补门。
        ///  (b) G2 的门查的是 `Kindling`，却保护 4 张表（CreditCard / Kindling /
        ///      M2_HeroPointActor1204 / GloryPoint）—— Kindling 不存在则另 3 张也不更新。
        ///      不要拆成 4 个独立门。
        /// </summary>
        private static readonly Stmt[] Cascade =
        {
            // U1: 无门。0x5A9C88 `.WeaponUpg set CharName="%s" where CharName="%s";`
            new Stmt { Gate = null,       Db = "gamedata", Table = "WeaponUpg",                    Column = "CharName",      Tag = "U1"  },
            // G1 -> U2/U3，同一张表两列
            new Stmt { Gate = "c2citems", Db = "gamedata", Table = "c2citems",                     Column = "FromChrName",   Tag = "U2"  },
            new Stmt { Gate = "c2citems", Db = "gamedata", Table = "c2citems",                     Column = "ToChrName",     Tag = "U3"  },
            // G2 门查 Kindling，保护下面 4 条（见 (b)）
            new Stmt { Gate = "Kindling", Db = "gamedata", Table = "CreditCard",                   Column = "CharName",      Tag = "U4"  },
            new Stmt { Gate = "Kindling", Db = "gamedata", Table = "Kindling",                     Column = "Charname",      Tag = "U5"  },
            new Stmt { Gate = "Kindling", Db = "gamedata", Table = "M2_HeroPointActor1204",        Column = "Charname",      Tag = "U6"  },
            new Stmt { Gate = "Kindling", Db = "gamedata", Table = "GloryPoint",                   Column = "Charname",      Tag = "U7"  },
            // G3
            new Stmt { Gate = "humantitle",    Db = "gamedata", Table = "humantitle",              Column = "ChrName",       Tag = "U8"  },
            // G4 -> U9/U10，同一张表两列
            new Stmt { Gate = "TitleRelation", Db = "gamedata", Table = "TitleRelation",           Column = "GrantName",     Tag = "U9"  },
            new Stmt { Gate = "TitleRelation", Db = "gamedata", Table = "TitleRelation",           Column = "ChrName",       Tag = "U10" },
            // G5 —— 注意库是 guild，不是 gamedata
            new Stmt { Gate = "guild_user",    Db = "guild",    Table = "guild_user",              Column = "CharName",      Tag = "U11" },
            // G6
            new Stmt { Gate = "FeedPetManager", Db = "gamedata", Table = "FeedPetManager",         Column = "MasterName",    Tag = "U12" },
            // G7 —— 注意库是 Mir3（M 大写，逐字）
            new Stmt { Gate = "dominatorpet",  Db = "Mir3",     Table = "dominatorpet",            Column = "MasterName",    Tag = "U13" },
            // G8
            new Stmt { Gate = "TransferAreaScore", Db = "gamedata", Table = "TransferAreaScore",   Column = "CharName",      Tag = "U14" },
            // G9 -> U15/U16，同一张表两列
            new Stmt { Gate = "dominatorvote", Db = "gamedata", Table = "dominatorvote",           Column = "DominatorName", Tag = "U15" },
            new Stmt { Gate = "dominatorvote", Db = "gamedata", Table = "dominatorvote",           Column = "VoterName",     Tag = "U16" },
            // G10..G15
            new Stmt { Gate = "m2_yb_deal_setinfo", Db = "gamedata", Table = "m2_yb_deal_setinfo", Column = "CharName",      Tag = "U17" },
            new Stmt { Gate = "humanachieve",  Db = "gamedata", Table = "humanachieve",            Column = "ChrName",       Tag = "U18" },
            new Stmt { Gate = "m2_offirankorders", Db = "gamedata", Table = "m2_offirankorders",   Column = "CharName",      Tag = "U19" },
            new Stmt { Gate = "m2_beatdownmonorder", Db = "gamedata", Table = "m2_beatdownmonorder", Column = "CharName",    Tag = "U20" },
            new Stmt { Gate = "mirmatchgroupapplymemberlist", Db = "gamedata", Table = "mirmatchgroupapplymemberlist", Column = "CharName", Tag = "U21" },
            new Stmt { Gate = "mirmatchgroupmemberlist",      Db = "gamedata", Table = "mirmatchgroupmemberlist",      Column = "CharName", Tag = "U22" },
        };

        /// <summary>
        /// 组装一条级联 UPDATE 的 SQL。提成静态纯函数**只为可断言性**：
        /// 此前 SQL 是在 RenameCascade 里内联插值的，审计够不着，于是把
        /// `IGNORE` 删掉、或把 `WHERE {Column}` 写成 `WHERE Idx`，
        /// 115 条级联行断言**全绿** —— 那是真缺口，不是理论风险。
        ///
        /// 原版模板逐字（例 U1 @0x5A9C88）：
        ///   `Update ignore gamedata.WeaponUpg set CharName="%s" where CharName="%s";`
        /// 三处必须逐字保留：
        ///  (a) `IGNORE` —— 新名已存在于该表时**跳过该行**而不报错；
        ///  (b) SET 列与 WHERE 列**同名**（22/22 成立，已机器校验）；
        ///  (c) 库名带 schema 前缀（guild / Mir3 不是 gamedata）。
        ///
        /// 两个名字走参数化；db/table/column 只来自本文件的常量表，非外部输入。
        /// </summary>
        /// <remarks>
        /// 逐字状态（2026-08-11 复核，证据 = CODE 快照字面量头校验 + 机器解析）：
        /// 22 条模板已用 staging/_casc_verify.py 从 0x5A923C..0x5A9C68 反汇编里
        /// 按 exec VA 顺序还原并与本表逐行比对 —— **22 exec / 15 gate /
        /// 0 mismatch**，库名/表名/列名/门归属/顺序全部一致。
        ///
        /// ⚠️ 唯一未对齐项 = **关键字大小写与结尾分号**。原版是
        ///   前缀 0x5A9C68 `Update ignore gamedata`（另 0x5A9FC8 `Update ignore
        ///   guild`、0x5AA0C4 `Update ignore Mir3`）
        ///   + 模板 0x5A9C88 `.WeaponUpg set CharName="%s" where CharName="%s";`
        /// 即小写 `ignore`/`set`/`where` 且**带结尾分号**；本方法发的是全大写
        /// 且无分号。**没有改**，原因有两条：
        ///  1. MySQL 关键字大小写不敏感、单语句尾分号可省 ⇒ 行为完全等价，
        ///     不是数据风险；
        ///  2. 两个不属于本文件的审计闸按 Ordinal 硬钉了大写文本 ——
        ///     NativeSqlVerbatimCheck/Program.cs:571 `Contains(cascade,
        ///     "UPDATE IGNORE {db}.{table}")`，以及
        ///     NativeRenameCascadeCheck/Program.cs:124 `Equal("UPDATE IGNORE
        ///     gamedata.WeaponUpg SET CharName=@n WHERE CharName=@o", sql, ...)`。
        ///     单改本文件会把这两闸打红。要真逐字须与那两个闸同步改，
        ///     属跨文件改动，留给拥有闸文件的一方裁决。
        /// </remarks>
        public static string BuildCascadeSql(string db, string table, string column)
            => $"UPDATE IGNORE {db}.{table} SET {column}=@n WHERE {column}=@o";

        /// <summary>该表在 Cascade 里的条数，供审计交叉校验。</summary>
        public static int CascadeStatementCount => Cascade.Length;

        /// <summary>
        /// 级联表的只读视图，供审计**逐行**断言库名/表名/列名/门归属。
        ///
        /// 加这个视图的理由（记在代码里，因为它是一次真实的覆盖缺口）：
        /// 此前审计只断言 `CascadeStatementCount==22` 与 `GateCount==15` 两条，
        /// 于是把 `guild` 写成 `gamedata`、把 `Charname` 写成 `CharName`、
        /// 把 G2 拆成 4 个独立门 —— 全部仍然绿。条数与门数对不代表内容对。
        ///
        /// 顺序即原版 exec VA 升序，审计据此逐条比对，不做集合比较
        /// （集合比较会漏掉顺序错误，而顺序在原版里是 fail-abort 语义的一部分）。
        /// </summary>
        public static IReadOnlyList<(string Gate, string Db, string Table,
            string Column, string Tag)> CascadeRows
        {
            get
            {
                var rows = new (string, string, string, string, string)[Cascade.Length];
                for (var i = 0; i < Cascade.Length; i++)
                    rows[i] = (Cascade[i].Gate, Cascade[i].Db, Cascade[i].Table,
                        Cascade[i].Column, Cascade[i].Tag);
                return rows;
            }
        }

        /// <summary>去重后的门数（原版 15 个 show tables 查询）。</summary>
        public static int GateCount
        {
            get
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var s in Cascade)
                    if (s.Gate != null) seen.Add(s.Gate);
                return seen.Count;
            }
        }

        public bool RenameMasterRecords(int idx, string newName)
        {
            // 前置门 0x5A8EE7 `cmp dword [eax+0xc],0` / 0x5A8EEB `jle 0x5A9162`
            // ⇒ Idx <= 0 直接失败，主档不写、级联不跑。
            if (idx <= 0) return false;

            using var conn = OpenConn();
            if (conn == null) return false;
            try
            {
                // 原版把两条语句拼成一次提交，五段字面量在 0x5A8EF1..0x5A8F2D
                // 依次 push，mov edx,9 / call 0x404F78 拼成（9 段 = 5 个字面量
                // + 名字 + 两个 IntToStr 结果）：
                //   0x5A91D0 len=31 `Update user_index set ChrName="`
                //   0x5A91F8 len=12 `" where Idx=`      ← Idx 大写 I
                //   0x5A9210 len=31 `;Update user_data set ChrName="`
                //   0x5A91F8 len=12 `" where Idx=`      （同一份，复用）
                //   0x5A9238 len=1  `;`
                // 逐字点：两条都是小写 `set` / 小写 `where`，表名无 schema 前缀
                //（0x5BAD84 `use mir3;` 坐实无前缀即 mir3），且**没有** ignore
                // ——与 22 条级联的 `Update ignore` 前缀正好相反，不要补。
                // 按数值 Idx 筛而非按旧名，与原版 `where Idx=<IntToStr>` 一致。
                using var cmd = new MySqlCommand(
                    "Update user_index set ChrName=@n where Idx=@i;"
                    + "Update user_data set ChrName=@n where Idx=@i;", conn);
                cmd.Parameters.Add(LegacyGbkText.Parameter("@n", newName));
                cmd.Parameters.AddWithValue("@i", idx);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                // 主档失败 = 原版的唯一中断点。调用方必须据此**不执行级联**。
                DBShare.MainOutMessage(
                    "[GameSoc] 原生改名主档写入失败，级联已中止: " + ex.Message);
                return false;
            }
        }

        public int RenameCascade(string oldName, string newName)
        {
            using var conn = OpenConn();
            if (conn == null) return 0;

            return RunCascade(
                probeGate: (db, table) => TableExists(conn, db, table),
                execute: sql =>
                {
                    using var cmd = new MySqlCommand(sql, conn);
                    cmd.Parameters.Add(LegacyGbkText.Parameter("@n", newName));
                    cmd.Parameters.Add(LegacyGbkText.Parameter("@o", oldName));
                    cmd.ExecuteNonQuery();
                },
                onError: (tag, db, table, message) => DBShare.MainOutMessage(
                    $"[GameSoc] 原生改名级联 {tag} ({db}.{table}) 失败(已忽略): "
                    + message));
        }

        /// <summary>
        /// 级联遍历的**纯逻辑**部分：门缓存、门关跳过、fire-and-forget。
        /// 抽出来只为可断言性 —— 这三条语义此前全绑在真连接上，审计够不着，于是
        ///  · 删掉 gateCache（变成每行查一次门，原版是 15 次 show-tables）
        ///  · 把 `continue` 改成 `return`（门关就中止后续全部）
        ///  · 在 catch 里 `throw`（破坏 fire-and-forget）
        /// 三种改坏**115 条级联行断言全绿**。那是真缺口，不是理论风险。
        ///
        /// 原版对应语义：
        ///  · 门缓存：一个 `show tables ... like "T"` 保护其后连续若干条
        ///    （15 个门 / 22 条语句，G2 一门保 4 表）
        ///  · 门关跳过：`test eax,eax / jle` ⇒ 跳过该块且**不报错**
        ///  · fire-and-forget：每条 exec 之后**没有** `test al,al`，
        ///    失败照做下一条，不回滚、不重试、不中断
        /// </summary>
        /// <param name="probeGate">(db, gateTable) =&gt; 表是否存在。每个门最多调一次。</param>
        /// <param name="execute">下发一条已组装好的 UPDATE；抛异常即视为该条失败。</param>
        /// <param name="onError">失败回调（tag, db, table, message），不得中断遍历。</param>
        /// <returns>实际成功下发的条数（原版 applied 计数，仅用于日志）。</returns>
        public static int RunCascade(Func<string, string, bool> probeGate,
            Action<string> execute,
            Action<string, string, string, string> onError = null)
        {
            // 门结果缓存：原版对同一张表只查一次门，一个门保护其后连续若干条。
            var gateCache = new Dictionary<string, bool>(StringComparer.Ordinal);
            var applied = 0;

            foreach (var s in Cascade)
            {
                if (s.Gate != null)
                {
                    if (!gateCache.TryGetValue(s.Gate, out var open))
                    {
                        open = probeGate(s.Db, s.Gate);
                        gateCache[s.Gate] = open;
                    }
                    // 门关闭 ⇒ 跳过该块，且**不报错**（原版 test eax,eax / jle）。
                    if (!open) continue;
                }

                try
                {
                    // `Update ignore <db>.<table> set <col>="新" where <col>="旧";`
                    execute(BuildCascadeSql(s.Db, s.Table, s.Column));
                    applied++;
                }
                catch (Exception ex)
                {
                    // fire-and-forget：原版每条 call 之后没有 test al,al，
                    // 失败照做下一条，也不回滚、不重试、不中断。
                    onError?.Invoke(s.Tag, s.Db, s.Table, ex.Message);
                }
            }
            return applied;
        }

        /// <summary>
        /// 复刻 15 个 `show tables ... like "T"` 门。原版用 like 精确名，
        /// 查询异常返回 -1/-2 且调用方 `jle` ⇒ DB 故障时该块静默跳过（fail-closed）。
        /// </summary>
        private static bool TableExists(MySqlConnection conn, string db, string table)
        {
            try
            {
                using var cmd = new MySqlCommand(
                    $"SHOW TABLES FROM {db} LIKE @t", conn);
                cmd.Parameters.AddWithValue("@t", table);
                using var reader = cmd.ExecuteReader();
                return reader.Read();
            }
            catch
            {
                return false;
            }
        }

        private static MySqlConnection OpenConn()
        {
            try
            {
                var c = new MySqlConnection(DBShare.DBConnection);
                c.Open();
                using (var sc = new MySqlCommand(
                    "SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED; "
                    + "SET SESSION wait_timeout=2073600", c))
                    sc.ExecuteNonQuery();
                return c;
            }
            catch
            {
                return null;
            }
        }
    }
}

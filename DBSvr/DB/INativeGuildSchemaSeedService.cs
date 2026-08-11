namespace DBSvr
{
    /// <summary>
    /// 原生 guild 库的种子行（NATIVE-ONLY 缺口，本次补齐 <c>Guild.Castle</c> 一条）。
    ///
    /// 原版把这条 INSERT 排在 guild 库的建库/建表序列中间，字面量按地址顺序：
    ///   0x5BF7E8 len=36  <c>CREATE DATABASE IF NOT EXISTS Guild;</c>
    ///   0x5BF818 len=782 <c>CREATE TABLE IF NOT EXISTS Guild.Castle(...)</c>
    ///   0x5BFB30 len=947 <c>... Guild.guild_list ...</c>
    ///   0x5BFEEC len=462 <c>... Guild.guild_rank ...</c>
    ///   0x5C00C4 len=690 <c>... Guild.guild_user ...</c>
    ///   0x5C0380 len=545 <c>... Guild.guild_relation ...</c>
    ///   0x5C05AC len=331 <c>... Guild.guild_log ...</c>
    ///   0x5C0700 len=95  ★本条种子行
    ///   0x5C0768 len=50  <c>show columns from Guild.guild_user like "sfLevel";</c>
    ///   0x5C07A4 len=69  <c>Alter table Guild.guild_user add sfLevel ...</c>
    /// 每条 refcount 均 -1、len 与文本等长。
    ///
    /// ⚠️ 表在 <b>guild</b> 库，不在 gamedata —— 原文写的就是 <c>Guild.Castle</c>，
    /// 且同族全部带 <c>Guild.</c> 前缀。真库已核 guild 库确有 Castle 表。
    ///
    /// 0x5C0700 逐字节（rc=-1 len=95）：
    ///   insert into Guild.Castle(Guid,name) values(1,"&lt;GBK&gt;")
    ///   on duplicate key update name = "&lt;GBK&gt;";
    /// 其中 &lt;GBK&gt; 是 6 字节 <c>C9 B3 B0 CD BF CB</c>（GBK「沙巴克」），
    /// 在字面量里出现**两次**（values 里一次、update 里一次），两次字节完全相同。
    ///
    /// <c>on duplicate key update</c> 尾部只更新 <c>name</c> 一列 —— 已按整条 95
    /// 字节读全，尾部无截断：<c>Guid</c> 是 Castle 的 PRIMARY KEY
    /// （0x5BF818 DDL 末尾 <c>PRIMARY KEY (Guid)</c>），故重复键即 Guid=1 那一行，
    /// 其余列（TotalGold / TodayIncome / WineCount / OwnGuild / IncomeToday /
    /// changeDate / WarDate / Data / ExtValue1..6）**一律不动** —— 这正是它不能
    /// 写成 REPLACE 或 DELETE+INSERT 的原因：那会清掉城堡累计数据。
    ///
    /// ⚠️ 本接口只覆盖种子行。整个 guild/gamedata 建库建表（N-a1：24 条
    /// CREATE TABLE + 37 条 ALTER + 4 条 CREATE DATABASE）仍是独立缺口，不在此处。
    /// </summary>
    public interface INativeGuildSchemaSeedService
    {
        /// <summary>
        /// 下发 0x5C0700 的种子行。
        /// </summary>
        /// <returns>
        /// <c>true</c> = 语句已成功执行；<c>false</c> = 连接或执行失败
        /// （缺库/缺表都落这里，按原版 0x5C1DE0 的 -1 语义只记日志不抛）。
        /// </returns>
        bool SeedCastleRow();
    }
}

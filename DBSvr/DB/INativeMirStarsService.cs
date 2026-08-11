using System.Collections.Generic;

namespace DBSvr
{
    /// <summary>
    /// 原生 <c>gamedata.mirStars</c> 排行查询（NATIVE-ONLY 缺口，本次补齐）。
    ///
    /// 原版把 14 条排行 SQL 放在一个 <b>AnsiString 定长数组</b>里，数组基址
    /// 0x5D6828、元素数 14 —— 判据是该数组的 Delphi 单元终结代码：
    ///   0x479442  b8 28 68 5d 00   mov eax, 0x5D6828      ; 数组基址
    ///   0x479447  b9 0e 00 00 00   mov ecx, 0x0E          ; ★元素数 = 14
    ///   0x47944C  8b 15 e8 10 40   mov edx, [0x4010E8]    ; TypeInfo = AnsiString
    ///   0x479452  e8 29 c3 f8 ff   call 0x405780          ; @FinalizeArray
    /// 数组各槽（按 vmp1 明文副本里的 dword 逐个解引用，全部 refcount=-1 且
    /// len 与文本等长）：
    ///   [ 0] 0x4788E4 user_index Job = 0        [ 1] 0x4789AC Job = 1
    ///   [ 2] 0x478A74 Job = 2                   [ 3] 0x478B3C 不带 Job
    ///   [ 4] 0x478BF8 hero_index Job = 0        [ 5] 0x478CCC Job = 1
    ///   [ 6] 0x478DA0 Job = 2                   [ 7] 0x478E74 不带 Job
    ///   [ 8] 0x478F3C ApprenticeNum             [ 9] 0x478FF4 FightPoints
    ///   [10] 0x4790A4 ForceLv                   [11] 0x479148 mirStars sex = 0
    ///   [12] 0x4791C4 mirStars sex = 1          [13] 0x479240 user_index Job = 3
    ///
    /// ⚠️ 这**两条是独立语句**，不是一条参数化 SQL：0x479148 与 0x4791C4 各自是
    /// 一条完整字面量（refcount 均 -1、len 均 113），只差 <c>sex</c> 常量。
    ///
    /// BLOCKED：槽号 11/12 与**线上排行类别号**的对应关系拿不到字节。读取该数组的
    /// 例程被 VMP 虚拟化 —— 活进程 CODE 快照里对数组基址只有上面那一处终结引用，
    /// 对 14 个元素地址各自的 dword 引用数为 0，故「类别 11 = sex0、类别 12 = sex1」
    /// 只能靠 C# 侧自己的 CategoryOrder 数组（Tier-2）推，不是证据。
    /// 因此本服务只负责**取数**，不产出排行报文，也不动
    /// <see cref="DBSvr.Core.NativeType2RankingPacketBuilder"/> 对类别 11/12 的拒绝。
    ///
    /// ⚠️ 本部署无 <c>gamedata.mirStars</c> 表（离线扫过全部 11 个库的 .frm、
    /// 也在线 SHOW TABLES 查过，均无）⇒ 本条链路**无法在本机端到端验证**。
    /// 缺表时的行为按原版查询执行器 0x5C1DE0 的异常路径复刻，见实现注释。
    /// </summary>
    public interface INativeMirStarsService
    {
        /// <summary>
        /// 按性别取排行前 100。<paramref name="sex"/> 只接受 0 / 1 —— 原版就是两条
        /// 写死 <c>sex = 0</c> / <c>sex = 1</c> 的语句，没有第三条。
        /// </summary>
        /// <returns>
        /// 查询成功返回行集（可能为空）；查询失败（含缺表）返回 <c>null</c>，
        /// 对应原版 0x5C1DE0 的 <c>-1</c> 返回码。调用方必须区分"空"与"失败"。
        /// </returns>
        List<NativeMirStarsRow> Load(int sex);
    }

    /// <summary>
    /// <c>select ChrName, nValue from gamedata.mirStars ...</c> 的一行。
    /// 只有这两列被 SELECT，故只建模这两列。
    /// ORDER BY 里的 <c>level</c> / <c>exp</c> 不在选择列表内，不建模。
    /// </summary>
    public sealed class NativeMirStarsRow
    {
        /// <summary>ChrName —— 原生名字列是 latin1_bin 容器装 GBK 字节。</summary>
        public byte[] ChrName = System.Array.Empty<byte>();

        /// <summary>
        /// nValue。宽度未证（该表无 DDL：<c>mirStars</c> 在 CODE 快照里只被
        /// 上述两条 SELECT 引用，普查中无对应 <c>Create table</c> ⇒ 建表在
        /// DBServer 之外）。按其余排行列（ForceLv / FightPoints /
        /// ApprenticeNum 均为 <c>int unsigned</c>）取 uint，与
        /// <see cref="DBSvr.Core.NativeType2RankingRow.Value"/> 同宽。
        /// </summary>
        public uint Value;
    }
}

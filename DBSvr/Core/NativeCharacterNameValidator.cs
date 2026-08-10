using System;

namespace DBSvr.Core
{
    /// <summary>
    /// 角色名字符白名单，复刻原版 DBServer <c>fn_5CCDE4</c>（VA 0x5CCDE4..0x5CCF3A）。
    ///
    /// 这是改名链**唯一**的输入校验防线：原版 22+2 条改名 SQL 全是字面串插值，
    /// 没有参数化，所以整条链的注入安全性完全压在这个函数上。
    /// 校验层 <c>fn_5CD2EC</c> 在 0x5CD35D 调它，返回 false ⇒ err = -1 ⇒ 不碰数据库。
    ///
    /// 证据底本 DBServer_repaired_20260803.exe
    /// （sha256 70234272f417a07ab61ffafe1ebb255d31422a5ee25840481a5a10d6c6028666），
    /// ImageBase 0x400000。⚠️ 此处曾写“CODE 未 VMP 虚拟化”，那是错的：
    /// 该镜像有 .vmp0/.vmp1 两段，且 CODE 有 688 处转移进去
    /// （E8 568 + E9 120，已独立复核）。准确说法是
    /// **大部分游戏逻辑函数未虚拟化**，所以以下每条规则均直读反汇编。
    ///
    /// ⚠️ Delphi 的 <c>add al,K / sub al,N / jb|jae</c> 是区间判定的惯用形式：
    /// <c>add</c> 把区间下界搬到 0（对 byte 自然回绕），<c>sub</c> 置借位标志，
    /// <c>jb</c> = 落在区间内，<c>jae</c> = 落在区间外。照抄时极性极易写反。
    /// </summary>
    public static class NativeCharacterNameValidator
    {
        /// <summary>
        /// 长度门，出自校验层 <c>fn_5CD2EC</c> 而非白名单本身：
        ///   0x5CD352  add eax,-4     ; eax = Len - 4
        ///   0x5CD355  sub eax,0x0b   ; eax = Len - 15，置 CF
        ///   0x5CD358  jae 0x5CD36F   ; 无借位（Len >= 15）-> err = -1
        /// 无符号语义 ⇒ 合法区间 <b>4 &lt;= Len &lt;= 14</b>（含端点）。
        /// Len &lt; 4 时 <c>Len-4</c> 下溢成大正数，<c>-11</c> 仍无借位，同样落 -1。
        /// </summary>
        public const int MinimumNameLength = 4;

        /// <inheritdoc cref="MinimumNameLength"/>
        public const int MaximumNameLength = 14;

        /// <summary>
        /// 复刻 <c>fn_5CD2EC</c> 0x5CD34F..0x5CD358 的长度门。
        /// 入参是 GBK **字节**长度（原版 0x5CD347 <c>call 0x404EB8</c> 取的是
        /// Delphi 字符串长度，即字节数，一个汉字算 2）。
        /// </summary>
        public static bool IsLengthAllowed(int gbkByteLength)
            => gbkByteLength >= MinimumNameLength
               && gbkByteLength <= MaximumNameLength;

        /// <summary>
        /// 字符白名单本体。入参是 GBK 编码后的**原始字节**，与原版逐字节比对一致。
        /// </summary>
        public static bool IsNameAllowed(byte[] gbkName)
        {
            if (gbkName == null) return false;

            // 0x5CCDF1 call 0x4050B8 (Trim) / 0x5CCDFC call 0x40C730 (Length)
            // 0x5CCE0B cmp ebx,eax / jne -> 返回 false
            // ⇒ Trim 后长度必须与原长相等：首尾有空白即拒。
            if (!TrimmedLengthEquals(gbkName)) return false;

            // 0x5CCE1F call 0x404EB8 / test eax,eax / je -> 返回 false（空串拒）
            if (gbkName.Length == 0) return false;

            // 0x5CCE30 [ebp-0x14] = 汉字计数；0x5CCE37 [ebp-0x18] = 字母数字计数
            var cjkCount = 0;
            var alnumCount = 0;
            // 0x5CCE2C [ebp-0x0d] = "上一字节是 GBK 首字节" 状态位
            var inGbkLead = false;

            for (var i = 0; i < gbkName.Length; i++)
            {
                var b = gbkName[i];

                if (!inGbkLead)
                {
                    // 单字节态。0x5CCE61 cmp byte [ebp-0x0d],0 / jne
                    // 'a'..'z' : 0x5CCE6A add al,0x9f / sub al,0x1a / jb
                    //            0x61+0x9F = 0x100 -> 0；'z'(0x7A)+0x9F = 0x19 < 0x1A
                    var lower = unchecked((byte)(b + 0x9F));
                    if (lower < 0x1A) { alnumCount++; continue; }

                    // 'A'..'Z' : 0x5CCE73 add al,0xbf / sub al,0x1a / jae -> 非字母
                    var upper = unchecked((byte)(b + 0xBF));
                    if (upper < 0x1A) { alnumCount++; continue; }

                    // '0'..'9' : 0x5CCE84 add al,0xd0 / sub al,0x0a / jae -> 非数字
                    // 命中时 0x5CCE8A inc [ebp-0x18]（注意计入的是**数字**计数器）
                    var digit = unchecked((byte)(b + 0xD0));
                    if (digit < 0x0A) { alnumCount++; continue; }

                    // 其余必须是 GBK 首字节：
                    // 0x5CCE92 add al,0x50 / sub al,0x48 / jae 0x5CCF33 -> 直接返回 false
                    // 穷举 0..0xFF 解得合法区恰为 **0xB0..0xF7**（连续 72 个值），
                    // 与下方区码门 (add 0xF0 / sub 0x48) 解出的区间完全相同 ——
                    // 原版这两道门同源、互为冗余，照抄时两处都要保留。
                    var lead = unchecked((byte)(b + 0x50));
                    if (lead >= 0x48) return false;

                    // 0x5CCE9C mov byte [ebp-0x0d],1 -> 进入双字节态
                    inGbkLead = true;
                    continue;
                }

                // 双字节态：当前 b 是 GBK 尾字节。
                // 0x5CCEA5 add al,0x5f / sub al,0x5e / jae 0x5CCF33 -> 返回 false
                // ⇒ 尾字节必须落在 0xA1..0xFE
                var trail = unchecked((byte)(b + 0x5F));
                if (trail >= 0x5E) return false;

                // 0x5CCEAF mov byte [ebp-0x0d],0 -> 退出双字节态
                inGbkLead = false;

                // 0x5CCEB9 取**前一个**字节（[edx+eax-2]，即首字节）
                // 0x5CCEC3 sub al,0xa0 -> [ebp-0x19] = 区码 = lead - 0xA0
                // 0x5CCECB sub al,0xa0 -> [ebp-0x1a] = 位码 = trail - 0xA0
                var zone = unchecked((byte)(gbkName[i - 1] - 0xA0));
                var cell = unchecked((byte)(b - 0xA0));

                // 0x5CCED3 add al,0xf0 / sub al,0x48 / jae 0x5CCF33 -> 返回 false
                // ⇒ 区码必须落在 0x10..0x57（即首字节 0xB0..0xF7，GBK 汉字区）
                var zoneChk = unchecked((byte)(zone + 0xF0));
                if (zoneChk >= 0x48) return false;

                // 三组定点屏蔽（原版硬编码，逐条照抄，不合并不外推）：
                // 0x5CCED9 cmp [ebp-0x19],0x37 / jne
                //   0x5CCEE2 add al,0xa6 / sub al,5 / jb 0x5CCF33 -> 拒
                //   ⇒ 区码 0x37 且 位码 0x5A..0x5E
                if (zone == 0x37)
                {
                    var t = unchecked((byte)(cell + 0xA6));
                    if (t < 5) return false;
                }

                // 0x5CCEE8 cmp [ebp-0x19],0x38 / jne
                //   0x5CCEEE sub al,0x0d / je -> 拒 (cell == 0x0D)
                //   0x5CCEF5 sub al,2    / je -> 拒 (cell == 0x0F)
                //   0x5CCEF9 sub al,0x0d / je -> 拒 (cell == 0x1C)
                if (zone == 0x38 && (cell == 0x0D || cell == 0x0F || cell == 0x1C))
                    return false;

                // 0x5CCEFD cmp [ebp-0x19],0x4c / jne
                //   0x5CCF03 cmp [ebp-0x1a],0x41 / je -> 拒
                if (zone == 0x4C && cell == 0x41) return false;

                // 0x5CCF09 inc [ebp-0x14] -> 汉字计数 +1
                cjkCount++;
            }

            // 0x5CCF18 cmp byte [ebp-0x0d],0 / jne 0x5CCF2A -> 返回 false
            // ⇒ 结尾仍停在双字节态（GBK 首字节后没有尾字节）即拒。
            if (inGbkLead) return false;

            // 0x5CCF1E cmp [ebp-0x14],1 / jge 0x5CCF2E -> true
            // 0x5CCF24 cmp [ebp-0x18],2 / jge 0x5CCF2E -> true
            // 0x5CCF2A xor eax,eax -> false
            // ⇒ 汉字数 >= 1 **或** 字母数字数 >= 2 才合法。
            return cjkCount >= 1 || alnumCount >= 2;
        }

        /// <summary>
        /// 复刻 0x5CCDF1/0x5CCDFC/0x5CCE0B 那段：Trim 后长度必须等于原长。
        /// Delphi <c>Trim</c> 去除的是 &lt;= ' ' 的字节（含 0x00..0x20）。
        ///
        /// ⚠️ 这道门在原版里是**死门** —— 语义上被下面的逐字符门完全覆盖。
        /// 穷举证明：Trim 去除的字节集 = 0x00..0x20；逐字符门接受的字节集 =
        /// a-z / A-Z / 0-9 / GBK 首字节 0xB0..0xF7；两者交集为**空**。
        /// 且 GBK 尾字节合法区 0xA1..0xFE 也不含任何 &lt;= 0x20 的值。
        /// ⇒ 不存在"能被 Trim 去掉、却能过逐字符门"的字节，
        ///   故不存在任何输入能单独触发本门。
        /// （变异测试实证：删掉本门后 NativeRenameCascadeCheck 仍全绿。）
        ///
        /// 仍然保留它：忠实优先，且它是廉价的提前退出。但不要为它写
        /// "独立可观测"的断言 —— 那种断言在原版语义下不可能构造。
        /// </summary>
        private static bool TrimmedLengthEquals(byte[] value)
        {
            var start = 0;
            var end = value.Length - 1;
            while (start <= end && value[start] <= 0x20) start++;
            while (end >= start && value[end] <= 0x20) end--;
            return end - start + 1 == value.Length;
        }
    }
}

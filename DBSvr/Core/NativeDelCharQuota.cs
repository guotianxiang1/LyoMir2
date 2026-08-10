using System;
using System.Collections.Generic;

namespace DBSvr.Core
{
    /// <summary>
    /// 复刻原版 DBServer 的「每日删除角色配额」。
    ///
    /// 原版实现在删除角色的 worker <c>fn_5A5978</c>（0x5A5978..0x5A5AB5）里，
    /// 配额状态挂在**账号对象**上，不是挂在连接上：
    /// worker 先用角色名在 <c>self+0x14</c> 的名字索引里查到角色记录
    /// （<c>0x5A59A7 mov eax,[eax+0x14]</c> / <c>0x5A59AA call 0x5AEF10</c>），
    /// 再**解一层指针**拿到账号对象（<c>0x5A5A19 mov eax,[ebp-0x18]</c> /
    /// <c>0x5A5A1C mov eax,[eax]</c>），配额的两个字段都在这个账号对象上：
    ///
    ///   dword[act+0x0C] = 上次成功删除的日期（整数天，Delphi TDateTime 的整数部分）
    ///   dword[act+0x10] = 当日已成功删除的角色数
    ///
    /// ⚠️ 偏移极易看串：<c>+0x10</c> 是账号对象上的**计数器**，而角色记录上
    /// <c>rec+0x10</c> 是角色名字符串、<c>rec+0x1E</c> 是跨服锁定标志。
    /// 这两组偏移分属两个对象，中间隔着 <c>0x5A5A1C mov eax,[eax]</c> 那一次解引用。
    ///
    /// 跨日重置（0x5A5A11..0x5A5A31）：
    ///   0x5A5A11  call 0x4034B0          ; today（fistp 取整数天）
    ///   0x5A5A16  mov [ebp-0x14], eax
    ///   0x5A5A1E  mov edx, [ebp-0x14]
    ///   0x5A5A21  sub edx, [eax+0x0C]    ; today - lastDay
    ///   0x5A5A24  dec edx                ; -1
    ///   0x5A5A25  jl  0x5A5A31           ; &lt;0 ⇒ 同一天，**不**重置
    ///   0x5A5A2C  xor edx, edx
    ///   0x5A5A2E  mov [eax+0x10], edx    ; 否则计数器清零
    /// 即 <c>today - lastDay - 1 &gt;= 0</c>（也就是相隔 ≥2 天）才清零。
    /// 注意这不是「换一天就清零」：<c>lastDay+1 == today</c> 时 edx 为 0，
    /// <c>jl</c> **不**跳转，于是走到清零。故实际语义是「跨过任意一天即清零」，
    /// 只有 <c>today == lastDay</c>（edx = -1）才保留计数。极性极易写反。
    ///
    /// 配额门（0x5A5A31..0x5A5A3A）：
    ///   0x5A5A36  cmp dword [eax+0x10], 4
    ///   0x5A5A3A  jge 0x5A5A8A           ; ≥4 ⇒ 返回码 2（配额用尽）
    /// 上限 4，且用 <c>jge</c>，所以每天最多**成功**删除 4 个。
    ///
    /// 计数与日期的写入（0x5A5A45..0x5A5A55）发生在置删除标志**之前**，
    /// 且没有任何回滚路径：
    ///   0x5A5A4D  mov [eax+0x0C], edx    ; lastDay = today
    ///   0x5A5A55  inc dword [eax+0x10]   ; count++
    ///
    /// ⛔ 原版**不持久化**这两个字段。我按 9 种可能的列名拼写在整个 CODE 段
    /// （0x401000..0x5D5000）搜过 SQL 字面量：DelCount / delcount / DelNum /
    /// delnum / DeleteCount / DelChrCount / DelDate / deldate / DelDay
    /// —— 全部 0 命中。该搜索有正对照：<c>inc dword [eax+0x10]</c>
    /// （字节 <c>ff 40 10</c>）能命中 30 处、其中包含 0x5A5A55 本身，
    /// 说明搜索式确实能匹配，0 命中不是工具假阴性。
    /// 结论：DBServer 重启即清空所有账号的当日配额。本类因此**只在内存里**，
    /// 不加任何数据库列 —— 加列反而是伪造原版没有的行为。
    ///
    /// 线程模型：与 DBSvr 其余账号级缓存一致，用一把锁护住整张表。
    /// </summary>
    public static class NativeDelCharQuota
    {
        /// <summary>
        /// 每日上限，原版 <c>0x5A5A36 cmp dword [eax+0x10], 4</c>。
        /// </summary>
        public const int DailyLimit = 4;

        private sealed class Entry
        {
            /// <summary>对应 dword[act+0x0C]。</summary>
            public int LastDay;

            /// <summary>对应 dword[act+0x10]。</summary>
            public int Count;
        }

        private static readonly object Gate = new();

        // 账号名 → 配额。账号名大小写不敏感：原版比较账号/角色名用 0x40AFB0，
        // 该函数把 'a'..'z'（0x61..0x7A）减 0x20 折叠成大写后再比
        // （0x40AFD6 cmp bl,0x61 / 0x40AFDB cmp bl,0x7A / 0x40AFE0 sub bl,0x20）。
        private static readonly Dictionary<string, Entry> Quotas =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 原版 <c>0x4034B0</c>：取「今天」的整数天数。
        /// 原版是 Delphi TDateTime（以 1899-12-30 为 0 day）经 fistp 取整。
        /// 这里只需要一个**单调的整数天**用于比较与差值，故用 DateTime.Date 的
        /// 天数；两侧都用同一基准时差值语义等价（原版也只做差值与比较，
        /// 从不把该值写进数据库，见类注释的零列命中结论）。
        /// </summary>
        private static int Today() =>
            (int)(DateTime.Now.Date - new DateTime(1899, 12, 30)).TotalDays;

        /// <summary>
        /// 配额检查 + 记账，复刻 0x5A5A11..0x5A5A55 的**顺序**。
        ///
        /// 返回 true = 放行（并且已经把计数记上）。
        /// 返回 false = 配额用尽，调用方应回原版返回码 2。
        ///
        /// ⚠️ 原版是「先记账，再置删除标志」，且失败无回滚
        /// （0x5A5A4D/0x5A5A55 在 0x5A5A66 的 vmt 调用之前）。所以即使后续
        /// 删除动作失败，当日配额也已经被消耗掉一个 —— 这是原版行为，别「修」它。
        /// </summary>
        public static bool TryConsume(string account)
        {
            if (string.IsNullOrEmpty(account)) return false;

            var today = Today();
            lock (Gate)
            {
                if (!Quotas.TryGetValue(account, out var e))
                {
                    e = new Entry();
                    Quotas[account] = e;
                }

                // 0x5A5A21 sub edx,[eax+0xc] / 0x5A5A24 dec edx / 0x5A5A25 jl
                // ⇒ today - lastDay - 1 >= 0 才清零；只有 today == lastDay 时保留。
                if (today - e.LastDay - 1 >= 0)
                    e.Count = 0;

                // 0x5A5A36 cmp [eax+0x10],4 / 0x5A5A3A jge ⇒ 返回码 2
                if (e.Count >= DailyLimit)
                    return false;

                // 0x5A5A4D mov [eax+0xc],edx  ; lastDay = today
                // 0x5A5A55 inc dword [eax+0x10]
                e.LastDay = today;
                e.Count++;
                return true;
            }
        }

        /// <summary>
        /// 只读查询当日已用量，供审计使用。不做跨日重置的副作用。
        /// </summary>
        public static int UsedToday(string account)
        {
            if (string.IsNullOrEmpty(account)) return 0;
            var today = Today();
            lock (Gate)
            {
                if (!Quotas.TryGetValue(account, out var e)) return 0;
                return (today - e.LastDay - 1 >= 0) ? 0 : e.Count;
            }
        }

        /// <summary>
        /// 清空全部配额。仅用于测试与「重启等价」的显式复位
        /// —— 原版重启即清空（零持久化列），本方法就是那个语义。
        /// </summary>
        public static void ResetAll()
        {
            lock (Gate) Quotas.Clear();
        }
    }
}

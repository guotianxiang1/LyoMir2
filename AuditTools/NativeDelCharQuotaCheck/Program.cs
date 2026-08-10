using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DBSvr.Core;

// 每日删除角色配额闸门。
//
// 被审对象 = DBSvr/Core/NativeDelCharQuota.cs，其行为逐条对应原版 worker
// fn_5A5978（0x5A5978..0x5A5AB5）：
//   0x5A5A36  cmp dword [eax+0x10], 4   ; 上限 4
//   0x5A5A3A  jge                       ; >=4 拒（不是 >）
//   0x5A5A21  sub edx,[eax+0x0C] / dec edx / jl  ; today-lastDay-1>=0 才清零
//   0x5A5A4D  mov [eax+0x0C],edx        ; lastDay = today
//   0x5A5A55  inc dword [eax+0x10]      ; count++
//
// 退出码约定与 AuditTools 其余闸门一致：0=PASS，非 0=FAIL，2=INCOMPLETE。
//
// 断言计数会打印在 PASS/FAIL 行。**加了断言就必须看到这个数上涨**；
// 没涨说明断言根本没被执行（假绿），必须查清。
internal static class Program
{
    private static int _asserts;
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        _asserts++;
        if (ok) return;
        _failures++;
        Console.WriteLine($"FAIL {what}");
    }

    private static void Equal(int expected, int actual, string what)
    {
        _asserts++;
        if (expected == actual) return;
        _failures++;
        Console.WriteLine($"FAIL {what}: expected {expected}, got {actual}");
    }

    // 用反射直接摆放内部状态，才能测「跨日」而不用等一天。
    // 反射失败必须是 INCOMPLETE 而不是 PASS —— 否则改了字段名就静默变绿。
    private static bool TrySetState(string account, int lastDay, int count)
    {
        var t = typeof(NativeDelCharQuota);
        var quotas = t.GetField("Quotas", BindingFlags.NonPublic | BindingFlags.Static);
        if (quotas == null) return false;
        var dict = quotas.GetValue(null);
        if (dict == null) return false;

        var entryType = t.GetNestedType("Entry", BindingFlags.NonPublic);
        if (entryType == null) return false;
        var fLastDay = entryType.GetField("LastDay");
        var fCount = entryType.GetField("Count");
        if (fLastDay == null || fCount == null) return false;

        var entry = Activator.CreateInstance(entryType);
        fLastDay.SetValue(entry, lastDay);
        fCount.SetValue(entry, count);

        var indexer = dict.GetType().GetProperty("Item");
        if (indexer == null) return false;
        indexer.SetValue(dict, entry, new object[] { account });
        return true;
    }

    // 直接读内部 Entry 的字段。返回 int.MinValue 表示读不到，
    // 那会让调用处的 Equal 立刻变红 —— 不能静默当成 0。
    private static int ReadEntryField(string account, string field)
    {
        var t = typeof(NativeDelCharQuota);
        var quotas = t.GetField("Quotas", BindingFlags.NonPublic | BindingFlags.Static);
        var dict = quotas?.GetValue(null);
        if (dict == null) return int.MinValue;
        var indexer = dict.GetType().GetProperty("Item");
        if (indexer == null) return int.MinValue;
        object entry;
        try { entry = indexer.GetValue(dict, new object[] { account }); }
        catch { return int.MinValue; }
        if (entry == null) return int.MinValue;
        var f = entry.GetType().GetField(field);
        if (f == null) return int.MinValue;
        return (int)f.GetValue(entry);
    }

    private static int ReadCount(string account) => ReadEntryField(account, "Count");

    private static int ReadLastDay(string account) => ReadEntryField(account, "LastDay");

    private static int TodayValue()
    {
        var m = typeof(NativeDelCharQuota)
            .GetMethod("Today", BindingFlags.NonPublic | BindingFlags.Static);
        return (int)m.Invoke(null, null);
    }

    private static int Main()
    {
        // ---- 上限常量 = 原版 0x5A5A36 的立即数 4 --------------------------
        Equal(4, NativeDelCharQuota.DailyLimit, "DailyLimit == native imm32 at 0x5A5A36");

        var probe = typeof(NativeDelCharQuota)
            .GetMethod("Today", BindingFlags.NonPublic | BindingFlags.Static);
        if (probe == null)
        {
            Console.WriteLine("SKIP: NativeDelCharQuota.Today() not found -- "
                            + "cannot place state, so cross-day legs are unproven.");
            Console.WriteLine($"INCOMPLETE asserts={_asserts}");
            return 2;
        }

        var today = TodayValue();

        // ---- jge 而不是 jg：第 4 次成功后第 5 次必须被拒 ------------------
        NativeDelCharQuota.ResetAll();
        for (var i = 1; i <= 4; i++)
            Check(NativeDelCharQuota.TryConsume("acct"), $"consume #{i} of 4 allowed");
        Check(!NativeDelCharQuota.TryConsume("acct"), "5th consume refused (jge, not jg)");
        Equal(4, NativeDelCharQuota.UsedToday("acct"), "used == 4 after exhaustion");

        // 变异哨兵：若把 >= 写成 >，上面那条会放行第 5 次。
        // 若把上限写成 5，第 5 次也会放行。两种都被这条断言抓住。

        // ---- 账号名大小写不敏感（原版 0x40AFB0 折叠 a-z） ----------------
        NativeDelCharQuota.ResetAll();
        Check(NativeDelCharQuota.TryConsume("Player"), "mixed-case account consumes");
        Equal(1, NativeDelCharQuota.UsedToday("player"), "lowercase sees the same quota");
        Equal(1, NativeDelCharQuota.UsedToday("PLAYER"), "uppercase sees the same quota");

        // ---- 跨日重置的极性：today-lastDay-1 >= 0 才清零 ------------------
        // 关键边界：lastDay == today 时 edx = -1，jl 跳转，**保留**计数。
        if (!TrySetState("sameday", today, 4))
        {
            Console.WriteLine("SKIP: cannot place internal state (field layout changed).");
            Console.WriteLine($"INCOMPLETE asserts={_asserts}");
            return 2;
        }
        Check(!NativeDelCharQuota.TryConsume("sameday"),
            "same day with count=4 stays exhausted (jl taken, no reset)");
        Equal(4, NativeDelCharQuota.UsedToday("sameday"), "same-day count preserved");

        // lastDay+1 == today 时 edx = 0，jl **不**跳转 ⇒ 清零。
        // 这是最容易写反的一条：直觉会以为「差 1 天算同一天」。
        TrySetState("nextday", today - 1, 4);
        Check(NativeDelCharQuota.TryConsume("nextday"),
            "next day resets (edx==0, jl NOT taken)");
        Equal(1, NativeDelCharQuota.UsedToday("nextday"),
            "next day count restarts at 1");

        // 更久之前同样清零。
        TrySetState("longago", today - 30, 4);
        Check(NativeDelCharQuota.TryConsume("longago"), "30 days ago resets");
        Equal(1, NativeDelCharQuota.UsedToday("longago"), "long-ago count restarts at 1");

        // 未来日期（时钟回拨）：today-lastDay-1 < 0 ⇒ 不清零，保持耗尽。
        // 原版就是这个算术，没有额外的 clamp，别「修」成清零。
        TrySetState("future", today + 5, 4);
        Check(!NativeDelCharQuota.TryConsume("future"),
            "clock skew into the future does NOT reset (native has no clamp)");

        // ---- UsedToday 是只读的：不得有跨日副作用 ------------------------
        // ⚠️ 变异测试逮到过这里的断言缺口：只比 UsedToday 的**返回值**是抓不住
        // 「它顺手把 Count 清了」的 —— 清零后返回值照样是 0，两次调用也照样一致。
        // 必须直接读内部状态来断言「没被改过」。
        TrySetState("readonly1", today - 1, 3);
        Equal(0, NativeDelCharQuota.UsedToday("readonly1"),
            "UsedToday reports 0 across a day boundary");
        Equal(0, NativeDelCharQuota.UsedToday("readonly1"),
            "UsedToday is idempotent (no hidden mutation)");
        Equal(3, ReadCount("readonly1"),
            "UsedToday did NOT mutate the stored count (read-only)");
        Equal(today - 1, ReadLastDay("readonly1"),
            "UsedToday did NOT mutate the stored lastDay (read-only)");

        // 同一条在「耗尽 + 跨日」组合下再验一次：这是最容易被顺手清零的形状。
        TrySetState("readonly2", today - 1, 4);
        NativeDelCharQuota.UsedToday("readonly2");
        Equal(4, ReadCount("readonly2"),
            "UsedToday leaves an exhausted cross-day entry untouched");

        // ---- 账号之间互不影响 --------------------------------------------
        NativeDelCharQuota.ResetAll();
        for (var i = 0; i < 4; i++) NativeDelCharQuota.TryConsume("a");
        Check(!NativeDelCharQuota.TryConsume("a"), "account a exhausted");
        Check(NativeDelCharQuota.TryConsume("b"), "account b unaffected by a");
        Equal(1, NativeDelCharQuota.UsedToday("b"), "account b count independent");

        // ---- 空账号名不放行（原版找不到账号对象就不会记账） --------------
        // ⚠️ 变异测试陷阱：若把 null 检查删掉，后续字典操作会抛 NullReferenceException，
        // 导致整个进程崩溃（exit 0xE0434352），而不是让断言失败 —— 那不算「闸门 CAUGHT」。
        // 所以要在这里捕异常，把崩溃变成受控 FAIL，才能说明闸门确实在守这条不变量。
        try
        {
            Check(!NativeDelCharQuota.TryConsume(null), "null account refused");
            Check(!NativeDelCharQuota.TryConsume(""), "empty account refused");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL null/empty guard threw {ex.GetType().Name}: " +
                              "TryConsume must not crash on null/empty");
        }

        // ---- 零持久化：不得出现任何 SQL 列名 -----------------------------
        // 原版对这两个字段零 SQL 列命中（9 种拼写全 0，正对照 inc dword [eax+0x10]
        // = 字节 ff 40 10 命中 30 处含 0x5A5A55 本身，证明搜索式有效）。
        // 若有人给它加了 DB 列，就是伪造原版没有的持久化，这里必须变红。
        var src = ReadQuotaSource();
        if (src == null)
        {
            Console.WriteLine("SKIP: cannot locate NativeDelCharQuota.cs to scan for SQL.");
            Console.WriteLine($"INCOMPLETE asserts={_asserts}");
            return 2;
        }
        // ⚠️ 必须先剥掉注释再扫。首跑就被这条咬过：类注释里有一句
        // 「DBServer 重启即清空」旁边的英文散文含 "DELETE"（列名拼写清单
        // DelCount/DeleteCount 那段），于是扫描命中注释、报了一条假红。
        // 注释里的 SQL 关键字不是代码，判据必须只看**真代码行**。
        var code = StripComments(src);
        foreach (var sql in new[]
                 {
                     "SELECT", "INSERT", "UPDATE", "DELETE",
                     "MySqlCommand", "MySqlConnection"
                 })
        {
            Check(code.IndexOf(sql, StringComparison.OrdinalIgnoreCase) < 0,
                $"quota store contains no `{sql}` in code (native persists nothing)");
        }

        // 正对照：剥注释后仍必须留下真代码，否则 StripComments 把整个文件吃光了，
        // 上面 6 条会全部「因为什么都没有」而变绿 —— 那是最隐蔽的一种假绿。
        Check(code.Contains("TryConsume"), "StripComments kept real code (positive control)");
        Check(code.Contains("DailyLimit"), "StripComments kept the limit constant");

        // 上限必须是写死的 4，不能读配置 —— 原版是立即数。
        Check(src.Contains("DailyLimit = 4"), "DailyLimit is the literal 4, not configurable");

        if (_failures == 0)
        {
            Console.WriteLine($"PASS asserts={_asserts}");
            return 0;
        }
        Console.WriteLine($"FAILED asserts={_asserts} failures={_failures}");
        return 1;
    }

    // 去掉 // 行注释与 /* */ 块注释，只留真代码。
    // 不追求完整的 C# 词法（字符串里的 // 会被误剥），但对本用途足够：
    // 被审文件里没有含 // 的字符串字面量，而漏剥注释会造成假红、
    // 过度剥离会造成假绿，两者都有上面的正对照兜着。
    private static string StripComments(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                sb.Append('\n');
                continue;
            }
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i++;
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    // 用 [CallerFilePath] 定位源码：AppContext.BaseDirectory 指向 bin，
    // 那里没有 .cs，会让整条扫描静默变成 SKIP（假绿的常见来源）。
    private static string ReadQuotaSource([CallerFilePath] string thisFile = null)
    {
        var dir = Path.GetDirectoryName(thisFile);
        if (dir == null) return null;
        var path = Path.GetFullPath(Path.Combine(
            dir, "..", "..", "DBSvr", "Core", "NativeDelCharQuota.cs"));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}

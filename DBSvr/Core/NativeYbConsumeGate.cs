using System;
using System.Collections.Generic;

namespace DBSvr.Core
{
    /// <summary>
    /// 排队系统的 VIP 免排队闸，复刻原版 0x5C9A5C 的**记忆化**语义。
    ///
    /// 原版用 <c>TStringHash</c>（Self+0x4C）做 PTID -> 三态记忆：
    ///   0x5C9A84  call 0x49BAA8   ; ValueOf：未命中返 0（0x49BAD5 xor eax,eax）
    ///   0x5C9A89  sub eax,1 / jb  ; 0 -> 查库
    ///             je              ; 1 -> true
    ///             dec eax / je    ; 2 -> false
    ///   0x5C9AE0  ecx=1 -> Add(ptid, 1)   ; 过闸
    ///   0x5C9AF9  ecx=2 -> Add(ptid, 2)   ; 未过闸（含查询失败的 -1）
    /// 节点布局由 0x49B410 给出：node+0x10 = key 串、node+0x14 = value
    /// （0x49B464 `mov [edx+0x14],eax`）。
    ///
    /// ★关键行为：**这张表在进程生命周期内从不清空**。判据是 +0x4C 的访问点普查
    /// （disp8 形式 `8b /r 4c`，区间 0x5C9000..0x5CE000 共 5 处）：
    ///   0x5C9A81 / 0x5C9AEB / 0x5C9B04 —— 全部在 0x5C9A5C 这一个函数内，
    ///     分别是 ValueOf 与两处 Add；
    ///   0x5CCB97 / 0x5CCCF2 —— 属另一个类（0x5CCCF5 紧接 `call 0x404EB8`
    ///     = Length(AnsiString)，是字符串字段，不是 hash 表）。
    /// 即没有任何 Clear / Free 调用点 ⇒ 一旦某 PTID 被记成 2（未过闸），
    /// **重启前不会再查库**。玩家中途充够元宝也不会当场生效 —— 这是原版行为，
    /// 不是缺陷，本侧照此复刻，不加"过期时间"之类的发明。
    ///
    /// ⚠️ 未接线：两个原生调用点分别在会话准入路径上
    ///   0x5A157A（打 <c>sess+0x94</c> 免排队标记）与
    ///   0x5CD5C6（登录准入，失败返回码 7 = 进排队），
    /// 对应的 C# 侧在 <c>DBSvr/Services/UserSocService.cs</c> —— 本轮不属本文件范围。
    /// 接线时的门必须是 <see cref="NativeUserAdmissionControl.QueueEnabled"/>：
    /// 原版 0x5A152E / 0x5CD594 都先 `cmp byte [[0x5D9D48]],0 / je` 才查闸。
    /// </summary>
    public sealed class NativeYbConsumeGate
    {
        /// <summary>原版记忆值：1 = 过闸、2 = 未过闸。0 表示未命中（不入表）。</summary>
        private const int MemoPassed = 1;
        private const int MemoRejected = 2;

        private readonly object _sync = new();

        // key 比较：原版 TStringHash 的键比较走 0x405004（LStrCmp），是**字节精确**
        // 比较，无大小写折叠 ⇒ 用 Ordinal，不用 OrdinalIgnoreCase。
        private readonly Dictionary<string, int> _memo = new(StringComparer.Ordinal);

        private readonly INativeYbConsumeService _service;

        public NativeYbConsumeGate(INativeYbConsumeService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// 该 PTID 是否免排队。<paramref name="threshold"/> 应传
        /// <see cref="DBShare.VipYBConsume"/>（原版 Self+0x84，见 0x5C15E3 的配置回写）。
        /// </summary>
        public bool IsExempt(string ptid, int threshold)
        {
            var key = ptid ?? string.Empty;
            lock (_sync)
            {
                if (_memo.TryGetValue(key, out var memo))
                    return memo == MemoPassed;
            }

            // 查库在锁外：原版这段没有临界区，且 0x5C1DE0 内部才加库锁
            // （0x5C1E16 call 0x562A98 / 0x5C1E34 call 0x562A80 是一对
            //  Lock/Unlock），闸本身不串行化。
            var result = _service.IsOverThreshold(key, threshold);

            // 原版把 0x5C1DE0 的返回码直接喂 `test eax,eax / jle`（0x5C9ADC）：
            // -1（查询失败）与 0（无行）走**同一条** else 分支 ⇒ 都记 2、都不过闸。
            // 故这里 null 与 false 合流，fail-closed 与原版一致。
            var passed = result == true;

            lock (_sync)
            {
                // 原版无条件 Add（0x5C9AE0 / 0x5C9AF9 两处都在写库结果之后），
                // 并发下可能重复 Add 同键；C# 侧用索引赋值等价且不抛。
                _memo[key] = passed ? MemoPassed : MemoRejected;
            }
            return passed;
        }

        /// <summary>
        /// 已记忆的 PTID 数。仅供审计/诊断 —— 原版无对应导出，
        /// 不参与任何游戏行为判定。
        /// </summary>
        public int MemoCount
        {
            get { lock (_sync) return _memo.Count; }
        }
    }
}

using System;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// CM 1300 / 1301 / 1320 — the "current-NPC session" trio (cm-2 台账里被标为
    /// "分身/机器人 NPC 会话", 见 GameSvr/Services/NativeCmQ2FailClosed.cs 的 1300/
    /// 1301/1320 条目)。反汇编底本 flat_image.bin (base 0x400000) 后**订正**这一命名：
    ///
    ///   [self+0xCD8] 不是"分身/机器人对象", 而是玩家**当前对话中的 NPC**——本端口早已
    ///   建模为 <see cref="m_NPC"/> (TPlayObject.Base.cs:211 `public TBaseObject m_NPC`)。
    ///   该结论有双重佐证：
    ///     (a) 唯一写入 [self+0xCD8] 的 setter 是 NPC 点击处理器 sub_6B8B28
    ///         (0x6B8BA7 / 0x6B8C48 `mov [player+0xCD8],esi`, esi=按 Recog 查得并
    ///         Click 过的 NPC; 另有属性 setter 0x63DFAC 0x63DFAF)。全镜像 0x00000CD8
    ///         位移穷举=57 处, 写入仅此三处, 无一处写 0。
    ///     (b) 本端口 TPlayObject.cs:1314-1321 与 TPlayObject.Operate.cs:2257 的原生
    ///         注释已确认 `[player+0xCD8] == player.m_NPC`。
    ///
    /// 会话数据结构（偏移 → C# 映射, 三件套）：
    ///   ① [self+0xCD8]          → TPlayObject.m_NPC (TBaseObject)   当前对话 NPC 句柄
    ///   ② [m_NPC+0x570]         → NPC 的**脚本对象** (VMT [0x7286FC], 构造 0x7295B0;
    ///                             与怪物脚本对象 monster+0x4D0 是不同类, 见
    ///                             PasScriptHost.cs:308-328)。C# 侧建模为
    ///                             M2Share.PasEngine / NormNpc 的脚本接口:
    ///                               • VMT+0x44 (0x73AA20 → 0x733D84) = 跑 label(无参)
    ///                                   ≡ NormNpc.GotoLable / PasEngine.TryCallNpcLabel
    ///                               • VMT+0x48 (0x73A960 → 0x733B98) = 跑 label(带
    ///                                   array-of-const 参数) — **C# 无 1:1 等价**
    ///   ③ CM 1320 请求记录 (0x28 字节, 由 sub_765E68 构造, sub_76C11C 入队)：
    ///        +0x00 word  = SM ident (0x27A3)        +0x02 word  = 0
    ///        +0x04 dword = m_NPC 指针                +0x08 dword = Param(1..3)
    ///        +0x0C dword = 0                         +0x10 ptr   = 内嵌串缓冲(此处 nil)
    ///        +0x14 word  = 串长+1(此处 0)            +0x16 word  = 0
    ///        +0x18..+0x20 = 0                        +0x24 dword = m_NPC 指针(重复)
    ///        +0x28.. = 内嵌串缓冲(len+1<=0x64 时内联, 否则堆分配)
    ///      该记录含**裸对象指针**且经**内部消息队列** sub_76C11C 投递, 非可上线的
    ///      客户端包体 → 无建模、无法逐字节推导。
    ///
    /// 分发臂 (CM 选择树 sub_6D7D68, 共享静默出口 0x6DBC2C)：
    ///   1300 leaf 0x6DAA17 → worker 0x63D980   (@DoRequestBless, VMT+0x48 带参)
    ///   1301 leaf 0x6DAA72 → worker 0x63DC98   (@DoAcceptBless,  VMT+0x44 无参)
    ///   1320 leaf 0x6DAB6A → worker 0x765E68   (SM 0x27A3 记录入队)
    ///
    /// 处置（逐条按证据, 可证则忠实、不可证则 fail-closed, 绝不捏造脚本接口语义）：
    ///   • 1301 = 忠实实现。leaf 只有 nil 门 (0x6DAA7D); worker 对当前 NPC 的脚本对象
    ///     调 VMT+0x44 跑 "@DoAcceptBless"(无参), 正是本端口通用的 NPC label 机制
    ///     (NormNpc.GotoLable → PasEngine.TryCallNpcLabel)。worker 尾部"脚本失败→
    ///     SM 0x38FF + 刷新 NPC (0x6403CC/[0x7D5ECC])"仅在 GM 等级>3 可见, 且
    ///     0x6403CC 语义未证——按本端口既定策略(见 PasScriptHost.TryCallMonsterMain
    ///     的同类 GM-only 诊断省略)不复刻。
    ///   • 1300 = 门复刻 + 终局 fail-closed。leaf 只有 nil 门 (0x6DAA22)。worker 走
    ///     VMT+0x48 (带 array-of-const), 把客户端 Param 格式化成十进制串作为**唯一
    ///     位置参数**传给 "@DoRequestBless" 段。C# 的 label 接口 (TryCallNpcLabel)
    ///     不吃参数; TryCallNpcProcedure 解析的是 Pascal procedure 名而非 "@label"
    ///     段 → 无法忠实传参。丢弃该参数=对"段是否用此参"的无据臆断, 故 fail-closed。
    ///   • 1320 = 门复刻 + 终局 fail-closed。leaf 门: nil + 同图([+0x128]相等) +
    ///     15 格内(Chebyshev, sub_7743E0, cx=0xF) + Param∈{1,2,3}(0x6DABB4)。终局
    ///     的 SM 0x27A3 记录(见三件套③)含裸指针经内部队列, 未建模、无法推导。
    ///
    /// 挂钩（集成者：本文件遵循 TPlayObject.NativeCmProtocol_Q2.cs 的"自包含 + 由集成
    /// 者接线"约定, 不改 Operate 开关本身）：在 TPlayObject.Message.cs::Operate 的
    /// default: 腿里, **CM Q2 之前**加一条短路——保证 1300/1301/1320 由本子系统接管而非
    /// 落到 Q2 的 fail-closed drop：
    ///     default:
    ///         if (TryHandleCloneNpcCm(ProcessMsg)) break;          // ← 加此行(Q2 前)
    ///         if (!TryHandleNativeSocialProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)      // ← 现有链, 保持不动
    ///             &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    ///             result = base.Operate(ProcessMsg);
    ///         break;
    /// TryHandleCloneNpcCm 只对它拥有的 3 个 ident 返回 true。
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>
        /// 分身/当前-NPC 会话子系统的自包含入口。仅认领 CM 1300/1301/1320；其余一律
        /// 返回 false 交回原链。集成点见类头注释（须插在 TryHandleNativeCmQ2 之前）。
        /// </summary>
        private bool TryHandleCloneNpcCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1300:
                    ClientCloneNpc_1300_NpcRequestBless();
                    return true;
                case Grobal2.CM_1301:
                    ClientCloneNpc_1301_NpcAcceptBless();
                    return true;
                case Grobal2.CM_1320:
                    // 分发器把 word[msg+6]=Param 落到 nParam2 (Grobal2.cs 注释)。
                    ClientCloneNpc_1320_NpcSessionRequest(processMessage.nParam2);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 1300, leaf 0x6DAA17, worker 0x63D980 — 令当前 NPC 跑脚本段
        /// "@DoRequestBless"(0x6DBFD0), 并把客户端 Param 经 array-of-const 作为唯一
        /// 位置参数传入(VMT+0x48 → 0x733B98)。leaf 门: [self+0xCD8]==0 → 原生静默。
        ///
        /// 忠实边界: label 运行本身可建模(NormNpc.GotoLable), 但"带位置参数跑 @label"
        /// 无 1:1 C# 接口(TryCallNpcLabel 不吃参; TryCallNpcProcedure 解析 procedure
        /// 名而非 @label 段)。传参不可证、丢参属臆断 → 门后 fail-closed。
        /// </summary>
        private void ClientCloneNpc_1300_NpcRequestBless()
        {
            if (m_NPC == null)
            {
                return; // 0x6DAA22 je 0x6DBC2C — 无当前 NPC, 原生静默
            }

            CloneNpcFailClosed(Grobal2.CM_1300,
                "@DoRequestBless VMT+0x48(0x73A960→0x733B98) 带 array-of-const 位置参数" +
                "(客户端 Param 格式化串), C# label 接口不吃参, 无法忠实传参");
        }

        /// <summary>
        /// CM 1301, leaf 0x6DAA72, worker 0x63DC98 — 令当前 NPC 跑脚本段
        /// "@DoAcceptBless"(0x6DBFE8, 无参; VMT+0x44 → 0x733D84)。leaf 门:
        /// [self+0xCD8]==0 → 原生静默。**忠实实现**：走本端口通用 NPC label 机制。
        ///
        /// worker 尾部的失败诊断(GM 等级>3 才可见的 SM 0x38FF "[ExecScript Fail]" +
        /// 经 0x6403CC/[0x7D5ECC] 的"--ReInitializet--"刷新 NPC)语义未证, 按端口既定
        /// 策略(同 PasScriptHost.TryCallMonsterMain 省略同类 GM-only 诊断)不复刻。
        /// </summary>
        private void ClientCloneNpc_1301_NpcAcceptBless()
        {
            if (m_NPC == null)
            {
                return; // 0x6DAA7D je 0x6DBC2C — 无当前 NPC, 原生静默
            }

            if (m_NPC is not NormNpc npc)
            {
                // [self+0xCD8] 是被点击的脚本怪物(m_NPC=animal, 见 TPlayObject.cs:1365)：
                // 原生此时仍解 [monster+0x570].vmt+0x44, 但怪物脚本对象在 +0x4D0、
                // +0x570 是别的字段(PasScriptHost.cs:308-328) → 该分支未建模, fail-closed。
                CloneNpcFailClosed(Grobal2.CM_1301,
                    "当前会话对象为脚本怪物; 原生解 [monster+0x570] 与 NPC 脚本对象" +
                    "(npc+0x570)非同类, 未建模");
                return;
            }

            // VMT+0x44 (0x73AA20 → 0x733D84): 在 NPC 的脚本对象上跑 label(无参)。
            // ≡ NormNpc.GotoLable → PasEngine.TryCallNpcLabel。段不存在时静默 no-op,
            // 与原生 result=False 分支(仅 GM>3 诊断)一致。
            npc.GotoLable(this, "@DoAcceptBless", false);
        }

        /// <summary>
        /// CM 1320, leaf 0x6DAB6A, worker 0x765E68 — 构 0x28 字节请求记录 (SM 0x27A3)
        /// 经内部队列 sub_76C11C 投递。leaf 门(全部复刻)：
        ///   0x6DAB75 [self+0xCD8]==0                         → 原生静默
        ///   0x6DAB8A [m_NPC+0x128] != [self+0x128] (非同图)  → 原生静默
        ///   0x6DABA0 sub_7743E0(self,m_NPC,0xF)==0 (>15 格)  → 原生静默
        ///            (0x7743E0 = Chebyshev: abs(dX)&lt;=N &amp;&amp; abs(dY)&lt;=N,
        ///             +0x12C=CurrX, +0x130=CurrY)
        ///   0x6DABB4 Param∉{1,2,3}                            → 原生静默
        /// 门后终局: 记录含**裸 m_NPC 指针**(三件套③)且走**内部消息队列**, 非客户端
        /// 包 → 无建模、无法逐字节推导 → fail-closed。
        /// </summary>
        /// <param name="nParam">word[msg+6] = Param, 分发器落于 nParam2。</param>
        private void ClientCloneNpc_1320_NpcSessionRequest(int nParam)
        {
            if (m_NPC == null)
            {
                return; // 0x6DAB75 je 0x6DBC2C
            }
            if (m_NPC.m_PEnvir != m_PEnvir)
            {
                return; // 0x6DAB8A jne 0x6DBC2C — [+0x128] 同图门
            }
            if (Math.Abs(m_nCurrX - m_NPC.m_nCurrX) > 15 ||
                Math.Abs(m_nCurrY - m_NPC.m_nCurrY) > 15)
            {
                return; // 0x6DABA0 je 0x6DBC2C — sub_7743E0 15 格 Chebyshev 门
            }
            if (nParam < 1 || nParam > 3)
            {
                return; // 0x6DABB4 jae 0x6DBC2C — Param∈{1,2,3} 门
            }

            CloneNpcFailClosed(Grobal2.CM_1320,
                "SM 0x27A3 0x28 字节请求记录(含裸 m_NPC 指针) + 内部队列 sub_76C11C 未建模, " +
                "非客户端包体, 无法逐字节推导");
        }

        // ------------------------------------------------------------------
        // 本子系统自带的 fail-closed 记账：每 ident 每进程仅报一次, 丢包不回客户端。
        // 独立于 NativeCmQ2FailClosed(其 1300/1320 条目基于"会话对象未建模"的旧判断,
        // 本文件已订正为 m_NPC), 以记录**正确的**缺口原因, 且不改他人文件。
        // ------------------------------------------------------------------

        private static readonly HashSet<int> s_cloneNpcReported = new HashSet<int>();

        private static readonly object s_cloneNpcGate = new object();

        private void CloneNpcFailClosed(int ident, string blocker)
        {
            lock (s_cloneNpcGate)
            {
                if (!s_cloneNpcReported.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[CM未移植] CM {ident} (当前-NPC会话) 已丢弃; " +
                $"角色={(string.IsNullOrEmpty(m_sCharName) ? "<unknown>" : m_sCharName)}; " +
                $"缺口={blocker}");
        }
    }
}

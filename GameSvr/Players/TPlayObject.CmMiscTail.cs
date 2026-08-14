using System.Collections.Generic;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// CmMiscTail — 收尾四个零散、必然 fail-closed 的原生客户端消息:
    ///   CM 1248 拼合脚本 @AckPieceUp        (leaf 0x6DA58E -> worker 0x6E5384)
    ///   CM 4102 交易市场字段写 [+0x18DC/0x18DE] (leaf 0x6DABFC -> worker 0x6B7BCC)
    ///   CM 4204 短信认证码校验              (leaf 0x6DAF87 -> worker 0x6F03E8)
    ///   CM 4205 短信认证码下发              (leaf 0x6DAFAF -> worker 0x6F01E4)
    ///
    /// 这四个 ident 分别被 cm-1(Q1) / cm-3(Q3) / cm-4(Tail) 以 triage 级证据登记为 fail-closed
    /// (<see cref="NativeCmQ1FailClosed"/> 1248 / <see cref="NativeCmQ3FailClosed"/> 4102 /
    /// <see cref="NativeCmTailFailClosed"/> 4204+4205)。本分部把它们收敛为**权威、可审计**的处置:
    /// 逐 worker 反汇编 flat_image.bin(ImageBase 0x400000, capstone),据此
    ///   (a) 1:1 复刻唯一可求值的门 —— CM 1248 body&lt;0x20 静默;
    ///   (b) 其余(脚本对象 / 未建模字段 / 外部短信网关 / 未证倍率语义)一律 fail-closed 并登记完整证据。
    /// 反汇编原件见 tools/cmmisctail_re.py + tools/cmmisctail_dump.txt、字段读写全景见
    /// tools/cmmisctail_fieldxref.py。所有涉及的 CM/SM 常量已由 cm-1/cm-3/cm-4 加入 Grobal2(复用)。
    ///
    /// ============================================================================
    /// CM 1248 — 碎片拼合 @AckPieceUp  (三件套)
    /// ----------------------------------------------------------------------------
    /// VA:   leaf 0x6DA58E `movzx ecx,si`(bodyLen) / `mov edx,[ebp-8]`(body 指针) /
    ///       `mov eax,[ebp-4]`(self) -> call worker 0x6E5384。
    /// 逻辑: 0x6E53AF `cmp edi,0x20 / jl 0x6E556D` —— body&lt;0x20 直接跳到出口、不发包(唯一可求值门,已复刻)。
    ///       否则清 8 指针局部表,扫背包 [self+0x508]:对每件物品把 [item+0x18](UID)与 body 内 8 个 dword
    ///       逐一比对,命中计数;命中数 != [self+0x9C0..0x9C8] 之一时回 SM 0xB88(SM_2952) Recog=1;
    ///       再校验每件命中物品的 StdItem [item+0x1C]: byte[+0x14]==0x37 && byte[+0x15]==[self+0x9C0]、
    ///       Σword[+0x24]==[self+0x9C8];全过则 result=2,要求脚本对象 [self+0xCD8] 跑
    ///       '@AckPieceUp'(0x6E55B0,经 0x63D980),成功后删这 8 件(0x425020/0x404690)+刷新 0x73CEE4,
    ///       回 SM 0xB88 Recog=0。
    /// 缺口: 拼合配置字段 [self+0x9C0]/[0x9C4]/[0x9C8]、物品 [+0x18]/[+0x1C] 及其 StdItem [+0x14/+0x15/+0x24]、
    ///       以及脚本对象 [self+0xCD8]+'@AckPieceUp' 过程均未建模 —— SM 0xB88 的 Recog(0/1/2)无法推导。
    ///       故 body&gt;=0x20 时结算 fail-closed。
    ///
    /// ============================================================================
    /// CM 4102 — 交易市场字段写 [+0x18DC]/[+0x18DE]  (三件套)
    /// ----------------------------------------------------------------------------
    /// VA:   leaf 0x6DABFC: `cx=word[rec+8]`(Tag) / `dx=word[rec+6]`(Param) / push [ebp-8](body 指针) /
    ///       push esi(bodyLen) / eax=self -> call worker 0x6B7BCC(ret 8)。
    /// 逻辑: 0x6B7BD5 `cmp ebx(bodyLen),0xC`。长包腿(len&gt;=0xC 且 body!=0): [self+0x18DC]=word[body+0]、
    ///       [self+0x18DE]=word[body+2];短包腿(len&lt;0xC): [self+0x18DC]=Param、[self+0x18DE]=Tag。全程**不发包**。
    ///       消费端 0x68DAF9 `movzx bx,[self+0x18DC]` 后算 [owner+0x2B0]*常量(0x68DB58)*[self+0x18DC] 与
    ///       [owner+0x2AC] 比较返回 bool —— [self+0x18DC] 被当整数倍率用。
    /// 缺口: [self+0x18DC]/[0x18DE] 无 C# 模型;消费端 [owner+0x2AC]/[0x2B0]+常量未建模,倍率语义未证。
    ///       依§铁律"不臆造倍率语义",保持 fail-closed(丢包、不写该二字段)。
    ///
    /// ============================================================================
    /// CM 4205 — 短信认证码下发  (三件套)  worker 0x6F01E4
    /// ----------------------------------------------------------------------------
    /// VA:   0x6F0215 `call GetTickCount` / 0x6F021A `sub eax,[self+0x18D8]` / 0x6F0220 `cmp eax,0xBB8`。
    /// 逻辑: 冷却门 now-[self+0x18D8]&lt;0xBB8(3000ms) -> SM 0x106D(SM_4205) Recog=-1 + 系统消息
    ///       "发送短信太频繁"(0xFCFF, vmt+0xD4);否则 [self+0x18D0]!=0 静默返回;否则生成随机码(0x4C7820)写
    ///       [self+0x18D4],打包记录(头字 0x193 + 账号 [self+0xB09] + 地图 [self+0x106] + 码 + [self+0x278])
    ///       提交给管理器 [[0x7D62DC]] 的 0x71315C(跨服/短信网关通道)。冷却 tick [self+0x18D8] **在本 worker 内不写**。
    /// 缺口: [[0x7D62DC]] 0x71315C 即外部短信网关提交口(不可从镜像推导);冷却 tick [self+0x18D8] 与挂起标志
    ///       [self+0x18D0]、验证码串 [self+0x18D4] 仅由网关回执/host 侧写入(WRITE @0x654F13 tick、
    ///       @0x654D9D [0x18D0]=1、@0x654D96 [0x18D1]=1、@0x6B2460),这些路径本服未移植。因此冷却/挂起门恒
    ///       gate 在"从未置位"的状态上、无法独立触发 —— 整条下发链 fail-closed。
    ///
    /// ============================================================================
    /// CM 4204 — 短信认证码校验  (三件套)  worker 0x6F03E8
    /// ----------------------------------------------------------------------------
    /// VA:   eax=self、dx=Tag([ebp-2])、ecx=输入码串。0x6F0409/0x6F0464 `call 0x40591C` 比对
    ///       [self+0x18D4](已存码) 与输入码;0x6F044E `call 0x768BE0`(dx=0x2E)写主日志"校验短信验证码…"。
    /// 逻辑: [self+0x18D4] 为空或不等 -> 清码(0x405500)、SM 0x106E(SM_4206) Recog=-1;若
    ///       now-[self+0x18D8]&gt;0x1B7740(30min)再发系统消息"验证码已过期"(0xFCFF);Tag==0xA 追发
    ///       SM 0x21F(SM_543) Recog=-6。相等 -> 清码、[self+0x18D1]=1、[self+0x18D0]=1、SM 0x106E Recog=0;
    ///       Tag==0xA 调 0x6D6460、Tag==0xF 调 0x6F0B84([self+0xA50])。
    /// 缺口: 已存码 [self+0x18D4] 只由 4205 的网关下发链写入(见上, WRITE @0x654xxx / lea @0x654BA4/0x654C20)。
    ///       本服网关 fail-closed => 该码恒空 => 校验恒走失败腿;而失败腿的回包/过期系统消息/Tag 分支均反映
    ///       网关建立的状态(已存码、tick、挂起标志),不可忠实推导 —— 校验链 fail-closed。
    ///
    /// ============================================================================
    /// INTEGRATOR HOOKUP (防冲突: 本分部不改 TPlayObject.Message.cs / 他人文件)
    /// ----------------------------------------------------------------------------
    /// 在 Operate() 的 `default:` 臂里，把对 <see cref="TryHandleCmMiscTail"/> 的调用插到
    /// TryHandleNativeCmTailProtocol 之前 —— 这样同时满足"1248 插 Q1 前 / 4102 插 Q3 前 / 4204+4205 插
    /// Tail 前"(Tail 是三者中最靠前的一臂),使本权威处置优先于 cm-1/cm-3/cm-4 的浅层 fail-closed 臂:
    ///
    ///     default:
    ///         if (!TryHandleInlayCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleQiankunCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleItemTransferCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleStallWriteCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleEquipLockCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleQuizBroadcastCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleCloneNpcCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeSocialProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleCmMiscTail(ProcessMsg)          // ← add this, before Tail/Q1/Q3
    ///             &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    ///         {
    ///             result = base.Operate(ProcessMsg);
    ///         }
    ///         break;
    /// </summary>
    public partial class TPlayObject
    {
        // 每 ident 每进程只登记一次 fail-closed，避免被刷屏。
        private static readonly HashSet<int> s_cmMiscTailReported = new HashSet<int>();

        private static readonly object s_cmMiscTailGate = new object();

        /// <summary>
        /// 收尾派发 CM 1248 / 4102 / 4204 / 4205。命中其一即 return true(调用方短路,越过其后的
        /// Tail/Q1/Q3 fail-closed 臂)。派发说明与逐 worker 数据流见类级注释;INTEGRATOR HOOKUP 说明
        /// 见类级注释(插在 TryHandleNativeCmTailProtocol 之前)。
        /// </summary>
        private bool TryHandleCmMiscTail(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1248:
                    ClientCmMiscTailPieceUp1248(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_4102:
                    ClientCmMiscTailTradeField4102();
                    return true;
                case Grobal2.CM_4204:
                    ClientCmMiscTailSmsVerify4204();
                    return true;
                case Grobal2.CM_4205:
                    ClientCmMiscTailSmsIssue4205();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 1248 拼合脚本 @AckPieceUp。worker 0x6E5384 的第一动作
        /// <c>0x6E53AF cmp edi,0x20 / jl 0x6E556D</c>:body 不足 0x20 字节时 战神 什么都不做、不发包 ——
        /// 这是唯一可从 nBodyLen 求值的门,1:1 复刻为静默返回。body&gt;=0x20 的结算依赖未建模的拼合配置字段
        /// [self+0x9C0]/[0x9C4]/[0x9C8]、物品 StdItem 字段与脚本对象 [self+0xCD8]+'@AckPieceUp',SM 0xB88
        /// (SM_2952) 的 Recog 无法推导,故 fail-closed。
        /// </summary>
        private void ClientCmMiscTailPieceUp1248(int nBodyLen)
        {
            // 0x6E53AF `cmp edi,0x20 / jl 0x6E556D` —— 短 body 是 战神 的忠实静默(edi 来自 movzx,故取低 16 位)。
            if ((nBodyLen & 0xFFFF) < 0x20)
            {
                return;
            }

            ReportCmMiscTailGap(Grobal2.CM_1248, "碎片拼合 @AckPieceUp",
                "worker 0x6E5384: 扫背包 [self+0x508] 比对 8 个 UID、核 [self+0x9C0]/[0x9C4]/[0x9C8] 与 " +
                "StdItem [+0x14=0x37]/[+0x15]/[+0x24],经脚本对象 [self+0xCD8] 跑 '@AckPieceUp'(0x6E55B0/0x63D980) " +
                "成功后删物品并回 SM 0xB88(SM_2952); 拼合配置字段与脚本对象未建模,Recog 不可推导");
        }

        /// <summary>
        /// CM 4102 交易市场字段写。worker 0x6B7BCC 只把两个 word 写进 [self+0x18DC]/[0x18DE](长包腿取
        /// body[0]/body[2],短包腿取 Param/Tag),**不发任何 SM**。消费端 0x68DAF9 把 [self+0x18DC] 当整数倍率
        /// (乘 [owner+0x2B0] 与常量后同 [owner+0x2AC] 比较),但 [owner+0x2AC]/[0x2B0]+常量未建模、语义未证。
        /// 依§铁律不臆造倍率语义,保持 fail-closed(丢包、不写该二字段)。
        /// </summary>
        private void ClientCmMiscTailTradeField4102()
        {
            ReportCmMiscTailGap(Grobal2.CM_4102, "交易市场字段写 [self+0x18DC/0x18DE]",
                "leaf 0x6DABFC(Param=word[rec+6], Tag=word[rec+8]) -> worker 0x6B7BCC: len>=0xC 写 " +
                "[self+0x18DC]=word[body+0]/[0x18DE]=word[body+2], 短包腿写 Param/Tag; 无 SM 回包。" +
                "消费端 0x68DAF9 当整数倍率用([owner+0x2B0]*常量(0x68DB58)*[self+0x18DC] vs [owner+0x2AC]); " +
                "该二字段无 C# 模型且倍率语义未证 —— 不臆造");
        }

        /// <summary>
        /// CM 4204 短信认证码校验。worker 0x6F03E8 比对 [self+0x18D4](已存码)与输入码后回 SM 0x106E
        /// (SM_4206) Recog 0/-1(并可能发"验证码已过期"系统消息、Tag==0xA 追发 SM 0x21F/SM_543)。已存码只由
        /// 4205 的外部短信网关下发链写入(WRITE @0x654xxx),本服网关 fail-closed => 该码恒空 => 校验恒失败;
        /// 但失败腿的回包/过期消息/Tag 分支都反映网关建立的状态,不可忠实推导 —— fail-closed。
        /// </summary>
        private void ClientCmMiscTailSmsVerify4204()
        {
            ReportCmMiscTailGap(Grobal2.CM_4204, "短信认证码校验",
                "worker 0x6F03E8: 比对 [self+0x18D4] 与输入码(0x40591C),回 SM 0x106E(SM_4206) Recog 0/-1; " +
                "失败且 now-[self+0x18D8]>0x1B7740(30min) 发系统消息'验证码已过期', Tag==0xA 追发 SM 0x21F(SM_543)。" +
                "已存码/tick/挂起标志仅由外部短信网关回执链写入(@0x654F13/0x654D96/0x654D9D/0x6B2460),未移植 —— 校验恒失败且回包反映网关态,不可推导");
        }

        /// <summary>
        /// CM 4205 短信认证码下发。worker 0x6F01E4 的冷却门 now-[self+0x18D8]&lt;0xBB8(3000ms) 回 SM 0x106D
        /// (SM_4205) Recog=-1 + 系统消息"发送短信太频繁";否则 [self+0x18D0]!=0 静默;否则生成码写 [self+0x18D4]
        /// 并提交给管理器 [[0x7D62DC]] 的 0x71315C(外部短信网关)。冷却 tick [self+0x18D8] / 挂起标志
        /// [self+0x18D0] 仅由网关回执链写入(本 worker 内不写),本服未移植 => 门恒 gate 在从未置位的状态上、
        /// 无法独立触发 —— 整条下发链 fail-closed。
        /// </summary>
        private void ClientCmMiscTailSmsIssue4205()
        {
            ReportCmMiscTailGap(Grobal2.CM_4205, "短信认证码下发",
                "worker 0x6F01E4: 冷却门 now-[self+0x18D8]<0xBB8(3000ms) 回 SM 0x106D(SM_4205) Recog=-1 + 系统消息" +
                "'发送短信太频繁';否则生成随机码写 [self+0x18D4] 并经管理器 [[0x7D62DC]] 0x71315C 提交(外部短信网关)。" +
                "tick [self+0x18D8]/挂起 [self+0x18D0] 仅由网关回执链写入(@0x654F13/0x654D9D),未移植 —— 网关不可复刻,门不可独立触发");
        }

        /// <summary>
        /// 丢弃该 CM 并每 ident 每进程登记一次完整证据(worker VA + 原生接口 + 缺口)。不向客户端发任何包 ——
        /// 战神 在此要构造的回包/字段写取决于脚本对象/未建模字段/外部短信网关,均无法从镜像推导,发出即等于凭空
        /// 编造字节。这四个 ident 的规范缺口条目亦见 <see cref="NativeCmQ1FailClosed"/>(1248)/
        /// <see cref="NativeCmQ3FailClosed"/>(4102)/<see cref="NativeCmTailFailClosed"/>(4204+4205);本处置以更深
        /// 证据取代之并经 INTEGRATOR HOOKUP 优先于其臂,故只由本 reporter 登记。
        /// </summary>
        private void ReportCmMiscTailGap(int ident, string subsystem, string evidence)
        {
            lock (s_cmMiscTailGate)
            {
                if (!s_cmMiscTailReported.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[CmMiscTail] CM {ident} ({subsystem}) 已丢弃(fail-closed); {evidence}; " +
                $"角色={(string.IsNullOrEmpty(m_sCharName) ? "<unknown>" : m_sCharName)}");
        }
    }
}

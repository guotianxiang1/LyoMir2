using System;
using System.Collections.Generic;

namespace GameSvr.Services
{
    /// <summary>
    /// Registry for CM missing-set Quarter 1 (idents 1054..1260, the lowest 25 of
    /// the 99-item gap between 战神's CM dispatch arms and the C# Operate() coverage)
    /// whose 战神 handler resolves into a subsystem this port cannot reproduce from
    /// image bytes.
    ///
    /// The dispatcher is sub_6D7D68; its selector tree is rooted at 0x6D805C and every
    /// arm that does nothing jumps to the shared exit label 0x6DBC2C
    /// (`33 C0 5A 59 59 64 89 10 E9 D5 00 00 00`). Every ident registered here reaches
    /// a REAL leaf and a REAL worker — none is the 0x6DBC2C no-op sink (the missing set
    /// excludes no-ops by construction). What is withheld is only the terminal action
    /// and its SM reply, because the reply body is a function of runtime subsystem
    /// state — shop / 元宝寄售 / booth / quiz-broadcast managers, the task-board and
    /// piece-up scripts, the equip-secret lock, the std-item/strengthen tables — none
    /// of which is a compile-time constant in the image. Emitting such a reply would
    /// put invented bytes on the wire, so the packet is dropped instead and the gap is
    /// recorded here, throttled once per ident.
    ///
    /// This mirrors cm-4 (NativeCmTailFailClosed, idents 4125..4651, whole quarter
    /// fail-closed) and the C# port's own deliberate dormancy of these write-side
    /// subsystems (NativeStallWriteGate off by default; NativeYbDealPurchaseStateMachine
    /// host-driven and dormant; the read-only 元宝 views CM 1252/1253/1256/1257 are the
    /// only wired half). The class name is the Q1 distinguisher, so nothing here
    /// collides with cm-4's NativeCmTailFailClosed.
    /// </summary>
    internal static class NativeCmQ1FailClosed
    {
        /// <summary>Native evidence for one unported CM Q1 handler.</summary>
        internal readonly struct Entry
        {
            public Entry(int ident, uint handlerVa, uint calleeVa, string subsystem,
                string blocker)
            {
                Ident = ident;
                HandlerVa = handlerVa;
                CalleeVa = calleeVa;
                Subsystem = subsystem;
                Blocker = blocker;
            }

            /// <summary>CM ident as it appears in the dispatch tree.</summary>
            public int Ident { get; }

            /// <summary>Tree leaf the selector resolves this ident to.</summary>
            public uint HandlerVa { get; }

            /// <summary>Worker the leaf calls, i.e. the entry into the unported code.</summary>
            public uint CalleeVa { get; }

            public string Subsystem { get; }

            /// <summary>What has to exist in C# before the terminal action can run.</summary>
            public string Blocker { get; }
        }

        private static readonly Dictionary<int, Entry> Entries = Build();

        private static readonly HashSet<int> Reported = new HashSet<int>();

        private static readonly object Gate = new object();

        private static Dictionary<int, Entry> Build()
        {
            var map = new Dictionary<int, Entry>();
            void Add(int ident, uint handler, uint callee, string subsystem, string blocker)
                => map[ident] = new Entry(ident, handler, callee, subsystem, blocker);

            // ---- shop/mall manager [[0x7D5D98]] (shopMgr, named by NativeGmItemCommands
            //      case 0x624DD6 sub_63B4E4(shopMgr@off_7D5D98,...)) ----------------------
            Add(1054, 0x006D942F, 0x006D3694, "商城/mall 提交",
                "0x6D942F 节流 [self+0x788]<0x7D0(2000ms) 后经 0x6D3694 把 {name[self+0xAF4], " +
                "map[self+0x106], [self+0xB09], [self+0xB33]} 提交给 shopMgr [[0x7D5D98]] 的 " +
                "0x637A00(subcmd 0x7B)；提交结果决定是否回 SM 0x38FF(0x6DBF88 '请稍候')，" +
                "shopMgr 运行态与 [self+0x788] 均未建模");
            Add(1055, 0x006D9492, 0x006D3694, "商城/mall 提交",
                "0x6D9492 节流 [self+0x788]<0x7D0 后按 Param(1..4)映射 subcmd={0x6F,0x75,0x7A,0x7D} " +
                "经 0x6D3694 提交给 shopMgr [[0x7D5D98]] 0x637A00(dx=0x6B)；Param 越界则静默，" +
                "但节流字段与 shopMgr 运行态未建模，无法到达该门");
            Add(1056, 0x006D953A, 0x006CB9B4, "商城/mall 提交",
                "0x6CB9B4 门 0x6C7D88(self,1) + [self+0x758]>0 后经 0x6D3694 提交 subcmd 0x76 给 " +
                "shopMgr [[0x7D5D98]]；门字段 [self+0x758] 与 shopMgr 未建模");
            Add(1057, 0x006D9547, 0x006CB9F0, "商城/mall 提交",
                "0x6CB9F0 门 0x6C7D88(self,1)+[self+0x75C]>0+[self+0x758]>0，[vmt+0x244] 成功则 " +
                "提交 subcmd 0x75，失败回 SM 0x38FF(0x6CBA64 '[失败]…单位不足，无法领取。')；" +
                "[self+0x758]/[self+0x75C] 与 shopMgr [[0x7D5D98]] 未建模");

            // ---- 0x6D7794 activity throttle -----------------------------------------------
            Add(1059, 0x006D9554, 0x006D7794, "限时活动确认",
                "0x6D7794 节流 [self+0x744]<0x2710(10000ms)+标志 [self+0x757] 后调 0x6E3944(self,dl=1)；" +
                "节流/标志字段与 0x6E3944 目标子系统未建模");

            // ---- skill-stone copy ---------------------------------------------------------
            Add(1061, 0x006D9579, 0x006CBDD4, "技能石复制",
                "leaf 空 body 门(test si,si)由 NativeClientBodyLengthGate[1061] 上游复现；非空时 " +
                "0x6CBDD4 要求 body>=0x3C，按 0x3C 字节记录在背包 [self+0x508] 里比对并经管理器 " +
                "[[0x7D5F20]]/[[0x7D6630]] 复制技能石，记录格式与两管理器未建模");

            // ---- equip-secret confirmation lock ------------------------------------------
            //      NativeMakeItemUseDiamHost: [player+0x711] 锁 / SM_LOCKEQUIP(689) / CM 1068,1084
            //      经核实本 C# 服务端不存在(无 +0x711 字段、无 CM 1068/1084、无 SM_LOCKEQUIP)。
            Add(1068, 0x006D959B, 0x006D1780, "装备密码锁输入",
                "0x6D1780 以 Param 为 0..0xB 走 12 路跳表(0x6D17F8)驱动装备密码锁输入(0x6D1A98 " +
                "'系统已禁能交易输入…')；锁状态 [player+0x711] 及该子系统经 NativeMakeItemUseDiamHost " +
                "核实本服不存在，回码无法推导");
            Add(1084, 0x006D95C9, 0x006D1AB8, "装备密码锁计时",
                "0x6D1AB8 门 [self+0xB78]>0 + 0x6C7D88(self,1)，按 (0x2BF20-(now-[self+0x740]))/1000 " +
                "算剩余秒并回 SM 0x2733/0x2737；[self+0xB78]/[0xB7B]/[0x74C]/[0x740] 等锁字段未建模");

            // ---- quiz / cross-server broadcast manager [[0x7D62DC]] (off_7D62DC, named by
            //      NativeGmItemExtraCommands reloadStditem sub_713094([off_7D62DC],...)) -----
            Add(1090, 0x006D9732, 0x006BD674, "答题/广播提交",
                "0x6BD674(cl=0) 校验 [self+0x7C3]/[0x7C4] 答题态，经 [[0x7D5D6C]] 0x750F3C + " +
                "0x6C87B4 结算并回 '回答正确,请稍后再来'(0xFFDB)/'超过次数'(0x38FF)，提交经 " +
                "[[0x7D62DC]] 0x71315C；答题字段 [self+0x7B0..0x7C4] 与两管理器未建模");
            Add(1200, 0x006DA21F, 0x006BD674, "答题/广播提交",
                "同 1090 worker 0x6BD674，cl=(Param==1)，body 串为答案；[self+0x7B0..0x7C4] 答题态 " +
                "与管理器 [[0x7D62DC]]/[[0x7D5D6C]] 未建模");
            Add(1217, 0x006DA372, 0x006C53B8, "广播提交",
                "0x6C53B8 把 {name[self+0xAF4], map[self+0x106], body} 以 subcmd 0x165 提交给 " +
                "[[0x7D62DC]] 0x71315C；管理器未建模");

            // ---- booth / stall trade manager [[0x7D7190]] (envir [self+0x128] cell ops;
            //      C# NativeStall* write side dormant, NativeStallWriteGate off) ------------
            Add(1210, 0x006DA418, 0x006E3974, "摆摊交易",
                "leaf 门 [self+0x1899]==0；0x6E3974(cl=Param) 在 Param==1 且 [envir+0x7C]==0 时调 " +
                "[[0x7D7190]] 0x612F6C，返回<=0 回 SM 0x6C2；摆摊管理器 [[0x7D7190]] 与 [self+0x1899] 未建模");
            Add(1211, 0x006DA45D, 0x006E39C8, "摆摊交易",
                "leaf 门 [self+0x1899]==0；0x6E39C8 在 [envir+0x7C]==0 时调 [[0x7D7190]] 0x6131A0，" +
                "结果<=0 回 SM 0x6C3、<0 再调 0x6137E0；管理器 [[0x7D7190]] 未建模");
            Add(1212, 0x006DA49B, 0x006E3A34, "摆摊交易",
                "leaf 门 [self+0x1899]==0；0x6E3A34 以 Param 调 [[0x7D7190]] 0x6137E0；管理器未建模");
            Add(1213, 0x006DA4BF, 0x006E3A4C, "摆摊交易",
                "leaf 门 [self+0x1899]!=0；Tag==1 先过交易费门 0x6151CC(读 [self+0xD8]/[0xED]/[0xF7] " +
                "'交易费')与 0x6152B8 后调 0x6E3A4C(->0x613B40)回 SM 0x6C6/0x6CA；交易费字段与 " +
                "摆摊 envir 状态未建模");
            Add(1214, 0x006DA529, 0x006E3A88, "摆摊交易",
                "leaf 门 [self+0x1899]!=0；0x6E3A88(->0x613A88(envir,...))回 SM 0x6C8；摆摊 envir 状态未建模");

            // ---- piece-up / fragment synthesis (script @AckPieceUp) -----------------------
            Add(1248, 0x006DA58E, 0x006E5384, "碎片拼合",
                "0x6E5384 需 body>=0x20(可由 nBodyLen 复现静默)，在背包 [self+0x508] 按 8 个 id 比对并核对 " +
                "[self+0x9C0]/[0x9C4]/[0x9C8]，经脚本对象 [self+0xCD8] 跑 '@AckPieceUp'(0x6E55B0) 成功后 " +
                "删物品并回 SM 0xB88；拼合计数字段与脚本对象未建模");

            // ---- task-publish board script object [[0x7D5D20]] (same as cm-4 4150/4151) ----
            Add(1250, 0x006DA5A1, 0x006E1CEC, "任务发布板",
                "0x6E1CEC 在 [[0x7D5D20]]+0 非空时调脚本 0x6996E8 生成列表并回 SM 0xB31；" +
                "任务板脚本对象 [[0x7D5D20]] 与 @Main 过程未移植(同 cm-4 CM 4150/4151/4651)");

            // ---- 元宝寄售 (YB consignment) manager [[0x7D6ABC]] (named by
            //      NativeYbConsignmentQuery: 'the MANAGER at [[0x7D6ABC]], a singleton') -----
            Add(1251, 0x006DA66A, 0x006E7E0C, "元宝寄售",
                "0x6E7E0C 先过 0x6F9594(元宝系统开启门)再调 0x6CB94C(self, ecx=bodylen, edx=body, Tag)；" +
                "寄售管理器写侧未建模(C# NativeYbDealPurchaseStateMachine 休眠)");
            Add(1254, 0x006DA69F, 0x006F9538, "元宝寄售(成交)",
                "0x6F9538 以 {map[self+0x106], Recog} 调 [[0x7D6ABC]] 0x6326F4 —— 即 CM 1254 成交回调；" +
                "C# NativeYbDealPurchaseStateMachine 已建模但按设计休眠，回码不可推导");
            Add(1255, 0x006DA6B1, 0x006E8350, "元宝寄售",
                "0x6E8350 以 {map[self+0x106], Recog} 调 [[0x7D6ABC]] 0x632B4C；寄售管理器写侧未建模");
            Add(1258, 0x006DA6C3, 0x006E82F4, "元宝寄售",
                "0x6E82F4 以 {map[self+0x106], Recog} 调 [[0x7D6ABC]] 0x632FC4；寄售管理器写侧未建模");
            Add(1259, 0x006DA6EF, 0x006E8454, "元宝寄售(开启提交)",
                "0x6E8454 过 0x6F9594(元宝系统开启门)后经 0x6D3694 提交 dx=0x70 给 shopMgr [[0x7D5D98]]，" +
                "失败回 '元宝系统暂时关闭…'(0x6E8494)；开启态与管理器未建模");
            Add(1260, 0x006DA6FC, 0x006E84BC, "设置元宝交易金额",
                "0x6E84BC 以 Param(0→0xFFFF)在 [self+0x4B7]==0 时写 [self+0x18A0] 并回 SM 0xBC2，" +
                "否则写 [self+0x18A2] 调 0x6D64B8('修改元宝交易金额')；[self+0x18A0] 有持久化模型 " +
                "(m_nNativeTradeProtectAmount) 但门 [self+0x4B7]、[self+0x18A2] 及 0x6D64B8 分支未建模");

            return map;
        }

        internal static IReadOnlyDictionary<int, Entry> All => Entries;

        /// <summary>
        /// Drop the packet and record the gap once per ident per process. Nothing is
        /// sent to the client, because the reply 战神 would build here cannot be derived
        /// from the image.
        /// </summary>
        internal static void Drop(int ident, string charName)
        {
            if (!Entries.TryGetValue(ident, out var entry))
            {
                throw new ArgumentOutOfRangeException(nameof(ident),
                    $"CM {ident} 不在 CM Q1 未移植清单里");
            }

            lock (Gate)
            {
                if (!Reported.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[CM未移植/Q1] CM {entry.Ident} ({entry.Subsystem}) 已丢弃; " +
                $"handler=0x{entry.HandlerVa:X6} callee=0x{entry.CalleeVa:X6}; " +
                $"角色={(string.IsNullOrEmpty(charName) ? "<unknown>" : charName)}; " +
                $"缺口={entry.Blocker}");
        }
    }
}

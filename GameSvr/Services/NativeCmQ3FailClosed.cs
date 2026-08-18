using System;
using System.Collections.Generic;

namespace GameSvr.Services
{
    /// <summary>
    /// Registry for the third quarter (idents 3180..4124) of the missing native
    /// client-message handlers whose 战神 worker resolves into a subsystem this
    /// port has not modelled yet.
    ///
    /// Every ident here reaches a REAL leaf of the CM dispatch tree (sub_6D7D68,
    /// selector root 0x6D805C, `word[record+4]`) — none of them falls through to
    /// the shared exit label 0x6DBC2C. The gates 战神 evaluates BEFORE the unported
    /// call are reproduced 1:1 at the call sites in
    /// TPlayObject.NativeCmProtocol_Q3.cs; only the terminal action is withheld.
    ///
    /// Withholding is deliberate (§铁律 fail-closed): emitting an SM reply whose
    /// body/Recog reflects state this port cannot derive from the image would put
    /// invented bytes on the wire, so the packet is dropped instead and the gap is
    /// recorded here. The record is throttled per ident so a client spamming an
    /// unported opcode cannot flood the log.
    ///
    /// HandlerVa / CalleeVa were read from flat_image.bin (ImageBase 0x400000) with
    /// capstone; see tools/cm3_leaf.py, tools/cm3_worker.py, tools/cm3_triage.py.
    /// </summary>
    internal static class NativeCmQ3FailClosed
    {
        /// <summary>Native evidence for one unported CM Q3 handler.</summary>
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

            /// <summary>Worker the leaf tail-calls, i.e. the unported code.</summary>
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

            Add(3180, 0x006DA405, 0x006E3280, "名册/成员槽匹配",
                "worker 用 0x73CF08 按名查对象并匹配 [obj+0x1C]+0x44 的 5×6 槽表，回发 SM 0x24 明细与 SM 0x6BF 状态；名册表与槽结构未建模（nBodyLen<0x18 短包腿已复现为 SM 0x6BF 全零）");
            Add(3190, 0x006DA5AE, 0x006E590C, "成员集合查询",
                "[self+0x508] 成员集合 + 管理器 [0x7D5D6C]，SM 0xB40 的 Recog 取自 0x752A20 结果；集合与管理器未建模");
            Add(3191, 0x006DA5C0, 0x006E5BA8, "成员集合操作(日期门)",
                "leaf 用服务器日期 [0x7D6A88] 经 0x40EB9C 解码做 month/day 门；worker 走 [self+0x508] 成员集合并回 SM 0xB41；日期全局与成员集合未建模");
            Add(3208, 0x006DA54C, 0x006EA5E0, "任务发布板命令A",
                "任务板脚本对象 [0x7D5D20]、对象表 [0x7D6D50]、字段 [+0xA1C]；脚本过程未移植");
            Add(3209, 0x006DA56D, 0x006EA858, "任务发布板命令B",
                "同任务板 [0x7D5D20]；短包腿经 0x765E68 发 notice 0x3008；脚本与 notice 未移植");
            Add(3282, 0x006DA600, 0x006E64BC, "按名查询回执",
                "0x73CF08 名查 + [0x7D5D6C]，字段 [+0x3C0]，SM 0xCD3 (vmt+0x254 与 +0x250)；对象表未建模（nBodyLen<0x14 短包腿已复现为静默）");
            Add(3283, 0x006DA626, 0x006E67B0, "槽数组重建",
                "字段 [+0x9F4]/[+0x9F8]/[+0x9FC] 集合 + [0x7D64B8]；集合结构未建模");
            Add(3284, 0x006DA650, 0x006E6EA4, "槽数组清空",
                "由 Q3 前置 TryHandleQiankunCm 实现；Q3 fallback 不应成为第二 owner");
            Add(3285, 0x006DA638, 0x006E6DE8, "槽数组应用",
                "dl=(Param==1)，读写 [+0x9F4]/[+0x9FC]，链 0x6E68A8/0x6DF62C/0x6D3694；集合未建模");
            Add(3286, 0x006DA65D, 0x006E6B54, "槽数组提交",
                "空列表腿由 Q3 前置 TryHandleQiankunCm 实现；非空配置奖励仍 fail-closed");
            Add(3287, 0x006DA895, 0x006E8734, "宠物/召唤命令A",
                "[0x7D6784] + [+0x128]/[+0x760]/[+0x9A0]，ecx=MakeLong(Param,Tag,Series)；子系统未建模");
            Add(3288, 0x006DA8C4, 0x006E8820, "宠物/召唤命令B",
                "同 3287 子系统 + [+0x178]；子系统未建模");
            Add(3294, 0x006DA613, 0x006EB190, "摆摊/展示建立",
                "[+0x3C4]/[+0xB74]/[+0x18BC]/[+0x18C0] + [0x7D5D6C]/[0x7D7038]/[0x7D6784]，SM 0xCF1/0xB9A；未建模（nBodyLen<4 短包腿已复现为静默）");
            Add(3306, 0x006DAB39, 0x006EFD54, "按名双值操作",
                "[+0x12C]/[+0x130]/[+0x24C] + [0x7D5D6C]，SM 0x275/0x276；未建模（leaf nBodyLen<4 已复现为静默）");
            Add(3307, 0x006DABEA, 0x006CBD78, "成员关系操作",
                "[+0x508]/[+0x258]/[+0x248] + [0x7D5D6C]/[0x7D5F20]，SM 0xD04；未建模");
            Add(3340, 0x006DAC30, 0x0079E78C, "文本命令(配置门)",
                "leaf 门 byte[[0x7D7038]+3]&0x20 是配置标志（不可从镜像求值）；worker 用 [0x7D5ECC]+[0x7DD050] 执行 [self+0x106]/[self+0xB09] 字符串命令；未建模");
            Add(3344, 0x006DADD6, 0x006EC5D8, "字段对更新",
                "[+0x1F0]/[+0x1F4]/[+0x290]，链 0x6BCE2C/0x741698；未建模");
            Add(3410, 0x006DAED9, 0x006EBE50, "定长40字节记录",
                "leaf 门 nBodyLen==0x28；worker 读 body[+0x10]/[+0x20]/[+0x24] 并走 [+0x760]/[+0xA10]/[+0xA14]/[+0xA18] + [0x7D6D50]，SM 0xD27；未建模（nBodyLen!=0x28 已复现为静默）");
            Add(3503, 0x006DAF44, 0x006EF970, "英雄相关通知",
                "hero [+0xBB0] 门；有英雄时 0x690A24 返回 -1/-2 决定 SM 0x38FF 通知文本 (0x6EFA04/0x6EFA20)；hero 子系统未建模（无英雄腿已复现为静默）");
            Add(4102, 0x006DABFC, 0x006B7BCC, "交易/市场命令",
                "globals [0x7D62DC]/[0x7D6214]/[0x7D5C0C]/[0x7D6038]/[0x7D5D98] + [+0x18DC]/[+0x18DE]/[+0xAF4]；短包腿写 [+0x18DC]/[+0x18DE]；未建模");
            Add(4105, 0x006DA005, 0x007742C0, "状态刷新三连",
                "leaf 依次调 0x7742C0(隐身态 0x40 揭示，已建模为 BreakNativeStealthOnAction)、0x6BCE2C(取消挂起通道三连 0x4D0/0x4D2/0xD57，已建模为 CancelNativeActionChannels)、0x6EE174(召唤坐骑，读 [+0x4C0]/[+0xA24]==0x72/[+0x1914])；前两个 worker 已建模，第三个未建模，故整臂仍丢弃");
            Add(4123, 0x006DAE32, 0x006BF908, "洗灵(英雄/本人)",
                "Tag==1&&hero 走英雄腿 / Tag==0 走本人腿，均用洗灵字段 [+0x610]/[+0x5A4] 与链 0x747B38/0x747878/0x74738C，回 SM 0xFC3；洗灵字段未建模（Tag 非法腿已复现为 SM 0xFC3 Recog=1）");
            Add(4124, 0x006DAE53, 0x006BFA88, "洗灵(英雄/本人)-2",
                "同 4123 家族，Tag 选本人/英雄腿，洗灵字段 [+0x610] 与链 0x747878/0x746EA8/0x747444，SM 0xFC3；未建模（Tag 非法腿已复现为 SM 0xFC3 Recog=0）");

            return map;
        }

        internal static IReadOnlyDictionary<int, Entry> All => Entries;

        /// <summary>
        /// Drop the packet and record the gap once per ident per process. Nothing
        /// is sent to the client, because the reply 战神 would build here cannot be
        /// derived from the image.
        /// </summary>
        internal static void Q3Drop(int ident, string charName)
        {
            if (!Entries.TryGetValue(ident, out var entry))
            {
                throw new ArgumentOutOfRangeException(nameof(ident),
                    $"CM {ident} 不在 CM 第三片(Q3)未移植清单里");
            }

            lock (Gate)
            {
                if (!Reported.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[CM未移植Q3] CM {entry.Ident} ({entry.Subsystem}) 已丢弃; " +
                $"handler=0x{entry.HandlerVa:X6} callee=0x{entry.CalleeVa:X6}; " +
                $"角色={(string.IsNullOrEmpty(charName) ? "<unknown>" : charName)}; " +
                $"缺口={entry.Blocker}");
        }
    }
}

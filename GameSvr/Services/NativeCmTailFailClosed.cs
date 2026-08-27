using System;
using System.Collections.Generic;

namespace GameSvr.Services
{
    /// <summary>
    /// Registry for the CM idents in 4125..4651 whose 战神 handler resolves into a
    /// subsystem that has no C# model yet.
    ///
    /// Every ident here reaches a REAL leaf of the native dispatch tree
    /// (sub_6D7D68, selector root 0x6D805C) — none of them is one of the 233
    /// idents that fall through to the shared exit label 0x6DBC2C. The gates that
    /// 战神 evaluates BEFORE the unported call are reproduced 1:1 at the call
    /// sites in TPlayObject.NativeCmTailProtocol.cs; only the terminal action is
    /// withheld.
    ///
    /// Withholding is deliberate. The alternative — emitting the SM reply with a
    /// body this port cannot derive from the image — would put invented bytes on
    /// the wire, so the packet is dropped instead and the gap is recorded here.
    /// The record is throttled per ident so a client that spams an unported
    /// opcode cannot flood the log.
    /// </summary>
    internal static class NativeCmTailFailClosed
    {
        /// <summary>Native evidence for one unported CM tail handler.</summary>
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

            // 【已废弃条目，2026-08-27】4125 已由 TPlayObject.NativeCmTailProtocol.cs 的
            // ClientNativeShenYouAttributeQuery 完整实现，本表的 Drop 不可达。
            // 最后一个阻塞点 byte[[0x7D6938]] 已定位：mir2Actor.ini [setup]/ShenYouAbilSwitch，
            // 由 0x755350 结尾的 0x7555F1 ReadBool / 0x755604 写入（三条出口都会走），
            // GM 臂 0x628AA3 -> 0x6BF658 负责改写并回写。保留条目仅为不破坏 Drop 的参数校验。
            Add(4125, 0x006DAE25, 0x00746C34, "神佑属性表查询",
                "【SUPERSEDED】已实现：Count<=0 (0x746C4A jle) 两包都不发；否则 SM 4032 " +
                "(Recog=Count, Param=0, Tag=word[[0x7D5AEC]]=4, Series=0, body=Count*0x2B 按文件顺序) " +
                "接 SM 4038 (Recog=0, Param=ShenYouAbilSwitch?1:0, Tag=Series=0, 空 body)");
            Add(4126, 0x006DAE74, 0x006BF75C, "洗灵石",
                "洗灵字段 [+0x59C]/[+0x5A0]/[+0x5A4]/[+0x610] 未建模");
            Add(4127, 0x006DAE8D, 0x00747CF4, "洗灵石重算",
                "0x747CF4 重算的 [+0x59C]/[+0x5A0]/[+0x5A8]/[+0x5BC]/[+0x60C] 未建模");
            Add(4128, 0x006DAF23, 0x006B7184, "邻域对象洗灵态查询",
                "SM 4037 的 24 字节 body 取自未建模的 [T+0x60C]+[T+0x5A8]");
            // 【已废弃条目，2026-08-27】4150/4151 已由 TPlayObject.TaskBoard.cs 的 TryHandleTaskBoardCm
            // 完整实现（它嵌套在 NativeCmTailProtocol.cs:43，先于本文件的 fallback switch 运行），
            // 故本表这两条的 Drop 不可达。保留条目仅为不破坏 Drop 的参数校验。
            Add(4150, 0x006DAF51, 0x00699B68, "任务发布板",
                "【SUPERSEDED】已实现于 TaskBoard.cs:106-155：经 M2Share.PasEngine 实调 " +
                "GetTaskDispatchCnt/GetTaskAcceptCnt/GetTaskPrizeDesc/GetTaskAcceptDesc，" +
                "逐字段填满 873 字节后无条件发 SM 3452。原文称脚本过程未移植，与代码矛盾");
            Add(4151, 0x006DAF5E, 0x006999D4, "任务发布板",
                "【SUPERSEDED】已实现于 TaskBoard.cs:167-183：Tag 1/2/3 分别走 " +
                "DoTaskDispatch/DoTaskAccept/DoTaskComplete，其余 Tag 静默；原生 worker 本身不发包，" +
                "故无回包即完整。原文称脚本过程未移植，与代码矛盾");
            Add(4173, 0x006DB068, 0x006E600C, "免费回收装备",
                "回收链会删物品并结算声望，物品选择与结算规则未移植");
            Add(4204, 0x006DAF87, 0x006F03E8, "短信认证码校验",
                "依赖外部短信网关，无法从镜像推导");
            Add(4205, 0x006DAFAF, 0x006F01E4, "短信认证码下发",
                "依赖外部短信网关，无法从镜像推导");
            Add(4215, 0x006DAFCA, 0x006E8684, "邻域对象交互",
                "0x6E8684 的三处 vmt+0x250 回包语义与目标字段未建模");
            Add(4218, 0x006DB00C, 0x006F3104, "物品转移到本人",
                "物品类型表 [0x780574] 与 0x774378 门未移植");
            Add(4408, 0x006DB08A, 0x006F37EC, "神珠镶嵌(本人)",
                "0x7487A8 镶嵌链与物品镶嵌计数字段未移植");
            Add(4409, 0x006DB0B2, 0x006F38A8, "宝玉镶嵌(本人)",
                "0x748A18 镶嵌链、神珠模板表 [0x7D3F34]、物品元素字节未移植");
            Add(4410, 0x006DB0D0, 0x006F37EC, "神珠镶嵌(英雄)",
                "同 4408；英雄有效时的镶嵌链未移植");
            Add(4411, 0x006DB0F8, 0x006F38A8, "宝玉镶嵌(英雄)",
                "同 4409；英雄有效时的镶嵌链未移植");
            Add(4417, 0x006DB1BF, 0x00699EB4, "任务发布板脚本",
                "【SUPERSEDED 2026-08-27】已由 TaskBoardScript.cs:91→107-110 接管（链位 23，早于本表的 25）：" +
                "转 TryCallHelperQuestMain；原生仅在板 +0x2C 非空时进入且无玩家状态门，脚本缺失时 " +
                "TryCall 返回 false 即忠实 no-op。该文件自述“缺口：无”。本表条目 Drop 不可达");
            Add(4496, 0x006DBBDC, 0x006FAC8C, "新手任务",
                "FreshmanTaskCommand 脚本入口未接入");
            Add(4626, 0x006DB394, 0x006AE260, "分页列表查询",
                "【SUPERSEDED 2026-08-27】已由 NewbieQuest.cs:53→126-177 完整实现（链位 10，早于本表的 25）：" +
                "Tag>0x20 静默门(原生 0x6AE285)、sub_705690 成员<30 过滤、分页/status 语义、" +
                "64 字节记录经 EncodeNativeCorpsDescriptions 发 SM 0x1212。本表条目 Drop 不可达");
            Add(4646, 0x006DBBEB, 0x006FBB90, "领奖列表",
                "[[0x7D605C]] 领奖管理器与 [self+0x62C]/[+0x658] 奖励 id 数组未建模");
            Add(4647, 0x006DBBF5, 0x006FB6FC, "领奖前置校验",
                "[[0x7D605C]] 领奖管理器与金刚石货币结算未建模");
            Add(4648, 0x006DBBFF, 0x006FB874, "领奖结算",
                "[[0x7D605C]] 领奖管理器与声望/金币加账链未建模");
            Add(4649, 0x006DBC09, 0x006FBB28, "领奖(含删物品)",
                "【SUPERSEDED 2026-08-27】已由 RewardList.cs:125→246-254 完整实现且不含任何 Drop" +
                "（链位 15，早于本表的 25）：NativeDeleteBagItemByWIndex 按 wIndex 倒序扫包删除、" +
                "发 SM_DELITEM、NativeGrantDiamonds(588)，再发 SM 0x1229 Recog=deleted?0:1。" +
                "本表条目 Drop 不可达");
            Add(4650, 0x006DBC18, 0x006FB51C, "藏宝图合成",
                "0x69C03C 合成状态机未移植，6 路结果码无法推导");
            Add(4651, 0x006DB1D8, 0x006FC054, "任务发布板文本命令",
                "【SUPERSEDED 2026-08-27】已由 TaskBoardScript.cs:94→119-133 接管（链位 23，早于本表的 25，" +
                "也早于嵌套的 TaskBoardCm）：忠实复刻 sub_6B8CC4 四道门(ghost/death/dealing/文本非空)" +
                "后转 TryCallHelperQuestLabel。本表条目与 TaskBoard.cs:206 的 Drop 均不可达");

            return map;
        }

        internal static IReadOnlyDictionary<int, Entry> All => Entries;

        /// <summary>
        /// Drop the packet and record the gap once per ident per process. Nothing
        /// is sent to the client, because the reply 战神 would build here cannot be
        /// derived from the image.
        /// </summary>
        internal static void Drop(int ident, string charName)
        {
            if (!Entries.TryGetValue(ident, out var entry))
            {
                throw new ArgumentOutOfRangeException(nameof(ident),
                    $"CM {ident} 不在 CM 尾段未移植清单里");
            }

            lock (Gate)
            {
                if (!Reported.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[CM未移植] CM {entry.Ident} ({entry.Subsystem}) 已丢弃; " +
                $"handler=0x{entry.HandlerVa:X6} callee=0x{entry.CalleeVa:X6}; " +
                $"角色={(string.IsNullOrEmpty(charName) ? "<unknown>" : charName)}; " +
                $"缺口={entry.Blocker}");
        }
    }
}

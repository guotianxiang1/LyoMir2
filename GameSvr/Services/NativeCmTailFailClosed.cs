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

            Add(4125, 0x006DAE25, 0x00746C34, "定长记录表广播",
                "[[0x7D6014]] 表的 0x2B 字节记录格式未定义，SM 4032 body 无法推导");
            Add(4126, 0x006DAE74, 0x006BF75C, "洗灵石",
                "洗灵字段 [+0x59C]/[+0x5A0]/[+0x5A4]/[+0x610] 未建模");
            Add(4127, 0x006DAE8D, 0x00747CF4, "洗灵石重算",
                "0x747CF4 重算的 [+0x59C]/[+0x5A0]/[+0x5A8]/[+0x5BC]/[+0x60C] 未建模");
            Add(4128, 0x006DAF23, 0x006B7184, "邻域对象洗灵态查询",
                "SM 4037 的 24 字节 body 取自未建模的 [T+0x60C]+[T+0x5A8]");
            Add(4150, 0x006DAF51, 0x00699B68, "任务发布板",
                "任务板状态机与 GetTaskDispatchCnt 等脚本过程未移植，SM 3452 的 873 字节 body 无法推导");
            Add(4151, 0x006DAF5E, 0x006999D4, "任务发布板",
                "DoTaskDispatch / DoTaskAccept / DoTaskComplete 脚本过程未移植");
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
                "任务板 @Main 脚本对象 [[0x7D5D20]]+0x2C 未建模");
            Add(4446, 0x006DBB37, 0x006F75C4, "元宝交易设置",
                "[player+0x192C] 挂单集合子对象未建模，SM 4446 的 Recog 无法推导");
            Add(4496, 0x006DBBDC, 0x006FAC8C, "新手任务",
                "FreshmanTaskCommand 脚本入口未接入");
            Add(4626, 0x006DB394, 0x006AE260, "分页列表查询",
                "[[0x7D5C60]] 列表源与记录格式未建模");
            Add(4646, 0x006DBBEB, 0x006FBB90, "领奖列表",
                "[[0x7D605C]] 领奖管理器与 [self+0x62C]/[+0x658] 奖励 id 数组未建模");
            Add(4647, 0x006DBBF5, 0x006FB6FC, "领奖前置校验",
                "[[0x7D605C]] 领奖管理器与金刚石货币结算未建模");
            Add(4648, 0x006DBBFF, 0x006FB874, "领奖结算",
                "[[0x7D605C]] 领奖管理器与声望/金币加账链未建模");
            Add(4649, 0x006DBC09, 0x006FBB28, "领奖(含删物品)",
                "0x69C47C 会按 client-id 扫背包删物品，规则未移植");
            Add(4650, 0x006DBC18, 0x006FB51C, "藏宝图合成",
                "0x69C03C 合成状态机未移植，6 路结果码无法推导");
            Add(4651, 0x006DB1D8, 0x006FC054, "任务发布板文本命令",
                "任务板脚本对象 [[0x7D5D20]]+0x2C 未建模");

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

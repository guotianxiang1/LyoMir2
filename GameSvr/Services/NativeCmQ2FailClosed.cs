using System;
using System.Collections.Generic;

namespace GameSvr.Services
{
    /// <summary>
    /// Registry for the CM idents in the missing-set 2nd quarter (ascending
    /// 26..50, ident 1265..3179) whose 战神 handler resolves into a subsystem that
    /// has no C# model yet.
    ///
    /// Every ident here reaches a REAL leaf of the native dispatch tree
    /// (sub_6D7D68, selector root 0x6D805C) — none of them falls through to the
    /// shared exit label 0x6DBC2C. The gates that 战神 evaluates BEFORE the
    /// unported call and that this port CAN evaluate from the image (hero
    /// presence, Series, BodyLen, the Param/Tag range on 1364) are reproduced 1:1
    /// at the call sites in TPlayObject.NativeCmProtocol_Q2.cs as genuine silence;
    /// only the terminal action is withheld here.
    ///
    /// Withholding is deliberate. The alternative — emitting the SM reply with a
    /// body this port cannot derive from the image (yb-consignment write records
    /// forwarded through the manager singleton [0x7D5D98]/0x637A00, the hero
    /// spirit-bead / zodiac-inlay masks, the clone-session object [self+0xCD8],
    /// the item-extension chain [item+0x1C]…) — would put invented bytes or an
    /// invented return code on the wire, so the packet is dropped instead and the
    /// gap is recorded here. The record is throttled per ident so a client that
    /// spams an unported opcode cannot flood the log.
    ///
    /// See docs/cm_q2_missing_impl_20260813.md for the full byte-level evidence.
    /// </summary>
    internal static class NativeCmQ2FailClosed
    {
        /// <summary>Native evidence for one unported CM Q2 handler.</summary>
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

            Add(1265, 0x006DA710, 0x006E8564, "元宝交易设置",
                "挂单集合子对象 [self+0x192C] 未建模；空集合时原生仅服务端日志、不回包");
            Add(1280, 0x006DA8F3, 0x006E9208, "自身对象回显",
                "门=客户端 Recog 等于服务端对象指针(C# 无同表示指针身份)，SM 0xCDB body=[self+0x554] 0x1C 字段块未建模");
            Add(1291, 0x006DA3CA, 0x0069059C, "英雄灵珠",
                "英雄灵珠物品链(类 [0x780A74])与荣耀点 [hero+0x68C] 未建模，SM 0xA/0x278B body 无法推导");
            Add(1300, 0x006DAA17, 0x0063D980, "分身点击NPC",
                "分身/机器人会话对象 [self+0xCD8] 未建模；[self+0x570] vmt+0x48 点击链未移植");
            Add(1301, 0x006DAA72, 0x0063DC98, "分身执行NPC过程",
                "分身会话对象 [self+0xCD8] 与 [self+0x570] vmt+0x44 脚本过程回执未移植");
            Add(1316, 0x006DAACF, 0x00746908, "英雄生肖镶嵌",
                "神佑袋位掩码 [self+0x60C]/[self+0x610] 与生肖镶嵌链未建模，SM 0xCFD body 无法推导");
            Add(1320, 0x006DAB6A, 0x00765E68, "分身会话请求",
                "分身会话对象 [self+0xCD8] 与 0x28 字节请求记录格式未建模，SM 0x27A3 无法构造");
            Add(1350, 0x006DAC8E, 0x006F09C4, "元宝寄售·写",
                "忙门 0x6F0A24([self+0x18C8]/[0x7D7038]/地图标志) 与管理器 [0x7D5D98](0x637A00) 未建模，req SM 0x136/ack 0x4E2 无法推导");
            Add(1351, 0x006DACA7, 0x006F0A98, "元宝寄售·写",
                "忙门 0x6F0A24 + 坐标/寄售格 [self+0x18A0]/[+0x18A4] + 管理器 [0x7D5D98] 未建模，req 0x137/ack 0x4E3");
            Add(1352, 0x006DACD0, 0x006F0B84, "元宝寄售·上架",
                "背包物品模板(StdItem [0x7D5D6C])构成的 0x10A 字节 body 与管理器 [0x7D5D98] 未建模，req 0x138/ack 0x4E4");
            Add(1353, 0x006DACE4, 0x006F0E0C, "元宝寄售·写",
                "忙门 0x6F0A24 与管理器 [0x7D5D98] 未建模，req 0x139/ack 0x4ED");
            Add(1354, 0x006DACF6, 0x006F0E64, "元宝寄售·写",
                "忙门 0x6F0A24 与管理器 [0x7D5D98] 未建模，req 0x13A/ack 0x4E7");
            Add(1355, 0x006DAD08, 0x006F0EBC, "元宝寄售·写",
                "忙门 0x6F0A24 + body 结构 [body+4]/[body+0] + 管理器 [0x7D5D98] 未建模，req 0x13B/ack 0x4E5");
            Add(1356, 0x006DAD21, 0x006F0F28, "元宝寄售·写",
                "忙门 0x6F0A24 与管理器 [0x7D5D98] 未建模，req 0x13C/ack 0x4E6");
            Add(1357, 0x006DAD33, 0x006F0F80, "元宝寄售·写",
                "忙门 0x6F0A24 与管理器 [0x7D5D98] 未建模，req 0x13D/ack 0x4EE");
            Add(1358, 0x006DAD45, 0x006F0FD8, "元宝寄售·写",
                "忙门 0x6F0A24 与管理器 [0x7D5D98] 未建模，req 0x13E/ack 0x4E8");
            Add(1359, 0x006DAD57, 0x006F1028, "元宝寄售·取回(cl=1)",
                "管理器 [0x7D5D98] 未建模；两条提示腿(安全区 0x76858C/背包空位 0x7441D8 → SysMsg 0x38FF/0xFFDB)依赖安全区判定与文本发送腿，req 0x13F/ack 0x4E9");
            Add(1360, 0x006DAD6B, 0x006F1028, "元宝寄售·取回(cl=0)",
                "同 1359(cl=0 走 req 0x140)；管理器 [0x7D5D98] 与提示腿未移植，ack 0x4E9");
            Add(1361, 0x006DAD7F, 0x006F110C, "元宝寄售·写",
                "忙门 0x6F0A24 与管理器 [0x7D5D98] 未建模，req 0x141/ack 0x4EB");
            Add(1362, 0x006DAD91, 0x006F1164, "元宝寄售·写",
                "忙门 0x6F0A24 与管理器 [0x7D5D98] 未建模，req 0x142/ack 0x4EC");
            Add(1363, 0x006DADA3, 0x006F11BC, "元宝寄售·写",
                "忙门 0x6F0A24 与管理器 [0x7D5D98] 未建模，req 0x143/ack 0x4EF");
            Add(1364, 0x006DADB5, 0x006F120C, "元宝寄售·写",
                "管理器 [0x7D5D98] 未建模，req 0x146(无 ack)；MakeLong(Param,Tag) 请求体不可求值");
            Add(1376, 0x006DAFF3, 0x006F2E44, "坐骑马牌",
                "当前背包物品类 [0x75DC48](马牌)与 0x7632E0/0x7632E4 语义未移植，SM 0x50A/提示\"请放入马牌\"无法推导");
            Add(2815, 0x006D9B52, 0x006D4E4C, "消息板/relay",
                "单例 [0x7D60FC](0x6A4144) 与玩家坐标/串字段 [self+0x9E4]/[+0x9E6]/[+0xB09]/[+0xB33] 未建模，SM 0xAFF body 无法推导");
            Add(3179, 0x006DA3F3, 0x006E320C, "商人物品字节查询",
                "背包物品扩展子对象链 [item+0x1C]->+0x44->+0x14[byte] 未建模；查得物品时返回真实字节，无法求值，回 -1 会捏造返回码");

            return map;
        }

        internal static IReadOnlyDictionary<int, Entry> All => Entries;

        /// <summary>
        /// Drop the packet and record the gap once per ident per process. Nothing
        /// is sent to the client, because the reply 战神 would build here cannot be
        /// derived from the image.
        /// </summary>
        internal static void Q2Drop(int ident, string charName)
        {
            if (!Entries.TryGetValue(ident, out var entry))
            {
                throw new ArgumentOutOfRangeException(nameof(ident),
                    $"CM {ident} 不在 CM 第2片未移植清单里");
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

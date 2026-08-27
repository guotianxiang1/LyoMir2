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

            Add(1280, 0x006DA8F3, 0x006E9208, "自身对象回显",
                "【A/SUPERSEDED 2026-08-27】已由 MessageBoard.cs:73→137 接管(链位 16，早于本表 27)，" +
                "改走自有 MessageBoardFailClosed 记账，本表 Drop 不可达；Get(Recog)==null 腿忠实静默。" +
                "注意：缺口本身未关闭，只是换了记账口——SM 0xCDB body=[self+0x554] 0x1C 字段块仍未建模，" +
                "门(客户端 Recog 等于服务端对象指针，C# 无同表示指针身份)亦未复现");
            Add(1291, 0x006DA3CA, 0x0069059C, "英雄灵珠",
                "英雄灵珠物品链(类 [0x780A74])与荣耀点 [hero+0x68C] 未建模，SM 0xA/0x278B body 无法推导");
            // 1300/1301/1320：[self+0xCD8] 并非「未建模的分身会话对象」——CloneNpcSession.cs:12-20 用两条
            // 独立佐证订正为早已建模的 m_NPC（(a) 唯一写入者是 NPC 点击处理器 sub_6B8B28，全镜像 57 处
            // 0xCD8 位移中写入仅 3 处；(b) 本端口 TPlayObject.cs:1314-1321 已有同一结论）。
            Add(1300, 0x006DAA17, 0x0063D980, "分身点击NPC",
                "【A/SUPERSEDED 2026-08-27】已由 CloneNpcSession.cs:88→112 接管(链位 7)，改走自有 " +
                "CloneNpcFailClosed，本表 Drop 不可达；m_NPC==null 腿忠实。残留仅 [self+0x570] vmt+0x48 点击链");
            Add(1301, 0x006DAA72, 0x0063DC98, "分身执行NPC过程",
                "【A/SUPERSEDED 2026-08-27】已由 CloneNpcSession.cs:91→133 完整实现：" +
                "npc.GotoLable(this, \"@DoAcceptBless\", false)。原文称 [self+0xCD8] 未建模已被推翻(见上方注释)");
            Add(1316, 0x006DAACF, 0x00746908, "英雄生肖镶嵌",
                "神佑袋位掩码 [self+0x60C]/[self+0x610] 与生肖镶嵌链未建模，SM 0xCFD body 无法推导");
            Add(1320, 0x006DAB6A, 0x00765E68, "分身会话请求",
                "【A/SUPERSEDED 2026-08-27】已由 CloneNpcSession.cs:94→170 接管(链位 7)，四道门全复刻" +
                "(nil / 同图 / 15 格 Chebyshev / Param∈{1,2,3})，改走自有 drop，本表 Drop 不可达。" +
                "残留仅 0x28 字节请求记录格式与 SM 0x27A3 的构造");
            // ── 元宝寄售写侧 1350..1364：全部 15 条的 Q2 臂已不可达 ────────────────────────
            // TPlayObject.YbConsignWrite.cs(链位 12) 早于本表(链位 27) 接管了这 15 个 ident。
            // 【勿整段套用同一结论】——这 15 条分属三种情况，2026-08-27 校正前曾被整段误判为
            // 「配置门默认关 ⇒ 静默即忠实」，那是错的：
            //   A 门后完整梯子   1353/1354/1356/1357/1358/1361/1362/1363
            //   A 无忙门·已实现  1359/1360（reclaim，worker 0x6F1028，会真发包）
            //   D 门后仍扣留     1350/1351/1352/1355
            //   D 无忙门·原生无回包 1364
            // 忙门恒关有一条不依赖镜像的独立证据：NativeYbConsignmentWrite.WriteFeatureEnabled
            // 全仓只有 1 处声明(YbConsignWrite.cs:476)+1 处读取(:139)、零处赋值。
            // ──────────────────────────────────────────────────────────────────────────────
            Add(1350, 0x006DAC8E, 0x006F09C4, "元宝寄售·写",
                "【D/SUPERSEDED】忙门 0x6F0A24([self+0x18C8]/[0x7D7038]/地图标志) 已 1:1 复刻且恒关 ⇒ 静默，" +
                "与原生一致；门后仍走 YbConsignWrite 的 RecordWithheld。管理器 [0x7D5D98](0x637A00) 对端未建模，" +
                "req SM 0x136/ack 0x4E2");
            Add(1351, 0x006DACA7, 0x006F0A98, "元宝寄售·写",
                "【D/SUPERSEDED】同 1350；坐标/寄售格 [self+0x18A0]/[+0x18A4] 仍未建模，req 0x137/ack 0x4E3");
            Add(1352, 0x006DACD0, 0x006F0B84, "元宝寄售·上架",
                "【D/SUPERSEDED】同 1350；上架 body 依赖 StdItem [0x7D5D6C] 模板链的 0x10A 字节结构，" +
                "req 0x138/ack 0x4E4");
            Add(1353, 0x006DACE4, 0x006F0E0C, "元宝寄售·写",
                "【A/SUPERSEDED】YbConsignWrite.cs:168→230 门后是完整梯子 RecogGated(0x139/SM_1261/-1)，" +
                "非扣留。残留仅管理器 [0x7D5D98] 对端");
            Add(1354, 0x006DACF6, 0x006F0E64, "元宝寄售·写",
                "【A/SUPERSEDED】YbConsignWrite.cs:172→230 完整梯子 (0x13A/SM_1255)");
            Add(1355, 0x006DAD08, 0x006F0EBC, "元宝寄售·写",
                "【D/SUPERSEDED】同 1350；body 结构 [body+4]/[body+0] 仍未建模，req 0x13B/ack 0x4E5");
            Add(1356, 0x006DAD21, 0x006F0F28, "元宝寄售·写",
                "【A/SUPERSEDED】YbConsignWrite.cs:179→230 完整梯子 (0x13C/SM_1254)");
            Add(1357, 0x006DAD33, 0x006F0F80, "元宝寄售·写",
                "【A/SUPERSEDED】YbConsignWrite.cs:183→230 完整梯子 (0x13D/SM_1262)");
            Add(1358, 0x006DAD45, 0x006F0FD8, "元宝寄售·写",
                "【A/SUPERSEDED】YbConsignWrite.cs:187→259 Ungated(0x13E/SM_1256, Param=0)");
            Add(1359, 0x006DAD57, 0x006F1028, "元宝寄售·取回(cl=1)",
                "【A/SUPERSEDED】worker 0x6F1028 无忙门(YbConsignWrite.cs:76-79 明载 config-independent)，" +
                "已完整实现于 :191→294-331：安全区腿发 SysMsg 0x38FF、背包满腿发 0xFFDB、" +
                "主路无条件 SendDefMessage(SM_1257, nRecog, 0,0,0,\"\")(:330)。是发包的活实现，不是 no-op");
            Add(1360, 0x006DAD6B, 0x006F1028, "元宝寄售·取回(cl=0)",
                "【A/SUPERSEDED】同 1359，cl=0 走 :194→294");
            Add(1361, 0x006DAD7F, 0x006F110C, "元宝寄售·写",
                "【A/SUPERSEDED】YbConsignWrite.cs:197→230 完整梯子 (0x141/SM_1259)");
            Add(1362, 0x006DAD91, 0x006F1164, "元宝寄售·写",
                "【A/SUPERSEDED】YbConsignWrite.cs:201→230 完整梯子 (0x142/SM_1260)");
            Add(1363, 0x006DADA3, 0x006F11BC, "元宝寄售·写",
                "【A/SUPERSEDED】YbConsignWrite.cs:205→259 Ungated(0x143/SM_1263, Param=5)");
            Add(1364, 0x006DADB5, 0x006F120C, "元宝寄售·写",
                "【D/SUPERSEDED】无忙门(:340-351 无 gate 调用)；判 D 的理由与 1350 那批不同——" +
                "原生此处本就没有 ack ident(req 0x146 是 fire-and-forget)，故静默即忠实，与配置开关无关。" +
                "MakeLong(Param,Tag) 请求体仍不可求值");
            Add(1376, 0x006DAFF3, 0x006F2E44, "坐骑马牌",
                "【A/SUPERSEDED 2026-08-27】已由 HorseToken.cs:85→102 完整实现(链位 21)：TMaPai 校验 → " +
                "写 byte[item+0x33]=Param → SM 0x50A。0x7632E0/0x7632E4 语义已移植于 :120-128" +
                "(kind==1 只收 Param==1；kind==2 收 Param∈{1,2})，原文称其未移植已过期。" +
                "注意 HorseToken 用 if (wIdent != CM_1376) return false 而非 case 标签，纯 case-grep 会漏判");
            Add(2815, 0x006D9B52, 0x006D4E4C, "消息板/relay",
                "【A/SUPERSEDED 2026-08-27】已由 MessageBoard.cs:70→101 接管(链位 16)，BodyLen∈[1,0x40] 门忠实，" +
                "改走自有 drop，本表 Drop 不可达。缺口本身未关闭：单例 [0x7D60FC](0x6A4144) 与坐标/串字段 " +
                "[self+0x9E4]/[+0x9E6]/[+0xB09]/[+0xB33] 仍未建模，SM 0xAFF body 仍不可推导");
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

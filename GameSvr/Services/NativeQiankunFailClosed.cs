using System;
using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>
    /// 乾坤包子系统内仍 fail-closed 的分支登记表 + 每 ident 每进程一次的节流日志。
    ///
    /// 与 GameSvr.Services.NativeCmTailFailClosed 同构，但键为乾坤包/相邻 RM 广播的 ident。
    /// 说明各条目的性质:
    ///   - CM 3285 / 3286: 其"config 非空 / 列表非空"分支。本移植中 [self+0x9FC] 恒 null、
    ///     list@0x9F8 恒空 (CM 3283 鸿福袋开启 + 配置管理器 [0x7D64B8] 未移植)，故这两条
    ///     实际不可达；忠实的可观测路径已在 TPlayObject.Qiankun.cs 实现 (3285 静默返回、
    ///     3286 -> SM 2957)。此处仅登记 config 派生逻辑的边界，防止将来误触时静默出错。
    ///   - CM 3287 / 3288: 相邻分发臂上的"物品 RM 广播"独立特性 (不碰乾坤字段)，RM 0x3004/
    ///     0x3005 客户端语义与物品估值 helper 不可从服务端镜像求证 -> 整体 fail-closed。
    ///
    /// 丢弃时不向客户端发送任何字节 (拒绝把无法从镜像推导的 body 放上线)，仅按 ident 节流登记。
    /// </summary>
    internal static class NativeQiankunFailClosed
    {
        internal readonly struct Entry
        {
            public Entry(int ident, uint handlerVa, uint calleeVa, string subsystem, string blocker)
            {
                Ident = ident;
                HandlerVa = handlerVa;
                CalleeVa = calleeVa;
                Subsystem = subsystem;
                Blocker = blocker;
            }

            public int Ident { get; }
            public uint HandlerVa { get; }
            public uint CalleeVa { get; }
            public string Subsystem { get; }
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

            Add(3285, 0x006DA638, 0x006E6DE8, "乾坤包·荣耀点更换(config 非空分支)",
                "[self+0x9FC] config 恒 null(CM 3283 + 管理器 [0x7D64B8] 未移植); apply(0x6E68A8) 的荣耀点扣减与 SM 2956 body 依赖配置数据");
            Add(3286, 0x006DA65D, 0x006E6B54, "乾坤包·领取奖励(列表非空分支)",
                "list@0x9F8 恒空; 授予链 0x6C87B4 / RM 广播 0x5F701C / SM 2958 / SM 2956 body 依赖未移植配置");
            Add(3287, 0x006DA895, 0x006E8734, "附近玩家展示物品(RM 0x3004)",
                "RM 0x3004 客户端语义与物品估值 [item+0x38]/0x78472C 不可从镜像求证; RM 0x3004 未在 C# 定义");
            Add(3288, 0x006DA8C4, 0x006E8820, "自身物品广播(RM 0x3005)",
                "RM 0x3005 客户端语义与值计算 0x6E88C0/0x6E8C1C 不可从镜像求证; RM 0x3005 未在 C# 定义");

            return map;
        }

        internal static IReadOnlyDictionary<int, Entry> All => Entries;

        internal static void Drop(int ident, string charName)
        {
            if (!Entries.TryGetValue(ident, out var entry))
            {
                throw new ArgumentOutOfRangeException(nameof(ident),
                    $"CM {ident} 不在乾坤包 fail-closed 清单里");
            }

            lock (Gate)
            {
                if (!Reported.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[乾坤包未移植] CM {entry.Ident} ({entry.Subsystem}) 已丢弃; " +
                $"handler=0x{entry.HandlerVa:X6} callee=0x{entry.CalleeVa:X6}; " +
                $"角色={(string.IsNullOrEmpty(charName) ? "<unknown>" : charName)}; " +
                $"缺口={entry.Blocker}");
        }
    }
}

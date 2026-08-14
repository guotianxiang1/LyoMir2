namespace SystemModule.Packet
{
    /// <summary>
    /// 元宝转游戏时间 — native NPC 0x0063846C 展示/回调族。
    /// 异步扣费链经 shop 提交 0x6D3694；YBDB legacy77 ident **本轮 M2 镜像未钉死**
    /// （0x63846C 仅 0x652784 查人 + 0x6D340C 刷新资本，无 E8 YBDB 字面量）→ 仅常量层。
    /// </summary>
    public static class YbDbYuanbaoToTimeProtocol
    {
        // feat-econ 对齐 sub_638510 结果码 -4/-3/-2；请求 ident 待 YBDB 侧 RE。
        public const int NativeResultSuccess = -4;
        public const int NativeResultAutoConvertDenied = -3;
        public const int NativeResultFailure = -2;

        /// <summary>占位：YBDB 请求 ident（UNPROVEN — 禁止接线假值）。</summary>
        public const ushort RequestIdentUnproven = 0;
        public const ushort ResponseIdentUnproven = 0;
    }
}

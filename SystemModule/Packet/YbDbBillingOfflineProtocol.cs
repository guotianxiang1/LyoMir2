namespace SystemModule.Packet
{
    /// <summary>
    /// 元宝预冻/账号解冻离线回调 — native 0x0063BF9C / 0x0063C0C0。
    /// 成功在线路径分别 call 0x6E3D14 / 0x6E3FAC；回包 ident 在外部 YB 链，DBSvr 无存储（UNKNOWN）。
    /// </summary>
    public static class YbDbBillingOfflineProtocol
    {
        public const uint PrefreezeCallbackEa = 0x0063BF9C;
        public const uint AccountUnfreezeCallbackEa = 0x0063C0C0;

        /// <summary>寄售写 CM1350..1364 → 0x6D3694 → 0x637A00 外部链；非 DBSvr type-1。</summary>
        public const uint ConsignmentSubmitEa = 0x006D3694;
        public const uint ConsignmentFramerEa = 0x00637A00;
    }
}

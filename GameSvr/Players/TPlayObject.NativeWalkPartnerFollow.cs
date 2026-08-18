namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// MOVE-39 —— 人形 mover <c>sub_741224</c> 尾部 0x741328..0x741350 的移植。
        /// 门与被调例程都与 CM_RUN3(4108) mover <c>sub_767694</c> 尾部 0x767789..0x7677B4
        /// 逐字节同构（同样是 <c>InBodyState(0x33) &amp;&amp; [self+0x3C0]!=0</c> 后
        /// <c>call sub_6BBEE4</c>），所以复用 run3 已落地的同一个移植体。
        /// <para>
        /// <c>sub_6BBEE4(partner, newX, newY, dir)</c> 本身：
        /// 0x6BBEF3 <c>[partner+0x154]=dir</c> → 0x6BBF12 <c>call sub_779CD8</c>
        /// （纯摘链/头插；压入的常量 1 在函数体内未读取，无边界、地形或占位门）→ 成功才
        /// 0x6BBF1B/0x6BBF21 提交同伴 X/Y、0x6BBF27 清定时状态 0x17、
        /// 0x6BBF38 <c>call sub_778EC0</c>、0x6BBF3D <c>call sub_6E37C4</c>；
        /// 全程不广播 RM_WALK（客户端把乘客画在坐骑上）。
        /// </para>
        /// <para>
        /// 只有 TPlayObject 持有 <c>m_NativeHorsePartner</c>（[+0x3C0]），HeroObject 走
        /// AnimalObject 分支且永不持有同伴，所以覆写落在本类即精确对齐原生作用域。
        /// </para>
        /// </summary>
        protected override void OnNativeHumanWalkMoverCommitted()
        {
            SyncNativeHorsePartnerAfterRun3();
        }
    }
}

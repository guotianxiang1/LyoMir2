namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// 眼神数字隧道 7（ys_DropItem）的金币分支需要的入口。宿主 0x0064E6F4 在
        /// 名字等于「金币」时不建物品，而是 0064E748 / 0064E765 反复
        /// `call 0x00768AAC`（eax=self, edx=金额, cl=0, push self, push self），
        /// 每次最多 2000。0x00768AAC 就是 DropGoldDown：0x00768AF4 用固定范围 3
        /// 调 0x00768688 找落点，与 C# 侧 GetDropPosition(..., 3, ...) 一致。
        ///
        /// DropGoldDown 是 protected，YanshenApi 够不着，所以在这里开一个内部转发。
        /// 它不扣玩家身上的钱——原生这条支路也不扣（官方文档：此函数在地面上
        /// 新产生物品，和角色背包毫无关系）。
        /// </summary>
        internal void YanshenTunnelDropGold(int amount)
        {
            DropGoldDown(amount, false, this, this);
        }
    }
}

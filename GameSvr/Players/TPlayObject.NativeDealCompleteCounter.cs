namespace GameSvr
{
    public partial class TPlayObject
    {
        // TRADE-50: 战神 sub_6C4580(ClientDealEnd) 在四道容量检查全部通过之后、
        // 变异块之前，对一个模块级全局 dword 自增一次：
        //   0x6C4752  0F 85 60 02 00 00     jne 0x6C49B8            ; 第4道检查失败→DealCancel
        //   0x6C4758  FF 05 90 3A 7D 00     inc dword ptr [0x7D3A90] ; ★ 成交完成计数
        //   0x6C475E  8B 83 DC 06 00 00     mov eax,[ebx+0x6DC]      ; 开始读 m_DealItemList(变异)
        // 全镜像对 0x7D3A90 仅有这一处引用（唯一写点，零读点——disasm 校验：
        // staging/_eqv05_work/dx.py f 903a7d00 → 1 命中 @0x6C475A）。即原生该计数器
        // 是「只写、从不读」的统计量，运行期无任何可观测消费点，但为 1:1 结构保真
        // 在此保留同一语义的全局计数器（public 静态，可经调试/内存镜像观测，恰如原生
        // 那个 module 级 dword）。自增点与原生一致：ClientDealEnd 内 `if (bo11)` 成立
        // 后、物品/金币转移循环之前。
        public static long g_nCompletedDealCount;
    }
}

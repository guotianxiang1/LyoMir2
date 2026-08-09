using SystemModule;

namespace GameSvr
{
    public struct TItemPrice
    {
        public short wIndex;
        public double nPrice;
    }

    public class TGoods
    {
        public string sItemName;
        public int nCount;
        public int dwRefillTime;
        public int dwRefillTick;
    }

}

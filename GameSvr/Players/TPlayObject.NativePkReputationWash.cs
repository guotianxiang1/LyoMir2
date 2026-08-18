using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const string NativePkWashSuccessMessage =
            "洗红成功，你的善恶值降低是好样的！";
        private const string NativePkWashFailMessage =
            "没有声望，或者PK值已经是0";
        private const int NativePkWashReputationCost = 1;
        private const int NativePkWashPkReduction = 100;

        /// <summary>
        /// 战神 sub_6C8CC8 @0x006C8CC8：需 [self+0x4F0]&gt;0 且 [self+0x160]&gt;0；
        /// dec [+0x4F0]；sub_6C8DA0(2,1)；sub_6CCB0C(+100)；成功 0x6C8D54 / 失败 0x6C8D88。
        /// GM 表 idx 33「洗红名」@0x00623B96 同体。
        /// </summary>
        internal bool TryWashPkPointWithReputation()
        {
            if (m_nPkPoint <= 0 || m_nShengWan <= 0)
            {
                SysMsg(NativePkWashFailMessage, MsgColor.Green, MsgType.Hint);
                return false;
            }

            m_nShengWan = unchecked(m_nShengWan - NativePkWashReputationCost);
            ReduceNativePkPoint(NativePkWashPkReduction);
            SysMsg(NativePkWashSuccessMessage, MsgColor.Green, MsgType.Hint);
            return true;
        }

        private void ReduceNativePkPoint(int amount)
        {
            if (amount <= 0)
                return;

            var beforeBucket = m_nPkPoint / NativePkWashPkReduction;
            m_nPkPoint -= amount;
            if (m_nPkPoint < 0)
                m_nPkPoint = 0;

            var afterBucket = m_nPkPoint / NativePkWashPkReduction;
            if (beforeBucket != afterBucket)
                RefreshNameColor();
        }

        private void RefreshNameColor()
        {
            // 战神 sub_6CCB0C 在 PK 档位跨 100 边界时 call 0x767548 刷新名色；此处折到现有外观刷新。
            RecalcAbilitys();
            FeatureChanged();
        }
    }
}

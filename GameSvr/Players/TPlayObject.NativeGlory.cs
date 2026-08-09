using System.Globalization;
using GameSvr.Services;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal bool DecNativeGloryPoint(int vsId, int nPrize, int nNum,
            bool bAddPoint, string description)
        {
            _ = bAddPoint;
            var total = unchecked(nPrize * nNum);
            int remaining;
            lock (m_CreditCard.SyncRoot)
            {
                if (total <= 0 || total > m_CreditCard.GloryPointValue)
                    return false;
                m_CreditCard.GloryPointValue =
                    unchecked(m_CreditCard.GloryPointValue - total);
                m_CreditCard.GloryPointDirty = true;
                m_CreditCard.GloryPointDirtyVersion++;
                remaining = m_CreditCard.GloryPointValue;
            }

            NativeGloryLogManager.Record(vsId, total);
            var logDescription = (description ?? string.Empty) + ":个数" +
                                 nNum.ToString(CultureInfo.InvariantCulture) +
                                 "; 剩余：" +
                                 remaining.ToString(CultureInfo.InvariantCulture);
            M2Share.AddGameDataLog(string.Join('\t', 42, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, "荣耀点", vsId, total,
                logDescription));
            RefreshNativeLingFu();
            AddNativeYbShopCreditValue2(total / 100);
            NotifyPlayerActivePoint(2, description ?? string.Empty, total, 0);
            return true;
        }
    }
}

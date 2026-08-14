using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// <c>TPercentResumeDrug</c> use path (VMT 0x77E4D0) including富贵兽 branch
        /// @0x787918 when StdMode0/Shape 1 or 10 and Dura &gt;= 1000.
        /// </summary>
        private bool UseNativePercentResumeDrug(GoodItem stdItem, TUserItem item)
        {
            if (!NativePercentResumeDrugGate.TryAllowUse(this, stdItem,
                    out var denial, out var fColor, out var bColor))
            {
                if (denial != null)
                    SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, fColor, bColor, 0, denial);
                return false;
            }

            if (!CanUseNativeDrug())
                return false;

            // 0x787248 cmp word [std+0x1E],1 / shape branches 1 or 10 (0x787253 sub 9).
            var shapeKind = stdItem.Shape;
            if (shapeKind == 1 || shapeKind == 10)
            {
                if (item.Dura < 1000)
                    return false;
                if (!NativeFuguiBeastProximity.TryFindNearbyBeast(this, m_nCurrX,
                        m_nCurrY, out _, out var beastDenial))
                {
                    if (beastDenial != null)
                        SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC, 0, beastDenial);
                    return false;
                }
                // 0x7872B6 sub word [item+0x26],0x3E8
                item.Dura = (ushort)(item.Dura - 1000);
                SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                    EnsureClientItemId(item), item.Dura, item.DuraMax, 0, "");
                return true;
            }

            // Default percent resume: same shape-3 math as legacy EatItems.
            IncHealthSpell(
                HUtil32.Round(m_WAbil.MaxHP / 100.0 * stdItem.Ac),
                HUtil32.Round(m_WAbil.MaxMP / 100.0 * stdItem.Mac));
            return true;
        }
    }
}

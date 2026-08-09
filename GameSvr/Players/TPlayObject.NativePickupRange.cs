namespace GameSvr
{
    public partial class TPlayObject
    {
        private void ClientPickUpRange()
        {
            var envir = m_PEnvir;
            if (envir?.Flag?.boPICKUP != true)
            {
                return;
            }

            foreach (var (x, y) in EnumerateNativePickupRangeCells(
                         m_nCurrX, m_nCurrY))
            {
                var mapItem = envir.GetNativePickupRangeItem(x, y, this);
                if (mapItem != null)
                {
                    ClientPickUpItem(mapItem, x, y);
                }
            }
        }

        internal static IEnumerable<(int X, int Y)>
            EnumerateNativePickupRangeCells(int centerX, int centerY)
        {
            for (var x = centerX - 2; x <= centerX + 2; x++)
            {
                for (var y = centerY - 2; y <= centerY + 2; y++)
                {
                    yield return (x, y);
                }
            }
        }
    }
}

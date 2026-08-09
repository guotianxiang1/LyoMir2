namespace GameSvr
{
    public partial class TPlayObject
    {
        public int IncActivePoint(int value)
        {
            var current = m_nActivePoint;
            var sum = unchecked(current + value);
            if (sum >= int.MaxValue)
            {
                m_nActivePoint = int.MaxValue;
                return unchecked(int.MaxValue - current);
            }

            m_nActivePoint = sum;
            return value;
        }

        public int DecActivePoint(int value)
        {
            if (value > 0)
                m_nActivePoint = unchecked(m_nActivePoint - value);
            return m_nActivePoint;
        }
    }
}

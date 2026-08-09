using SystemModule;

namespace GameSvr
{
    public class Event : IDisposable
    {
        
        
        
        public int Id;
        public int nVisibleFlag = 0;
        public Envirnoment m_Envir = null;
        public int m_nX = 0;
        public int m_nY = 0;
        public int m_nEventType = 0;
        public int m_nEventParam = 0;
        public string m_sEventOwnerName = string.Empty;
        internal byte[] m_EventOwnerNameBytes = null;
        public string m_sEventStallName = string.Empty;
        public long m_lEventOwnerId = 0;
        protected int m_dwOpenStartTick = 0;

        internal int OpenStartTick => m_dwOpenStartTick;
        
        
        
        private int m_dwContinueTime = 0;
        
        
        
        public int m_dwCloseTick = 0;
        
        
        
        public bool m_boClosed = false;
        public int m_nDamage = 0;
        public TBaseObject m_OwnBaseObject = null;
        
        
        
        public int m_dwRunStart = 0;
        
        
        
        public int m_dwRunTick = 0;
        
        
        
        public bool m_boVisible = false;
        
        
        
        public bool m_boActive = false;

        public Event(Envirnoment envir, int ntX, int ntY, int nType, int dwETime, bool boVisible)
            : this(envir, ntX, ntY, nType, dwETime, boVisible, null, null)
        {
        }

        internal Event(Envirnoment envir, int ntX, int ntY, int nType,
            int dwETime, bool boVisible, string eventOwnerName,
            byte[] eventOwnerNameBytes)
        {
            Id = HUtil32.Sequence();
            m_dwOpenStartTick = HUtil32.GetTickCount();
            m_nEventType = nType;
            m_nEventParam = 0;
            m_dwContinueTime = dwETime;
            m_boVisible = boVisible;
            m_boClosed = false;
            m_Envir = envir;
            m_nX = ntX;
            m_nY = ntY;
            m_sEventOwnerName = eventOwnerName ?? string.Empty;
            m_EventOwnerNameBytes = eventOwnerNameBytes == null
                ? null
                : (byte[])eventOwnerNameBytes.Clone();
            m_boActive = true;
            m_nDamage = 0;
            m_OwnBaseObject = null;
            m_dwRunStart = HUtil32.GetTickCount();
            m_dwRunTick = 500;
            if (m_Envir != null && m_boVisible)
            {
                if (!ReferenceEquals(m_Envir.AddToMap(m_nX, m_nY,
                        CellType.OS_EVENTOBJECT, this), this))
                {
                    m_dwContinueTime = 0;
                }
            }
            else
            {
                m_boVisible = false;
            }
        }

        public virtual void Run()
        {
            Run(HUtil32.GetTickCount());
        }

        public virtual void Run(int currentTick)
        {
            if (unchecked((uint)(currentTick - m_dwOpenStartTick)) >
                unchecked((uint)m_dwContinueTime))
            {
                Close(currentTick);
            }
        }

        public virtual bool ApplyTo(TBaseObject target)
        {
            return false;
        }

        public void Close()
        {
            Close(HUtil32.GetTickCount());
        }

        internal void Close(int currentTick)
        {
            if (m_boClosed)
            {
                return;
            }

            m_boClosed = true;
            m_boActive = false;
            m_dwCloseTick = currentTick;

            var envir = m_Envir;
            m_boVisible = false;
            m_Envir = null;
            m_dwContinueTime = 0;
            if (envir != null)
            {
                envir.DeleteFromMap(m_nX, m_nY, CellType.OS_EVENTOBJECT, this);
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}

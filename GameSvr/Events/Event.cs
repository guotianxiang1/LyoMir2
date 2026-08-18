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

        /// <summary>
        /// Native sub_7199B8, the whole body of which is
        /// <c>mov ebx,eax / call GetTickCount / mov [ebx+8],eax</c>
        /// (@0x7199BE-0x7199C3). Field +0x08 is the expiry baseline: the Run
        /// body at 0x719985 computes <c>now - [obj+8]</c> and compares it
        /// against the duration at <c>[obj+0x20]</c>, which is the same pair
        /// this class calls m_dwOpenStartTick / m_dwContinueTime. Restamping
        /// it restarts the lifetime WITHOUT re-adding the object to the map.
        /// </summary>
        internal void RefreshOpenStartTick(int tick)
        {
            m_dwOpenStartTick = tick;
        }
        
        
        
        private int m_dwContinueTime = 0;

        /// <summary>
        /// Native field <c>[obj+0x20]</c>. Several subclass constructors write it
        /// again after the TMapEvent constructor has clamped it, and
        /// TOnceDamageTrapEvent zeroes it from inside ApplyTo
        /// (<c>0x717F6F 33 C0 / 0x717F71 89 43 20</c>) so the event dies on the
        /// next Run. Exposed for those paths only.
        /// </summary>
        internal int ContinueTime
        {
            get => m_dwContinueTime;
            set => m_dwContinueTime = value;
        }
        
        
        
        public int m_dwCloseTick = 0;
        
        
        
        public bool m_boClosed = false;
        public int m_nDamage = 0;
        public TBaseObject m_OwnBaseObject = null;
        
        
        
        public int m_dwRunStart = 0;
        
        
        
        public int m_dwRunTick = 0;
        
        
        
        public bool m_boVisible = false;
        
        
        
        public bool m_boActive = false;

        /// <summary>
        /// Native <c>TMapEvent+0x34</c>. The base constructor clears it at
        /// <c>0x71733A</c>; only event classes with an immediate landing callback
        /// set it. This is independent of the managed lifecycle flag
        /// <see cref="m_boActive"/>.
        /// </summary>
        internal bool NativeAppliesOnLanding { get; set; }

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
            // TMapEvent ctor 0x717352  81 FE 80 FC 0A 00  cmp esi, 0xAFC80
            //               0x717358  7E 05              jle 0x71735F  (signed)
            //               0x71735A  BE 80 FC 0A 00     mov esi, 0xAFC80
            //               0x71735F  89 73 20           mov [ebx+0x20], esi
            // Negatives are kept (they compare below the ceiling). 720000 ms is 12 min.
            if (dwETime > 0xAFC80)
                dwETime = 0xAFC80;
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

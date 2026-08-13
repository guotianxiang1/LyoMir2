using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TStallEvent</c>, self-pointer 0x716B28 / VMT 0x716B74,
    /// instance size 76, parent TMapEvent. Constructor <c>sub_7199F8</c>
    /// (<c>ret 0x10</c>), Run <c>0x719AC8</c> (VMT+0x10), close helper
    /// <c>sub_7199CC</c>, destructor <c>0x719A9C</c>. ApplyTo is inherited
    /// unchanged (VMT+0x08 is still TMapEvent's <c>0x717420 xor eax,eax</c>),
    /// so a stall never touches anyone who walks onto it.
    /// <para>
    /// What makes it different from every other TMapEvent subclass is that it
    /// mutates the map itself: it flips the cell's walk attribute to LowWall so
    /// nobody can step on the stall, and gives it back on close. The single
    /// extra field <c>[+0x48]</c> is the "I am the one who locked it" flag.
    /// </para>
    /// </summary>
    public class StallEvent : Event
    {
        /// <summary>
        /// Native <c>[obj+0x48]</c>. Set only when the constructor found the cell
        /// walkable and locked it (<c>0x719A63 C6 43 48 01</c>); every release
        /// path tests it first.
        /// </summary>
        private bool m_boCellLocked;

        /// <summary>
        /// Native ctor <c>sub_7199F8</c>: ecx = Envir, [ebp+0x14] owner,
        /// [ebp+0x10] X, [ebp+0x0C] Y, [ebp+8] duration.
        /// </summary>
        public StallEvent(Envirnoment envir, TBaseObject owner, int nX, int nY,
            int nTime)
            : base(envir, nX, nY, Grobal2.ET_STALL, nTime, true)
        {
            // 0x719A33  8B 45 08 / 89 43 20   mov [ebx+0x20],[ebp+8]
            // Unconditional, unlike the traps: a stall always gets the raw
            // duration back, so the 0xAFC80 (12 min) clamp never applies to it.
            ContinueTime = nTime;
            // 0x719A39  C6 43 34 00   mov byte [ebx+0x34],0
            // The trap and fire constructors all write 1 here; the stall writes 0
            // (and TMapEvent's own constructor already wrote 0 at 0x71733A).
            // 0x719A40  89 43 14      mov [ebx+0x14],eax
            m_OwnBaseObject = owner;
            // 0x719A43  C6 43 48 00   mov byte [ebx+0x48],0
            m_boCellLocked = false;

            // 0x719A52  E8 51 DC 05 00  call 0x7776A8 = resolve the cell
            // 0x719A57  84 C0 / 74 1C   off-map -> no lock
            // 0x719A5E  80 38 00 / 75 14  only an already-walkable cell is taken
            var cell = new MapCellinfo();
            if (m_Envir != null &&
                m_Envir.GetMapCellInfo(m_nX, m_nY, ref cell) &&
                cell.Attribute == CellAttribute.Walk)
            {
                m_boCellLocked = true;
                // 0x719A67  6A 00  push 0  -> sub_7792EC stores attribute 2
                m_Envir.SetNativeStallCellAttribute(m_nX, m_nY, false);
            }
        }

        /// <summary>
        /// Native <c>0x719AC8</c>. It does not chain to TMapEvent.Run; it inlines
        /// the expiry test (<c>0x719AD3 2B 43 08 sub eax,[ebx+8]</c> /
        /// <c>0x719AD6 3B 43 20 cmp eax,[ebx+0x20]</c> / <c>0x719AD9 76 jbe</c>)
        /// and then releases the cell twice — once inline at 0x719AE7 and once
        /// more inside the close helper it calls at 0x719B00. Writing the same
        /// attribute twice is idempotent, so this is reproduced as-is.
        /// </summary>
        public override void Run(int currentTick)
        {
            if (unchecked((uint)(currentTick - OpenStartTick)) <=
                unchecked((uint)ContinueTime))
            {
                return;
            }

            ReleaseCell();
            // 0x719AF7  C6 43 24 01   mov byte [ebx+0x24],1
            // 0x719AFB  89 73 10      mov [ebx+0x10],esi
            // 0x719AFE  E8 C7 FE FF FF  call 0x7199CC  (releases again, then Close)
            ReleaseCell();
            Close(currentTick);
        }

        /// <summary>
        /// The <c>[obj+0x48]</c>-guarded half of native <c>sub_7199CC</c>:
        /// <c>0x7199D2 80 7B 48 00 / 74 16</c> then
        /// <c>0x7199D8 83 7B 38 00 / 74 10</c> then
        /// <c>0x7199DE 6A 01</c> — flag 1 means sub_7792EC writes attribute 0.
        /// </summary>
        private void ReleaseCell()
        {
            if (!m_boCellLocked || m_Envir == null)
            {
                return;
            }
            m_Envir.SetNativeStallCellAttribute(m_nX, m_nY, true);
        }

        /// <summary>
        /// Native <c>TStallEvent.Destroy = 0x719A9C</c> does NOT release the cell:
        /// its whole body is <c>call 0x404A70</c>, <c>and dl,0xFC</c>,
        /// <c>call 0x7173E0</c> (TMapEvent.Destroy, which only does
        /// DeleteFromMap). A stall freed without going through Run/Close leaves
        /// the cell permanently non-walkable. That is native behaviour and is not
        /// "fixed" here; this method exists so a caller that wants the Run path's
        /// effect can ask for it explicitly.
        /// </summary>
        internal void CloseAndReleaseCell(int currentTick)
        {
            ReleaseCell();
            Close(currentTick);
        }
    }
}

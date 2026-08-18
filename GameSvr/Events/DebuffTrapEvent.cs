using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TDebuffTrapEvent</c>, self-pointer 0x716CF8 / VMT 0x716D44,
    /// instance size 80, parent TMapEvent. Constructor <c>sub_717CA4</c>
    /// (<c>ret 0x10</c>), ApplyTo <c>0x717D50</c> (VMT+0x08), Run
    /// <c>0x717DB4</c> (VMT+0x10), destructor <c>0x717D1C</c>.
    /// <para>
    /// It deals no damage at all. Every 3 seconds it sweeps the cell and puts
    /// two states on each proper target. Two fields beyond TMapEvent:
    /// <c>[+0x48]</c> the pulse timestamp and <c>[+0x4C]</c> a TList that is
    /// allocated once in the constructor and freed in the destructor
    /// (<c>0x717D2A 8B 46 4C / 0x717D2D call 0x404690</c>), not per pulse.
    /// </para>
    /// </summary>
    public class DebuffTrapEvent : Event
    {
        /// <summary>Native <c>[obj+0x48]</c>.</summary>
        private int m_trapRunTick;

        /// <summary>
        /// Native <c>[obj+0x4C]</c>. The list is a member, not a local: the
        /// constructor builds it at <c>0x717CE4 mov eax,[0x421E8C] / call
        /// 0x404660</c> and Run clears it in place at <c>0x717DD1 cmp
        /// dword [eax+8],0 / 0x717DD9 call [TList.VMT+8]</c>.
        /// </summary>
        private readonly List<TBaseObject> m_targetList = new();

        /// <summary>
        /// Native ctor <c>sub_717CA4</c>: ecx = Envir (passed straight through),
        /// [ebp+0x10] X, [ebp+0x0C] Y, [ebp+8] duration, [ebp+0x14] owner.
        /// </summary>
        public DebuffTrapEvent(Envirnoment envir, int nX, int nY, int nTime,
            TBaseObject owner)
            : base(envir, nX, nY, Grobal2.ET_TRAP, nTime, true)
        {
            // 0x717CD8  C6 43 34 01   mov byte [ebx+0x34],1
            NativeAppliesOnLanding = true;
            // 0x717CDF  89 43 14      mov [ebx+0x14],eax
            m_OwnBaseObject = owner;
            // 0x717CF1  83 7B 14 00   cmp dword [ebx+0x14],0
            // 0x717CF5  75 03         jne 0x717CFA
            // 0x717CF7  89 73 20      mov [ebx+0x20],esi
            // A nil owner restores the raw duration, undoing the 0xAFC80 clamp
            // and any AddToMap-failure zeroing.
            if (owner == null)
            {
                ContinueTime = nTime;
            }
        }

        /// <summary>
        /// Native <c>0x717DB4</c>. Period is <c>0xBB8</c> = 3000 ms
        /// (<c>0x717DC4 3D B8 0B 00 00 cmp eax,0xBB8</c> /
        /// <c>0x717DC9 76 jbe</c>, so strictly greater fires), then the inherited
        /// TMapEvent.Run at <c>0x717E2B call 0x717448</c> does the expiry check.
        /// </summary>
        public override void Run(int currentTick)
        {
            if (unchecked((uint)(currentTick - m_trapRunTick)) > 0xBB8u)
            {
                m_trapRunTick = currentTick;
                // 0x717DD1 clears the member list only when Count > 0.
                m_targetList.Clear();
                // 0x717DDC  8B 43 38 / 85 C0 / 74 44   nil Envir skips the sweep
                if (m_Envir != null)
                {
                    // 0x717DE3  6A 01          push 1     ; skip the dead
                    // 0x717DF5  E8 66 07 06 00 call 0x778560 = GetBaseObjects
                    m_Envir.GetBaseObjects(m_nX, m_nY, true, m_targetList);
                    // 0x717DFD  8B 70 08 / 4E / 85 F6 / 7C   count-1 < 0 -> skip
                    for (var i = 0; i < m_targetList.Count; i++)
                    {
                        // 0x717E1E  FF 51 08   call [Self.VMT+8] = ApplyTo
                        ApplyTo(m_targetList[i]);
                    }
                }
            }
            base.Run(currentTick);
        }

        /// <summary>
        /// Native <c>0x717D50</c>. The result byte is seeded to 1 at
        /// <c>0x717D5C B3 01 mov bl,1</c> and never cleared, so it always
        /// reports true — unlike TFireBurnEvent, whose twin body seeds 0.
        /// </summary>
        public override bool ApplyTo(TBaseObject target)
        {
            TBaseObject owner = m_OwnBaseObject;
            // 0x717D61  85 F6 / 74 46          no owner
            // 0x717D65  3B 75 FC / 74 41       owner is the target
            if (owner == null || ReferenceEquals(owner, target))
            {
                return true;
            }
            // 0x717D6A  80 7E 73 00 / 74 07    ghost owner is dropped, permanently:
            // 0x717D72  89 47 14               mov [edi+0x14],eax  (eax = 0)
            if (owner.m_boGhost)
            {
                m_OwnBaseObject = null;
                return true;
            }
            // 0x717D7C  E8 17 F7 04 00  call 0x767498 = IsProperTarget
            if (!owner.IsProperTarget(target))
            {
                return true;
            }

            // 0x717D85  6A 01          push 1        ; value/level
            // 0x717D87  66 B9 0A 00    mov cx,0x0A   ; 10 seconds
            // 0x717D8B  B2 11          mov dl,0x11   ; state 17
            // 0x717D92  FF 96 C8 00 00 00  call [target.VMT+0xC8]
            // and again with dl = 0x18 at 0x717D9E.
            // VMT+0xC8 is MakePosion, which takes the STATE ID in dl — see
            // 0x666D48 B2 1F for what C# calls MakePosion(POISON_DECHEALTH),
            // i.e. dl = 31 - slot. Ids 0x11 and 0x18 have no legacy slot, so the
            // legacy face cannot carry them; the slot itself can, and going
            // through it is what native does. ApplyNativeStateSeconds jumped
            // straight to AddState and so lost 0x76B3C8's own guards and, for a
            // player or hero target, the whole 0x746604 override.
            target.NativeMakePosion(0x11, 10, 1);
            target.NativeMakePosion(0x18, 10, 1);
            return true;
        }
    }
}

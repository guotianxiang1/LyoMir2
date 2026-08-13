using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TBTFireBurnEvent</c>, self-pointer 0x716BB8 / VMT 0x716C04,
    /// instance size 136, parent TFireBurnEvent. Constructor
    /// <c>sub_717A38</c> (<c>ret 0x1C</c>), Run <c>0x717ABC</c> (VMT+0x10),
    /// ApplyTo <c>0x717B3C</c> (VMT+0x14 — TFireBurnEvent's VMT+0x08 at
    /// 0x7179AC is just <c>mov ecx,[eax] / call [ecx+0x14]</c>, so slot 0x14 is
    /// the real body), destructor 0x7178EC shared with TFireBurnEvent.
    /// <para>
    /// It adds three fields on top of TFireBurnEvent: <c>[+0x7C]</c> growth
    /// timestamp, <c>[+0x80]</c> growth interval, <c>[+0x84]</c> damage step —
    /// plus <c>[+0x78]</c>, the re-assert timestamp, which the constructor never
    /// touches and therefore starts at 0 from Delphi's zeroed NewInstance.
    /// </para>
    /// </summary>
    public class BTFireBurnEvent : FireBurnEvent
    {
        /// <summary>Native <c>[obj+0x7C]</c>; 0 means "not seeded yet".</summary>
        private int m_growTick;

        /// <summary>Native <c>[obj+0x80]</c>, milliseconds between growth steps.</summary>
        private int m_growInterval;

        /// <summary>Native <c>[obj+0x84]</c>, damage added per step.</summary>
        private int m_growStep;

        /// <summary>Native <c>[obj+0x78]</c>, the map re-assert timestamp.</summary>
        private int m_reAddTick;

        /// <summary>
        /// Native ctor <c>sub_717A38</c>. Parameter order comes from the stack
        /// slots it reads: ecx = Envir (passed straight through to
        /// TFireBurnEvent.Create, never reloaded), then [ebp+0x20] X,
        /// [ebp+0x1C] Y, [ebp+0x18] duration, [ebp+0x14] damage,
        /// [ebp+0x10] growth interval, [ebp+0x0C] growth step, [ebp+8] owner.
        /// </summary>
        public BTFireBurnEvent(Envirnoment envir, int nX, int nY, int nTime,
            int nDamage, int nGrowInterval, int nGrowStep, TBaseObject owner)
            : base(envir, null, nX, nY, Grobal2.ET_FIRE, nTime, nDamage)
        {
            // 0x717A6D  89 83 80 00 00 00   mov [ebx+0x80],eax
            m_growInterval = nGrowInterval;
            // 0x717A76  89 83 84 00 00 00   mov [ebx+0x84],eax
            m_growStep = nGrowStep;
            // 0x717A7E  89 43 7C            mov [ebx+0x7C],eax   (eax = 0)
            m_growTick = 0;
            // 0x717A81  C7 43 54 E8 03 00 00  mov [ebx+0x54],0x3E8
            m_fireRunInterval = 0x3E8;
            // 0x717A8B  89 43 14            mov [ebx+0x14],eax
            m_OwnBaseObject = owner;
            // 0x717A8E  83 7B 20 00         cmp dword [ebx+0x20],0
            // 0x717A92  74 07               je 0x717A9B     <- skips BOTH stores
            // 0x717A94  89 73 20            mov [ebx+0x20],esi
            // 0x717A97  C6 43 0C 15         mov byte [ebx+0x0C],0x15
            // The type stamp lives inside the guard: an event whose duration was
            // zeroed by a failed AddToMap keeps the inherited type 5 and stays at
            // duration 0. Only the successful path becomes a 0x15.
            if (ContinueTime != 0)
            {
                ContinueTime = nTime;
                m_nEventType = Grobal2.ET_BTFIREBURN;
            }
        }

        /// <summary>
        /// Native <c>0x717ABC</c>. Two independent timers run before the inherited
        /// TFireBurnEvent.Run pulse.
        /// </summary>
        public override void Run(int currentTick)
        {
            // 0x717AC6  8B 4B 7C   mov ecx,[ebx+0x7C]
            // 0x717AC9  85 C9      test ecx,ecx
            // 0x717ACB  75 05      jne 0x717AD2
            // 0x717ACD  89 73 7C   mov [ebx+0x7C],esi     ; first Run only seeds
            if (m_growTick == 0)
            {
                m_growTick = currentTick;
            }
            else
            {
                // 0x717AD4  2B C1 / 0x717AD6 99 / 0x717AD7 33 C2 / 0x717AD9 2B C2
                //   = abs(now - growTick), a SIGNED absolute value
                // 0x717ADB  3B 83 80 00 00 00  cmp eax,[ebx+0x80]
                // 0x717AE1  7C 26              jl  0x717B09   -> below interval, skip
                var delta = unchecked(currentTick - m_growTick);
                if (delta < 0)
                {
                    delta = unchecked(-delta);
                }
                if (delta >= m_growInterval)
                {
                    m_growTick = currentTick;
                    // 0x717AE6  83 BB 84 00 00 00 00  cmp dword [ebx+0x84],0
                    // 0x717AED  7D 08                 jge 0x717AF7
                    // 0x717AEF  33 C0 / 89 83 84...   negative step is clamped to 0
                    if (m_growStep < 0)
                    {
                        m_growStep = 0;
                    }
                    // 0x717AF7  81 7B 48 A0 86 01 00  cmp dword [ebx+0x48],0x186A0
                    // 0x717AFE  7D 09                 jge 0x717B09
                    // 0x717B06  01 43 48              add [ebx+0x48],eax
                    // The ceiling is tested BEFORE the add, so the final damage can
                    // overshoot 100000 by one step. That is native behaviour.
                    if (m_nDamage < 0x186A0)
                    {
                        m_nDamage = unchecked(m_nDamage + m_growStep);
                    }
                }
            }

            // 0x717B0B  2B 43 78            sub eax,[ebx+0x78]
            // 0x717B0E  3D E0 93 04 00      cmp eax,0x493E0     ; 300000 ms
            // 0x717B13  72 18               jb  0x717B2D        ; unsigned
            // 0x717B2A  FF 57 28            call [Envir.VMT+0x28] = AddToMap
            // m_reAddTick starts at 0 (Delphi zero-fills the instance and the
            // constructor never writes +0x78), so the very first Run re-asserts.
            if (unchecked((uint)(currentTick - m_reAddTick)) >= 0x493E0u)
            {
                m_reAddTick = currentTick;
                if (m_Envir != null)
                {
                    m_Envir.AddToMap(m_nX, m_nY, CellType.OS_EVENTOBJECT, this);
                }
            }

            // 0x717B31  E8 82 FE FF FF  call 0x7179B8  = inherited TFireBurnEvent.Run
            base.Run(currentTick);
        }

        /// <summary>
        /// Native <c>0x717B3C</c>. Note what is NOT here: no owner test, no
        /// IsProperTarget, no self-exclusion. It hits anything alive standing on
        /// the cell, including the caster. Result byte <c>[ebp-1]</c> is seeded to
        /// 1 at 0x717B5B and never cleared, so it always reports true.
        /// </summary>
        public override bool ApplyTo(TBaseObject target)
        {
            // 0x717B5F  85 DB / 0F 84 9E 00 00 00   je 0x717C05
            if (target == null)
            {
                return true;
            }
            // 0x717B69  E8 3A B2 05 00  call 0x772DA8 = `mov al,[eax+0x74]` (death)
            // 0x717B6E  84 C0 / 0F 85 8F 00 00 00  jne 0x717C05
            if (target.m_boDeath)
            {
                return true;
            }
            // 0x717B76  80 7B 73 00 / 0F 85 85 00 00 00   ghost byte [obj+0x73]
            if (target.m_boGhost)
            {
                return true;
            }

            // 0x717B80  8B 46 48 / 50        push damage
            // 0x717B84  6A 00                push 0
            // 0x717B86  66 B9 D4 00          mov cx,0xD4      ; kind 212
            // 0x717B8A  33 D2                xor edx,edx      ; NIL attacker
            // 0x717B90  FF 97 B4 01 00 00    call [target.VMT+0x1B4] = sub_76C35C
            // sub_76C35C picks the armour arm: 212 is < 1000 (0x76C42C cmp bx,0x3E8)
            // and bit 212 of the bitset at 0x76C49C is clear (set bits are
            // 3,4,7,12,25,26,27,50,58,70,71,72,73,230), so 0x76C447 jae falls to
            // 0x76C45E -> sub_7679B8 = GetMagStruckDamage (MAC).
            // The PK preamble at 0x76C397-0x76C427 needs a non-nil attacker
            // (0x76C38A InheritsFrom on [ebp-4]); edx is 0 here so it is skipped.
            var damage = target.GetMagStruckDamage(null, m_nDamage);
            // 0x717B96  85 C0 / 7E 1B    jle 0x717BB5
            if (damage > 0)
            {
                // 0x717B9A  68 D4 00 00 00  push 0xD4   -> wParam  ([ebp+0x1C])
                // 0x717B9F  50              push eax    -> nParam1 ([ebp+0x18])
                // 0x717BA0-0x717BA6  four zeroes
                // 0x717BA8  66 B9 2C 27     mov cx,0x272C
                // 0x717BAC  33 D2           xor edx,edx  ; BaseObject = nil
                // 0x717BB0  E8 B3 E2 04 00  call 0x765E68
                target.SendRefMsg(Grobal2.RM_10028, 0xD4, damage, 0, 0,
                    string.Empty);
            }

            // 0x717BB5  81 7E 48 80 C3 C9 01  cmp dword [esi+0x48],0x1C9C380
            // 0x717BBC  75 47                 jne 0x717C05
            // A damage value of exactly 30,000,000 is a sentinel, not a number:
            // it runs the script call `@AddFatalBombCount` (Delphi long string at
            // 0x717C34, length prefix 0x11 = 17 at 0x717C30) against the global
            // script engine at [0x7D5D20], gated on owner present, owner is not the
            // target, and m_btRaceServer ([+0x178]) == 0 — i.e. both sides are
            // players — at 0x717BC9 and 0x717BD2.
            // Not modelled: the C# script host has no @AddFatalBombCount entry and
            // inventing one would be worse than leaving the sentinel inert.
            return true;
        }
    }
}

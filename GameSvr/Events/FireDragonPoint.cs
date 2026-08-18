using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TFireDragonPoint</c>, self-pointer 0x717144 / VMT 0x717190,
    /// instance size 80, parent TMapEvent. Constructor <c>sub_718BB8</c>
    /// (<c>ret 0x18</c>), ApplyTo <c>0x718B58</c>, Run <c>0x718C50</c>,
    /// destructor inherited (0x7173E0). Fields beyond TMapEvent:
    /// <c>[+0x48]</c> damage and <c>[+0x4C]</c> the pulse timestamp.
    /// <para>
    /// Its type byte is <c>0x0F</c>, from <c>0x718BD9 6A 0F push 0xF</c> — the
    /// factory confirms it at <c>0x718A31 sub dl,7</c>, which is 5+3+7 = 15 on
    /// the cumulative chain. (The <c>0x718AB6 6A 01</c> nearby is the visible
    /// flag of the factory's DEFAULT plain-TMapEvent arm, not this class's type.)
    /// </para>
    /// </summary>
    public class FireDragonPoint : Event
    {
        /// <summary>Native <c>[obj+0x4C]</c>.</summary>
        private int m_pointRunTick;

        /// <summary>
        /// Native ctor <c>sub_718BB8</c>: ecx = Envir, [ebp+0x1C] owner,
        /// [ebp+0x18] X, [ebp+0x14] Y, [ebp+0x10] duration, [ebp+0x0C] damage,
        /// [ebp+8] visible. Unlike its siblings this one really does take the
        /// visible flag as a parameter — <c>0x718BDB 8A 45 08 / 50</c> is the
        /// push that lands on TMapEvent's visible slot.
        /// </summary>
        public FireDragonPoint(Envirnoment envir, TBaseObject owner, int nX,
            int nY, int nTime, int nDamage, bool boVisible)
            : base(envir, nX, nY, Grobal2.ET_FIREDRAGONPOINT, nTime, boVisible)
        {
            // 0x718BEC  C6 46 34 01  mov byte [esi+0x34],1
            NativeAppliesOnLanding = true;
            // 0x718BF3  89 46 48     mov [esi+0x48],eax
            m_nDamage = nDamage;
            // 0x718BF9  89 46 14     mov [esi+0x14],eax
            m_OwnBaseObject = owner;
            // There is no `owner == nil restores the duration` tail here; that
            // idiom belongs to the fire/trap constructors, not this one.
        }

        /// <summary>
        /// Native <c>0x718C50</c>. Pulses on <c>0x7D0</c> = 2000 ms
        /// (<c>0x718C5F 3D D0 07 00 00</c> / <c>0x718C64 76 jbe</c>) and, unlike
        /// the traps, grabs a SINGLE occupant through <c>0x718C80 call
        /// 0x7784A8</c> rather than sweeping a list.
        /// </summary>
        public override void Run(int currentTick)
        {
            if (unchecked((uint)(currentTick - m_pointRunTick)) > 0x7D0u)
            {
                m_pointRunTick = currentTick;
                if (m_Envir != null)
                {
                    // 0x718C70  6A 01  push 1
                    if (m_Envir.GetMovingObject((short)m_nX, (short)m_nY, true)
                        is TBaseObject occupant)
                    {
                        // 0x718C8F  FF 51 08  call [Self.VMT+8] = ApplyTo
                        ApplyTo(occupant);
                    }
                }
            }
            // 0x718C96  E8 AD E7 FF FF  call 0x717448 = inherited TMapEvent.Run
            base.Run(currentTick);
        }

        /// <summary>
        /// Native <c>0x718B58</c>. Result byte is seeded to 0 at
        /// <c>0x718B63 C6 45 FF 00</c> and never set, so it always reports false.
        /// </summary>
        public override bool ApplyTo(TBaseObject target)
        {
            TBaseObject owner = m_OwnBaseObject;
            // 0x718B67  83 7B 14 00 / 74 40 — no owner, no effect. The engine
            // factory builds these with owner = nil (0x718A94 6A 00), so a
            // factory-built fire dragon point is inert by construction.
            if (owner == null)
            {
                return false;
            }
            // 0x718B71  E8 A6 00 00 00  call 0x718C1C — a local predicate:
            //   0x718C27 target nil, 0x718C2D ghost [+0x73],
            //   0x718C31 death (0x772DA8 = [+0x74]),
            //   0x718C3E call [target.VMT+0xB4] must be non-zero
            if (target == null || target.m_boGhost || target.m_boDeath)
            {
                return false;
            }

            // 0x718B7A  8B 43 48 / 50      push damage
            // 0x718B7E  6A 00              push 0
            // 0x718B80  66 B9 16 00        mov cx,0x16   ; kind 22
            // 0x718B84  33 D2              xor edx,edx   ; NIL attacker
            // 0x718B8A  FF 97 B4 01 00 00  call [target.VMT+0x1B4]
            // 22 < 1000 and bit 22 of the bitset at 0x76C49C is clear, so
            // sub_76C35C falls to sub_7679B8 = GetMagStruckDamage (MAC). The nil
            // attacker skips the PK preamble at 0x76C397.
            var damage = target.GetMagStruckDamage(null, m_nDamage);
            // 0x718B90  85 C0 / 7E 19  jle
            if (damage > 0)
            {
                // 0x718B94  6A 16   push 0x16   -> wParam
                // 0x718B96  50      push eax    -> nParam1
                // 0x718B9F  66 B9 2C 27  mov cx,0x272C
                // 0x718BA3  8B 53 14     mov edx,[ebx+0x14]  ; BaseObject = owner
                target.SendRefMsg(Grobal2.RM_10028, 0x16, damage, 0, 0,
                    string.Empty);
            }
            return false;
        }
    }
}

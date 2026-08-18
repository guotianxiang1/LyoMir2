using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TOnceDamageTrapEvent</c>, self-pointer 0x716D94 / VMT
    /// 0x716DE0, instance size 84, parent TMapEvent. Constructor
    /// <c>sub_717E38</c> (<c>ret 0x14</c>), ApplyTo <c>0x717EE8</c>, Run
    /// <c>0x717F80</c>, destructor <c>0x717EB4</c>.
    /// <para>
    /// It carries the same type byte 0x19 as <see cref="DebuffTrapEvent"/>
    /// (<c>0x717E5B 6A 19</c> vs <c>0x717CC7 6A 19</c>) but is a different class
    /// with a different field layout: <c>[+0x48]</c> damage, <c>[+0x4C]</c>
    /// pulse timestamp, <c>[+0x50]</c> the member TList. The type byte alone
    /// cannot tell the two apart.
    /// </para>
    /// <para>
    /// "Once" is literal: the first target that takes damage ends the event, via
    /// <c>0x717F6F 33 C0 / 0x717F71 89 43 20 mov [ebx+0x20],eax</c> — duration
    /// goes to 0 so the inherited Run closes it on the very next tick.
    /// </para>
    /// </summary>
    public class OnceDamageTrapEvent : Event
    {
        /// <summary>Native <c>[obj+0x4C]</c>.</summary>
        private int m_trapRunTick;

        /// <summary>
        /// Native <c>[obj+0x50]</c>, allocated in the constructor at
        /// <c>0x717E83</c> and freed in the destructor at <c>0x717EC5</c>.
        /// </summary>
        private readonly List<TBaseObject> m_targetList = new();

        /// <summary>
        /// Native ctor <c>sub_717E38</c>: ecx = Envir, [ebp+0x18] owner,
        /// [ebp+0x14] X, [ebp+0x10] Y, [ebp+0x0C] duration, [ebp+8] damage.
        /// </summary>
        public OnceDamageTrapEvent(Envirnoment envir, TBaseObject owner, int nX,
            int nY, int nTime, int nDamage)
            : base(envir, nX, nY, Grobal2.ET_TRAP, nTime, true)
        {
            // 0x717E6C  C6 43 34 01   mov byte [ebx+0x34],1
            NativeAppliesOnLanding = true;
            // 0x717E73  89 43 14      mov [ebx+0x14],eax
            m_OwnBaseObject = owner;
            // 0x717E79  89 43 48      mov [ebx+0x48],eax
            m_nDamage = nDamage;
            // 0x717E8B  83 7B 14 00 / 75 03 / 89 73 20
            if (owner == null)
            {
                ContinueTime = nTime;
            }
        }

        /// <summary>
        /// Native <c>0x717F80</c> — the same sweep as TDebuffTrapEvent.Run with
        /// the fields shifted by four (<c>[+0x4C]</c> tick, <c>[+0x50]</c> list).
        /// Period is <c>0xBB8</c> = 3000 ms
        /// (<c>0x717F90 3D B8 0B 00 00</c> / <c>0x717F95 76 jbe</c>).
        /// </summary>
        public override void Run(int currentTick)
        {
            if (unchecked((uint)(currentTick - m_trapRunTick)) > 0xBB8u)
            {
                m_trapRunTick = currentTick;
                m_targetList.Clear();
                if (m_Envir != null)
                {
                    // 0x717FAF  6A 01 push 1  ; skip the dead
                    m_Envir.GetBaseObjects(m_nX, m_nY, true, m_targetList);
                    for (var i = 0; i < m_targetList.Count; i++)
                    {
                        ApplyTo(m_targetList[i]);
                    }
                }
            }
            base.Run(currentTick);
        }

        /// <summary>
        /// Native <c>0x717EE8</c>. Result byte seeded to 1 at
        /// <c>0x717EF5 C6 45 FB 01</c> and never cleared.
        /// </summary>
        public override bool ApplyTo(TBaseObject target)
        {
            TBaseObject owner = m_OwnBaseObject;
            // 0x717EFE / 0x717F03 — no owner, or owner is the target
            if (owner == null || ReferenceEquals(owner, target))
            {
                return true;
            }
            // 0x717F05  80 7E 73 00 / 74 07 -> 0x717F0D 89 43 14 (drop the owner)
            if (owner.m_boGhost)
            {
                m_OwnBaseObject = null;
                return true;
            }
            // 0x717F17  E8 7C F5 04 00  call 0x767498 = IsProperTarget
            if (!owner.IsProperTarget(target))
            {
                return true;
            }

            // 0x717F20  8B 43 48 / 50        push damage
            // 0x717F24  6A 00                push 0
            // 0x717F26  66 B9 EE 03          mov cx,0x3EE     ; kind 1006
            // 0x717F2A  8B 53 14             mov edx,[ebx+0x14] ; attacker = owner
            // 0x717F32  FF 96 B4 01 00 00    call [target.VMT+0x1B4] = sub_76C35C
            // 1006 >= 1000, so 0x76C42C `cmp bx,0x3E8 / jae` takes the AC arm
            // sub_767958 = GetHitStruckDamage. Unlike the BT fire, the attacker is
            // NOT nil here, so sub_76C35C's PK preamble at 0x76C397-0x76C427 can
            // add a kind-dependent bonus before the armour roll. That preamble is
            // not modelled anywhere in C# yet - see the report.
            var damage = (int)target.GetHitStruckDamage(owner, m_nDamage);

            // Native does NOT gate this pair on damage > 0 the way the BT fire
            // does; there is no `test eax,eax / jle` between 0x717F38 and the two
            // sends. Both fire even for a fully absorbed hit.
            // 0x717F38  68 EE 03 00 00  push 0x3EE  -> wParam
            // 0x717F3D  50              push eax    -> nParam1 (the damage)
            // 0x717F46  66 B9 2C 27     mov cx,0x272C
            // 0x717F4A  33 D2           xor edx,edx  ; BaseObject = nil
            // 0x717F4F  E8 14 DF 04 00  call 0x765E68
            target.SendRefMsg(Grobal2.RM_10028, 0x3EE, damage, 0, 0, string.Empty);

            // 0x717F54  6A 20           push 0x20   -> wParam
            // 0x717F5E  33 C9           xor ecx,ecx
            // 0x717F60  66 BA 05 29     mov dx,0x2905
            // 0x717F69  FF 96 D8 00 00 00  call [target.VMT+0xD8] (enqueue slot)
            target.SendRefMsg(Grobal2.RM_10501, 0x20, 0, 0, 0, string.Empty);

            // 0x717F6F  33 C0 / 0x717F71 89 43 20 — the event kills itself.
            ContinueTime = 0;
            return true;
        }
    }
}

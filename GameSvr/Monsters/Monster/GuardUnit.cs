using SystemModule;

namespace GameSvr
{
    public class GuardUnit : AnimalObject
    {
        public int dw54C = 0;
        public int m_nX550 = 0;
        public int m_nY554 = 0;

        /// <summary>
        /// 战神 <c>obj+0x4E0</c> — the guard's "idle facing", a SIGNED dword whose
        /// <c>-1</c> means "no idle facing, never turn".
        /// <para>
        /// It must not be a <c>byte</c>. The ArcherGuard ctor writes the sentinel
        /// as a full dword — <c>0x684819 C7 86 E0 04 00 00 FF FF FF FF</c> =
        /// <c>mov dword [esi+0x4E0], -1</c> — and <c>Run</c> tests it signed:
        /// <c>0x6848A0 mov eax,[ebx+0x4E0] / 0x6848A6 test eax,eax / 0x6848A8
        /// jl</c> skips the idle turn entirely. Only then does it compare against
        /// the current facing (<c>0x6848AC mov dl,[ebx+0x154]</c> = m_btDirection)
        /// and call the turn at <c>0x6848BE</c>.
        /// </para>
        /// <para>
        /// With a <c>byte</c> field the <c>-1</c> is unrepresentable, so the guard
        /// defaults to facing 0 and a <c>&gt;= 0</c> test can never fail — the
        /// sentinel branch becomes dead code and freshly-spawned archers snap to
        /// direction 0 instead of holding whatever way they were placed.
        /// </para>
        /// </summary>
        public int m_nDirection = -1;

        /// <summary>
        /// 战神 <c>obj+0x4E4</c> — the DEAD-structure repair clock, distinct from
        /// <c>obj+0x338</c> (<c>m_dwStruckTick</c>), which is the ALIVE one.
        /// <para>
        /// Both repair paths pick between them on a death test: the door at
        /// <c>0x65B53B call 0x772DA8 / test al,al / 0x65B542 jne 0x65B570</c> takes
        /// the alive branch <c>0x65B54C sub eax,[esi+0x338]</c> or the dead branch
        /// <c>0x65B578 sub eax,[esi+0x4E4]</c>; the wall repeats it exactly at
        /// <c>0x65B5F6</c> / <c>0x65B604</c> / <c>0x65B630</c>. Both compare
        /// against <c>0xEA60</c> = 60000 ms.
        /// </para>
        /// <para>
        /// The clock is started by the structure's own <c>Die</c> — for the door
        /// that is <c>TCastleDoor</c> VMT <c>0x6841AC</c> slot <c>+0x84</c> =
        /// <c>sub_684AB8</c>, which calls the inherited death at <c>0x684AC0</c>
        /// then <c>0x684AC5 call GetTickCount / 0x684ACA mov [ebx+0x4E4],eax</c>.
        /// (VMT confirmed by the Delphi SelfPtr check <c>dword[V-0x4C] == V</c>,
        /// class name ShortString at <c>V-0x2C</c> reading 'TCastleDoor', parent
        /// 'TGuardUnit'.)
        /// </para>
        /// <para>
        /// Using <c>m_dwStruckTick</c> for the dead branch makes a destroyed gate
        /// repairable the instant it falls, because nothing struck it after death
        /// so its struck-tick is already 60 s stale.
        /// </para>
        /// </summary>
        public int m_dwDeadRepairTick = 0;

        public override void Struck(TBaseObject hiter)
        {
            base.Struck(hiter);
            if (m_Castle != null)
            {
                bo2B0 = true;
                m_dw2B4Tick = HUtil32.GetTickCount();
            }
        }

        public override bool IsProperTarget(TBaseObject BaseObject)
        {
            var result = false;
            if (m_Castle != null)
            {
                if (m_LastHiter == BaseObject)
                {
                    result = true;
                }
                if (BaseObject.bo2B0)
                {
                    if ((HUtil32.GetTickCount() - BaseObject.m_dw2B4Tick) < (2 * 60 * 1000))
                    {
                        result = true;
                    }
                    else
                    {
                        BaseObject.bo2B0 = false;
                    }
                    if (BaseObject.m_Castle != null)
                    {
                        BaseObject.bo2B0 = false;
                        result = false;
                    }
                }
                if (m_Castle.m_boUnderWar)
                {
                    result = true;
                }
                if (m_Castle.m_MasterGuild != null)
                {
                    if (BaseObject.m_Master == null)
                    {
                        if (m_Castle.m_MasterGuild == BaseObject.m_MyGuild || m_Castle.m_MasterGuild.IsAllyGuild(BaseObject.m_MyGuild))
                        {
                            if (m_LastHiter != BaseObject)
                            {
                                result = false;
                            }
                        }
                    }
                    else
                    {
                        if (m_Castle.m_MasterGuild == BaseObject.m_Master.m_MyGuild || m_Castle.m_MasterGuild.IsAllyGuild(BaseObject.m_Master.m_MyGuild))
                        {
                            if (m_LastHiter != BaseObject.m_Master && m_LastHiter != BaseObject)
                            {
                                result = false;
                            }
                        }
                    }
                }
                if (BaseObject.m_boAdminMode || BaseObject.m_boStoneMode || BaseObject.m_btRaceServer >= Grobal2.RC_NPC && BaseObject.m_btRaceServer < Grobal2.RC_ANIMAL || BaseObject == this || BaseObject.m_Castle == m_Castle)
                {
                    result = false;
                }
                return result;
            }
            if (m_LastHiter == BaseObject)
            {
                result = true;
            }
            if (BaseObject.m_TargetCret != null && BaseObject.m_TargetCret.m_btRaceServer == 112)
            {
                result = true;
            }
            if (BaseObject.PKLevel() >= 2)
            {
                result = true;
            }
            if (BaseObject.m_boAdminMode || BaseObject.m_boStoneMode || BaseObject == this)
            {
                result = false;
            }
            return result;
        }
    }
}
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TMapScriptEvt</c>, self-pointer 0x7171E0 / VMT 0x71722C,
    /// instance size 76, parent TMapEvent. Constructor <c>sub_719B54</c>
    /// (<c>ret 0x14</c>), ApplyTo <c>0x719B0C</c>, Run <c>0x719BB4</c>,
    /// destructor inherited. One extra field, <c>[+0x48]</c>, holding the NPC
    /// whose script gets called. Type byte <c>0x23</c> from
    /// <c>0x719B78 6A 23</c>.
    /// </summary>
    public class MapScriptEvt : Event
    {
        /// <summary>
        /// The label the event calls on its NPC. Delphi long string at
        /// <c>0x719B40</c>, refcount <c>FF FF FF FF</c> at 0x719B38 and length
        /// prefix <c>11 00 00 00</c> = 17 at 0x719B3C, bytes
        /// <c>40 4D 61 70 53 63 72 69 70 74 45 76 74 50 72 6F 63</c>.
        /// </summary>
        public const string ScriptLabel = "@MapScriptEvtProc";

        /// <summary>Native <c>[obj+0x48]</c>, set at <c>0x719B92 89 77 48</c>.</summary>
        private readonly object m_ScriptNpc;

        /// <summary>
        /// Native ctor <c>sub_719B54</c>. Note the Envirnoment does NOT arrive in
        /// ecx here the way it does for every sibling: ecx is the NPC, and the map
        /// is reloaded from a stack slot right before the base call
        /// (<c>0x719B82 8B 4D 18 mov ecx,[ebp+0x18]</c>). Slots are
        /// ecx = NPC, [ebp+0x18] Envir, [ebp+0x14] X, [ebp+0x10] Y,
        /// [ebp+0x0C] duration, [ebp+8] visible.
        /// </summary>
        public MapScriptEvt(object scriptNpc, Envirnoment envir, int nX, int nY,
            int nTime, bool boVisible)
            : base(envir, nX, nY, Grobal2.ET_MAPSCRIPT, nTime, boVisible)
        {
            // 0x719B8E  C6 47 34 01  mov byte [edi+0x34],1
            NativeAppliesOnLanding = true;
            m_ScriptNpc = scriptNpc;
        }

        /// <summary>
        /// Native <c>0x719BB4</c>. It calls the inherited TMapEvent.Run FIRST
        /// (<c>0x719BC1 E8 82 D8 FF FF call 0x717448</c>) and only then runs its
        /// own 5-minute timer, which re-asserts the map cell — and, unlike the BT
        /// fire's equivalent, restamps <c>[obj+8]</c>, the lifetime baseline:
        /// <c>0x719BD2 89 73 08 mov [ebx+8],esi</c>.
        /// <para>
        /// The consequence is worth stating plainly: because the expiry test that
        /// already ran used the OLD baseline, any script event whose duration is
        /// at least 300000 ms has its clock reset before it can ever expire, so it
        /// lives forever. Shorter ones expire normally. Replicated as-is.
        /// </para>
        /// </summary>
        public override void Run(int currentTick)
        {
            base.Run(currentTick);
            // 0x719BCB  3D E0 93 04 00  cmp eax,0x493E0
            // 0x719BD0  72 18           jb (unsigned)
            if (unchecked((uint)(currentTick - OpenStartTick)) < 0x493E0u)
            {
                return;
            }
            RefreshOpenStartTick(currentTick);
            if (m_Envir != null)
            {
                // 0x719BE7  FF 53 28  call [Envir.VMT+0x28] = AddToMap
                m_Envir.AddToMap(m_nX, m_nY, CellType.OS_EVENTOBJECT, this);
            }
        }

        /// <summary>
        /// Native <c>0x719B0C</c>. The only gate is the target's <c>[+0x178]</c>
        /// byte (<c>0x719B13 80 BA 78 01 00 00 00 / 0x719B1A 75 13 jne</c>) — no
        /// owner, no IsProperTarget, no ghost or death test. <c>[+0x178]</c> is
        /// m_btRaceServer and zero is RC_PLAYOBJECT (same field and same test as
        /// <c>0x7483BD cmp byte [ebx+0x178],0</c> and
        /// <c>0x786813 cmp byte [ecx+0x178],0</c>), so only players trip it.
        /// When it passes it calls the NPC's script entry through
        /// <c>0x719B2C FF 56 44</c> (NPC VMT+0x44) and returns true.
        /// </summary>
        public override bool ApplyTo(TBaseObject target)
        {
            if (target == null || target.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
            {
                return false;
            }
            // 0x719B1E  68 40 9B 71 00  push 0x719B40  ("@MapScriptEvtProc")
            // 0x719B23  6A 00 / 0x719B25 33 C9  two nil arguments
            // 0x719B27  8B 40 48        mov eax,[eax+0x48]   ; the NPC
            // 0x719B2C  FF 56 44        call [NPC.VMT+0x44]
            // Clone-NPC and map-quest callsites independently identify NPC
            // VMT+0x44 as the no-argument label entry represented by GotoLable.
            // The two pushed zeroes are the empty argument and false jump mode.
            ((NormNpc)m_ScriptNpc).GotoLable((TPlayObject)target,
                ScriptLabel, false);
            return true;
        }
    }
}

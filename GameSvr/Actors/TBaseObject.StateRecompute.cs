namespace GameSvr
{
    // =====================================================================
    // STATE-32 — the bonus-ability recompute switch of native sub_7733C0.
    //
    // sub_7733C0 is the "apply all timed-ability contributions" pass. Its four
    // callers (0x60A8B9 / 0x71E1BC / 0x73DD6B / 0x73E43E) each do
    // `lea edx,[obj+0x264]` then `call 0x7733C0`, so:
    //   edi = Self          (the object)
    //   esi = Self+0x264    (the ability accumulator — STATE-33: it is the
    //                        object's OWN live ability record, accumulated IN
    //                        PLACE, never zeroed by the recompute — STATE-35)
    //   ebx = current state record; ebx+1 = id, ebx+0xA = value (STATE-04)
    // The pass walks the head-inserted state list (newest first), so any
    // handler that multiplies vs. subtracts sees list order — STATE-34. The C#
    // port keeps that: AddTimedAbilityInternal head-inserts and
    // ApplyTimedAbilityBonuses walks m_TimedAbilityHead head->tail.
    //
    // Dispatch @0x773400 (byte-verified):
    //   773400  83 C0 EB              add  eax,-0x15          ; bias by state 0x15
    //   773403  83 F8 55              cmp  eax,0x55           ; 86 entries
    //   773406  0F 87 ..              ja   0x773B1D           ; DEFAULT no-op
    //   77340C  8A 80 19 34 77 00     mov  al,[eax+0x773419]  ; bytetab (86 bytes)
    //   773412  FF 24 85 6F 34 77 00  jmp  [eax*4+0x77346F]   ; jmptab (29 slots)
    // jmptab H00 = 0x773B1D is the DEFAULT (silent no-op). The other 28 slots
    // are real handlers. Full state->handler map (measured from the image):
    //
    //   state 0x15 H02 0x7734FE  MAC-hi += Level(+0x278)/7 + 2   FAIL-CLOSED
    //   state 0x16 H01 0x7734E3  AC-hi  += Level(+0x278)/7 + 2   FAIL-CLOSED
    //   state 0x17..0x1F        H00 default no-op
    //   state 0x20 H03 0x773519  DC += v (lo+hi)         -> ApplyTimedAbilityBonuses case 0
    //   state 0x21 H04 0x77352A  MC += v (lo+hi)         -> case 1
    //   state 0x22 H05 0x77353B  SC += v (lo+hi)         -> case 2
    //   state 0x23 H09 0x77357F  hitspeed(+0x274) += v   -> case 3   (STATE-47)
    //   state 0x24 H0A 0x77358C  MaxHP(+0x2B0) += v      -> case 4
    //   state 0x25 H0B 0x773597  MaxMP(+0x2B8) += v      -> case 5
    //   state 0x26 H0C 0x7735A2  speed(+0x266) += v      -> case 6
    //   state 0x27 H0D 0x7735AF  antiMagic(+0x270) += v  -> case 7
    //   state 0x28 H06 0x77354C  AC += v (lo+hi)         -> case 8
    //   state 0x29 H07 0x77355D  MAC += v (lo+hi)        -> case 9
    //   state 0x2A H12 0x773636  x1.2 DC/MC/SC, x1.5 AC/MAC/MaxHP/MaxMP when
    //                            value==1                -> case 10 (STATE-37)
    //   state 0x2B H08 0x77356E  SC += v (lo+hi)         -> case 11 (THIS FILE)
    //   state 0x2C H0F 0x7735C9  MaxWeight/Wear/Hand x2  -> case 12 (STATE-39)
    //   state 0x2D..0x35        H00 default no-op
    //   state 0x36 H10 0x7735E0  AC/MAC -= v, floor 0    -> case 22 (STATE-38)
    //   state 0x37..0x4D        H00 default no-op
    //   state 0x4E H11 0x773625  CC(+0x2A4/+0x2A8) += v  FAIL-CLOSED (no CC field)
    //   state 0x4F..0x54        H00 default no-op
    //   state 0x55 H13 0x77376E  job-scaled MaxHP/MaxMP + SM 0xFA message,
    //                            [Self+0x439] dirty flag  FAIL-CLOSED (side effect)
    //   state 0x56..0x5A        H00 default no-op
    //   state 0x5B H14 0x7739CB  job DC/MC/SC-hi (job3 CC) += v -> case 59
    //   state 0x5C H15 0x773A0D  drugJobBonus(+0x3DC) += v      -> case 60
    //   state 0x5D H16 0x773A1D  effectStrength(+0x276) += v    -> case 61
    //   state 0x5E H17 0x773A2A  effectResist(+0x26C) += v      -> case 62
    //   state 0x5F..0x63        H00 default no-op
    //   state 0x60 H18 0x773A37  holyDefense(+0x314) += v  -> GetTimedHolyDefense 96
    //   state 0x64 H19 0x773A45  holyDefense += trunc(cur*v/100) -> GetTimedHolyDefense 100 (STATE-40)
    //   state 0x61 H1A 0x773A71  job-scaled MaxHP/MaxMP  -> case 65 (THIS FILE)
    //   state 0x62..0x66        H00 default no-op
    //   state 0x67 H1B 0x773ADC  DC/MC/SC-hi += v        -> case 71 (THIS FILE)
    //   state 0x68 H1C 0x773AF9  AC/MAC += v (lo+hi)     -> case 72 (THIS FILE)
    //   state 0x69              H00 default no-op
    //   state 0x6A H0E 0x7735BC  type74(+0x272) += v     FAIL-CLOSED (no field)
    //
    // Handlers whose targets are EXISTING m_WAbil fields and are pure
    // accumulations live below and are wired into the switch. Handlers that
    // would need a new field with no consumer (0x4E CC, 0x6A type74), or that
    // carry an un-portable side effect with no live producer (0x55 message), or
    // whose only producer is itself fail-closed (0x15 -> ArmLightGuard VMT+0x198,
    // 0x16 has no producer), are left as documented FAIL-CLOSED entries. Their
    // native producers, where they exist, live in the still-unported magic-skill
    // domain (e.g. state 0x2B @0x60A522/0x76F877, state 0x55 @0x669051), so the
    // recompute arms below are dormant until that work lands — exactly as the
    // native arms are dormant until their AddState producer fires.
    // =====================================================================
    public partial class TBaseObject
    {
        // STATE-32 H08 — band handler for state 0x2B @0x77356E, byte-verified:
        //   77356E  8B 43 0A            mov  eax,[ebx+0xA]     ; node value
        //   773571  01 46 38            add  [esi+0x38],eax    ; esi+0x38 = SC lo
        //   773574  8B 43 0A            mov  eax,[ebx+0xA]
        //   773577  01 46 3C            add  [esi+0x3C],eax    ; esi+0x3C = SC hi
        //   77357A  E9 ..               jmp  0x773B1D
        // esi+0x38/0x3C are the same SC lo/hi dwords state 0x22 (H05) writes, so
        // this is the identical AddTimedRange(SC, value) that case 2 uses. Native
        // producers: 0x60A522 and 0x76F877 (both 6000 ms), in the magic-skill
        // band that is not yet ported.
        private void ApplyRecomputeState2B_ScBoost(int value)
        {
            m_WAbil.SC = AddTimedRange(m_WAbil.SC, value);
        }

        // STATE-32 H1B — band handler for state 0x67 @0x773ADC, byte-verified:
        //   773ADC  8B 43 0A            mov  eax,[ebx+0xA]
        //   773ADF  01 87 90 02 00 00   add  [edi+0x290],eax   ; DC hi
        //   773AE5  8B 43 0A            mov  eax,[ebx+0xA]
        //   773AE8  01 87 98 02 00 00   add  [edi+0x298],eax   ; MC hi
        //   773AEE  8B 43 0A            mov  eax,[ebx+0xA]
        //   773AF1  01 87 A0 02 00 00   add  [edi+0x2A0],eax   ; SC hi
        //   773AF7  EB 24               jmp  0x773B1D
        // edi+0x290/0x298/0x2A0 are Self+0x290/0x298/0x2A0 = esi+0x2C/0x34/0x3C,
        // i.e. the SAME DC/MC/SC HIGH dwords the seed writes at 0x60A877/0x60A88B/
        // 0x60A89F (STATE-35) and that state 0x20/0x21/0x22 accumulate into
        // (STATE-36: edi- and esi-relative alias the same fields). Only the HIGH
        // word is touched, so this is AddTimedUpper on DC/MC/SC.
        private void ApplyRecomputeState67_UpperCombat(int value)
        {
            m_WAbil.DC = AddTimedUpper(m_WAbil.DC, value);
            m_WAbil.MC = AddTimedUpper(m_WAbil.MC, value);
            m_WAbil.SC = AddTimedUpper(m_WAbil.SC, value);
        }

        // STATE-32 H1C — band handler for state 0x68 @0x773AF9, byte-verified:
        //   773AF9  8B 43 0A            mov  eax,[ebx+0xA]
        //   773AFC  01 87 7C 02 00 00   add  [edi+0x27C],eax   ; AC lo
        //   773B02  8B 43 0A            mov  eax,[ebx+0xA]
        //   773B05  01 87 80 02 00 00   add  [edi+0x280],eax   ; AC hi
        //   773B0B  8B 43 0A            mov  eax,[ebx+0xA]
        //   773B0E  01 87 84 02 00 00   add  [edi+0x284],eax   ; MAC lo
        //   773B14  8B 43 0A            mov  eax,[ebx+0xA]
        //   773B17  01 87 88 02 00 00   add  [edi+0x288],eax   ; MAC hi
        //   (falls through into 0x773B1D)
        // edi+0x27C/0x280 = AC lo/hi and edi+0x284/0x288 = MAC lo/hi (== esi+0x18/
        // 0x1C/0x20/0x24, the fields state 0x28/0x29 use, STATE-36). Both words of
        // each, so AddTimedRange on AC and MAC.
        private void ApplyRecomputeState68_AcMac(int value)
        {
            m_WAbil.AC = AddTimedRange(m_WAbil.AC, value);
            m_WAbil.MAC = AddTimedRange(m_WAbil.MAC, value);
        }

        // STATE-32 H1A — band handler for state 0x61 @0x773A71, byte-verified,
        // dispatched on the job byte edi+0x72 (sub al,1 ladder):
        //   job 0 (0x773A87)  imul eax,[ebx+0xA],0x64   ; value*100
        //                     add  [edi+0x2B0],eax       ; MaxHP  (no MaxMP write)
        //   job 1 (0x773A96)  mov  eax,[ebx+0xA]/add eax,eax/lea eax,[eax+eax*4]
        //                     add  [edi+0x2B0],eax       ; MaxHP += value*10
        //                     imul eax,[ebx+0xA],0x5A    ; value*90
        //                     add  [edi+0x2B8],eax       ; MaxMP += value*90
        //   job 2 (0x773AB0)  imul eax,[ebx+0xA],0x32 -> MaxHP += value*50
        //                     imul eax,[ebx+0xA],0x32 -> MaxMP += value*50
        //   job 3 (0x773AC6)  identical to job 2 (value*50 / value*50)
        //   else              nothing
        // edi+0x2B0/0x2B8 == esi+0x4C/0x54 = MaxHP/MaxMP (case 4/5). Plain 32-bit
        // adds, no clamp — same unchecked add the MaxHP/MaxMP arms already use.
        private void ApplyRecomputeState61_JobMaxHpMp(int value)
        {
            switch (m_btJob)
            {
                case 0:
                    m_WAbil.MaxHP = unchecked(m_WAbil.MaxHP + value * 100);
                    break;
                case 1:
                    m_WAbil.MaxHP = unchecked(m_WAbil.MaxHP + value * 10);
                    m_WAbil.MaxMP = unchecked(m_WAbil.MaxMP + value * 90);
                    break;
                case 2:
                case 3:
                    m_WAbil.MaxHP = unchecked(m_WAbil.MaxHP + value * 50);
                    m_WAbil.MaxMP = unchecked(m_WAbil.MaxMP + value * 50);
                    break;
            }
        }

        // =================================================================
        // STATE-32 FAIL-CLOSED REGISTRY
        //
        // The five arms below are byte-proven but deliberately NOT wired,
        // each for a concrete reason. They are registered here (not silently
        // dropped) so that when their producer/consumer domains are ported
        // the recompute contract is already on record. None of these states
        // is admitted by IsSupportedTimedAbilityType, so none can enter the
        // C# node list today; the switch simply never reaches them.
        //
        // ---- H02 state 0x15 @0x7734FE / H01 state 0x16 @0x7734E3 ----------
        //   7734E3  0F B7 87 78 02 00 00 movzx eax,word [edi+0x278] ; Level
        //   7734EA  B9 07 00 00 00       mov   ecx,7
        //   7734F1  F7 F1                div   ecx                  ; Level/7
        //   7734F3  83 C0 02             add   eax,2                ; +2
        //   7734F6  01 46 1C             add   [esi+0x1C],eax       ; 0x16 -> AC hi
        //   (0x15 is the same body with `add [esi+0x24],eax` -> MAC hi.)
        // edi+0x278 is the object Level word (RTTI 'LevelOrder', cross-checked
        // at 0x784300 in NativeItemAcquisitionStamp). Both write existing fields
        // (AC-hi / MAC-hi) but stay fail-closed because their ONLY native
        // producer is state 0x15 from ArmLightGuard's VMT+0x198 hit-transform,
        // which GameSvr/Monsters/Monster/ArmLightGuard.cs already documents as
        // wholesale fail-closed (the monster VMT+0x14/+0x198 folding leaves no
        // override entry). Wiring the consumer half alone would be exactly the
        // asymmetry that file avoids; state 0x16 has no producer at all.
        //
        // ---- H11 state 0x4E @0x773625 ------------------------------------
        //   773625  8B 43 0A            mov  eax,[ebx+0xA]
        //   773628  01 46 40            add  [esi+0x40],eax   ; CC lo (Self+0x2A4)
        //   77362B  8B 43 0A            mov  eax,[ebx+0xA]
        //   77362E  01 46 44            add  [esi+0x44],eax   ; CC hi (Self+0x2A8)
        // CC is the job-3 fourth combat range (Self+0x2A4/0x2A8). It has no
        // m_WAbil field and its consumers — the job-3 CC attack resolvers
        // 1024/260/264/268 and the type-46 endpoint selector — are the dormant
        // fail-closed model in NativeTimedAbilityCombatConsumer.cs. Adding the
        // carrier would be a dead field. Native producer: 0x78A6A4 (3600000 ms).
        //
        // ---- H13 state 0x55 @0x77376E ------------------------------------
        //   77376E job-dispatched MaxHP(+0x4C)/MaxMP(+0x54) accumulation:
        //     job 0 -> MaxHP += v, MaxMP += 0
        //     job 1 -> MaxHP += trunc(v*0.1), MaxMP += trunc(v*0.9)
        //     job 2/3 -> MaxHP += trunc(v*0.5), MaxMP += trunc(v*0.5)
        //   then, if [edi+0x439] (a recompute-dirty byte) is set, it clears the
        //   byte and emits an SM 0xFA client message built from the value (call
        //   0x769DB4 / 0x76CB44). The x87 constants 0.1/0.9/0.5 are at
        //   0x773B78 / 0x773B84 / 0x773B90. Fail-closed: the arm carries a
        //   client-message side effect gated on a native-only dirty byte
        //   (Self+0x439) that the C# recompute path does not model, and there is
        //   no live C# producer (native producer 0x669051, 5000 ms).
        //
        // ---- H0E state 0x6A @0x7735BC ------------------------------------
        //   7735BC  66 8B 43 0A         mov  ax,word [ebx+0xA]
        //   7735C0  66 01 46 0E         add  word [esi+0x0E],ax  ; Self+0x272
        // Self+0x272 is the type-74 magic-hit carrier read by the sub_7744B4
        // contest in front of 15 spell-damage owners — again the dormant
        // fail-closed model in NativeTimedAbilityCombatConsumer.cs. No m_WAbil
        // field, consumer unwired, so wiring it would be a dead field.
        // =================================================================
    }
}

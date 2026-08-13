using SystemModule;

namespace GameSvr
{
    // ------------------------------------------------------------------------------------------
    // The read side of the @Rest slave-rest switch, [master+0x4C7].
    //
    // The switch is a single byte on the OWNING PLAYER, not on the slave. It has exactly one
    // writer in the whole image (0x623A73, the @Rest arm) and five readers, all of which resolve
    // the owner first and then test the byte:
    //
    //   0x60ABBB  cmp byte [eax+0x4C7],0  eax = [ebx+0x38C]        TFieldHero-family Run
    //                                                              (sub_60AA20, VMT slot 0x88 of
    //                                                              TFieldHero/TFieldWarHero/
    //                                                              TFieldAssHero/TFieldWizHero/
    //                                                              TFieldTaosHero/TModelHero and
    //                                                              the five TMirDotaMatchHumMon
    //                                                              classes). Resting -> jump to
    //                                                              0x60ABF0, skipping both the
    //                                                              "have target" branch and the
    //                                                              VMT[0x210] target search, and
    //                                                              land straight on the shared
    //                                                              sub_71E50C base-Run call.
    //   0x666563  cmp byte [ebx+0x4C7],0  ebx = the owner itself   sub_66622C. Resting -> skip the
    //                                                              recall: the owner-distance test
    //                                                              (same map && |dx|<=20 &&
    //                                                              |dy|<=20) and the SpaceMove
    //                                                              that follows it are bypassed.
    //   0x66663E  cmp byte [eax+0x4C7],0  eax = [ebp-4]+0x38C      sub_66622C. Resting -> call
    //                                                              sub_71E50C (base Run) and
    //                                                              return immediately.
    //   0x6736FB  cmp byte [eax+0x4C7],0  eax = VMT[0xB4] result   sub_6736E0, the predicate
    //                                                              modelled below.
    //   0x767337  cmp byte [eax+0x4C7],0  eax = [ebp-4]            sub_7671F0 target filter.
    //                                                              Resting -> xor ebx,ebx, i.e.
    //                                                              "not a valid target".
    //
    // Of those, 0x666563 / 0x66663E / 0x767337 are already carried by Monster.Run,
    // MagicMonster.Run, PercentMonster.Run and TBaseObject's target filter. 0x60ABBB belongs to
    // TFieldHero, whose C# Run is deliberately dormant (it throws), so there is nothing to gate
    // there yet. This file adds the remaining one, the predicate.
    // ------------------------------------------------------------------------------------------
    public partial class TBaseObject
    {
        /// <summary>
        /// sub_6736E0 — "is my owning player currently holding subordinates at rest?".
        /// <code>
        /// 0x6736E0  55                    push ebp
        /// 0x6736E1  8B EC                 mov  ebp, esp
        /// 0x6736E3  53                    push ebx
        /// 0x6736E4  33 DB                 xor  ebx, ebx            ; result = false
        /// 0x6736E6  8B 10                 mov  edx, [eax]
        /// 0x6736E8  FF 92 B4 00 00 00     call dword [edx+0xB4]    ; resolve the owner
        /// 0x6736EE  85 C0                 test eax, eax
        /// 0x6736F0  74 14                 je   0x673706            ; no owner -> false
        /// 0x6736F2  80 B8 78 01 00 00 00  cmp  byte [eax+0x178], 0 ; m_btRaceServer
        /// 0x6736F9  75 0B                 jne  0x673706            ; owner not a player -> false
        /// 0x6736FB  80 B8 C7 04 00 00 00  cmp  byte [eax+0x4C7], 0
        /// 0x673702  74 02                 je   0x673706            ; not resting -> false
        /// 0x673704  B3 01                 mov  bl, 1               ; true
        /// 0x673706  8B C3                 mov  eax, ebx
        /// 0x673708  5B                    pop  ebx
        /// 0x673709  5D                    pop  ebp
        /// 0x67370A  C3                    ret
        /// </code>
        /// Three conjuncts, evaluated in this order: owner exists, owner race is
        /// RC_PLAYOBJECT (0), owner's rest byte is non-zero.
        /// <para>
        /// Its single native caller is 0x6739E8, inside the Run shared by 29 战神 monster classes
        /// (TSearchMon, the six *IceMon, TSnowHuman, the fox family, TPhysicalImmuneMon,
        /// TMagicImmuneMon, the TPanJun* family, TEvilMaster and the TBog* family). That Run does
        /// `0x6739E1 call 0x71E50C` (base Run) first, then
        /// `0x6739E8 call 0x6736E0 / 0x6739ED test al,al / 0x6739EF jne 0x673C66` — a true result
        /// skips the entire remaining think. None of those 29 classes is ported to C# yet, so this
        /// predicate currently has no in-tree caller; it is the shape their Run must gate on.
        /// </para>
        /// </summary>
        public bool IsMasterResting()
        {
            var master = NativeSlaveRestOwner();
            if (master == null)
                return false;
            if (master.m_btRaceServer != Grobal2.RC_PLAYOBJECT)
                return false;
            return master.m_boSlaveRelax;
        }

        /// <summary>
        /// The owner resolution behind VMT slot 0xB4, reproduced for the predicate above. Three
        /// distinct native bodies fill that slot for the classes that can reach it:
        /// <list type="bullet">
        /// <item><description>
        /// TPlayer / TGdMsgGMAgent -> 0x6C185C, a bare <c>C3 ret</c>. Under Delphi's register
        /// convention eax still holds Self on return, i.e. <c>Result := Self</c>. This is what
        /// terminates the recursion below.
        /// </description></item>
        /// <item><description>
        /// TCreature and 117 monster classes -> 0x769910:
        /// <c>0x769916 mov esi,[eax+0x38C] / 0x76991E je (nil) / 0x769924 call dword [edx+0xB4]</c>
        /// — nil when there is no master, otherwise recurse into the master.
        /// </description></item>
        /// <item><description>
        /// The hero classes (TSecWarHero / THeroAct / TWarHero / TTaosHero / ...) -> 0x686BDC:
        /// <c>8B 80 8C 06 00 00  mov eax,[eax+0x68C]</c> then <c>ret</c>. A hero's owner lives in
        /// [hero+0x68C], which is exactly what HeroObject.m_Master models (the generic
        /// [hero+0x38C] slot is pinned NULL by 0x690B0E). Native returns it in one step instead of
        /// recursing; that only differs if a hero's owner were itself owned, which cannot happen.
        /// </description></item>
        /// </list>
        /// This is deliberately NOT routed through <see cref="GetMaster"/>: that helper returns
        /// null for RC_PLAYOBJECT, whereas native's TPlayer slot returns Self, so reusing it would
        /// make the predicate silently wrong whenever it is asked about a player directly.
        /// </summary>
        private TBaseObject NativeSlaveRestOwner()
        {
            if (this is TPlayObject)
                return this;
            var master = m_Master;
            return master?.NativeSlaveRestOwner();
        }
    }
}

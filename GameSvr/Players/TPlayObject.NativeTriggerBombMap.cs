namespace GameSvr
{
    public partial class TPlayObject
    {
        // ── MOVE-89 / TRIGGERBOMB map-flag consumer — FAIL-CLOSED (dead native code) ─
        //
        // 战神 map flag TRIGGERBOMB -> native Envir[+0x83] (TMapFlag.boTRIGGERBOMB).
        // Verified against staging/_reunpack_work/flat_image.bin (ImageBase 0x400000).
        //
        // Parse side (already reproduced by Maps.cs / TMapFlag.boTRIGGERBOMB):
        //   parser A 0x7758DF/0x7758F3 and parser B 0x776894 write byte [ebx+0x83].
        // A whole-image scan for readers of map[+0x83] returns exactly ONE consumer
        // (every other 0x83-displacement hit is an immediate / mis-aligned false
        // positive): 0x789752, inside sub_7896FC.
        //
        // Consumer chain (fully reversed):
        //   sub_7896FC(Self=item, edx=player)  — VA 0x7896FC
        //     006896FC prologue; if player==nil -> ret false
        //     0078972B cmp byte [player+0x178],0 / jne exit   ; skip if that flag set
        //     0078974C mov eax,[player+0x128]                 ; player map (PEnvir)
        //     00789752 cmp byte [eax+0x83],0                  ; boTRIGGERBOMB ?
        //     00789759 je 0x7897CE                            ; NOT set -> refuse
        //       set branch:
        //     0078975B call 0x408340 (GetTickCount)
        //     00789764 sub eax,[player+0x3B4] / cdq/xor/sub   ; abs(tick - last)
        //     0078976F cmp eax,0x12C / jl exit                ; 300 ms throttle
        //     00789776 mov [player+0x3B4],tick                ; per-PLAYER throttle store
        //     0078979F edx=0x789818 "朱火弹(幻)" + IntToStr(item[+0x1C][+0x24])  (LStrCat)
        //     007897B9 call 0x67BDCC  ; UserEngine.RegenMonsters(map, name,1,1,0,Y,X)
        //     007897C2 mov [mon+0x4DC],player                 ; bomb monster owner=player
        //       not-set branch (0x7897CE):
        //     007897CE mov cx,0xFCFF / edx=0x78982C "在这里无法使用！" / call [vtbl+0xD4]
        //   i.e. on a TRIGGERBOMB map the item, throttled to once / 300 ms per player,
        //   spawns a "朱火弹(幻)N" bomb monster (0x67BDCC -> factory sub_679F8C) at the
        //   player's cell; off-flag it SysMsg's "在这里无法使用！" (Blue 0xFCFF).
        //
        //   Only caller of sub_7896FC is sub_789694 (VA 0x789694):
        //     if !(player is TPlayer)  -> false          ; InheritsFrom TPlayer (0x6AC87C)
        //     if word[item+0x26] < 0x3E8 -> false        ; item charge gate (1000)
        //     al = sub_7896FC(item, player); if al: word[item+0x26]-=0x3E8 ; consume 1000
        //     ... then a client-effect virtual call [player.VMT+0x260].
        //
        // CLASS IDENTITY — the owner of sub_789694/sub_7896FC is the class "TTimerBomb":
        //   classptr 0x781304 (vmtSelfPtr@0x7812B8=0x781304, vmtClassName@0x7812D8 ->
        //   shortstring len10 "TTimerBomb"@0x781354), instance size 0x100, parent
        //   "TBaseItem" (classptr 0x77D7A4). sub_789694 = TTimerBomb VMT+0x18, an
        //   OVERRIDE of TBaseItem VMT+0x18 whose base body 0x783D64 is the empty stub
        //   `xor eax,eax; ret 4`. So TTimerBomb is a placeable TBaseItem object, and
        //   the bomb effect is dispatched virtually via that item's VMT+0x18.
        //
        // REACHABILITY PROOF — this consumer is DEAD CODE in the native image:
        //   * sub_7896FC has exactly one caller (E8 scan): sub_789694, and zero data
        //     pointers.
        //   * sub_789694 has zero E8 callers and exactly one data pointer: the VMT slot
        //     0x78131C (= classptr 0x781304 + 0x18). It is reachable ONLY as
        //     TTimerBomb.VMT[+0x18].
        //   * the 4-byte value 0x781304 (TTimerBomb classptr) occurs in the ENTIRE
        //     image only twice — 0x7812B8 (its own vmtSelfPtr) and 0x781371 (its own
        //     RTTI ClassType self-ref). It is NEVER loaded as an immediate / classref
        //     global by any instruction, the class name has NO FindClass reference, and
        //     it is absent from the placeable-object factory 0x74CD00 (which builds its
        //     siblings — TFireFlower @classref 0x7804A4, etc.). Therefore no code path
        //     ever constructs a TTimerBomb, so TTimerBomb.VMT[+0x18] is never invoked,
        //     so sub_7896FC never runs, so the boTRIGGERBOMB byte is parsed but NEVER
        //     read at run time.
        //
        // CONSEQUENCE (1:1 obligation): in native the TRIGGERBOMB flag has NO observable
        // effect. Adding a live consumer in C# (e.g. a move-tick bomb spawner) would
        // make the port diverge from native — that would be fabrication, not
        // replication. The 300 ms field is internal to sub_7896FC (player[+0x3B4] is
        // touched by nothing else in the image); there is NO movement-code trigger.
        //
        // Therefore this consumer is deliberately UNWIRED / BLOCKED (fail-closed),
        // matching the boPAODIAN precedent (TMapFlag) and the fail-closed TFireFlower
        // detonation in TPlayObject.NativeMagic200Hijack.cs. The bomb effect itself is
        // also unportable without fabrication: it needs the unmodelled TTimerBomb /
        // TBaseItem placeable-object subsystem (the item, its +0x26 charge, +0x1C std
        // record and the +0x4DC-owner bomb-monster spawn). Do NOT wire any consumer off
        // boTRIGGERBOMB until that subsystem is reverse-engineered AND a live
        // constructor for TTimerBomb is found.
        //
        // Method kept only as the authoritative marker of the above finding; it has no
        // caller by design (there is no faithful call site) and performs no action.
        internal bool NativeTriggerBombMapConsumerIsReachable()
        {
            // Native TTimerBomb is never instantiated -> the boTRIGGERBOMB consumer is
            // dead code. No C# consumer is wired; see the header for the full proof.
            return false;
        }
    }
}

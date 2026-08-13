namespace GameSvr
{
    public partial class TPlayObject
    {
        // ── MOVE-90 / NOMAGIC map-flag consumer ──────────────────────────────────
        //
        // 战神 map flag NOMAGIC -> native Envir[+0x81] (TMapFlag.boNOMAGIC). Verified
        // against staging/_reunpack_work/flat_image.bin (ImageBase 0x400000).
        //
        // Parse side (already reproduced by Maps.cs / TMapFlag.boNOMAGIC):
        //   token str "NOMAGIC" @0x776E58 len 7; parser B sub_776008 sets byte
        //   [ebx+0x81]=1. Setters at 0x7758A5/0x7758B9 (parser A) and 0x77684E
        //   (parser B). A whole-image scan for readers of map[+0x81] returns exactly
        //   ONE consumer (all other 0x81-displacement hits are immediates/false
        //   positives): 0x6DA12B.
        //
        // Consumer — the CM_SPELL dispatcher sub_6D7D68 (the giant per-player client
        // command switch; the C# analogue is the CM_SPELL case in
        // TPlayObject.Message.cs which calls ClientSpellXY = native sub_6BC510).
        // The NOMAGIC gate sits in the dispatcher BEFORE the DoSpell call, in the
        // normal-player branch (block C, entered when the VMT+0x40 predicate is true —
        // see the block A/C note below):
        //   006DA122  8B 45 FC              mov  eax,[ebp-4]        ; Self (player)
        //   006DA125  8B 80 28 01 00 00     mov  eax,[eax+0x128]    ; m_PEnvir (map)
        //   006DA12B  80 B8 81 00 00 00 00  cmp  byte [eax+0x81],0  ; boNOMAGIC ?
        //   006DA132  75 46                 jne  0x6DA17A           ; set  -> REJECT
        //   ; not set -> fall through to DoSpell:
        //   006DA144..006DA153  push X/Y/target/magicId ; call 0x6BC510 (DoSpell)
        //   006DA158  test al / je 0x6DA17A                        ; DoSpell failed
        //   006DA15C  push 0,0,0,0 / mov dx,0x275 / call [vtbl+0x250] ; SUCCESS ack
        //   006DA17A  push 0,0,0,0 / mov dx,0x276 / call [vtbl+0x250] ; FAIL ack
        // The REJECT branch (0x6DA17A) is a SILENT fail: it emits the 0x276 spell
        // fail/ack message with four zero params and NO SysMsg text, and never calls
        // DoSpell. In other words: on a NOMAGIC map the cast is simply refused and the
        // client is told the action failed — exactly the answer the C# CM_SPELL case
        // already produces when ClientSpellXY returns false with dwDelayTime==0
        // (SendRefMsg(RM_MOVEFAIL) + SendDefMessage(SM_ACT_FAIL); dwDelayTime is 0 at
        // method entry — Message.cs:934 `var dwDelayTime = 0;`).
        //
        // BLOCK A vs BLOCK C (why this gate is faithful, and its one documented
        // deviation): native routes CM_SPELL through
        //   006DA09D  mov dl,1 / call [player.VMT+0x40]   ; sub_6E6700
        //   006DA0A7  test al / jne 0x6DA122              ; TRUE  -> block C (NOMAGIC)
        //   ; FALSE -> block A (0x6DA0AB): CanSpell(0x7725FC) gate, DoSpell, NO NOMAGIC
        // sub_6E6700 = `sub_76B354() && player[+0x574]==0`, and sub_76B354 returns
        // TRUE only when the player carries NONE of the status effects 0x1D/0x01/0x1A/
        // 0x18/0x3E (via HasStatus 0x772960) and passes 0x772DA8. For every ordinary
        // player with no such status and [+0x574]==0 the predicate is TRUE, so block C
        // (this NOMAGIC gate) is ALWAYS the path taken. Block A is only reached while
        // the caster is under one of those disabling states — where casting is already
        // heavily gated by ClientSpellXY's own m_boCanSpell / death / poison-stone
        // checks. Replicating sub_6E6700 verbatim would require pinning five status
        // ids + field [+0x574] to C# with certainty we do not have, so per fail-closed
        // this gate enforces NOMAGIC uniformly: 1:1 for all normal players, and the
        // restrictive (safe) direction for the rare block-A edge case. This never
        // relaxes NOMAGIC below native.
        //
        // WIRING (handed to the parent agent — TPlayObject.Message.cs is off-limits):
        //   In the CM_SPELL case (TPlayObject.Message.cs:1804-1805) guard the DoSpell
        //   call so a NOMAGIC map short-circuits into the existing fail path:
        //       case Grobal2.CM_SPELL:
        //           if (!NativeNoMagicMapForbidsSpell() &&
        //               ClientSpellXY((short)ProcessMsg.wIdent, ProcessMsg.wParam, ...))
        //   When the guard fires, ClientSpellXY is skipped (dwDelayTime stays 0) and
        //   the existing else-branch answers with the spell-fail messages — the C#
        //   analogue of native's silent 0x276 reject at 0x6DA17A.
        internal bool NativeNoMagicMapForbidsSpell()
        {
            // native 0x6DA125 mov eax,[Self+0x128] / 0x6DA12B cmp byte [eax+0x81],0.
            // A null map fails OPEN here (native dereferences [+0x128] unconditionally;
            // C# guards the null so an actor with no map can still be processed).
            return m_PEnvir != null && m_PEnvir.Flag != null && m_PEnvir.Flag.boNOMAGIC;
        }
    }
}

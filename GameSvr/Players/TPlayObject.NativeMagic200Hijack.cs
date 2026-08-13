namespace GameSvr
{
    public partial class TPlayObject
    {
        // MAGIC-U0 / ID 200 hijack. Native ClientSpellXY (sub_6BC510) runs the
        // interceptor sub_6BCD48 as its very first action, byte-verified against
        // staging/_reunpack_work/flat_image.bin (base 0x400000).
        //
        // Call site — CM_SPELL dispatcher @0x6DA0DD/0x6DA153 -> sub_6BC510
        // (Self, dx=nKey=msg.Series, ecx=Recog, X=msg.Tag, Y=msg.Param). Inside
        // sub_6BC510, ahead of the sub_772A50 skill-forbid gate (0x6BC541) and of
        // GetMagicInfo (VMT+0xE8 @0x6BC5CB):
        //   006BC52F  call 0x6BCD48                 ; hijacker(Self, nKey, Recog, X, Y)
        //   006BC534  test al,al / je 0x6BC541
        //   006BC538  mov [ebp-5],1 / jmp 0x6BCD02  ; hijacked -> return TRUE, skip dispatch
        //
        // Hijacker sub_6BCD48:
        //   006BCD6A  cmp dx,0xC8                   ; nKey == 200 ?
        //   006BCD6F  jne 0x6BCDF5                  ; no  -> return FALSE (run normal dispatch)
        //   006BCD75  mov [ebp-5],1                 ; yes -> result = TRUE, UNCONDITIONALLY
        //   006BCD7E  call 0x73CF08                 ; obj = find flower in [Self+0x508] by key=Recog
        //   006BCD87  test ebx,ebx / je 0x6BCDF5    ; miss -> return TRUE, no side effect
        //   006BCD8B  mov edx,[0x7804A4] (TFireFlower VMT 0x7804F0) / call 0x404828 (Delphi `is`)
        //   006BCD98  je 0x6BCDF5                   ; not a TFireFlower -> return TRUE
        //   006BCDA8  call 0x784C78                 ; validate(obj, Self, X, Y) / je -> return TRUE
        //   006BCDBA  call [Self.VMT+0x268]=0x73CBAC ; spawn magic-202 effect (cx=0xCA, key=obj[0x18])
        //   006BCDDF  call 0x768BE0                 ; send ident 0xB (text via sub_784568("0", obj[0x20]))
        //   006BCDE9  call 0x73D140                 ; TList.Remove([Self+0x508], obj)
        //   006BCDF0  call 0x404690                 ; obj.Free
        //
        // FAITHFUL SCOPE (implemented) — the swallow contract. result=TRUE is
        // written at 0x6BCD75 BEFORE any flower lookup, and every flower-miss branch
        // (`je 0x6BCDF5`) reaches the epilogue without clearing it, so nKey==200
        // ALWAYS returns TRUE and the normal magic ladder is skipped. Every observable
        // side effect (magic-202 spawn, ident-0xB message, remove+free) lives strictly
        // inside the "matching TFireFlower found" branch. Returning TRUE makes the
        // CM_SPELL caller (TPlayObject.Message.cs) answer with the success ack
        // SendSocket(GetGoodTick) — the C# analogue of native's 0x275 sent via
        // VMT+0x250 @0x6DA0E6 — instead of the failure answer native never emits for
        // id 200.
        //
        // FAIL-CLOSED (deliberately not fabricated) — the detonation side effect
        // depends on the TFireFlower object subsystem, which this port does not model:
        //   * the TFireFlower class and the per-player [player+0x508] flower TList;
        //   * the object factory that constructs flowers (native 0x74CD27/0x74CD53,
        //     class ref var 0x7804A4);
        //   * VMT+0x268/0x250 magic-202 spawn (the 200 -> 201 -> 202 state machine);
        //   * the ident-0xB custom message protocol;
        //   * even the input plumbing is absent — C# ClientSpellXY has no Recog (the
        //     flower key, native ecx); TPlayObject.Message.cs forwards only
        //     wParam(nKey)/nParam1/nParam2/nParam3(target).
        // With no TFireFlower ever placed (exactly this port's state), native's own
        // behaviour degenerates to "return TRUE, no side effect" — precisely what this
        // method does, so it is a 1:1 replica of native for the current subsystem
        // state, not an invented effect. When TFireFlower is ported, restore the
        // detonation branch (lookup -> is-check -> validate -> spawn 202 -> ident 0xB
        // -> remove -> free) here.
        private const int NativeMagic200HijackKey = 0xC8; // 200

        private bool TryNativeMagic200Hijack(int nKey)
        {
            if (nKey != NativeMagic200HijackKey)
            {
                return false;
            }
            // Detonation is fail-closed pending the TFireFlower subsystem (header).
            // Native swallows the cast regardless of detonation outcome, so the only
            // faithful, subsystem-independent effect is the swallow itself.
            return true;
        }
    }
}

using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// STATE-ARMS — the two state-change dispatch tables of native sub_741884.
    ///
    /// <para>
    /// sub_741884 is the shared "a state was gained / lost" notifier. It is
    /// reached as VMT+0x14; TPlayObject overrides that slot with 0x6D7628,
    /// which calls 0x741884 first and only then adds its own work. Prologue
    /// (0x741884), bytes verified:
    ///   741884  55 / 8B EC              push ebp / mov ebp, esp
    ///   74189B  8B F9                   mov  edi, ecx        ; seconds
    ///   74189D  8B DA                   mov  ebx, edx        ; state id (bl)
    ///   74189F  8B F0                   mov  esi, eax        ; Self
    ///   7418AF  8A 45 08                mov  al, [ebp+8]     ; 0 = lost, else gained
    ///   7418B9  E8 6E 9B 02 00          call 0x76B42C        ; -> 0x7729C4
    ///   7418BE  80 7D 08 00             cmp  byte [ebp+8], 0
    ///   7418C2  0F 84 CA 0D 00 00       je   0x742692        ; LOST table
    ///   7418C8  ...                                          ; GAINED table
    /// 0x76B42C is a bare thunk to 0x7729C4, the SM_CHARSTATUSCHANGED 657
    /// broadcast, so the status blob always goes out before any arm runs —
    /// that is the SendRefMsg at the head of SendTimedAbilityState.
    /// </para>
    ///
    /// <para>
    /// GAINED @0x7418C8 — 107-byte index map then a 53-entry jump table:
    ///   7418C8  33 C0                   xor  eax, eax
    ///   7418CA  8A C3                   mov  al, bl
    ///   7418CC  83 F8 6A                cmp  eax, 0x6A       ; 106
    ///   7418CF  0F 87 6D 13 00 00       ja   0x742C42        ; out of domain
    ///   7418D5  8A 80 E2 18 74 00       mov  al, [eax+0x7418E2]
    ///   7418DB  FF 24 85 4D 19 74 00    jmp  [eax*4+0x74194D]
    /// Index map @0x7418E2 (107 bytes, verbatim):
    ///   00 11 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 0E
    ///   0F 00 00 00 12 00 00 10 13 13 02 03 04 07 08 09 0B 0C 05 06 00 00
    ///   0A 15 00 00 00 14 00 00 00 13 00 00 16 00 00 00 00 00 17 18 00 00
    ///   00 00 00 00 00 00 1C 00 00 00 19 1A 1B 01 1D 1E 1F 20 21 22 23 24
    ///   25 26 00 27 28 29 2A 2B 00 2C 2D 2E 2F 30 31 32 33 34 00 0D
    /// Index 0 is the DEFAULT slot, so every state whose map byte is 0 —
    /// and every state &gt; 106 — converges on 0x742C42. 52 distinct arms.
    /// </para>
    ///
    /// <para>
    /// LOST @0x742692 — direct 93-entry jump table biased by -14:
    ///   742692  33 C0                   xor  eax, eax
    ///   742694  8A C3                   mov  al, bl
    ///   742696  83 C0 F2                add  eax, -0xE       ; state - 14
    ///   742699  83 F8 5C                cmp  eax, 0x5C       ; 92
    ///   74269C  0F 87 A0 05 00 00       ja   0x742C42
    ///   7426A2  FF 24 85 A9 26 74 00    jmp  [eax*4+0x7426A9]
    /// Domain is state 14..106; 44 of the 93 slots hold 0x742C42 verbatim
    /// (bytes <c>42 2C 74 00</c>), leaving 45 distinct arms.
    /// </para>
    ///
    /// <para>
    /// DEFAULT 0x742C42 is the function epilogue and nothing else — it is a
    /// silent no-op, not a refusal:
    ///   742C42  33 C0                   xor  eax, eax
    ///   742C44  5A / 59 / 59            pop  edx / pop ecx / pop ecx
    ///   742C47  64 89 10                mov  fs:[eax], edx   ; SEH unlink
    ///   742C4A  68 67 2C 74 00          push 0x742C67
    ///   742C4F  8D 85 98 FE FF FF       lea  eax, [ebp-0x168]
    ///   742C55  BA 5A 00 00 00          mov  edx, 0x5A       ; 90 locals
    ///   742C5A  E8 C5 28 CC FF          call 0x405524        ; finalize
    ///   742C5F  C3                      ret
    /// There is no store to Self anywhere on that path — no boSpellFail, no
    /// bitset touch, no list mutation. So a DEFAULT state still gets the
    /// 0x7729C4 status broadcast and the 3555 record; it just says nothing.
    /// Converging states are therefore left as literal holes in the switches
    /// below rather than being given invented behaviour.
    /// </para>
    ///
    /// <para>
    /// Both tables were solved exhaustively from the image; this file lands
    /// the ascending-order first third, i.e. every arm whose lowest state is
    /// &lt;= 45 (18 gained + 15 lost = 33 arms). Arms above 45 are still MISSING.
    /// State 75 is still handled inline in SendTimedAbilityState and is not
    /// part of this batch.
    /// </para>
    /// </summary>
    public partial class TBaseObject
    {
        // Arms come in exactly two colour/type pairs, both loaded as one word
        // into cx and split by the VMT+0xD4 sender (0x73C8F4) as cl = colour,
        // ch = type.
        //   `66 B9 DB FF   mov cx, 0xFFDB`  -> colour 0xDB, type 0xFF
        //   `66 B9 FF 38   mov cx, 0x38FF`  -> colour 0xFF, type 0x38
        private const byte NativeStateArmBuffColor = 0xDB;
        private const byte NativeStateArmBuffType = 0xFF;
        private const byte NativeStateArmAlertColor = 0xFF;
        private const byte NativeStateArmAlertType = 0x38;

        /// <summary>
        /// The tail every speaking arm shares:
        ///   mov eax, esi / mov ebx, [eax] / call [ebx+0xD4]
        /// TPlayObject VMT+0xD4 = 0x73C8F4, which forwards to the enqueue
        /// helper 0x765E68 with ident 0x2774 and the cx pair as the two
        /// colour/type words. For a hero the message is relayed to the owning
        /// player behind a "(英雄) " prefix (0x6899B4, Delphi length 7); that
        /// prefixing lives in the hero message pipeline, so it applies to
        /// every arm uniformly rather than to any single state.
        /// <para>
        /// Routing is lifted verbatim from the state-75 block in
        /// SendTimedAbilityState so the two stay byte-for-byte identical.
        /// Grobal2.RM_SYSMESSAGE is used rather than the raw 0x2774 the image
        /// carries, matching every other message send in the project.
        /// </para>
        /// </summary>
        private void SendNativeStateArmMsg(string text, byte color, byte type)
        {
            if (this is HeroObject hero)
            {
                if (hero.m_Master is TPlayObject master)
                {
                    master.SendMsg(hero, Grobal2.RM_SYSMESSAGE, 0,
                        color, type, 0, "(英雄) " + text);
                }
            }
            else if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                    color, type, 0, text);
            }
        }

        /// <summary>
        /// GAINED table @0x7418C8. <paramref name="seconds"/> is native edi.
        /// AddState computes it at 0x77318C, bytes verified:
        ///   77318C  6A 01                push 1              ; gained
        ///   77318E  8B 45 F8             mov  eax, [ebp-8]   ; node
        ///   773191  8B 40 02             mov  eax, [eax+2]   ; remaining ms
        ///   773194  B9 E8 03 00 00       mov  ecx, 0x3E8
        ///   773199  99 / F7 F9           cdq / idiv ecx      ; signed, to zero
        ///   77319C  8B C8                mov  ecx, eax
        ///   7731A8  FF 53 14             call [ebx+0x14]
        /// Arms that print it do `0F B7 C7 movzx eax, di`, so only the low 16
        /// bits reach the formatter — hence the ushort. A permanent node
        /// (-1 ms) divides to 0 and prints "0秒".
        /// </summary>
        private void DispatchNativeStateGainedArm(byte internalType, ushort seconds)
        {
            switch (internalType)
            {
                case 1:
                    // 0x741DAE  66 B9 FF 38 / BA 24 2E 74 00
                    // 0x742E24 len 20 C4E3B1BBD3C0BAE3B1F9C0CEC0A7D7A1C1CBA3A1
                    SendNativeStateArmMsg("你被永恒冰牢困住了！",
                        NativeStateArmAlertColor, NativeStateArmAlertType);
                    break;
                case 21:
                    // 0x741D20  68 DC 2D 74 00        push 0x742DDC
                    //           0FB7C7 movzx eax,di / E8 .. call 0x40C89C
                    //           68 94 2C 74 00        push 0x742C94
                    //           BA 03 00 00 00        mov edx,3  ; 3-part concat
                    //           E8 .. call 0x405890 / mov cx,0xFFDB
                    // 0x742DDC len 12 BFB9C4A7C1A6D4F6BCD3 3A 20 — note the
                    // trailing ":" + space are part of the literal.
                    // 0x742C94 len 2 C3EB "秒"
                    SendNativeStateArmMsg("抗魔力增加: " + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 22:
                    // 0x741D5B  68 F4 2D 74 00 ... same 3-part shape
                    // 0x742DF4 len 12 B7C0D3F9C1A6D4F6BCD3 3A 20
                    SendNativeStateArmMsg("防御力增加: " + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 26:
                    // 0x741DC6  66 B9 FF 38 / BA 44 2E 74 00
                    // 0x742E44 len 12 C4E3B1BBCAAFBBAFC1CBA3A1
                    SendNativeStateArmMsg("你被石化了！",
                        NativeStateArmAlertColor, NativeStateArmAlertType);
                    break;
                case 29:
                    // 0x741D96  66 B9 FF 38 / BA 0C 2E 74 00
                    // 0x742E0C len 12 C4E3B1BBB1F9B6B3C1CBA3A1
                    SendNativeStateArmMsg("你被冰冻了！",
                        NativeStateArmAlertColor, NativeStateArmAlertType);
                    break;
                case 30:
                case 31:
                case 53:
                    // One arm, three index-map entries: map bytes for states
                    // 30, 31 and 53 are all 0x13 (0x7418E2+30/+31/+53), so all
                    // three land on 0x741DDE. 53 is carried here because it
                    // shares this arm; it has no separate handler.
                    // 0x741DDE  66 B9 FF 38 / BA 5C 2E 74 00
                    // 0x742E5C len 10 C4E3D6D0B6BEC1CBA3A1
                    SendNativeStateArmMsg("你中毒了！",
                        NativeStateArmAlertColor, NativeStateArmAlertType);
                    break;
                // 32..41 and 44 all share the 3-part concat shape of arms 21/22,
                // differing only in the prefix literal they push.
                case 32:
                    // 0x741A5C  push 0x742CA0
                    // 0x742CA0 len 18 B9A5BBF7C9CFCFC2CFDECBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("攻击上下限瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 33:
                    // 0x741A97  push 0x742CBC
                    // 0x742CBC len 18 C4A7B7A8C9CFCFC2CFDECBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("魔法上下限瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 34:
                    // 0x741AD2  push 0x742CD8
                    // 0x742CD8 len 18 B5C0CAF5C9CFCFC2CFDECBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("道术上下限瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 35:
                    // 0x741B83  push 0x742D2C
                    // 0x742D2C len 16 B9A5BBF7CBD9B6C8CBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("攻击速度瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 36:
                    // 0x741BBE  push 0x742D48
                    // 0x742D48 len 14 C9FAC3FCD6B5CBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("生命值瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 37:
                    // 0x741BF9  push 0x742D60
                    // 0x742D60 len 14 C4A7B7A8D6B5CBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("魔法值瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 38:
                    // 0x741C6F  push 0x742D90
                    // 0x742D90 len 12 C3F4BDDDCBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("敏捷瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 39:
                    // 0x741CAA  push 0x742DA8
                    // 0x742DA8 len 12 C4A7B6E3CBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("魔躲瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 40:
                    // 0x741B0D  push 0x742CF4
                    // 0x742CF4 len 18 B7C0D3F9C9CFCFC2CFDECBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("防御上下限瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 41:
                    // 0x741B48  push 0x742D10
                    // 0x742D10 len 18 C4A7B7C0C9CFCFC2CFDECBB2BCE4CCE1B8DF
                    SendNativeStateArmMsg("魔防上下限瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 44:
                    // 0x741C34  push 0x742D78
                    // 0x742D78 len 12 C1A6C1BFCBB2BCE4CCE1B8DF
                    // 44 speaks on gain but is a DEFAULT convergence on loss
                    // (0x742721 holds 42 2C 74 00), so there is no "力量回复
                    // 正常" counterpart to look for.
                    SendNativeStateArmMsg("力量瞬间提高" + seconds + "秒",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 45:
                    // 0x741E38  66 B9 FF 38 / BA 88 2E 74 00
                    // 0x742E88 len 12 C4E3B1BBB6A8C9EDC1CBA3A1
                    SendNativeStateArmMsg("你被定身了！",
                        NativeStateArmAlertColor, NativeStateArmAlertType);
                    break;
            }
        }

        /// <summary>
        /// LOST table @0x742692. The lost path reaches the notifier from
        /// 0x77337C with no duration at all — <c>push 0 / xor ecx, ecx</c>
        /// (0x773386 / 0x773388) — so no lost arm can print a second count,
        /// and none does.
        /// </summary>
        private void DispatchNativeStateLostArm(byte internalType)
        {
            switch (internalType)
            {
                case 21:
                    // 0x742955  66 B9 DB FF / BA BC 33 74 00
                    // 0x7433BC len 16 BFB9C4A7B7A8C1A6BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("抗魔法力回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 22:
                    // 0x74296D  66 B9 DB FF / BA D8 33 74 00
                    // 0x7433D8 len 14 B7C0D3F9C1A6BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("防御力回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                // Every lost arm below is the same four-instruction shape as
                // 21/22: mov cx,0xFFDB / mov edx,<str> / mov eax,esi /
                // mov ebx,[eax] / call [ebx+0xD4] / jmp 0x742C42.
                case 32:
                    // 0x742835  BA 90 32 74 00
                    // 0x743290 len 12 B9A5BBF7BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("攻击回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 33:
                    // 0x74284D  BA A8 32 74 00
                    // 0x7432A8 len 12 C4A7B7A8BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("魔法回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 34:
                    // 0x742865  BA C0 32 74 00
                    // 0x7432C0 len 12 B5C0CAF5BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("道术回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 35:
                    // 0x7428AD  BA 08 33 74 00
                    // 0x743308 len 16 B9A5BBF7CBD9B6C8BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("攻击速度回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 36:
                    // 0x7428C5  BA 24 33 74 00
                    // 0x743324 len 14 C9FAC3FCD6B5BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("生命值回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 37:
                    // 0x7428DD  BA 3C 33 74 00
                    // 0x74333C len 14 C4A7B7A8D6B5BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("魔法值回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 38:
                    // 0x74290D  BA 70 33 74 00
                    // 0x743370 len 12 C3F4BDDDBBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("敏捷回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 39:
                    // 0x742925  BA 88 33 74 00
                    // 0x743388 len 12 C4A7B6E3BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("魔躲回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 40:
                    // 0x74287D  BA D8 32 74 00
                    // 0x7432D8 len 12 B7C0D3F9BBD8B8B4D5FDB3A3
                    // Note the asymmetry with gained 40 "防御上下限瞬间提高":
                    // the lost text drops 上下限. Both literals are verbatim.
                    SendNativeStateArmMsg("防御回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 41:
                    // 0x742895  BA F0 32 74 00
                    // 0x7432F0 len 12 C4A7B7C0BBD8B8B4D5FDB3A3
                    SendNativeStateArmMsg("魔防回复正常",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 43:
                    // 0x7428F5  BA 54 33 74 00
                    // 0x743354 len 16 CEDEBCABD5E6C6F8D7B4CCACBDE1CAF8
                    // 43 is the mirror of 44: silent on gain (index-map byte
                    // at 0x7418E2+43 is 0, the DEFAULT slot), speaking on loss.
                    // No trailing ！ on this one, unlike 45.
                    SendNativeStateArmMsg("无极真气状态结束",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
                case 45:
                    // 0x7429A4  BA 08 34 74 00
                    // 0x743408 len 14 B6A8C9EDD7B4CCACBDE1CAF8 A3A1
                    SendNativeStateArmMsg("定身状态结束！",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                    break;
            }
        }
    }
}

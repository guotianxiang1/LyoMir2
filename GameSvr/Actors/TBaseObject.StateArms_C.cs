namespace GameSvr
{
    // Batch-C tail of the two native timed-state dispatch tables: the four
    // speaking arms at the top of the domain (states 102, 103, 104, 106).
    //
    // These live in their own partial-class file, wired through the two
    // `...BatchC` hooks declared in TBaseObject.TimedAbility.cs, so the shared
    // dispatch switches are never edited here. Every state this file does not
    // name falls through both hooks and is therefore left exactly as native
    // leaves it — the jump-table default arm 0x742C42, which is the shared
    // epilogue (pop/ret, no SendMsg): a SILENT SUCCESS, not a refusal. The
    // state is still added/removed normally; only the on-screen line is absent.
    //
    // Evidence base: D:/loym2/staging/_reunpack_work/flat_image.bin, ImageBase
    // 0x400000 (file offset = VA - 0x400000, checked at 0x7418C8 = 33 C0 8A C3
    // 83 F8 6A). Every arm below was re-derived from the jump tables with
    // capstone, not copied: gained index map @0x7418E2 -> arm table @0x74194D,
    // lost arm table @0x7426A9 (index = state - 14). All strings are GBK, read
    // from the Delphi AnsiString literal (length dword at ptr-4).
    public partial class TBaseObject
    {
        // Gained side, reached from 0x7418C8 with boGained != 0. All four arms
        // carry cx 0xFFDB (colour 0xDB, type 0xFF) and render the seconds via
        // 0x40C89C (IntToStr) + 0x405890 (@LStrCatN), exactly like the 90..101
        // band already landed in the main switch.
        private void DispatchNativeStateGainedTextBatchC(byte internalType,
            int remainingMilliseconds)
        {
            switch (internalType)
            {
                // 102..104 are the only gained arms in the image that pad the
                // number with a space on both sides: their prefix ends 0x20 and
                // they join with 0x743230 " 秒" (20 C3 EB, len 3) instead of the
                // 0x742C94 "秒" (C3 EB, len 2) every other arm uses.
                case 102:
                    // arm[50] @0x7425BD (gained map[102] = 0x32)
                    //   7425BD  68 10 32 74 00        push 0x743210   ; prefix len 21
                    //   7425D6  68 30 32 74 00        push 0x743230   ; suffix " 秒" len 3
                    //   7425F1  66 B9 DB FF           mov cx, 0xFFDB
                    //   7425F9  FF 93 D4 00 00 00     call [ebx+0xD4]
                    //   7425FF  E9 3E 06 00 00        jmp 0x742C42
                    // 0x743210 = C4 E3 B4 A6 D3 DA D6 C2 CB C0 D7 B4 CC AC A3 AC
                    //            B3 D6 D0 F8 20
                    SendNativeStateSysMsg(0xFFDB,
                        $"你处于致死状态，持续 {NativeStateSeconds(remainingMilliseconds)} 秒");
                    break;
                case 103:
                    // arm[51] @0x742604 (gained map[103] = 0x33)
                    //   742604  68 3C 32 74 00        push 0x74323C   ; prefix len 23
                    //   74261D  68 30 32 74 00        push 0x743230   ; suffix " 秒"
                    //   742638  66 B9 DB FF           mov cx, 0xFFDB
                    //   742640  FF 93 D4 00 00 00     call [ebx+0xD4]
                    //   742646  E9 F7 05 00 00        jmp 0x742C42
                    // 0x74323C = B9 A5 BB F7 C4 A7 B7 A8 B5 C0 CA F5 CC E1 B8 DF
                    //            A3 AC B3 D6 D0 F8 20
                    SendNativeStateSysMsg(0xFFDB,
                        $"攻击魔法道术提高，持续 {NativeStateSeconds(remainingMilliseconds)} 秒");
                    break;
                case 104:
                    // arm[52] @0x74264B (gained map[104] = 0x34)
                    //   74264B  68 5C 32 74 00        push 0x74325C   ; prefix len 19
                    //   742664  68 30 32 74 00        push 0x743230   ; suffix " 秒"
                    //   74267F  66 B9 DB FF           mov cx, 0xFFDB
                    //   742687  FF 93 D4 00 00 00     call [ebx+0xD4]
                    //   74268D  E9 B0 05 00 00        jmp 0x742C42
                    // 0x74325C = B7 C0 D3 F9 C4 A7 B7 C0 CC E1 B8 DF A3 AC B3 D6
                    //            D0 F8 20
                    SendNativeStateSysMsg(0xFFDB,
                        $"防御魔防提高，持续 {NativeStateSeconds(remainingMilliseconds)} 秒");
                    break;

                // 105 is silent: gained map[105] = 0 -> arm[0] = 0x742C42. It is
                // deliberately absent (falls through this switch = no message).
                case 106:
                    // arm[13] @0x741CE5 (gained map[106] = 0x0D). 106 is the table
                    // ceiling (0x7418CC cmp eax,0x6A) and its arm sits down with
                    // the low states because the index map hands it slot 13. It
                    // uses the plain "秒" suffix, no space padding.
                    //   741CE5  68 C0 2D 74 00        push 0x742DC0   ; prefix len 16
                    //   741CF8  68 94 2C 74 00        push 0x742C94   ; suffix "秒" len 2
                    //   741D0D  66 B9 DB FF           mov cx, 0xFFDB
                    //   741D15  FF 93 D4 00 00 00     call [ebx+0xD4]
                    //   741D1B  E9 22 0F 00 00        jmp 0x742C42
                    // 0x742DC0 = C4 A7 B7 A8 C3 FC D6 D0 CB B2 BC E4 CC E1 B8 DF
                    SendNativeStateSysMsg(0xFFDB,
                        $"魔法命中瞬间提高{NativeStateSeconds(remainingMilliseconds)}秒");
                    break;
            }
        }

        // Lost side, reached from 0x742692 with boGained == 0 (state biased by
        // -14 and used to index 0x7426A9 directly). All four arms are the plain
        // `mov cx,0xFFDB / mov edx,<str> / call [vmt+0xD4]` shape with no number.
        private void DispatchNativeStateLostTextBatchC(byte internalType)
        {
            switch (internalType)
            {
                case 102:
                    // arm @0x742C05 (0x7426A9 + (102-14)*4)
                    //   742C05  66 B9 DB FF           mov cx, 0xFFDB
                    //   742C09  BA A4 36 74 00        mov edx, 0x7436A4
                    //   742C12  FF 93 D4 00 00 00     call [ebx+0xD4]
                    //   742C18  EB 28                 jmp 0x742C42
                    // 0x7436A4 = D6 C2 CB C0 D7 B4 CC AC BD E1 CA F8 A3 A1
                    SendNativeStateSysMsg(0xFFDB, "致死状态结束！");
                    break;
                case 103:
                    // arm @0x742C1A. 恢复正常 here (BB D6 = 恢), against the 回复
                    // (BB D8) every other lost arm uses — both spellings are in
                    // the image; this is native, not a transcription slip.
                    //   742C1A  66 B9 DB FF           mov cx, 0xFFDB
                    //   742C1E  BA BC 36 74 00        mov edx, 0x7436BC
                    //   742C27  FF 93 D4 00 00 00     call [ebx+0xD4]
                    //   742C2D  EB 13                 jmp 0x742C42
                    // 0x7436BC = B9 A5 BB F7 C4 A7 B7 A8 B5 C0 CA F5 CC E1 B8 DF
                    //            BB D6 B8 B4 D5 FD B3 A3
                    SendNativeStateSysMsg(0xFFDB, "攻击魔法道术提高恢复正常");
                    break;
                case 104:
                    // arm @0x742C2F. Also 恢复正常 (BB D6). This is the last arm
                    // before the shared epilogue, so it is the one arm in the
                    // whole table that FALLS THROUGH into 0x742C42 instead of
                    // jumping there:
                    //   742C2F  66 B9 DB FF           mov cx, 0xFFDB
                    //   742C33  BA E0 36 74 00        mov edx, 0x7436E0
                    //   742C3C  FF 93 D4 00 00 00     call [ebx+0xD4]
                    //   742C42                        (epilogue begins here)
                    // 0x7436E0 = B7 C0 D3 F9 C4 A7 B7 C0 CC E1 B8 DF BB D6 B8 B4
                    //            D5 FD B3 A3
                    SendNativeStateSysMsg(0xFFDB, "防御魔防提高恢复正常");
                    break;

                // 105 is silent: lost slot 0x7426A9 + (105-14)*4 = 0x742C42.
                case 106:
                    // arm @0x74293D (0x7426A9 + (106-14)*4). Uses 回复 (BB D8),
                    // unlike 103/104 above. Like its gained side, 106's lost arm
                    // sits with the low states rather than at the table end.
                    //   74293D  66 B9 DB FF           mov cx, 0xFFDB
                    //   742941  BA A0 33 74 00        mov edx, 0x7433A0
                    //   74294A  FF 93 D4 00 00 00     call [ebx+0xD4]
                    //   742950  E9 ED 02 00 00        jmp 0x742C42
                    // 0x7433A0 = C4 A7 B7 A8 C3 FC D6 D0 BB D8 B8 B4 D5 FD B3 A3
                    SendNativeStateSysMsg(0xFFDB, "魔法命中回复正常");
                    break;
            }
        }
    }
}

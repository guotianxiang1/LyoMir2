using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// The player-facing half of the native "a timed state changed" virtual,
    /// <c>THumanKind VMT+0x14 = sub_741884</c>.
    ///
    /// <para>
    /// Shape of the native function (bytes at 0x741884..0x7418E1):
    /// <code>
    ///   741884  55 8B EC                  push ebp / mov ebp,esp
    ///   74189B  8B F9                     mov edi, ecx     ; seconds
    ///   74189D  8B DA                     mov ebx, edx     ; state id
    ///   74189F  8B F0                     mov esi, eax     ; Self
    ///   7418AF  8A 45 08                  mov al, [ebp+8]  ; gained flag
    ///   7418B9  E8 6E 9B 02 00            call 0x76B42C    ; inherited base
    ///   7418BE  80 7D 08 00               cmp byte [ebp+8], 0
    ///   7418C2  0F 84 CA 0D 00 00         je  0x742692     ; flag==0 -> LOST table
    ///   7418C8  33 C0 / 8A C3             xor eax,eax / mov al,bl
    ///   7418CC  83 F8 6A                  cmp eax, 0x6A    ; state &lt;= 106
    ///   7418CF  0F 87 6D 13 00 00         ja  0x742C42     ; silent default
    ///   7418D5  8A 80 E2 18 74 00         mov al, [eax+0x7418E2]   ; byte index
    ///   7418DB  FF 24 85 4D 19 74 00      jmp [eax*4+0x74194D]     ; GAINED table
    /// </code>
    /// The inherited base <c>0x76B42C</c> is three instructions —
    /// <c>call 0x7729C4 / pop ebp / ret 4</c> — i.e. only the
    /// SM_CHARSTATUSCHANGED broadcast. That broadcast is what
    /// <see cref="TBaseObject.SendTimedAbilityState"/> already emits as its
    /// first statement, which is why this dispatch hangs off that method.
    /// </para>
    ///
    /// <para>
    /// <b>Scope.</b> Only <c>THumanKind</c> and its descendants own
    /// <c>sub_741884</c>. Verified by walking every Delphi VMT in the image
    /// (self-pointer at VMT-0x4C, class name at VMT-0x2C): slot +0x14 holds
    /// 0x741884 for <c>THumanKind</c> / <c>THeroAct</c> / <c>TWarHero</c> /
    /// <c>TTaosHero</c> / <c>TMagHero</c> / <c>TSecWarHero</c> /
    /// <c>TSecTaosHero</c> / <c>TSecMagHero</c>, while <c>TCreature</c> and
    /// all 130-odd monster VMTs hold <c>0x76B42C</c>, the broadcast-only base.
    /// Monsters therefore never emit any of this text even though they do
    /// carry timed states (poison, freeze, ...), so every arm below is gated
    /// on the object being a player or a hero.
    /// </para>
    ///
    /// <para>
    /// <b>Two tables, one function.</b> <c>[ebp+8]</c> is the gained/lost
    /// flag and picks the table:
    /// <list type="bullet">
    /// <item>GAINED — pushed as <c>1</c> at 0x77318C, at the tail of the
    /// native add routine, with <c>ecx</c> set at 0x773191..0x77319C to
    /// <c>node.RemainingMilliseconds / 1000</c> (a signed <c>idiv</c>).
    /// The very next instructions are <c>call 0x408340</c> (GetTickCount)
    /// and <c>mov [node+6], eax</c>, which is exactly the
    /// <c>node.LastTick = HUtil32.GetTickCount()</c> that closes
    /// <c>AddTimedAbilityInternal</c> — so the C# hook point is
    /// <c>SendTimedAbilityState(node, removed: false)</c>.</item>
    /// <item>LOST — pushed as <c>0</c> at 0x773386 inside <c>sub_77337C</c>
    /// with <c>xor ecx,ecx</c> at 0x773388, so the seconds argument is always
    /// 0 on the lost side. Those arms live in
    /// <see cref="TBaseObject.OnNativeTimedStateLost"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Colour words.</b> Every arm ends in
    /// <c>mov cx,&lt;word&gt; / mov edx,&lt;string&gt; / call [vmt+0xD4]</c>.
    /// On <c>TPlayer</c> (VMT 0x6AC8C8) slot +0xD4 is <c>sub_73C8F4</c>, a bare
    /// enqueuer that forwards the whole of <c>cx</c> as the packet's
    /// <c>wParam</c>; this tree's RM_SYSMESSAGE arm splits that word as
    /// <c>FColor = cx &amp; 0xFF</c> / <c>BColor = cx &gt;&gt; 8</c>. Only two
    /// words appear in this band:
    /// <c>0xFFDB</c> = (0xDB, 0xFF) = the Green pair, and
    /// <c>0x38FF</c> = (0xFF, 0x38) = the Red pair.
    /// The literal <c>SendMsg</c> form is used rather than
    /// <see cref="TBaseObject.SysMsg"/> because <c>SysMsg</c> re-derives the
    /// colours from config and prepends <c>sHintMsgPreFix</c>, neither of which
    /// <c>sub_73C8F4</c> does. This matches the state-75 arm that already lives
    /// in <see cref="TBaseObject.SendTimedAbilityState"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Interpolated arms.</b> Where the text carries a number the arm is
    /// <c>push &lt;prefix&gt; / movzx eax,di / call 0x40C89C (IntToStr) /
    /// push &lt;number&gt; / push 0x742C94 / mov edx,3 / call 0x405890
    /// (_LStrCatN)</c>. 0x742C94 is declen-2 <c>C3 EB</c> = "秒", and the parts
    /// are pushed in source order, so the shape is
    /// <c>prefix + seconds + "秒"</c>. <c>movzx eax,di</c> takes the low word
    /// of the seconds argument, hence the <c>(ushort)</c> cast — a permanent
    /// state (RemainingMilliseconds == -1) yields -1/1000 == 0 and prints
    /// "0秒", which is what native does.
    /// </para>
    /// </summary>
    public partial class TBaseObject
    {
        // sub_73C8F4 forwards cx verbatim; these are the two words this band uses.
        private const byte NativeStateHintGreenFColor = 0xDB;   // cx = 0xFFDB
        private const byte NativeStateHintGreenBColor = 0xFF;
        private const byte NativeStateHintRedFColor = 0xFF;     // cx = 0x38FF
        private const byte NativeStateHintRedBColor = 0x38;

        /// <summary>
        /// One <c>mov cx,&lt;word&gt; / call [vmt+0xD4]</c> site. Gated on the
        /// THumanKind subtree because <c>TCreature</c>'s VMT+0x14 is the
        /// broadcast-only <c>sub_76B42C</c> and never reaches a dispatch table.
        /// </summary>
        private void SendNativeStateDispatchHint(string text,
            byte fColor, byte bColor)
        {
            if (!(this is TPlayObject) && !(this is HeroObject))
            {
                return;
            }

            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, fColor, bColor, 0, text);
        }

        private void SendNativeStateDispatchHintGreen(string text)
        {
            SendNativeStateDispatchHint(text,
                NativeStateHintGreenFColor, NativeStateHintGreenBColor);
        }

        private void SendNativeStateDispatchHintRed(string text)
        {
            SendNativeStateDispatchHint(text,
                NativeStateHintRedFColor, NativeStateHintRedBColor);
        }

        /// <summary>
        /// GAINED table: byte index at 0x7418E2[state] (107 entries, state
        /// 0..106) selecting one of 53 dword slots at 0x74194D. Index 0 is the
        /// silent default 0x742C42, so every state whose byte is 0 converges on
        /// DEFAULT and emits nothing beyond the base broadcast.
        ///
        /// <para>
        /// Only the middle band (states 43..84) is wired here. States below 43
        /// and above 84 are still MISSING and are catalogued in
        /// docs/m_state_dispatch_band_b_20260813.md.
        /// </para>
        /// </summary>
        /// <param name="seconds">
        /// <c>ecx</c> at the 0x7731A8 call site: node.RemainingMilliseconds
        /// divided by 1000 with a signed idiv.
        /// </param>
        private void OnNativeTimedStateGained(byte internalType, int seconds)
        {
            var n = unchecked((ushort)seconds);

            switch (internalType)
            {
                case 44:
                    // byte tbl[0x2C] = 0x0A -> slot 10 -> 0x741C34.
                    //   741C34  68 78 2D 74 00        push 0x742D78
                    //   741C3C  0F B7 C7              movzx eax, di
                    //   741C3F  E8 58 AC CC FF        call 0x40C89C   ; IntToStr
                    //   741C47  68 94 2C 74 00        push 0x742C94   ; "秒"
                    //   741C4F  BA 03 00 00 00        mov edx, 3
                    //   741C54  E8 37 3C CC FF        call 0x405890   ; _LStrCatN
                    //   741C5C  66 B9 DB FF           mov cx, 0xFFDB
                    //   741C64  FF 93 D4 00 00 00     call [ebx+0xD4]
                    // 0x742D78 declen 12: C1 A6 C1 BF CB B2 BC E4 CC E1 B8 DF.
                    // NOTE the LOST side of state 44 is byte tbl A[44-14] =
                    // 0x742C42 = DEFAULT, i.e. native says nothing when it ends.
                    SendNativeStateDispatchHintGreen($"力量瞬间提高{n}秒");
                    break;

                case 45:
                    // byte tbl[0x2D] = 0x15 -> slot 21 -> 0x741E38.
                    //   741E38  66 B9 FF 38           mov cx, 0x38FF
                    //   741E3C  BA 88 2E 74 00        mov edx, 0x742E88
                    //   741E45  FF 93 D4 00 00 00     call [ebx+0xD4]
                    // 0x742E88 declen 12: C4 E3 B1 BB B6 A8 C9 ED C1 CB A3 A1.
                    SendNativeStateDispatchHintRed("你被定身了！");
                    break;

                case 49:
                    // byte tbl[0x31] = 0x14 -> slot 20 -> 0x741DF6.
                    //   741DF6  C6 86 E1 02 00 00 01  mov byte [esi+0x2E1], 1
                    //   741DFD  68 70 2E 74 00        push 0x742E70
                    //   741E05  0F B7 C7              movzx eax, di
                    //   741E08  E8 8F AA CC FF        call 0x40C89C
                    //   741E10  68 94 2C 74 00        push 0x742C94   ; "秒"
                    //   741E18  BA 03 00 00 00        mov edx, 3
                    //   741E25  66 B9 DB FF           mov cx, 0xFFDB
                    //   741E2D  FF 93 D4 00 00 00     call [ebx+0xD4]
                    // 0x742E70 declen 12: BD F8 C8 EB CE DE B5 D0 D7 B4 CC AC.
                    //
                    // FAIL-CLOSED: the `[Self+0x2E1] = 1` write is NOT mirrored.
                    // That byte is not a state-49 flag — an image-wide scan for
                    // the disp32 finds ~16 unrelated writers (0x5FABAD, 0x609056,
                    // 0x62FB17, 0x63D8B7, 0x66891A, 0x66B2AD, 0x681109, 0x681C56,
                    // 0x682007, 0x682713, 0x6829FC, 0x6B21F3 set it, 0x624A0F /
                    // 0x68278A / 0x682E02 clear it, 0x624968 xor-toggles it) and
                    // two readers, 0x624972 (the GM-mode reply next to the
                    // +0x2E0 toggle) and 0x7665B5 (a per-tick gate in front of
                    // the +0x264 ability record). Which of those this arm is
                    // actually driving is unresolved, so no C# field is invented
                    // here; the message is reproduced and the byte write is left
                    // as a documented gap.
                    SendNativeStateDispatchHintGreen($"进入无敌状态{n}秒");
                    break;

                case 53:
                    // byte tbl[0x35] = 0x13 -> slot 19 -> 0x741DDE. Slot 19 is
                    // SHARED: byte tbl[0x1E] and byte tbl[0x1F] are also 0x13, so
                    // states 30, 31 and 53 all land on the same arm. 30 and 31
                    // belong to band A and are not wired here.
                    //   741DDE  66 B9 FF 38           mov cx, 0x38FF
                    //   741DE2  BA 5C 2E 74 00        mov edx, 0x742E5C
                    //   741DEB  FF 93 D4 00 00 00     call [ebx+0xD4]
                    // 0x742E5C declen 10: C4 E3 D6 D0 B6 BE C1 CB A3 A1.
                    SendNativeStateDispatchHintRed("你中毒了！");
                    break;
            }
        }
    }
}

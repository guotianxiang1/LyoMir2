namespace GameSvr
{
    // ================================================================================
    // MINE-47 — anti-fatigue tier byte feed (300/180-minute threshold bucketer).
    //
    // The mining tier system already READS the fatigue byte at native Self+0x1828
    // (m_btNativeFatigueTier):
    //   * MINE-46 hard-blocks the dig when the byte == 3 (PileStones @0x6BC202).
    //   * MINE-21 halves the ore roll when the byte == 2 (PileStones @0x6BC2A3,
    //     Random(24) instead of Random(12)).
    // It also drives the death-drop divisor (0x71FADA / UsrEngn / TBaseObject).
    // But nothing WROTE the byte, so the tier stayed 0 (dormant) forever — the
    // missing "feed" half of the subsystem.
    //
    // The sole native writer is the bucketer sub_6D260C, reproduced 1:1 below:
    //
    //   0x6D260C  55 / 8B EC                push ebp / mov ebp,esp
    //   0x6D260F  81 FA 2C 01 00 00         cmp  edx,0x12C          ; edx = online minutes
    //   0x6D2615  7C 09                     jl   0x6D2620           ; < 300 -> check 180
    //   0x6D2617  C6 80 28 18 00 00 03      mov  byte [eax+0x1828],3 ; >= 300 -> tier 3
    //   0x6D261E  EB 18                     jmp  0x6D2638
    //   0x6D2620  81 FA B4 00 00 00         cmp  edx,0xB4
    //   0x6D2626  7C 09                     jl   0x6D2631           ; < 180 -> tier 1
    //   0x6D2628  C6 80 28 18 00 00 02      mov  byte [eax+0x1828],2 ; 180..299 -> tier 2
    //   0x6D262F  EB 07                     jmp  0x6D2638
    //   0x6D2631  C6 80 28 18 00 00 01      mov  byte [eax+0x1828],1 ; < 180 -> tier 1
    //   0x6D2638  52 51 6A00 6A00 6A00 6A00 push edx/ecx/0/0/0/0    ; enqueue self-msg
    //   0x6D2642  8B D0 / 66 B9 AD 27       mov  edx,eax / mov cx,0x27AD (=10157)
    //   0x6D2648  E8 1B 38 09 00            call 0x765E68           ; self-message queue
    //   0x6D264D  5D / C3                   pop ebp / ret
    //
    // Two things to note for fidelity:
    //   (a) The comparisons are signed (`jl`); online minutes are non-negative so
    //       signed/unsigned coincide. The floor is tier 1 — even 0 minutes online
    //       yields tier 1, the byte is never set back to 0 through this path (a
    //       zeroed Delphi field is the only source of tier 0).
    //   (b) The trailing self-message (ident 0x27AD/10157) is NOT the tier feed; it
    //       is the anti-fatigue on-screen notify. sub_765E68 only ENQUEUES; the next
    //       Run tick dispatches 0x27AD at 0x6B5748, which forwards a client packet
    //       SM ident 0x3BF (959) carrying word[msg+2]/word[msg+4]. That notify (and
    //       its exact SendDefMessage arg order via [vtbl+0x250]) belongs to the
    //       anti-fatigue message subsystem, not mining, and is documented here
    //       rather than reproduced so the tier byte — the piece the mining gates
    //       consume — stays evidence-exact. See NativeCheatSelfReport.cs for the
    //       0x1829 sibling feed (CM 205), which is wired.
    //
    // The single native CALLER is the anti-fatigue command handler sub_653C4C, an
    // arm of the cross-server command dispatcher (selector 0x131..0x13D, arm at
    // 0x654457): it copies the character name from msg+0x25, looks the player up
    // (sub_49F5F4 GetPlayObject), and on a hit calls the bucketer with
    //   edx = word[msg+2]  (online minutes)
    //   ecx = dword[msg+4] (notify passthrough)
    // That dispatcher is inter-server protocol (out of the mining domain and absent
    // from the C# port), so this method is left ready to be driven by that feed
    // rather than wired to an invented packet path.
    // ================================================================================
    public partial class TPlayObject
    {
        /// <summary>
        /// native sub_6D260C — buckets accumulated online time into the anti-fatigue
        /// tier byte (Self+0x1828 / <see cref="m_btNativeFatigueTier"/>):
        /// &gt;= 300 min -&gt; 3 (hard-block), 180..299 min -&gt; 2 (half-rate),
        /// &lt; 180 min -&gt; 1. Never sets 0. Fed by the anti-fatigue command
        /// handler (sub_653C4C) with the online-minute count from the packet.
        /// </summary>
        internal void ApplyNativeAntiFatigueTier(int nOnlineMinutes)
        {
            if (nOnlineMinutes >= 300)
            {
                m_btNativeFatigueTier = 3;
            }
            else if (nOnlineMinutes >= 180)
            {
                m_btNativeFatigueTier = 2;
            }
            else
            {
                m_btNativeFatigueTier = 1;
            }
        }
    }
}

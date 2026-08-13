using SystemModule;

namespace GameSvr
{
    // ================================================================================
    // CM 205 (0x00CD) — the client's own "third-party software detected" self-report.
    //
    // Native handler 0x006D8CA2, reached from the dispatch arm at 0x6D80DB
    // (`cmp eax,0xCD / je 0x6D8CA2`), full body:
    //
    //   0x6D8CA5  66 81 78 0A FF 00        cmp  word [msg+0x0A],0xFF   ; Series
    //   0x6D8CAB  0F 85 7B 2F 00 00        jne  0x6DBC2C               ; -> silent drop
    //   0x6D8CB4  66 83 78 06 00           cmp  word [msg+6],0         ; Param
    //   0x6D8CB9  0F 86 6D 2F 00 00        jbe  0x6DBC2C               ; unsigned <= 0 -> drop
    //   0x6D8CC2  C6 80 29 18 00 00 01     mov  byte [self+0x1829],1   ; tier := 1
    //   0x6D8CCC  66 83 B8 78 02 00 00 23  cmp  word [self+0x278],0x23 ; Level
    //   0x6D8CD4  72 5B                    jb   0x6D8D31               ; < 35 -> straight to the log
    //   0x6D8CD9  A0 8C 3A 7D 00           mov  al,byte [0x7D3A8C]     ; server penalty policy
    //   0x6D8CDE  88 82 29 18 00 00        mov  byte [self+0x1829],al  ; tier := policy
    //   0x6D8CE4  2C 02 / 74 1A            sub  al,2 / je 0x6D8D02     ; policy == 2
    //   0x6D8CE8  FE C8 / 75 45            dec  al  / jne 0x6D8D31     ; policy != 3 -> log only
    //   0x6D8CEC  66 B9 FF 38 / BA 54 BE 6D 00 / call [vmt+0xD4]       ; policy 3 hint
    //   0x6D8D02  66 B9 FF 38 / BA C4 BE 6D 00 / call [vmt+0xD4]       ; policy 2 hint
    //   0x6D8D16  6A 00 x6 / 66 B9 10 27 / E8 .. call 0x765E68         ; policy 2: self-msg 10000
    //   0x6D8D31..0x6D8D70                                             ; game-data log, then default
    //
    // `[self+0x278]` is m_Abil.Level (word) — the same field the hero exp-cap gate
    // reads at 0x687802 and the GM level commands write (player+0x278 / mirror +0x1FC).
    // `[self+0x1829]` is the cheat-penalty tier already modelled as
    // m_btNativeCheatPenaltyTier.
    //
    // Self-message 10000 (0x2710) resolves through the RM dispatcher to 0x6B44A5:
    //   0x6B44A8  C6 80 BB 04 00 00 01     mov byte [self+0x4BB],1
    // and `[self+0x4BB]` is the flag native CM_SOFTCLOSE (0x6D8ED0) also raises
    // (`0x6D8EDD mov byte [eax+0x4BB],1`, alongside `0x6D8ED3 [eax+0x710]`), i.e.
    // m_boSoftClose. Because 0x765E68 only ENQUEUES, native raises the flag on the
    // following Run tick; nothing between the enqueue and that tick reads it (the
    // only remaining statement in the handler is the log), so setting it inline is
    // observationally identical.
    // ================================================================================
    public partial class TPlayObject
    {
        /// <summary>
        /// The server-wide cheat penalty policy byte at native <c>0x7D3A8C</c>.
        /// A whole-image dword scan finds exactly three occurrences of the address:
        /// <c>0x6CD426</c> and <c>0x6D8CDA</c> (both <c>A0 8C 3A 7D 00 mov al,[0x7D3A8C]</c>,
        /// i.e. reads) and <c>0x7D6010</c>, which is a slot in the unit
        /// initialisation pointer list, not a config-key table. Nothing in the image
        /// ever writes it, so a Delphi zero-initialised global keeps the value 0 for
        /// the whole process lifetime. Modelled as a field rather than a const so the
        /// two policy arms below stay reachable code and keep documenting the contract.
        /// </summary>
        internal static byte NativeCheatReportPolicyTier;

        /// <summary>
        /// Header gate value native requires in Series (<c>0x6D8CA5</c>).
        /// </summary>
        private const int NativeCheatReportSeries = 0xFF;

        /// <summary>
        /// Level floor above which the policy tier replaces the flat tier 1
        /// (<c>0x6D8CCC cmp word [self+0x278],0x23</c>, unsigned <c>jb</c>).
        /// </summary>
        private const int NativeCheatReportLevelFloor = 0x23;

        private void ClientNativeCheatSelfReport(int series, int param)
        {
            if (series != NativeCheatReportSeries)
                return;
            if (param <= 0)
                return;

            m_btNativeCheatPenaltyTier = 1;
            if (m_Abil.Level < NativeCheatReportLevelFloor)
                return;

            m_btNativeCheatPenaltyTier = NativeCheatReportPolicyTier;
            if (NativeCheatReportPolicyTier == 2)
            {
                // 0x6D8D06 mov edx,0x6DBEC4 (declen 84), 0x6D8CEC-style cx=0x38FF = Red/Hint.
                SysMsg("由于检测到第三方软件，影响到游戏的正常运行，连接中断，"
                    + "请立即关闭第三方软件后重新登陆", MsgColor.Red, MsgType.Hint);
                m_boSoftClose = true;
            }
            else if (NativeCheatReportPolicyTier == 3)
            {
                // 0x6D8CF0 mov edx,0x6DBE54 (declen 100), cx=0x38FF = Red/Hint.
                SysMsg("由于检测到第三方软件，影响到游戏的正常运行，你已进入强制和平攻击模式。"
                    + "请立即关闭第三方软件后重新登陆", MsgColor.Red, MsgType.Hint);
            }

            // 0x6D8D31..0x6D8D6B also emits a game-data record through sub_768BE0
            // (dx=0x1D, reason literal 0x6DBF24 "使用外挂", plus ShortString [self+0xB33],
            // m_Abil.Level and Param). sub_768BE0 is a LOGGER, not a SysMsg — it loads the
            // singleton at [[0x7D5ECC]] and calls 0x79D3D8 with map name, CurrX/CurrY and
            // the character name, and never touches the actor vtable. The record is not
            // reproduced here because the 0x79D3D8 column order has not been reversed;
            // it is not player-observable and carries no protocol surface.
        }
    }
}

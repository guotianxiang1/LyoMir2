namespace GameSvr
{
    public partial class TPlayObject
    {
        // 战神 login-state cluster sub_6E9A98 (virtual, VMT+0x204; present on both the
        // player VMT @0x62EF8C and the hero VMT @0x6AC8C8). It is not called directly:
        // UserLogon (sub_6B1D64) queues RM 0x3010 at 0x6B2358, and the Operate loop's
        // secondary dispatcher sub_743AD8 (0x6B6247 call 0x743AD8) turns case 0x3010
        // into `0x743BF7 call [edx+0x204]`. The body fans out four legs, in this order:
        //
        //   0x6E9AA0 call 0x7468B4  -> SM 3324 : Recog=[self+0x60C], Param=word[self+0x610],
        //                             Tag=0, Series=(m_btRaceServer==54 hero ? 1 : 0).
        //                             [+0x60C]/[+0x610] not yet mapped to a C# field
        //                             (broadcast on change at 0x6EB495/0x747E79/0x6F00FE,
        //                             a 0..3 valued mode) -> leg still MISSING, do not fake.
        //   0x6E9AA7 call 0x6F0A50  -> SM 1264 : Recog=0, Tag=Series=0, no body,
        //                             Param = ([0x7D7038]+3 & 0x80) ? 1 : 0. That config
        //                             SET bit (byte 3 bit 7) is read ONLY here, so its
        //                             INI key is unidentified -> leg still MISSING; sending
        //                             it with a guessed Param would be fabrication.
        //   0x6E9AAE call 0x6E99B8  -> SM 3554 : timed-ability snapshot. FULLY resolved,
        //                             emitted below.
        //   0x6E9ABB call 0x74839C  -> SM 3556/4367 : the {edx,ecx} pair list. 0x7483EE
        //                             jle 0x74849B means an empty list sends nothing, and
        //                             production shows 3556=0, so on a normal login this
        //                             leg is silent. Not emitted (no C# source list).
        //
        // This round implements only the byte-verified 3554 leg. The other three remain
        // MISSING pending the field/config evidence noted above (fail-closed: no invented
        // payloads).
        private void SendNativeLogonStateSync()
        {
            var snapshot = BuildNativeTimedAbilitySnapshot();
            SendSocket(snapshot.Header, snapshot.Body);
        }
    }
}

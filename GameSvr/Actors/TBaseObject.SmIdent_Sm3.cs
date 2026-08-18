using System;
using System.IO;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// SM down-wire builders for the missing idents of batch 3 — the ascending
    /// #36..#70 slice of the 140 class-(c) idents in staging/_sm1_work/classes.txt
    /// (native wire-SM idents that fire through a real send slot in flat_image.bin
    /// yet had no C# constant/builder). Mirrors the reference pattern in
    /// <see cref="TBaseObject"/>.<c>BuildTimedAbilityClientState</c> (SM 3554/3555)
    /// and the batches SmIdent_Sm1.cs / SmIdent_Sm2.cs: each builder returns the
    /// header five-tuple plus the exact body and never touches a shared method body
    /// (anti-conflict rule for the parallel worktrees).
    ///
    /// Evidence base: flat_image.bin, ImageBase 0x400000 (offset = VA - 0x400000).
    /// Every frame below was reversed byte-for-byte with capstone from its own send
    /// site (see _work/smdis.py). The send-slot signatures (verified against SM
    /// 35/689/917/966/1201/1263 exemplars in the earlier batches):
    ///   [obj+0x250] SendDefMessage(Self=eax, wIdent=dx, nRecog=ecx, Param, Tag, Series, sMsg)
    ///   [obj+0x254] same, last two stack args become (Buf, Len)
    /// Left-to-right push order maps push#1=Param, #2=Tag, #3=Series, #4=sMsg|Buf,
    /// #5=Len, so Grobal2.MakeDefaultMsg(ident, Recog, Param, Tag, Series) lines up
    /// position-for-position with the native slot. The RM-record layout used by the
    /// dispatch arms is [rec+0x02]=wParam(word) [rec+0x04]=nParam1 [rec+0x08]=nParam2
    /// [rec+0x0C]=nParam3 [rec+0x10]=Buf [rec+0x14]=wLen(word) [rec+0x24]=BaseObject.
    ///
    /// FAIL-CLOSED (registered in Grobal2.cs, NO builder — body is a local,
    /// runtime-composed variable-length record buffer whose bytes are not resolvable
    /// at the send slot without inventing the producer):
    ///   - SM_1729 (0x6C1) @0x613925 [obj+0x254]: Buf=&local[ebp-0xFC], Len=0xE0
    ///     (eight 28-byte records built by the loop @0x6138A5 -> 0x613788).
    ///   - SM_2850 (0xB22) @0x6D30B7 [obj+0x254]: Buf=&local[ebp-4], Len=Count*20
    ///     (Delphi dyn-array filled by 0x5F4D4C from global manager [0x7D6528]).
    ///   - SM_2956 (0xB8C) @0x6E6AED [obj+0x254]: Buf=&local[ebp-0x488],
    ///     Len=Count*24 (record array filled by the loop @0x6E6A65).
    /// </summary>
    public partial class TBaseObject
    {
        // ---- empty-body [obj+0x250] sends (only the header 5-tuple varies) ----

        // SM 1264 (0x4F0) — sub_6F0A50 always sends. ServerSwitch.Bin bit 31
        // selects Param=1 @0x6F0A61 or Param=0 @0x6F0A7C; both arms keep
        // Recog/Tag/Series at zero and call the string slot [vmt+0x250].
        internal static (ClientPacket Header, byte[] Body) BuildSm1264(
            bool enabled)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1264, 0,
                enabled ? 1 : 0, 0, 0), Array.Empty<byte>());

        // SM 1265 (0x4F1) — send @0x6F1794. Recog = ecx (3rd register arg, untouched
        // to the slot); Param = word[ebp+0xC], Tag = word[ebp+8] (4th/5th stack args).
        //   006F177E 66 8B 45 0C push word[ebp+0xC] (Param) ; 006F1783 66 8B 45 08 push word[ebp+8] (Tag)
        //   006F1788 6A00x2 push (Series/sMsg=nil) ; 006F178C 66 BA F1 04 mov dx,0x4F1
        //   006F1794 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1265(int recog, ushort param, ushort tag)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1265, recog, param, tag, 0), Array.Empty<byte>());

        // SM 1726 (0x6BE) — send @0x6E3273. Recog=edi, a runtime value: default -1
        // (006E321E or edi,-1) or a byte read from an array (006E325D movzx edi,[eax+ebx]).
        //   006E3261 6A00x4 push (Param/Tag/Series/sMsg=nil) ; 006E3269 8B CF mov ecx,edi
        //   006E326B 66 BA BE 06 mov dx,0x6BE ; 006E3273 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1726(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1726, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 1727 (0x6BF) — send @0x6E343A. Recog=1 (006E3423 mov edi,1 -> ecx).
        //   006E3428 6A00x4 push ; 006E3430 8B CF mov ecx,edi(=1) ; 006E3432 66 BA BF 06 mov dx,0x6BF
        //   006E343A FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1727()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1727, 1, 0, 0, 0), Array.Empty<byte>());

        // SM 1730 (0x6C2) — send @0x6E39BC. Recog=edx = return of 0x612F6C (sent when <=0).
        //   006E39AA 6A00x4 push ; 006E39B2 8B CA mov ecx,edx ; 006E39B6 66 BA C2 06 mov dx,0x6C2
        //   006E39BC FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1730(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1730, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 1731 (0x6C3) — send @0x6E3A0D via [edi+0x250]. Recog=esi = return of 0x6131A0.
        //   006E39FB 6A00x4 push ; 006E3A03 8B CE mov ecx,esi ; 006E3A05 66 BA C3 06 mov dx,0x6C3
        //   006E3A0D FF 97 50 02 00 00 call [edi+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1731(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1731, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 1732 (0x6C4) — send @0x614AE8 via [esi+0x250]. Fully fixed frame (all 0).
        //   00614AD2 6A00x4 push ; 00614ADA 33 C9 xor ecx,ecx ; 00614ADC 66 BA C4 06 mov dx,0x6C4
        //   00614AE8 FF 96 50 02 00 00 call [esi+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1732()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1732, 0, 0, 0, 0), Array.Empty<byte>());

        // SM 1733 (0x6C5) — send @0x6149C7 via [esi+0x250]. Param = byte[self+0xF2].
        //   006149AC 8A 83 F2 00 00 00 mov al,[ebx+0xF2] / 50 push (Param) ; 006149B3 6A00x3 push
        //   006149B9 33 C9 xor ecx,ecx ; 006149BB 66 BA C5 06 mov dx,0x6C5
        //   006149C7 FF 96 50 02 00 00 call [esi+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1733(byte param)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1733, 0, param, 0, 0), Array.Empty<byte>());

        // SM 1734 (0x6C6) — send @0x6145F0 via [edi+0x250]. Param = byte[self+ebx+0xEC]
        // (indexed slot in the 0xEC.. array).
        //   006145D4 8A 84 1E EC 00 00 00 mov al,[esi+ebx+0xEC] / 50 push (Param) ; 006145DC 6A00x3 push
        //   006145E2 33 C9 xor ecx,ecx ; 006145E4 66 BA C6 06 mov dx,0x6C6
        //   006145F0 FF 97 50 02 00 00 call [edi+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1734(byte param)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1734, 0, param, 0, 0), Array.Empty<byte>());

        // SM 1735 (0x6C7) — send @0x61487F via [esi+0x250]. Fully fixed frame (all 0).
        //   00614869 6A00x4 push ; 00614871 33 C9 xor ecx,ecx ; 00614873 66 BA C7 06 mov dx,0x6C7
        //   0061487F FF 96 50 02 00 00 call [esi+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1735()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1735, 0, 0, 0, 0), Array.Empty<byte>());

        // SM 1736 (0x6C8) — send @0x6144E4 via [esi+0x250]. Param = byte[self+0xF3].
        //   006144C9 8A 83 F3 00 00 00 mov al,[ebx+0xF3] / 50 push (Param) ; 006144D0 6A00x3 push
        //   006144D6 33 C9 xor ecx,ecx ; 006144D8 66 BA C8 06 mov dx,0x6C8
        //   006144E4 FF 96 50 02 00 00 call [esi+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1736(byte param)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1736, 0, param, 0, 0), Array.Empty<byte>());

        // SM 1737 (0x6C9) — send @0x61478D via [edi+0x250]. Recog/Param/Tag/Series are
        // four consecutive self byte-fields 0xEC..0xEF.
        //   0061475E 8A 83 ED.. push byte[ebx+0xED] (Param) ; 00614767 push byte[ebx+0xEE] (Tag)
        //   00614770 push byte[ebx+0xEF] (Series) ; 00614777 6A 00 push (sMsg=nil)
        //   00614779 33 C9 xor ecx,ecx / 8A 8B EC.. mov cl,byte[ebx+0xEC] (Recog)
        //   00614781 66 BA C9 06 mov dx,0x6C9 ; 0061478D FF 97 50 02 00 00 call [edi+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1737(byte recog, byte param, byte tag, byte series)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1737, recog, param, tag, series), Array.Empty<byte>());

        // SM 1738 (0x6CA) — send @0x6152EE. Fully fixed frame (all 0).
        //   006152DC 6A00x4 push ; 006152E4 33 C9 xor ecx,ecx ; 006152E8 66 BA CA 06 mov dx,0x6CA
        //   006152EE FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1738()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1738, 0, 0, 0, 0), Array.Empty<byte>());

        // SM 2843 (0xB1B) — send @0x6DE6FA. Recog=6, rest 0. Gated on global [0x7D7038+3]&0x20.
        //   006DE6E5 6A00x4 push ; 006DE6ED B9 06 00 00 00 mov ecx,6 ; 006DE6F4 66 BA 1B 0B mov dx,0xB1B
        //   006DE6FA FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2843()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2843, 6, 0, 0, 0), Array.Empty<byte>());

        // SM 2880 (0xB40) — send @0x6E598B. Recog=[ebp-8], a member id looked up by the
        // loop above (0x752A20), default 0. Rest 0.
        //   006E5978 6A00x4 push ; 006E5980 8B 4D F8 mov ecx,[ebp-8] ; 006E5983 66 BA 40 0B mov dx,0xB40
        //   006E598B FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2880(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2880, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 2881 (0xB41) — send @0x6E5E10. Param=ebx (a runtime value assembled above);
        // Recog=0, rest 0. Empty body (the local string built at [ebp-0x1C] is not part
        // of this frame — sMsg is push 0).
        //   006E5DFE 53 push ebx (Param) ; 006E5DFF 6A00x3 push (Tag/Series/sMsg=nil)
        //   006E5E05 33 C9 xor ecx,ecx ; 006E5E07 66 BA 41 0B mov dx,0xB41
        //   006E5E10 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2881(ushort param)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2881, 0, param, 0, 0), Array.Empty<byte>());

        // SM 2951 (0xB87) — send @0x6E5376. The function stashes its args into self
        // fields then sends: Param=word[self+0x9C0](=arg edx), Tag=word[self+0x9C8]
        // (=arg [ebp+8]); Recog=esi (self).
        //   006E5358 66 8B 86 C0 09.. push word[esi+0x9C0] (Param) ; 006E5360 push word[esi+0x9C8] (Tag)
        //   006E5368 6A00x2 push (Series/sMsg=nil) ; 006E536C 8B CE mov ecx,esi (Recog)
        //   006E536E 66 BA 87 0B mov dx,0xB87 ; 006E5376 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2951(int recog, ushort param, ushort tag)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2951, recog, param, tag, 0), Array.Empty<byte>());

        // SM 2952 (0xB88) — send @0x6E5567. Param=word[ebp-0xC] (a count, 0 unless the
        // enumeration above sets it); Recog=esi (self).
        //   006E5552 66 8B 45 F4 push word[ebp-0xC] (Param) ; 006E5557 6A00x3 push
        //   006E555D 8B CE mov ecx,esi (Recog) ; 006E555F 66 BA 88 0B mov dx,0xB88
        //   006E5567 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2952(int recog, ushort param)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2952, recog, param, 0, 0), Array.Empty<byte>());

        // SM 2957 (0xB8D) — send @0x6E6EE7. Resets [self+0x9FC]=0/[self+0x9F4]=0 then
        // sends a fully fixed frame (all 0).
        //   006E6ED5 6A00x4 push ; 006E6EDD 33 C9 xor ecx,ecx ; 006E6EDF 66 BA 8D 0B mov dx,0xB8D
        //   006E6EE7 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2957()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2957, 0, 0, 0, 0), Array.Empty<byte>());

        // SM 2958 (0xB8E) — send @0x6E6CF6 via [esi+0x250]. Param=1, rest 0.
        //   006E6CE4 6A 01 push 1 (Param) ; 006E6CE6 6A00x3 push ; 006E6CEC 33 C9 xor ecx,ecx
        //   006E6CEE 66 BA 8E 0B mov dx,0xB8E ; 006E6CF6 FF 96 50 02 00 00 call [esi+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2958()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2958, 0, 1, 0, 0), Array.Empty<byte>());

        // ---- RM-dispatch forwarding arms (empty body, fields from the RM record) ----

        // SM 2813 (0xAFD) — RM arm @0x6B5D19 via [obj+0x250]. Param=wParam[rec+2],
        // Tag=nParam2[rec+8], Series=nParam3[rec+0xC], Recog=BaseObject[rec+0x24].
        //   006B5CFC 66 8B 43 02 push wParam ; 006B5D01 66 8B 43 08 push nParam2
        //   006B5D06 66 8B 43 0C push nParam3 ; 006B5D0B 6A 00 push (sMsg=nil)
        //   006B5D0D 8B 4B 24 mov ecx,[ebx+0x24] ; 006B5D10 66 BA FD 0A mov dx,0xAFD
        //   006B5D19 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2813(
            int baseObjectRecog, ushort wParam, ushort nParam2, ushort nParam3)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2813, baseObjectRecog, wParam, nParam2, nParam3),
                Array.Empty<byte>());

        // SM 2898 (0xB52) — RM arm @0x6B5FED via [obj+0x250]. Param=wParam[rec+2],
        // Tag=nParam1[rec+4], Series=0, Recog=BaseObject[rec+0x24].
        //   006B5FD3 66 8B 43 02 push wParam ; 006B5FD7 66 8B 43 04 push nParam1
        //   006B5FDD 6A 00 push (Series=0) ; 006B5FDF 6A 00 push (sMsg=nil)
        //   006B5FE1 8B 4B 24 mov ecx,[ebx+0x24] ; 006B5FE4 66 BA 52 0B mov dx,0xB52
        //   006B5FED FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2898(
            int baseObjectRecog, ushort wParam, ushort nParam1)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2898, baseObjectRecog, wParam, nParam1, 0),
                Array.Empty<byte>());

        // SM 2968 (0xB98) — RM arm @0x6B5F18 via [obj+0x250]. Param=nParam1[rec+4],
        // Tag=Series=0, Recog=BaseObject[rec+0x24].
        //   006B5F01 66 8B 43 04 push nParam1 ; 006B5F06 6A00x3 push (Tag/Series/sMsg=nil)
        //   006B5F0C 8B 4B 24 mov ecx,[ebx+0x24] ; 006B5F0F 66 BA 98 0B mov dx,0xB98
        //   006B5F18 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2968(int baseObjectRecog, ushort nParam1)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2968, baseObjectRecog, nParam1, 0, 0),
                Array.Empty<byte>());

        // ---- sMsg (string) bodies: sent through SendSocket(ClientPacket, string) ----

        // SM 2812 (0xAFC) — send @0x645320 via [obj+0x250]. Generic "send text to obj"
        // wrapper: Recog=eax(arg1), Param=ecx(arg3), Tag=word[ebp+0xC](arg5), Series=0,
        // sMsg=[ebp+8](arg4, a Delphi string pointer). eax=esi is the send object.
        //   0064530A 57 push edi(=ecx arg3, Param) ; 0064530B 66 8B 45 0C push word[ebp+0xC] (Tag)
        //   00645310 6A 00 push (Series=0) ; 00645312 8B 45 08 push [ebp+8] (sMsg)
        //   00645316 8B CB mov ecx,ebx(=eax arg1, Recog) ; 00645318 66 BA FC 0A mov dx,0xAFC
        //   00645320 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm2812(
            int recog, ushort param, ushort tag, string sMsg)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2812, recog, param, tag, 0), sMsg ?? string.Empty);

        // SM 2815 (0xAFF) — send @0x6D4ED7 via [obj+0x250]. All-0 frame; sMsg=[ebp-8], a
        // string composed above from self fields (word[self+0x9E4], word[self+0x9E6],
        // strings at [self+0xB33]/[self+0xB09]) via 0x6A4144. The composed text is the
        // trigger-side value and is supplied by the caller.
        //   006D4EC3 6A00x3 push (Param/Tag/Series=0) ; 006D4EC9 8B 45 F8 push [ebp-8] (sMsg)
        //   006D4ECD 33 C9 xor ecx,ecx (Recog=0) ; 006D4ECF 66 BA FF 0A mov dx,0xAFF
        //   006D4ED7 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm2815(string sMsg)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2815, 0, 0, 0, 0), sMsg ?? string.Empty);

        // SM 2865 (0xB31) — send @0x6E1D39 via [obj+0x250]. All-0 frame except Recog=ebx
        // (self); sMsg=[ebp-4], a string built by 0x6996E8 (empty if global [0x7D5D20]=0).
        //   006E1D25 6A00x3 push (Param/Tag/Series=0) ; 006E1D2B 8B 45 FC push [ebp-4] (sMsg)
        //   006E1D2F 8B CB mov ecx,ebx (Recog=self) ; 006E1D31 66 BA 31 0B mov dx,0xB31
        //   006E1D39 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm2865(int recog, string sMsg)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2865, recog, 0, 0, 0), sMsg ?? string.Empty);

        // SM 2878 (0xB3E) — send @0x624AC6 via [obj+0x250]. All-0 frame except Recog=ebx
        // (an id from 0x40CA18); sMsg=[ebp-0x2C], a string built by 0x69B14C (sent only
        // when non-empty).
        //   00624AB1 6A00x3 push (Param/Tag/Series=0) ; 00624AB7 8B 45 D4 push [ebp-0x2C] (sMsg)
        //   00624ABB 8B CB mov ecx,ebx (Recog) ; 00624ABD 66 BA 3E 0B mov dx,0xB3E
        //   00624AC6 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm2878(int recog, string sMsg)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2878, recog, 0, 0, 0), sMsg ?? string.Empty);

        // SM 2960 (0xB90) — RM arm @0x6B5ECE via [obj+0x250]. sMsg = the RM record's
        // message string [rec+0x10] (copied to a local via 0x405708). Param=nParam1
        // [rec+4], Tag=nParam2[rec+8], Series=wParam[rec+2], Recog=BaseObject[rec+0x24].
        // (This arm is taken when word[rec+0x14]!=0; the word[rec+0x14]==0 sibling
        // @0x6B5EF6 sends the same ident with an empty sMsg.)
        //   006B5E9E 66 8B 43 04 push nParam1 ; 006B5EA3 66 8B 43 08 push nParam2
        //   006B5EA8 66 8B 43 02 push wParam ; 006B5EAD.. copy [rec+0x10] -> [ebp-0x1C0]
        //   006B5EC1 push [ebp-0x1C0] (sMsg) ; 006B5EC2 8B 4B 24 mov ecx,[ebx+0x24] (Recog)
        //   006B5EC5 66 BA 90 0B mov dx,0xB90 ; 006B5ECE FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm2960(
            int baseObjectRecog, ushort nParam1, ushort nParam2, ushort wParam, string sMsg)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2960, baseObjectRecog, nParam1, nParam2, wParam),
                sMsg ?? string.Empty);

        // ---- [obj+0x254] (Buf,Len) sends ----

        // SM 2830 (0xB0E) — RM arm @0x6B555D via [obj+0x254]. Pure forward of the RM
        // record's buffer: Buf=[rec+0x10], Len=word[rec+0x14] (== body.Length). The
        // buffer is produced by the RM sender upstream (same shape as SM_917); its
        // per-field layout is not invented here — the caller supplies the exact bytes.
        // Param=wParam[rec+2], Tag=nParam2[rec+8], Series=nParam3[rec+0xC],
        // Recog=nParam1[rec+4].
        //   006B5539 66 8B 43 02 push wParam ; 006B553E 66 8B 43 08 push nParam2
        //   006B5543 66 8B 43 0C push nParam3 ; 006B5548 8B 43 10 push [rec+0x10] (Buf)
        //   006B554C 0F B7 43 14 push word[rec+0x14] (Len) ; 006B5551 8B 4B 04 mov ecx,[ebx+4] (Recog)
        //   006B5554 66 BA 0E 0B mov dx,0xB0E ; 006B555D FF 93 54 02 00 00 call [ebx+0x254]
        internal static (ClientPacket Header, byte[] Body) BuildSm2830(
            int nParam1Recog, ushort wParam, ushort nParam2, ushort nParam3, byte[] body)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2830, nParam1Recog, wParam, nParam2, nParam3),
                body ?? Array.Empty<byte>());

        // SM 2896 (0xB50) — RM arm @0x6B5F8E via [esi+0x254]. Buf=[rec+0x10] forward,
        // Len=word[rec+0x14]. Param=wParam[rec+2], Tag=0, Series=0,
        // Recog=BaseObject[rec+0x24]. Caller supplies the record buffer bytes.
        //   006B5F70 66 8B 43 02 push wParam ; 006B5F75 6A00x2 push (Tag/Series=0)
        //   006B5F79 8B 43 10 push [rec+0x10] (Buf) ; 006B5F7D 0F B7 43 14 push word[rec+0x14] (Len)
        //   006B5F82 8B 4B 24 mov ecx,[ebx+0x24] (Recog) ; 006B5F85 66 BA 50 0B mov dx,0xB50
        //   006B5F8E FF 96 54 02 00 00 call [esi+0x254]
        internal static (ClientPacket Header, byte[] Body) BuildSm2896(
            int baseObjectRecog, ushort wParam, byte[] body)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2896, baseObjectRecog, wParam, 0, 0),
                body ?? Array.Empty<byte>());

        // SM 2897 (0xB51) — RM arm @0x6B5FC8 via [obj+0x254]. Buf=[rec+0x10] forward,
        // Len=word[rec+0x14]. Param=wParam[rec+2], Tag=nParam1[rec+4], Series=0,
        // Recog=BaseObject[rec+0x24]. Caller supplies the record buffer bytes.
        //   006B5FA7 66 8B 43 02 push wParam ; 006B5FAC 66 8B 43 04 push nParam1
        //   006B5FB1 6A 00 push (Series=0) ; 006B5FB3 8B 43 10 push [rec+0x10] (Buf)
        //   006B5FB7 0F B7 43 14 push word[rec+0x14] (Len) ; 006B5FBC 8B 4B 24 mov ecx,[ebx+0x24] (Recog)
        //   006B5FBF 66 BA 51 0B mov dx,0xB51 ; 006B5FC8 FF 93 54 02 00 00 call [ebx+0x254]
        internal static (ClientPacket Header, byte[] Body) BuildSm2897(
            int baseObjectRecog, ushort wParam, ushort nParam1, byte[] body)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2897, baseObjectRecog, wParam, nParam1, 0),
                body ?? Array.Empty<byte>());

        // SM 2885 (0xB45) — send @0x744EF1 via [obj+0x254], fixed 20-byte struct body
        // (Len=0x14). The struct at [ebp-4] is allocated (0x402FA0, size 0x14) then
        // every one of its five dwords is written before the send, so the layout is
        // fully proven (no uninitialised bytes):
        //   [0x00] = LoWord(0x4C853C(rec))     (00744EAE movzx eax,ax / 89 06)
        //   [0x04] = 0x4C896C(rec) & 0xFF        (00744EBA and eax,0xFF / 89 46 04)
        //   [0x08] = 1                           (00744ECE mov [esi+8],1)
        //   [0x0C] = [rec+0x10]                  (00744EC2 mov eax,[ebx+0x10] / 89 46 0C)
        //   [0x10] = [rec+0x08]                  (00744EC8 mov eax,[ebx+8]    / 89 46 10)
        // Param = 0x4C853C(rec) (00744ED5 mov eax,ebx / call 0x4C853C / 50 push);
        // Tag=Series=0; Recog=edi (self, 1st arg). The four record-derived values are
        // caller-supplied (they come from getters on the unmapped record).
        //   00744EDC 50 push eax (Param) ; 00744EDD 6A00x2 push (Tag/Series=0)
        //   00744EE1 8B 45 FC push [ebp-4] (Buf) ; 00744EE5 6A 14 push 0x14 (Len)
        //   00744EE7 8B CF mov ecx,edi (Recog) ; 00744EE9 66 BA 45 0B mov dx,0xB45
        //   00744EF1 FF 93 54 02 00 00 call [ebx+0x254]
        internal static (ClientPacket Header, byte[] Body) BuildSm2885(
            int recog, ushort idWord, byte flag, uint recField0x10, uint recField0x8)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_2885, recog, idWord, 0, 0);
            using var stream = new MemoryStream(0x14);
            using var writer = new BinaryWriter(stream);
            writer.Write((uint)idWord);   // [0x00] movzx word
            writer.Write((uint)flag);     // [0x04] & 0xFF
            writer.Write((uint)1);        // [0x08] const 1
            writer.Write(recField0x10);   // [0x0C] [rec+0x10]
            writer.Write(recField0x8);    // [0x10] [rec+0x08]
            return (header, stream.ToArray());
        }
    }
}

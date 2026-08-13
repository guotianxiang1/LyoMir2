using System;
using System.IO;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// SM down-wire builders for the missing idents of the second quarter
    /// (native wire-SM ident 666..1256). Each builder mirrors the reference
    /// pattern in <see cref="TBaseObject"/>.<c>BuildTimedAbilityClientState</c>
    /// (SM 3554/3555): it returns the header five-tuple plus the exact body, and
    /// never touches an existing method body (anti-conflict rule for the 25 parallel
    /// worktrees).
    ///
    /// Evidence base: flat_image.bin, ImageBase 0x400000, CODE 0x401000..0x7A10D0.
    /// Every frame below was reversed byte-for-byte with capstone from its own send
    /// slot; the RM-record layout used throughout is the one pinned in
    /// docs/m_sm_b_20260813.md §2.3:
    ///   [rec+0x00]=tag(word) [rec+0x02]=wParam(word) [rec+0x04]=nParam1
    ///   [rec+0x08]=nParam2   [rec+0x0C]=nParam3       [rec+0x24]=BaseObject
    /// and the send-slot signatures (m_sm2 §5.2, independently reverified):
    ///   [obj+0x250] SendDefMessage(Self=eax, wIdent=dx, nRecog=ecx, Param, Tag, Series, sMsg)
    ///   [obj+0x254] same, last two stack args become (Buf, Len)
    /// The left-to-right push order maps push#1=Param, #2=Tag, #3=Series, #4=sMsg|Buf,
    /// #5=Len, so <c>Grobal2.MakeDefaultMsg(ident, Recog, Param, Tag, Series)</c> lines
    /// up position-for-position with the native slot.
    ///
    /// FAIL-CLOSED (no builder written, body layout not obtainable without inventing):
    ///  - SM 950 (0x3B6) @0x006012D5 via [obj+0x254]: Recog=0, Param=Tag=Series=0,
    ///    Buf=[ebp-0xF4], Len=0xD8 (216 bytes). The 216-byte record is filled by the
    ///    loop above the send (0x6012AB `mov cl,0x14`/`call 0x4039E4` ...); its per-field
    ///    layout is a cattle-prize container that is not resolvable from the send slot,
    ///    so reproducing the body would be invention. Registered, not built.
    ///  - SM 1108 (0x454) @0x0060F158 / 0x006CBD0D: variable string body plus a
    ///    Recog that comes from the global manager [0x7D6D50] + by-name lookup 0x652784
    ///    and the player field [Self+0xBFC]. BLOCKED in docs/m_sm_b_20260813.md §8-B1;
    ///    the body layout depends on an unmapped functional container. Registered, not built.
    /// </summary>
    public partial class TBaseObject
    {
        // SM 689 (0x2B1) — RM 10035 dispatcher arm @0x006B4BA1 via [obj+0x250], no body.
        //   006B4B84  66 8B 43 02        mov ax,[ebx+2]     ; #1 Param  = wParam
        //   006B4B88  50                 push eax
        //   006B4B89  66 8B 43 08        mov ax,[ebx+8]     ; #2 Tag    = LoWord(nParam2)
        //   006B4B8D  50                 push eax
        //   006B4B8E  66 8B 43 0C        mov ax,[ebx+0xC]   ; #3 Series = LoWord(nParam3)
        //   006B4B92  50                 push eax
        //   006B4B93  6A 00              push 0             ; #4 sMsg   = nil
        //   006B4B95  8B 4B 04           mov ecx,[ebx+4]    ; Recog     = nParam1
        //   006B4B98  66 BA B1 02        mov dx,0x2B1       ; ident 689
        //   006B4BA1  FF 93 50 02 00 00  call [ebx+0x250]
        // Trigger: reached from the shared RM dispatcher (jmp 0x6B624C tail); the arm
        // itself is unconditional once the RM tag selects it.
        internal static (ClientPacket Header, byte[] Body) BuildSm689(
            int nParam1, ushort wParam, ushort nParam2, ushort nParam3)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_689,
                nParam1, wParam, nParam2, nParam3);
            return (header, Array.Empty<byte>());
        }

        // SM 924 (0x39C, SM_HERO_SPLITSHADOW) — RM 10036 arm @0x006B4BC9 via [obj+0x250],
        // no body.
        //   006B4BAC  66 8B 43 04        mov ax,[ebx+4]     ; #1 Param  = LoWord(nParam1)
        //   006B4BB0  50                 push eax
        //   006B4BB1  66 8B 43 08        mov ax,[ebx+8]     ; #2 Tag    = LoWord(nParam2)
        //   006B4BB5  50                 push eax
        //   006B4BB6  66 8B 43 02        mov ax,[ebx+2]     ; #3 Series = wParam
        //   006B4BBA  50                 push eax
        //   006B4BBB  6A 00              push 0             ; #4 sMsg   = nil
        //   006B4BBD  8B 4B 24           mov ecx,[ebx+0x24] ; Recog     = BaseObject
        //   006B4BC0  66 BA 9C 03        mov dx,0x39C       ; ident 924
        //   006B4BC9  FF 93 50 02 00 00  call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm924(
            int baseObjectRecog, ushort nParam1, ushort nParam2, ushort wParam)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_SPLITSHADOW,
                baseObjectRecog, nParam1, nParam2, wParam);
            return (header, Array.Empty<byte>());
        }

        // SM 1109 (0x455) — RM 10156 arm @0x006B570E via [obj+0x250], no body. Same
        // field mapping as SM 689 (Recog=nParam1, Param=wParam, Tag=nParam2,
        // Series=nParam3):
        //   006B56F1  66 8B 43 02        mov ax,[ebx+2]     ; #1 Param  = wParam
        //   006B56F5  50                 push eax
        //   006B56F6  66 8B 43 08        mov ax,[ebx+8]     ; #2 Tag    = LoWord(nParam2)
        //   006B56FA  50                 push eax
        //   006B56FB  66 8B 43 0C        mov ax,[ebx+0xC]   ; #3 Series = LoWord(nParam3)
        //   006B56FF  50                 push eax
        //   006B5700  6A 00              push 0             ; #4 sMsg   = nil
        //   006B5702  8B 4B 04           mov ecx,[ebx+4]    ; Recog     = nParam1
        //   006B5705  66 BA 55 04        mov dx,0x455       ; ident 1109
        //   006B570E  FF 93 50 02 00 00  call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1109(
            int nParam1, ushort wParam, ushort nParam2, ushort nParam3)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1109,
                nParam1, wParam, nParam2, nParam3);
            return (header, Array.Empty<byte>());
        }

        // SM 1201 (0x4B1) — RM 10039 arm @0x006B4C41 via [obj+0x250], no body. Same
        // field mapping as SM 689/1109:
        //   006B4C24  66 8B 43 02        mov ax,[ebx+2]     ; #1 Param  = wParam
        //   006B4C28  50                 push eax
        //   006B4C29  66 8B 43 08        mov ax,[ebx+8]     ; #2 Tag    = LoWord(nParam2)
        //   006B4C2D  50                 push eax
        //   006B4C2E  66 8B 43 0C        mov ax,[ebx+0xC]   ; #3 Series = LoWord(nParam3)
        //   006B4C32  50                 push eax
        //   006B4C33  6A 00              push 0             ; #4 sMsg   = nil
        //   006B4C35  8B 4B 04           mov ecx,[ebx+4]    ; Recog     = nParam1
        //   006B4C38  66 BA B1 04        mov dx,0x4B1       ; ident 1201
        //   006B4C41  FF 93 50 02 00 00  call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1201(
            int nParam1, ushort wParam, ushort nParam2, ushort nParam3)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1201,
                nParam1, wParam, nParam2, nParam3);
            return (header, Array.Empty<byte>());
        }

        // SM 959 (0x3BF) — RM arm @0x006B5761 via [obj+0x250], no body.
        //   006B5748  66 8B 43 02        mov ax,[ebx+2]     ; #1 Param  = wParam
        //   006B574C  50                 push eax
        //   006B574D  66 8B 43 04        mov ax,[ebx+4]     ; #2 Tag    = LoWord(nParam1)
        //   006B5751  50                 push eax
        //   006B5752  6A 00              push 0             ; #3 Series = 0
        //   006B5754  6A 00              push 0             ; #4 sMsg   = nil
        //   006B5756  33 C9              xor ecx,ecx        ; Recog     = 0
        //   006B5758  66 BA BF 03        mov dx,0x3BF       ; ident 959
        //   006B5761  FF 93 50 02 00 00  call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm959(
            ushort wParam, ushort nParam1)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_959, 0, wParam, nParam1, 0);
            return (header, Array.Empty<byte>());
        }

        // SM 1107 (0x453) — RM 10501 arm @0x006B5CF1 via [obj+0x250], no body. Same
        // field mapping as SM 924 (Recog=BaseObject, Param=nParam1, Tag=nParam2,
        // Series=wParam):
        //   006B5CD4  66 8B 43 04        mov ax,[ebx+4]     ; #1 Param  = LoWord(nParam1)
        //   006B5CD8  50                 push eax
        //   006B5CD9  66 8B 43 08        mov ax,[ebx+8]     ; #2 Tag    = LoWord(nParam2)
        //   006B5CDD  50                 push eax
        //   006B5CDE  66 8B 43 02        mov ax,[ebx+2]     ; #3 Series = wParam
        //   006B5CE2  50                 push eax
        //   006B5CE3  6A 00              push 0             ; #4 sMsg   = nil
        //   006B5CE5  8B 4B 24           mov ecx,[ebx+0x24] ; Recog     = BaseObject
        //   006B5CE8  66 BA 53 04        mov dx,0x453       ; ident 1107
        //   006B5CF1  FF 93 50 02 00 00  call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1107(
            int baseObjectRecog, ushort nParam1, ushort nParam2, ushort wParam)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1107,
                baseObjectRecog, nParam1, nParam2, wParam);
            return (header, Array.Empty<byte>());
        }

        // SM 951 (0x3B7) — @0x006CF5EF via [obj+0x250], no body.
        //   006CF5DC  6A 00              push 0             ; #1 Param  = 0
        //   006CF5DE  6A 00              push 0             ; #2 Tag    = 0
        //   006CF5E0  6A 00              push 0             ; #3 Series = 0
        //   006CF5E2  6A 00              push 0             ; #4 sMsg   = nil
        //   006CF5E4  8B 4D F4           mov ecx,[ebp-0xC]  ; Recog     = [ebp-0xC]
        //   006CF5E7  66 BA B7 03        mov dx,0x3B7       ; ident 951
        //   006CF5EF  FF 93 50 02 00 00  call [ebx+0x250]
        // Trigger: [ebp-0xC] is the result of the loop's `call 0x600F6C`
        // (0x6CF5CB mov [ebp-0xC],eax); the send is gated by `cmp [ebp-0xC],1 / je`
        // (0x6CF5D6), i.e. it fires only when that result != 1. Recog carries it.
        internal static (ClientPacket Header, byte[] Body) BuildSm951(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_951, recog, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        // SM 965 (0x3C5, SM_965) — @0x0072737B via [obj+0x250], no body.
        //   00727368  6A 00              push 0             ; #1 Param  = 0
        //   0072736A  6A 00              push 0             ; #2 Tag    = 0
        //   0072736C  6A 00              push 0             ; #3 Series = 0
        //   0072736E  6A 00              push 0             ; #4 sMsg   = nil
        //   00727370  8B C8              mov ecx,eax        ; Recog = eax (= [ebx+0x40])
        //   00727372  66 BA C5 03        mov dx,0x3C5       ; ident 965
        //   0072737B  FF 97 50 02 00 00  call [edi+0x250]
        // Trigger: guarded by `mov eax,[ebx+0x40] / test eax,eax / je` (0x727361);
        // fires only when [ebx+0x40] is non-nil, and that handle is the Recog.
        internal static (ClientPacket Header, byte[] Body) BuildSm965(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_965, recog, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        // SM 1250/1254/1255 — three sibling sends in the same activity subsystem, all
        // via [obj+0x250] with Recog=-1 and every stack arg 0 (no body). Verbatim, e.g.
        // 1250 @0x006F0A18:
        //   006F0A05  6A 00              push 0             ; #1 Param  = 0
        //   006F0A07  6A 00              push 0             ; #2 Tag    = 0
        //   006F0A09  6A 00              push 0             ; #3 Series = 0
        //   006F0A0B  6A 00              push 0             ; #4 sMsg   = nil
        //   006F0A0D  83 C9 FF           or  ecx,0xFFFFFFFF ; Recog     = -1
        //   006F0A10  66 BA E2 04        mov dx,0x4E2       ; ident 1250
        //   006F0A18  FF 93 50 02 00 00  call [obj+0x250]
        // 1254 @0x006F0F72 (dx=0x4E6) and 1255 @0x006F0EAE (dx=0x4E7) are byte-identical
        // apart from the ident immediate. Trigger for all three: `call 0x6D3694` result
        // is stored to byte[self+0x18C8] and the send fires only when it is 0
        // (test bl,bl / jne skip).
        internal static (ClientPacket Header, byte[] Body) BuildSm1250()
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1250, -1, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        internal static (ClientPacket Header, byte[] Body) BuildSm1254()
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1254, -1, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        internal static (ClientPacket Header, byte[] Body) BuildSm1255()
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1255, -1, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        // SM 1256 (0x4E8) — @0x006F101D via [obj+0x250], no body.
        //   006F100B  6A 00              push 0             ; #1 Param  = 0
        //   006F100D  6A 00              push 0             ; #2 Tag    = 0
        //   006F100F  6A 00              push 0             ; #3 Series = 0
        //   006F1011  6A 00              push 0             ; #4 sMsg   = nil
        //   006F1013  8B CF              mov ecx,edi        ; Recog     = edi (handle)
        //   006F1015  66 BA E8 04        mov dx,0x4E8       ; ident 1256
        //   006F101D  FF 93 50 02 00 00  call [obj+0x250]
        // A second native site exists (sites=2 in the frame table) which this pass did
        // not byte-verify; the frame here is the 0x006F101D form only.
        internal static (ClientPacket Header, byte[] Body) BuildSm1256(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1256, recog, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        // SM 1253 (0x4E5) — @0x006F0F1C via [obj+0x250], no body. The one dword at
        // [esi] is split into Tag(hi)/Series(lo):
        //   006F0EFF  6A 00              push 0             ; #1 Param  = 0
        //   006F0F01  8B 06              mov eax,[esi]      ; value dword
        //   006F0F03  E8 60 7E D1 FF     call 0x00408D68    ; 0x408D68: return eax >> 16
        //   006F0F08  50                 push eax           ; #2 Tag    = HiWord(value)
        //   006F0F09  66 8B 06           mov ax,word[esi]   ; LoWord(value)
        //   006F0F0C  50                 push eax           ; #3 Series = LoWord(value)
        //   006F0F0D  6A 00              push 0             ; #4 sMsg   = nil
        //   006F0F0F  B9 FE FF FF FF     mov ecx,0xFFFFFFFE ; Recog     = -2
        //   006F0F14  66 BA E5 04        mov dx,0x4E5       ; ident 1253
        //   006F0F1C  FF 93 50 02 00 00  call [obj+0x250]
        // Trigger: guarded by cmp byte[ebx+0x18C8],0 / jne (0x6F0EF6). A second native
        // site exists (sites=2) not byte-verified in this pass.
        internal static (ClientPacket Header, byte[] Body) BuildSm1253(int value)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1253, -2, 0,
                HUtil32.HiWord(value), HUtil32.LoWord(value));
            return (header, Array.Empty<byte>());
        }

        // SM 1251 (0x4E3) — @0x006D1666 via [obj+0x250], no body. Param is the constant 6.
        //   006D1650  6A 06              push 6             ; #1 Param  = 6
        //   006D1652  6A 00              push 0             ; #2 Tag    = 0
        //   006D1654  6A 00              push 0             ; #3 Series = 0
        //   006D1656  6A 00              push 0             ; #4 sMsg   = nil
        //   006D1658  8B 8E 54 0A 00 00  mov ecx,[esi+0xA54]; Recog     = [esi+0xA54]
        //   006D165E  66 BA E3 04        mov dx,0x4E3       ; ident 1251
        //   006D1666  FF 93 50 02 00 00  call [obj+0x250]
        // (Native has additional 1251 sites with a different Recog/Param; only the
        // Param=6 form at 0x006D1666 is byte-verified here.)
        internal static (ClientPacket Header, byte[] Body) BuildSm1251(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1251, recog, 6, 0, 0);
            return (header, Array.Empty<byte>());
        }

        // SM 1252 (0x4E4) — @0x00654C10 via [obj+0x250], no body. Param is the constant 4.
        //   00654BFA  6A 04              push 4             ; #1 Param  = 4
        //   00654BFC  6A 00              push 0             ; #2 Tag    = 0
        //   00654BFE  6A 00              push 0             ; #3 Series = 0
        //   00654C00  6A 00              push 0             ; #4 sMsg   = nil
        //   00654C02  8B 8E 50 0A 00 00  mov ecx,[esi+0xA50]; Recog     = [esi+0xA50]
        //   00654C08  66 BA E4 04        mov dx,0x4E4       ; ident 1252
        //   00654C10  FF 93 50 02 00 00  call [obj+0x250]
        // (Native has additional 1252 sites with other Recog/Param; only the Param=4
        // form at 0x00654C10 is byte-verified here.)
        internal static (ClientPacket Header, byte[] Body) BuildSm1252(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1252, recog, 4, 0, 0);
            return (header, Array.Empty<byte>());
        }

        // SM 925 (0x39D, SM_HERO_HELPOP_OK) — @0x0068C854 via [obj+0x250], no body. The
        // preceding call [edi+0xD4] (cx=0xFCFF) at 0x0068C837 is a separate SysMsg, not
        // part of this frame.
        //   0068C83D  56                 push esi           ; #1 Param  = esi
        //   0068C83E  66 8B 45 FC        mov ax,[ebp-4]     ; #2 Tag    = word[ebp-4]
        //   0068C842  50                 push eax
        //   0068C843  6A 00              push 0             ; #3 Series = 0
        //   0068C845  6A 00              push 0             ; #4 sMsg   = nil
        //   0068C847  B9 02 00 00 00     mov ecx,2          ; Recog     = 2
        //   0068C84C  66 BA 9D 03        mov dx,0x39D       ; ident 925
        //   0068C854  FF 93 50 02 00 00  call [obj+0x250]
        // (Native has a second 925 site @0x006D11B7 with Recog=1 and a different
        // Param/Tag; only the Recog=2 form is byte-verified here.)
        internal static (ClientPacket Header, byte[] Body) BuildSm925(int param, ushort tag)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_HELPOP_OK, 2, param, tag, 0);
            return (header, Array.Empty<byte>());
        }

        // SM 966 (0x3C6) — @0x006D663E via [obj+0x250]. This one carries a text sMsg (the
        // 4th stack arg is a string pointer, not a Buf), so it is sent through the
        // SendSocket(ClientPacket, string) path rather than the byte[] body path.
        //   006D661E  6A 01              push 1             ; #1 Param  = 1
        //   006D6620  6A 00              push 0             ; #2 Tag    = 0
        //   006D6622  6A 00              push 0             ; #3 Series = 0
        //   006D6624  68 80 66 6D 00     push 0x006D6680    ; #4 sMsg   = const string ptr
        //   006D6629  B8 34 08 00 00     mov eax,0x834      ; 2100
        //   006D662E  2B C6              sub eax,esi        ; 2100 - countdown(esi)
        //   006D6630  69 C8 E8 03 00 00  imul ecx,eax,0x3E8 ; Recog = (2100 - esi) * 1000
        //   006D6636  66 BA C6 03        mov dx,0x3C6       ; ident 966
        //   006D663E  FF 96 50 02 00 00  call [obj+0x250]
        // The Delphi long string at 0x006D6680 (length dword 20 at ptr-4) is the GBK
        // bytes C7 EB B8 FC D0 C2 B5 BD D7 EE D0 C2 B5 C4 BF CD BB A7 B6 CB =
        // "请更新到最新的客户端". Recog is left to the caller because it is the runtime
        // (2100 - countdown) * 1000 computed at the send site.
        internal const string Sm966Text = "请更新到最新的客户端";

        internal static (ClientPacket Header, string Text) BuildSm966(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_966, recog, 1, 0, 0);
            return (header, Sm966Text);
        }
    }
}

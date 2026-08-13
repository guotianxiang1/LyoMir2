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
    }
}

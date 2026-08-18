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

        // SM 917 (0x395, SM_HERO_DELITEMS) — @0x00689708 via [obj+0x254], variable body.
        //   006896ED  6A 00              push 0             ; #1 Param  = 0
        //   006896EF  6A 00              push 0             ; #2 Tag    = 0
        //   006896F1  6A 00              push 0             ; #3 Series = 0
        //   006896F3  8B 43 10           mov eax,[ebx+0x10] ; #4 Buf = [rec+0x10]
        //   006896F6  50                 push eax
        //   006896F7  0F B7 43 14        movzx eax,[ebx+0x14]; #5 Len = word[rec+0x14]
        //   006896FB  50                 push eax
        //   006896FC  8B 4B 04           mov ecx,[ebx+4]    ; Recog = nParam1
        //   006896FF  66 BA 95 03        mov dx,0x395       ; ident 917
        //   00689708  FF 93 54 02 00 00  call [obj+0x254]
        // The send point is a pure Buf/Len forward of the RM record's buffer. Producers
        // sub_73FC70 and sub_740078 fill it with count consecutive item+0x18 dwords;
        // TBaseObject.BuildNativeHeroDeletedItemBody preserves that exact count*4 layout.
        internal static (ClientPacket Header, byte[] Body) BuildSm917(int nParam1, byte[] body)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_HERO_DELITEMS, nParam1, 0, 0, 0);
            return (header, body ?? Array.Empty<byte>());
        }

        // SM 1233 (0x4D1) — RM 12323 arm @0x006B602C, sent through the CX wrapper
        // 0x006BCE54 which attaches the standard 32-byte actor-state body (the same body
        // the movement/action family — SM 6/7/9/10/11/13/27/32/33/34 — carries).
        // Caller arm (RM record fields pushed for the wrapper, ret 0xC = 3 stack args):
        //   006B6013  66 8B 43 04        mov ax,[ebx+4]     ; -> [ebp+0x10] = Param  = nParam1
        //   006B6018  66 8B 43 08        mov ax,[ebx+8]     ; -> [ebp+0xC]  = Tag    = nParam2
        //   006B601D  66 8B 43 02        mov ax,[ebx+2]     ; -> [ebp+8]    = Series = wParam
        //   006B6022  8B 53 24           mov edx,[ebx+0x24] ; 主体 actor = BaseObject
        //   006B6025  66 B9 D1 04        mov cx,0x4D1       ; ident 1233
        //   006B602C  E8 23 6E 00 00     call 0x006BCE54
        // Wrapper 0x006BCE54 body build (buffer [ebp-0x24], 32 bytes, pre-zeroed):
        //   [0..3]   call [actor VMT+0x1C8](edx=recipient)   = GetFeature(recipient)
        //   [4..19]  16 bytes copied from [actor+0x168]       = WriteBodyState
        //   [20..29] call [actor VMT+0x70](0,recipient)       = GetMobileFeature
        //            (dword race/sex/hair, dword weapon/dress, word horse = 10 bytes)
        //   [30..31] remain 0 (pad)
        // then send via [recipient VMT+0x254] with Recog=ebx=BaseObject, Len=0x20.
        // This is byte-for-byte the C# BuildMobileActorStateBody used by the FAITHFUL
        // movement idents; it is reproduced here (rather than reused) only because that
        // helper is a private member of the TPlayObject partial. `this` is the actor.
        internal (ClientPacket Header, byte[] Body) BuildSm1233(
            TBaseObject recipient, ushort nParam1, ushort nParam2, ushort wParam)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_1233,
                ObjectId, nParam1, nParam2, wParam);
            using var stream = new MemoryStream(32);
            using var writer = new BinaryWriter(stream);
            writer.Write(GetFeature(recipient)); // [0..3]
            WriteBodyState(writer);              // [4..19]
            writer.Write(GetMobileFeature());    // [20..29]
            writer.Write((ushort)0);             // [30..31] pad
            return (header, stream.ToArray());
        }
    }
}

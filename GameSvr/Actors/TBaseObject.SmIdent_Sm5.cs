using System;
using System.IO;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// SM down-wire builders for the missing idents of batch 5 — the ascending
    /// rank 106..140 slice (the highest 35 by value, idents 4349..4650) of the
    /// 140 class-(c) native wire-SM idents in staging/_sm1_work/classes.txt
    /// (native server->client idents that fire through a real send slot in
    /// flat_image.bin yet had no C# constant/builder). Mirrors the reference
    /// pattern of SmIdent_Sm1/Sm2/Sm3 and SmA: each builder returns the header
    /// five-tuple plus the exact body and never touches a shared method body
    /// (anti-conflict rule for the parallel worktrees). The trigger side (which
    /// caller/record feeds Recog/Param/…) is intentionally NOT wired here.
    ///
    /// Evidence base: flat_image.bin, ImageBase 0x400000 (offset = VA - 0x400000).
    /// Every frame below was reversed byte-for-byte with capstone from its own
    /// send site (see _work/smdis.py, _work/fwd.py, _work/dis_*.txt). Send-slot
    /// signatures (reverified against the earlier batches and the two member
    /// broadcast wrappers 0x705954 [0x250] / 0x7059D0 [0x254]):
    ///   [obj+0x250] SendDefMessage: push Param,Tag,Series,sMsg ; ecx=Recog dx=ident
    ///   [obj+0x254] SendSocket:     push Param,Tag,Series,Buf,Len ; ecx=Recog dx=ident
    /// so Grobal2.MakeDefaultMsg(ident, Recog, Param, Tag, Series) lines up
    /// position-for-position. MakeDefaultMsg casts Param/Tag/Series to ushort but
    /// stores Recog as a full int, so native 16-bit reads (movzx) into Recog are
    /// reproduced explicitly with HUtil32.LoWord().
    ///
    /// FAIL-CLOSED (registered in Grobal2.cs, NO builder — body cannot be
    /// evaluated at the send slot): SM_4363, SM_4441, SM_4442, SM_4443, SM_4612,
    /// SM_4626, SM_4646, SM_4647. See the notes at the end of this file.
    /// </summary>
    public partial class TBaseObject
    {
        // ---- RM-dispatch forwarding arms (empty body) ----

        // SM 4349 (0x10FD) — RM arm @0x6B5215 via [obj+0x250]. The dword nParam2
        // [rec+8] is split: Tag = LoWord, Series = HiWord.
        //   006B51F6 66 8B 43 02 push wParam[rec+2]  (Param)
        //   006B51FB 66 8B 43 08 push loword[rec+8]  (Tag)
        //   006B5200 8B 43 08 / C1 E8 10 / 50 push hiword[rec+8]  (Series)
        //   006B5207 6A 00 push 0 (sMsg) ; 006B5209 8B 4B 04 mov ecx,[rec+4] (Recog=nParam1)
        //   006B520C 66 BA FD 10 mov dx,0x10FD ; 006B5215 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4349(
            int nParam1Recog, ushort wParam, int nParam2)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4349, nParam1Recog, wParam,
                    HUtil32.LoWord(nParam2), HUtil32.HiWord(nParam2)),
                Array.Empty<byte>());

        // SM 4350 (0x10FE) — RM arm @0x68980D via [obj+0x250]. Same 5-tuple shape
        // as SM 4349 (Recog=nParam1[rec+4], Param=wParam[rec+2], Tag=LoWord(nParam2),
        // Series=HiWord(nParam2)); a sibling of the SM 915/919 arms in the same
        // dispatcher.
        //   006897EE push wParam ; push loword[rec+8] ; push hiword[rec+8] ; push 0
        //   006897FF 8B 4B 04 mov ecx,[rec+4] ; 00689804 66 BA FE 10 mov dx,0x10FE
        //   0068980D FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4350(
            int nParam1Recog, ushort wParam, int nParam2)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4350, nParam1Recog, wParam,
                    HUtil32.LoWord(nParam2), HUtil32.HiWord(nParam2)),
                Array.Empty<byte>());

        // ---- helper-function empty-body sends (Recog = caller int arg) ----

        // SM 4351 (0x10FF) — @0x647F38 via [obj+0x250]. Helper (eax=Recog value,
        // edx=recipient); sent when recipient!=nil, not-ghost (0x772DA8=false) and
        // byte[recipient+0x73]==0. Fully fixed frame apart from Recog.
        //   00647F26 6A00x4 push (Param/Tag/Series/sMsg) ; 00647F2E 8B CE mov ecx,esi (Recog=eax arg)
        //   00647F30 66 BA FF 10 mov dx,0x10FF ; 00647F38 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4351(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4351, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 4361 (0x1109) — @0x64013C via [obj+0x250]. Same helper family/shape as
        // SM 4351 (eax=Recog, edx=recipient, same three gates); one of three
        // near-identical helpers that only differ by ident (0x10F4/0x10FC/0x1109).
        //   0064012A 6A00x4 push ; 00640132 8B CE mov ecx,esi (Recog) ; 00640134 66 BA 09 11 mov dx,0x1109
        //   0064013C FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4361(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4361, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 4352 (0x1100) — @0x6E616A via [obj+0x250]. Recog is the sending object
        // itself (native loads ecx=[ebp-4] and eax=[ebp-4]); fully fixed frame.
        //   006E6156 6A00x4 push ; 006E615E 8B 4D FC mov ecx,[ebp-4] (Recog=self)
        //   006E6161 66 BA 00 11 mov dx,0x1100 ; 006E616A FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4352(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4352, recog, 0, 0, 0), Array.Empty<byte>());

        // ---- paired result-code arms (Recog = a computed result, empty body) ----
        // Two functions each pick between two idents by a bool flag; both arms send
        // the same all-0 frame with Recog = the result of a helper (0x7487A8 /
        // 0x748A18). flag==0 -> 4408/4409 ; flag!=0 -> 4410/4411.

        // SM 4408 (0x1138) — @0x6F3897 via [obj+0x250]. Recog=esi (0x7487A8 result).
        //   006F3885 6A00x4 push ; 006F388D 8B CE mov ecx,esi ; 006F388F 66 BA 38 11 mov dx,0x1138
        internal static (ClientPacket Header, byte[] Body) BuildSm4408(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4408, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 4410 (0x113A) — @0x6F387D via [obj+0x250]. Recog=esi (0x7487A8 result);
        // flag!=0 sibling of SM 4408.
        //   006F386B 6A00x4 push ; 006F3873 8B CE mov ecx,esi ; 006F3875 66 BA 3A 11 mov dx,0x113A
        internal static (ClientPacket Header, byte[] Body) BuildSm4410(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4410, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 4409 (0x1139) — @0x6F393A via [obj+0x250]. Recog=esi (0x748A18 result).
        //   006F3928 6A00x4 push ; 006F3930 8B CE mov ecx,esi ; 006F3932 66 BA 39 11 mov dx,0x1139
        internal static (ClientPacket Header, byte[] Body) BuildSm4409(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4409, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 4411 (0x113B) — @0x6F3920 via [obj+0x250]. Recog=esi (0x748A18 result);
        // flag!=0 sibling of SM 4409.
        //   006F390E 6A00x4 push ; 006F3916 8B CE mov ecx,esi ; 006F3918 66 BA 3B 11 mov dx,0x113B
        internal static (ClientPacket Header, byte[] Body) BuildSm4411(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4411, recog, 0, 0, 0), Array.Empty<byte>());

        // ---- other empty-body [obj+0x250] sends ----

        // SM 4446 (0x115E) — @0x6F75EF via [obj+0x250]. Recog=LoWord(0x712BE4
        // result) (native movzx ecx,si); gated on [self+0x192C]!=0. All-0 frame.
        //   006F75DC 6A00x4 push ; 006F75E4 0F B7 CE movzx ecx,si ; 006F75E7 66 BA 5E 11 mov dx,0x115E
        internal static (ClientPacket Header, byte[] Body) BuildSm4446(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4446, HUtil32.LoWord(recog), 0, 0, 0),
                Array.Empty<byte>());

        // SM 4457 (0x1169) — @0x6A8C9F via [obj+0x250]. Param = a byte flag (the dl
        // argument saved at [ebp-1]); Recog=0. Empty body.
        //   006A8C89 33 C0 / 8A 45 FF mov al,[ebp-1] / 50 push (Param) ; 006A8C8F 6A00x3 push
        //   006A8C95 33 C9 xor ecx,ecx (Recog=0) ; 006A8C97 66 BA 69 11 mov dx,0x1169
        internal static (ClientPacket Header, byte[] Body) BuildSm4457(byte param)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4457, 0, param, 0, 0), Array.Empty<byte>());

        // SM 4496 (0x1190) — @0x6FAD1B via [obj+0x250]. Recog=esi, a runtime value:
        // default -1 (006FACB0 or esi,-1) or the id returned by 0x4177C0. All-0.
        //   006FAD09 6A00x4 push ; 006FAD11 8B CE mov ecx,esi ; 006FAD13 66 BA 90 11 mov dx,0x1190
        internal static (ClientPacket Header, byte[] Body) BuildSm4496(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4496, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 4638 (0x121E) — @0x64E832 via [obj+0x250]. Recog=0, all-0; sent to the
        // edx-argument object when it is non-nil.
        //   0064E820 6A00x4 push ; 0064E828 33 C9 xor ecx,ecx (Recog=0) ; 0064E82C 66 BA 1E 12 mov dx,0x121E
        internal static (ClientPacket Header, byte[] Body) BuildSm4638()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4638, 0, 0, 0, 0), Array.Empty<byte>());

        // SM 4649 (0x1229) — @0x6FBB5F via [obj+0x250]. Recog=esi = a 0/1 result flag
        // (esi starts 1, set 0 when 0x69C47C returns true). All-0 frame.
        //   006FBB4D 6A00x4 push ; 006FBB55 8B CE mov ecx,esi ; 006FBB57 66 BA 29 12 mov dx,0x1229
        internal static (ClientPacket Header, byte[] Body) BuildSm4649(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4649, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 4650 (0x122A) — @0x6FB610 via [obj+0x250]. Recog=[ebp-4], a runtime
        // local reached after several SysMsg (cx=0x38FF) validation branches. All-0.
        //   006FB5FD 6A00x4 push ; 006FB605 8B 4D FC mov ecx,[ebp-4] ; 006FB608 66 BA 2A 12 mov dx,0x122A
        internal static (ClientPacket Header, byte[] Body) BuildSm4650(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4650, recog, 0, 0, 0), Array.Empty<byte>());
    }
}

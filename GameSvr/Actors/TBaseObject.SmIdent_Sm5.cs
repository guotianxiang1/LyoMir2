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

        // ---- sMsg (string) bodies: sent through the SendDefMessage string path ----
        // For string-body idents the payload is the sMsg push (4th stack arg); the
        // builder returns (header, text) and the SendSocket(ClientPacket,string) path
        // performs the ANSI/GBK encoding, exactly as the SmA/Sm3 text builders do.

        // SM 4407 (0x1137) — RM arm @0x6B60F2 via [obj+0x250]. sMsg = a local copy of
        // the RM record's message string [rec+0x10] (via 0x405708); Recog = wParam
        // (movzx word[rec+2]); Param/Tag/Series = 0.
        //   006B60C4 6A00x3 push (Param/Tag/Series=0) ; 006B60CA..D6 copy [rec+0x10]->[ebp-0x1C4]
        //   006B60E1 push [ebp-0x1C4] (sMsg) ; 006B60E5 0F B7 48 02 movzx ecx,word[rec+2] (Recog)
        //   006B60E9 66 BA 37 11 mov dx,0x1137 ; 006B60F2 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm4407(ushort wParam, string sMsg)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4407, wParam, 0, 0, 0), sMsg ?? string.Empty);

        // SM 4444 (0x115C) — @0x6FE929 via [obj+0x250], per-target loop. sMsg = the
        // recipient's char name ([tgt+0x106]); Recog=0; Param = byte[rec+4]
        // (movzx byte). Tag/Series = 0.
        //   006FE8FE 0F B6 40 04 movzx eax,byte[rec+4] / 50 push (Param) ; 006FE906 6A00x2 push (Tag/Series)
        //   006FE90A..1B build name string [ebp-0x10] -> push (sMsg) ; 006FE91F 33 C9 xor ecx,ecx (Recog=0)
        //   006FE921 66 BA 5C 11 mov dx,0x115C ; 006FE929 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm4444(byte param, string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4444, 0, param, 0, 0), charName ?? string.Empty);

        // SM 4445 (0x115D) — @0x6FE865 via [obj+0x250], per-target loop. Same shape as
        // SM 4444 (Recog=0, Param=byte[rec+4], sMsg=char name).
        //   006FE83A 0F B6 40 04 movzx eax,byte[rec+4] (Param) ; 006FE85A push name (sMsg)
        //   006FE85B 33 C9 xor ecx,ecx ; 006FE85D 66 BA 5D 11 mov dx,0x115D ; 006FE865 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm4445(byte param, string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4445, 0, param, 0, 0), charName ?? string.Empty);

        // SM 4455 (0x1167) — @0x6A89E0 via [obj+0x250], per-nearby-player loop. sMsg =
        // char name; Recog=0; Param/Tag are two 7-bit stat values (word[ebp-6]/[ebp-8],
        // each an "&0x7F" of a computed stat).
        //   006A89B5 push word[ebp-6] (Param) ; 006A89BA push word[ebp-8] (Tag) ; 006A89BF 6A 00 push (Series)
        //   006A89C1..D2 name -> push (sMsg) ; 006A89D6 33 C9 xor ecx,ecx (Recog=0) ; 006A89D8 mov dx,0x1167
        internal static (ClientPacket Header, string Text) BuildSm4455(int param, int tag, string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4455, 0, param, tag, 0), charName ?? string.Empty);

        // SM 4456 (0x1168) — @0x6A8AAB via [obj+0x250]. sMsg = char name; Recog=0;
        // Param = a 7-bit stat (word[ebp-8]); Tag = a byte (byte[ebp-5]).
        //   006A8A7F push word[ebp-8] (Param) ; 006A8A84 33 C0 / 8A 45 FB mov al,[ebp-5] / push (Tag)
        //   006A8A8A 6A 00 push (Series) ; 006A8A8C..9D name -> push (sMsg) ; 006A8AA1 xor ecx,ecx (Recog=0)
        //   006A8AA3 66 BA 68 11 mov dx,0x1168 ; 006A8AAB FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm4456(int param, byte tag, string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4456, 0, param, tag, 0), charName ?? string.Empty);

        // SM 4458 (0x116A) — @0x6A8D22 via [obj+0x250]. sMsg = char name; Recog=0;
        // Param = a byte flag (byte[ebp-5], the cl argument).
        //   006A8CF9 33 C0 / 8A 45 FB mov al,[ebp-5] / push (Param) ; 006A8CFE 6A00x2 push (Tag/Series)
        //   006A8D03..14 name -> push (sMsg) ; 006A8D18 33 C9 xor ecx,ecx (Recog=0) ; 006A8D1A mov dx,0x116A
        internal static (ClientPacket Header, string Text) BuildSm4458(byte param, string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4458, 0, param, 0, 0), charName ?? string.Empty);

        // SM 4459 (0x116B) — @0x6A8DC2 via [obj+0x250]. Same shape as SM 4458
        // (Recog=0, Param=byte[ebp-5], sMsg=char name).
        //   006A8D99 mov al,[ebp-5] / push (Param) ; 006A8DB4 push name (sMsg) ; 006A8DB8 xor ecx,ecx
        //   006A8DBA 66 BA 6B 11 mov dx,0x116B ; 006A8DC2 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm4459(byte param, string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4459, 0, param, 0, 0), charName ?? string.Empty);

        // SM 4469 (0x1175) — @0x6F788B via [obj+0x250]. All-0 frame; sMsg = char name
        // ([arg+0x106]); sent only when the arg object (esi) is non-nil.
        //   006F7869 6A00x3 push (Param/Tag/Series=0) ; 006F786F..7D name -> push (sMsg)
        //   006F7881 33 C9 xor ecx,ecx (Recog=0) ; 006F7883 66 BA 75 11 mov dx,0x1175 ; 006F788B call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm4469(string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4469, 0, 0, 0, 0), charName ?? string.Empty);

        // SM 4470 (0x1176) — @0x6F78F3 via [obj+0x250]. Same shape as SM 4469 (all-0
        // frame, sMsg = char name [arg+0x106]).
        //   006F78D1 6A00x3 push ; 006F78D7..E5 name -> push (sMsg) ; 006F78E9 xor ecx,ecx
        //   006F78EB 66 BA 76 11 mov dx,0x1176 ; 006F78F3 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm4470(string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4470, 0, 0, 0, 0), charName ?? string.Empty);

        // SM 4499 (0x1193) — @0x6FBD25 via [obj+0x250]. sMsg = esi (a string argument,
        // the ecx arg); Recog = edi (an int argument, the edx arg); Param/Tag/Series=0.
        // Sent only when the string (esi) is non-nil.
        //   006FBD14 6A00x3 push (Param/Tag/Series=0) ; 006FBD1A 56 push esi (sMsg)
        //   006FBD1B 8B CF mov ecx,edi (Recog) ; 006FBD1D 66 BA 93 11 mov dx,0x1193 ; 006FBD25 call [ebx+0x250]
        internal static (ClientPacket Header, string Text) BuildSm4499(int recog, string sMsg)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4499, recog, 0, 0, 0), sMsg ?? string.Empty);

        // SM 4480 (0x1180) — @0x7068AF, broadcast through the member wrapper 0x705954
        // (walks [group+0x30] and re-sends [member+0x250] to each; the same wrapper
        // SM_108 uses). All-0 frame; sMsg = _LStrCatN(3) of three parts. Per the
        // _LStrCatN stack order (first-pushed = leftmost), the parts are the const
        // prefix "与" (@0x7068F4), the variable middle string [ebp-4], and the const
        // suffix "行会的行会战结束" (@0x706900):
        //   00706882 6A00x3 push (Param/Tag/Series=0) ; 00706888 push "与" ; push [ebp-4] ; push "行会…束"
        //   00706895 lea eax,[ebp-0x14]; mov edx,3; call 0x405890 (_LStrCatN) ; 007068A2 push [ebp-0x14] (sMsg)
        //   007068A6 33 C9 xor ecx,ecx (Recog=0) ; 007068A8 66 BA 80 11 mov dx,0x1180 ; 007068AF call 0x705954
        // Const strings decoded from the image (Delphi long strings, GBK / cp936):
        //   0x7068F4 len=2  D3 EB          = "与"
        //   0x706900 len=16 D0 D0 BB E1 B5 C4 D0 D0 BB E1 D5 BD BD E1 CA F8 = "行会的行会战结束"
        // The middle string is the trigger-side guild name (caller-supplied).
        internal const string Sm4480Prefix = "与";
        internal const string Sm4480Suffix = "行会的行会战结束";

        internal static (ClientPacket Header, string Text) BuildSm4480(string guildName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4480, 0, 0, 0, 0),
                Sm4480Prefix + (guildName ?? string.Empty) + Sm4480Suffix);

        // ---- fixed byte[] body ----

        // SM 4614 (0x1206) — @0x70214D, broadcast through the member wrapper 0x7059D0
        // (the [0x254] Buf/Len sibling of 0x705954; walks [group+0x30] and re-sends
        // [member+0x254]). Fully determinate 8-byte body = the two dword ids at
        // &[ebp+8] (the caller's [ebp+8] and [ebp+0xC], compared against [group+0x20]
        // / [group+0x24] just above). Param=0, Tag=0, Series=1, Recog=0.
        //   00702139 6A 00 push 0 (Param) ; 0070213B 6A 00 push 0 (Tag) ; 0070213D 6A 01 push 1 (Series)
        //   0070213F 8D 45 08 lea eax,[ebp+8] / 50 push (Buf) ; 00702143 6A 08 push 8 (Len)
        //   00702145 33 C9 xor ecx,ecx (Recog=0) ; 00702147 66 BA 06 12 mov dx,0x1206 ; 0070214D call 0x7059D0
        internal static (ClientPacket Header, byte[] Body) BuildSm4614(int idA, int idB)
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_4614, 0, 0, 0, 1);
            using var stream = new MemoryStream(8);
            using var writer = new BinaryWriter(stream);
            writer.Write(idA);   // body[0..3] = [ebp+8]
            writer.Write(idB);   // body[4..7] = [ebp+0xC]
            return (header, stream.ToArray());
        }

        // ------------------------------------------------------------------
        // FAIL-CLOSED (batch-5 idents whose body cannot be evaluated at the
        // send slot -> registered in Grobal2.cs, NO builder fabricated).
        //
        //  SM 4363 (0x110B) @0x767160 — send via [vmt+0xE0], a non-standard slot
        //      with a 6-arg stack shape (push 0,0,0,[rec+0x10] Buf, word[rec+0x14]
        //      Len, 1) plus ecx=[esi+4] Recog. Not the SendDefMessage/SendSocket
        //      5-tuple; the +0xE0 virtual's signature is unproven (same class of
        //      block as SM_554). Body/frame not reproducible without inventing.
        //  SM 4441 (0x1159) @0x6FF4D9 [obj+0x254] Buf=[ebp-0x24] Len=0x24 (36) —
        //      locally composed struct: name1 ShortString[15]@0x00, word[src+0x18]
        //      @0x10, byte[src+0x1A]@0x12, name2 ShortString[15]@0x13 (from the
        //      global by-name lookup [0x7D6D50]->0x652784->0x6ADAE4), flag@0x23.
        //      Param=bl(mode 1/2/3), Recog=0. The two ShortString[15] fields are
        //      filled by 0x4039E4 (writes length+chars only), so their tail bytes
        //      are uninitialised stack -> body not byte-exact at the slot.
        //  SM 4442 (0x115A) @0x6FFE30 [obj+0x254] Buf=[ebp-0x16] Len=0x16 (22) —
        //      name SS[15]@0x00, word[src+0x18]@0x10, byte[src+0x1A]@0x12,
        //      byte[src+0x1B]@0x13, online flag@0x14 (by-name lookup), pad@0x15.
        //      Param=bl, Recog=0. SS tail padding + pad@0x15 uninitialised.
        //  SM 4443 (0x115B) @0x700918 [obj+0x254] Buf=[ebp-0x14] Len=0x14 (20) —
        //      name SS[15]@0x00, word[src+0x18]@0x10, online flag@0x12, pad@0x13.
        //      Param=bl, Recog=0. SS tail padding + pad@0x13 uninitialised.
        //  SM 4612 (0x1204) @0x6F781C [obj+0x254] Buf=[ebp-8] Len=[src+8]*0x11 —
        //      a count*17 record array {byte flag; ShortString[15] name} copied by
        //      the loop @0x6F779E from an unmapped source dyn-array (0x424D4C on
        //      esi). Param/Tag/Series=0, Recog=0. Per-record ShortString tail bytes
        //      uninitialised; source container not modeled.
        //  SM 4626 (0x1212) @0x6AE363 [obj+0x254] Buf=[ebp-0x1C] Len=[ebp-0x14]*0x40
        //      — a count*64 record array whose 64-byte elements are filled by the
        //      opaque serializer 0x7060B8 (several ShortString[15] fields via
        //      0x4039E4 + sub-calls 0x70570C/0x70569C). Param=word[ebp-0x1E],
        //      Tag=word[ebp-0xC], Series=word[ebp-0x14], Recog=[ebp-8]. Element
        //      layout not fully resolvable / padding uninitialised.
        //  SM 4646 (0x1226) @0x6FBC4C [obj+0x254] Buf=[ebp-4] Len=[self+0x658] —
        //      a Delphi dynamic array (elements filled by 0x69C57C then assigned via
        //      0x403260 with a large stride); Len is the ELEMENT COUNT, not a byte
        //      length, so the on-wire body size/layout is not resolvable at the slot.
        //  SM 4647 (0x1227) @0x6FB7FF [obj+0x254] Buf=[ebp-0x32] Len=0x18 (24) —
        //      a 24-byte record produced by 0x69C514: ShortString[20]@0x00..0x14,
        //      an unwritten gap byte @0x15, word@0x16. The gap byte (and the
        //      ShortString tail) are uninitialised -> body not byte-exact.
        // ------------------------------------------------------------------
    }
}

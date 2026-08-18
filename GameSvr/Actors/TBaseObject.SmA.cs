using System;
using System.IO;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Isolated builders for SM idents in the 3000-3600 band that native
    /// M2Server sends but C# had not yet reproduced. Every builder returns the
    /// exact wire packet (<see cref="ClientPacket"/> header + body bytes) that
    /// the native send site assembles, with the field mapping proven from the
    /// two send-slot callees (SendDefMessage @0x6D7CB0 / SendSocket @0x6D7BF8).
    ///
    /// Idents are named in <see cref="SmIdentConstsA"/>. These builders are kept
    /// standalone (no existing method body is touched); a later integration pass
    /// wires them into the corresponding dispatch paths.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>
        /// SM 3003 (0xBBB) - YB-deal buyer result. slot 0x250 (SendDefMessage),
        /// header only, no body. Native send site @0x006329A5, verbatim:
        /// <code>
        /// 0063298F  85 F6                 test esi, esi      ; esi = result code
        /// 00632991  7F 18                 jg   0x6329AB      ; result &gt; 0 -&gt; no send
        /// 00632993  6A 00                 push 0             ; Param  = 0
        /// 00632995  6A 00                 push 0             ; Tag    = 0
        /// 00632997  6A 00                 push 0             ; Series = 0
        /// 00632999  6A 00                 push 0             ; sMsg   = nil
        /// 0063299B  8B CE                 mov  ecx, esi      ; Recog  = result
        /// 0063299D  66 BA BB 0B           mov  dx, 0xBBB     ; Ident  = 3003
        /// 006329A1  8B C3                 mov  eax, ebx      ; self
        /// 006329A3  8B 18                 mov  ebx, [eax]
        /// 006329A5  FF 93 50 02 00 00     call [ebx+0x250]   ; SendDefMessage
        /// </code>
        /// The guard at 0x632991 (only non-positive result codes are sent) lives
        /// in the caller and is not part of the packet; this builder assembles the
        /// packet the send instruction emits.
        /// </summary>
        internal static (ClientPacket Header, byte[] Body) BuildSm3003(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3003, recog, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        /// <summary>
        /// SM 3004 (0xBBC) - YB-deal result delivered to a resolved target player.
        /// slot 0x250 (SendDefMessage), header only. Native send site @0x00632BC0:
        /// <code>
        /// 00632BA7  E8 B0 E5 FF FF       call 0x63115C      ; esi = result
        /// 00632BAC  8B F0                mov  esi, eax
        /// 00632BAE  6A 00                push 0             ; Param  = 0
        /// 00632BB0  6A 00                push 0             ; Tag    = 0
        /// 00632BB2  6A 00                push 0             ; Series = 0
        /// 00632BB4  6A 00                push 0             ; sMsg   = nil
        /// 00632BB6  8B CE                mov  ecx, esi      ; Recog  = result
        /// 00632BB8  66 BA BC 0B          mov  dx, 0xBBC     ; Ident  = 3004
        /// 00632BBC  8B C3                mov  eax, ebx      ; self = resolved target
        /// 00632BBE  8B 18                mov  ebx, [eax]
        /// 00632BC0  FF 93 50 02 00 00    call [ebx+0x250]   ; SendDefMessage
        /// </code>
        /// </summary>
        internal static (ClientPacket Header, byte[] Body) BuildSm3004(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3004, recog, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        /// <summary>
        /// SM 3007 (0xBBF) - YB-deal count/value result. slot 0x250
        /// (SendDefMessage), header only. The Recog is the caller's incoming int
        /// (<c>mov [ebp-4], edx</c> @0x00633E28). Native send site @0x00633E84:
        /// <code>
        /// 00633E71  6A 00                push 0             ; Param  = 0
        /// 00633E73  6A 00                push 0             ; Tag    = 0
        /// 00633E75  6A 00                push 0             ; Series = 0
        /// 00633E77  6A 00                push 0             ; sMsg   = nil
        /// 00633E79  8B 4D FC             mov  ecx, [ebp-4]  ; Recog  = incoming value
        /// 00633E7C  66 BA BF 0B          mov  dx, 0xBBF     ; Ident  = 3007
        /// 00633E80  8B C7                mov  eax, edi      ; self = resolved target
        /// 00633E82  8B 30                mov  esi, [eax]
        /// 00633E84  FF 96 50 02 00 00    call [esi+0x250]   ; SendDefMessage
        /// </code>
        /// </summary>
        internal static (ClientPacket Header, byte[] Body) BuildSm3007(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3007, recog, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        /// <summary>
        /// SM 3015 (0xBC7) - YB trade-setting result. slot 0x250 (SendDefMessage),
        /// header only. Recog is the operation result (error path sets esi=-1 at
        /// 0x006E85CE). Native send site @0x006E85FA:
        /// <code>
        /// 006E85E8  6A 00                push 0             ; Param  = 0
        /// 006E85EA  6A 00                push 0             ; Tag    = 0
        /// 006E85EC  6A 00                push 0             ; Series = 0
        /// 006E85EE  6A 00                push 0             ; sMsg   = nil
        /// 006E85F0  8B CE                mov  ecx, esi      ; Recog  = result
        /// 006E85F2  66 BA C7 0B          mov  dx, 0xBC7     ; Ident  = 3015
        /// 006E85F6  8B C3                mov  eax, ebx      ; self
        /// 006E85F8  8B 18                mov  ebx, [eax]
        /// 006E85FA  FF 93 50 02 00 00    call [ebx+0x250]   ; SendDefMessage
        /// </code>
        /// Same numeric value as CM_HEAVYHIT (3015) but opposite direction; this
        /// is the server-&gt;client SM, so it does not clash semantically.
        /// </summary>
        internal static (ClientPacket Header, byte[] Body) BuildSm3015(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3015, recog, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        /// <summary>
        /// SM 3310 (0xCEE) - three substrings concatenated into one string body,
        /// with each substring's ANSI byte length carried in Param/Tag/Series so
        /// the client can re-split. slot 0x250 (SendDefMessage); returns
        /// (header, message). Native function @0x006EB0BC:
        /// <code>
        /// 006EB0E2  8B C6 / E8 .. (0x4057D0=Length) / 50   push Length(esi)      ; -> Param
        /// 006EB0EA  8B C7 / E8 ..                   / 50   push Length(edi)      ; -> Tag
        /// 006EB0F2  8B 45 08 / E8 ..                / 50   push Length([ebp+8])  ; -> Series
        /// 006EB0FB  56                                     push esi              ; str1
        /// 006EB0FC  57                                     push edi              ; str2
        /// 006EB0FD  FF 75 08                               push [ebp+8]          ; str3
        /// 006EB100  8D 45 F8 / BA 03 00 00 00 / E8 ..      lea eax,[ebp-8]; mov edx,3
        ///                                                  call 0x405890         ; _LStrCatN(3): [ebp-8]=str1+str2+str3
        /// 006EB10D  8B 45 F8 / 50                          push [ebp-8]          ; sMsg = concatenation
        /// 006EB111  8B 4D FC                               mov ecx,[ebp-4]       ; Recog
        /// 006EB114  66 BA EE 0C                            mov dx, 0xCEE         ; Ident = 3310
        /// 006EB11C  FF 93 50 02 00 00                      call [ebx+0x250]      ; SendDefMessage
        /// </code>
        /// _LStrCatN (@0x405890) cleans only its <c>count</c>=3 string arguments
        /// (<c>lea esp,[esp+edx*4]</c> @0x405914), so the three Length dwords
        /// pushed first survive on the stack and land in Param/Tag/Series;
        /// SendDefMessage's 4-dword frame (ret 0x10) forces exactly this. The
        /// lengths are Delphi <c>Length()</c> = ANSI byte counts, reproduced here
        /// with <see cref="HUtil32.GetBytes(string)"/> (GBK / codepage 936).
        /// </summary>
        internal static (ClientPacket Header, string Msg) BuildSm3310(
            int recog, string str1, string str2, string str3)
        {
            str1 ??= string.Empty;
            str2 ??= string.Empty;
            str3 ??= string.Empty;
            var param = HUtil32.GetBytes(str1).Length;
            var tag = HUtil32.GetBytes(str2).Length;
            var series = HUtil32.GetBytes(str3).Length;
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3310, recog, param, tag, series);
            return (header, str1 + str2 + str3);
        }

        /// <summary>
        /// SM 3312 (0xCF0) - a two-argument helper (eax = Recog, edx = self,
        /// [ebp+8] = series flag). slot 0x250 (SendDefMessage), header only. The
        /// send site substitutes Series = 4 when the flag argument is 0. Native
        /// function @0x0064F114:
        /// <code>
        /// 0064F11E  8B 55 08             mov  edx, [ebp+8]  ; series flag
        /// 0064F121  85 D2                test edx, edx
        /// 0064F123  75 05                jne  0x64F12A
        /// 0064F125  BA 04 00 00 00       mov  edx, 4        ; default Series = 4
        /// 0064F12A  6A 00                push 0             ; Param  = 0
        /// 0064F12C  6A 00                push 0             ; Tag    = 0
        /// 0064F12E  52                   push edx           ; Series = flag|4
        /// 0064F12F  6A 00                push 0             ; sMsg   = nil
        /// 0064F131  8B CE                mov  ecx, esi      ; Recog  = eax arg
        /// 0064F133  8B C7                mov  eax, edi      ; self   = edx arg
        /// 0064F135  66 BA F0 0C          mov  dx, 0xCF0     ; Ident  = 3312
        /// 0064F139  8B 18                mov  ebx, [eax]
        /// 0064F13B  FF 93 50 02 00 00    call [ebx+0x250]   ; SendDefMessage
        /// </code>
        /// </summary>
        internal static (ClientPacket Header, byte[] Body) BuildSm3312(int recog, int seriesFlag)
        {
            var series = seriesFlag != 0 ? seriesFlag : SmIdentConstsA.SM_3312_DefaultSeries;
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3312, recog, 0, 0, series);
            return (header, Array.Empty<byte>());
        }

        /// <summary>
        /// SM 3340 (0xD0C) - full-dword variant of the visible-entity refresh
        /// record. slot 0x254 (SendSocket), 8-byte body. The same record is also
        /// sent as idents 0xF/0x10/0x11 with word-split fields; this arm sends the
        /// full 32-bit values at record +0xC and +0x8. Native send site @0x006B46DD:
        /// <code>
        /// 006B46AE  8B 43 0C             mov  eax, [ebx+0xC]
        /// 006B46B1  89 45 F0             mov  [ebp-0x10], eax   ; body[0..3] = [ebx+0xC]
        /// 006B46B4  8B 43 08             mov  eax, [ebx+8]
        /// 006B46B7  89 45 F4             mov  [ebp-0xC], eax    ; body[4..7] = [ebx+8]
        /// 006B46BA  66 8B 43 04          mov  ax, [ebx+4]
        /// 006B46BE  50                   push eax               ; Param  = loword[ebx+4]
        /// 006B46BF  8B 43 04             mov  eax, [ebx+4]
        /// 006B46C2  C1 E8 10             shr  eax, 0x10
        /// 006B46C5  50                   push eax               ; Tag    = hiword[ebx+4]
        /// 006B46C6  66 8B 43 02          mov  ax, [ebx+2]
        /// 006B46CA  50                   push eax               ; Series = word[ebx+2]
        /// 006B46CB  8D 45 F0             lea  eax, [ebp-0x10]
        /// 006B46CE  50                   push eax               ; Buf    = &body
        /// 006B46CF  6A 08                push 8                 ; Len    = 8
        /// 006B46D1  8B 4B 24             mov  ecx, [ebx+0x24]    ; Recog  = [ebx+0x24]
        /// 006B46D4  66 BA 0C 0D          mov  dx, 0xD0C          ; Ident  = 3340
        /// 006B46D8  8B 45 FC             mov  eax, [ebp-4]       ; self
        /// 006B46DB  8B 18                mov  ebx, [eax]
        /// 006B46DD  FF 93 54 02 00 00    call [ebx+0x254]        ; SendSocket
        /// </code>
        /// Parameters map to the source record: <paramref name="recog"/>=[ebx+0x24],
        /// <paramref name="packedParamTag"/>=[ebx+4] (low word = Param, high word =
        /// Tag), <paramref name="series"/>=word[ebx+2], and the body dwords
        /// <paramref name="bodyLow"/>=[ebx+0xC], <paramref name="bodyHigh"/>=[ebx+8].
        /// </summary>
        internal static (ClientPacket Header, byte[] Body) BuildSm3340(
            int recog, int packedParamTag, ushort series, int bodyLow, int bodyHigh)
        {
            var param = unchecked((ushort)packedParamTag);
            var tag = unchecked((ushort)(packedParamTag >> 16));
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3340, recog, param, tag, series);

            using var stream = new MemoryStream(SmIdentConstsA.SM_3340_BodyLength);
            using var writer = new BinaryWriter(stream);
            writer.Write(bodyLow);
            writer.Write(bodyHigh);
            return (header, stream.ToArray());
        }

        /// <summary>
        /// SM 3341 (0xD0D) - name notice carrying the player name as the string
        /// body. slot 0x250 (SendDefMessage); the payload is the <c>sMsg</c>
        /// string, so this builder returns (header, message) exactly as
        /// SendDefMessage splits it. Native function @0x0073FBF8:
        /// <code>
        /// 0073FC12  84 C9                test cl, cl            ; flag
        /// 0073FC16  66 BB 02 00          mov  bx, 2             ; flag != 0 -> Param = 2
        /// 0073FC1C  66 BB 01 00          mov  bx, 1             ; flag == 0 -> Param = 1
        /// 0073FC20  8D 45 FC / 8D 97 06 01 00 00 / E8 ...   lea eax,[ebp-4]; lea edx,[edi+0x106]
        ///                                                  call 0x405774  ; [ebp-4] = name copy
        /// 0073FC2E  66 8B 87 78 02 00 00 mov  ax, [edi+0x278]
        /// 0073FC35  53                   push ebx               ; Param  = flag?2:1
        /// 0073FC36  50                   push eax               ; Tag    = word[edi+0x278]
        /// 0073FC37  6A 00                push 0                 ; Series = 0
        /// 0073FC39  8B 45 FC / 50        mov eax,[ebp-4]; push eax ; sMsg = name
        /// 0073FC3D  33 C9                xor  ecx, ecx          ; Recog  = 0
        /// 0073FC3F  66 BA 0D 0D          mov  dx, 0xD0D         ; Ident  = 3341
        /// 0073FC43  8B C6                mov  eax, esi          ; self
        /// 0073FC45  8B 18                mov  ebx, [eax]
        /// 0073FC47  FF 93 50 02 00 00    call [ebx+0x250]       ; SendDefMessage
        /// </code>
        /// <paramref name="flag"/> is the <c>cl</c> argument, <paramref name="word278"/>
        /// is <c>word[player+0x278]</c>, and <paramref name="charName"/> is the
        /// player name at <c>player+0x106</c>.
        /// </summary>
        internal static (ClientPacket Header, string Msg) BuildSm3341(
            bool flag, ushort word278, string charName)
        {
            var param = flag ? 2 : 1;
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3341, 0, param, word278, 0);
            return (header, charName ?? string.Empty);
        }

        /// <summary>
        /// SM 3367 (0xD27) - validated-action result. slot 0x250 (SendDefMessage),
        /// header only. Recog is the validation result; the caller only reaches the
        /// send when it is non-zero (<c>test ebx,ebx / je</c> @0x006E9867). Native
        /// send site @0x006E987E:
        /// <code>
        /// 006E9867  85 DB                test ebx, ebx      ; ebx = result
        /// 006E9869  74 63                je   0x6E98CE      ; 0 -> no send
        /// 006E986B  6A 00                push 0             ; Param  = 0
        /// 006E986D  6A 00                push 0             ; Tag    = 0
        /// 006E986F  6A 00                push 0             ; Series = 0
        /// 006E9871  6A 00                push 0             ; sMsg   = nil
        /// 006E9873  8B CB                mov  ecx, ebx      ; Recog  = result
        /// 006E9875  66 BA 27 0D          mov  dx, 0xD27     ; Ident  = 3367
        /// 006E9879  8B 45 FC             mov  eax, [ebp-4]  ; self
        /// 006E987C  8B 30                mov  esi, [eax]
        /// 006E987E  FF 96 50 02 00 00    call [esi+0x250]   ; SendDefMessage
        /// </code>
        /// </summary>
        internal static (ClientPacket Header, byte[] Body) BuildSm3367(int recog)
        {
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3367, recog, 0, 0, 0);
            return (header, Array.Empty<byte>());
        }

        /// <summary>
        /// SM 3324 (0xCFC) - login state sync, header only. The two native arms
        /// differ only in Series: race 0x36 sends 1, every other race sends 0.
        /// Native send sites @0x007468DB / 0x007468FE:
        /// <code>
        /// 007468B8  80 B8 78 01 00 00 36 cmp byte [eax+0x178],0x36
        /// 007468C1  66 8B 90 10 06 00 00 mov dx,word [eax+0x610] ; Param
        /// 007468C9  6A 00                push 0                ; Tag
        /// 007468CB  6A 01                push 1                ; Series (hero arm)
        /// 007468CD  6A 00                push 0                ; sMsg
        /// 007468CF  8B 88 0C 06 00 00    mov ecx,[eax+0x60C]   ; Recog
        /// 007468D5  66 BA FC 0C          mov dx,0xCFC          ; Ident
        /// 007468DB  FF 93 50 02 00 00    call [ebx+0x250]
        /// 007468E4..007468FE repeats the send with Series=0 for the non-hero arm.
        /// </code>
        /// </summary>
        internal static (ClientPacket Header, string Msg) BuildSm3324(
            int recog, ushort param, bool heroRace)
        {
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3324, recog,
                param, 0, heroRace ? 1 : 0);
            return (header, string.Empty);
        }

        /// <summary>
        /// SM 3325 (0xCFD) - state-0x36 spirit/shape sync notice, the sibling that
        /// the state dispatcher selects when <c>byte[self+0x178] == 0x36</c>
        /// (0x00746A10). slot 0x250 (SendDefMessage); returns (header, message).
        /// Native send site @0x00746A37:
        /// <code>
        /// 00746A10  80 BE 78 01 00 00 36 cmp byte [esi+0x178], 0x36
        /// 00746A17  75 26                jne  0x746A3F        ; else -&gt; SM 3324
        /// 00746A19  66 8B 86 10 06 00 00 mov ax, [esi+0x610]
        /// 00746A20  50                   push eax             ; Param  = word[self+0x610]
        /// 00746A21  6A 00                push 0               ; Tag    = 0
        /// 00746A23  6A 01                push 1               ; Series = 1
        /// 00746A25  8B 45 F8 / 50        mov eax,[ebp-8]; push eax ; sMsg = notice text
        /// 00746A29  8B 8E 0C 06 00 00    mov ecx, [esi+0x60C] ; Recog = [self+0x60C]
        /// 00746A2F  66 BA FD 0C          mov dx, 0xCFD        ; Ident = 3325
        /// 00746A33  8B C6                mov eax, esi         ; self
        /// 00746A35  8B 18                mov ebx, [eax]
        /// 00746A37  FF 93 50 02 00 00    call [ebx+0x250]     ; SendDefMessage
        /// </code>
        /// The source fields <c>self+0x60C</c> / <c>self+0x610</c> are persisted at
        /// record offsets 0x580 / 0x57C and are supplied explicitly to this builder.
        /// </summary>
        internal static (ClientPacket Header, string Msg) BuildSm3325(
            int recog, ushort param, string msg)
        {
            var header = Grobal2.MakeDefaultMsg(SmIdentConstsA.SM_3325, recog, param, 0,
                SmIdentConstsA.SM_3325_Series);
            return (header, msg ?? string.Empty);
        }

        // ------------------------------------------------------------------
        // FAIL-CLOSED (3000-3600 native SM idents whose body layout is not
        // recoverable from evidence alone -> no builder is fabricated).
        //
        //  SM 3283 (0xCD3) @0x6E65BB slot 0x254 - body is the return of an
        //      opaque per-object serializer `call [obj+0x34]` (obj composed from
        //      a 5-slot array via 0x75339C); length = dyn-array Length (0x791F3C).
        //      The +0x34 record layout is not modeled in C#.
        //  SM 3291 (0xCDB) @0x6DA925 slot 0x254 - body is 0x1C (28) raw bytes
        //      copied verbatim from `target+0x554`. That record layout has not
        //      been reversed (see TPlayObject.Base.cs; CM 1280 is still MISSING).
        //  SM 3313 (0xCF1) @0x6EB25A slot 0x254 - body is `call [obj+0x34]`
        //      serializer output (obj from 0x754D40), dyn length; Series = esi.
        //      Same opaque serializer as 3283.
        //  SM 3332 (0xD04) @0x6CBDC0 slot 0x254 - body is `call [obj+0x34]`
        //      serializer output (obj from 0x74C2FC by id=edi), dyn length;
        //      Recog = edi. Same opaque serializer.
        //  SM 3452 (0xD7C) @0x699DBA slot 0x254 - body is a fixed 0x369 (873)
        //      byte aggregate at [ebp-0x371] filled by several opaque VMT calls
        //      ([eax+0x6A]/[eax+0x48]/[eax+0x33]) plus 0x41A65C; the 873-byte
        //      field layout is not recoverable within scope.
        // ------------------------------------------------------------------
    }
}

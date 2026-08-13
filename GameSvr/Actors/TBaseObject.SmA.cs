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
    }
}

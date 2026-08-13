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
    }
}

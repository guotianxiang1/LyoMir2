using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// SM missing-ident batch 4/4 — census rank 71-105 (second-highest quarter of the
    /// 140 class-(c) idents in staging/_sm1_work/reconcile.txt) plus the ascending
    /// 顺延 back-fill through the census tail (rank 106-140) needed to reach a full
    /// batch, because most of rank 71-105 was already handled by earlier passes
    /// (sm-A builders 3003/3004/3007/3310/3312/3325/3340/3341/3367 + fail-closed
    /// 3283/3291/3313/3324/3332/3452; TimedAbility 3554/3555; NativeScriptUiOpen
    /// 4331/4339/4340/4348/4351/4361; NativeYbCredit 3009; m_sm_d relation 4441-4446;
    /// slave-list 4469/4470; social 4612; and CM-tail SM_4034).
    ///
    /// Evidence base: staging/_reunpack_work/flat_image.bin, ImageBase 0x400000
    /// (file offset = VA - 0x400000). Every send point was located with the capstone
    /// send-slot backtrack in _work/smscan.py and reversed byte-for-byte.
    ///
    /// Frame convention (verified against the two send-slot callees and the merged
    /// sm-1/sm-2/sm-A builders):
    ///   call [obj+0x250] SendDefMessage(ret 0x10): push Param; push Tag; push Series;
    ///     push sMsg;                 ecx = Recog, dx = ident. String/empty body.
    ///   call [obj+0x254] SendSocket  (ret 0x14): push Param; push Tag; push Series;
    ///     push Buf; push Len;        ecx = Recog, dx = ident. Binary body (Buf,Len).
    /// C# assembles the header with Grobal2.MakeDefaultMsg(ident, Recog, Param, Tag,
    /// Series); MakeDefaultMsg stores Param/Tag/Series as (ushort) and Recog as int,
    /// so 16-bit native reads (mov ax,[..] / movzx ecx,word[..]) map to ushort/byte
    /// params here and the truncation is faithful. Each builder returns the exact
    /// (Header, Body) the send instruction emits; string-bodied sends return
    /// (Header, string). Trigger-side fields feeding Recog/Param/Tag are taken as
    /// parameters (no shared method body is touched — anti-conflict rule).
    ///
    /// The 10 fail-closed idents of this batch (no builder, constant + gap only) are
    /// documented in the trailing block; their bodies/frames are not evaluable at a
    /// mapped [0x250]/[0x254] slot without inventing bytes.
    /// </summary>
    public partial class TBaseObject
    {
        // ============================================================
        // RM-dispatch forwarding arms (same big dispatcher sm-1 mined for
        // 35/37/70/71/72/73/689/959/1201). Record layout:
        //   [rec+2]=wParam [rec+4]=nParam1 [rec+8]=nParam2 [rec+0xC]=nParam3
        //   [rec+0x24]=BaseObject. Empty body ([0x250], sMsg=nil).
        // ============================================================

        // SM 2969 (0xB99) — send [obj+0x250] @0x6B5F3D.
        //   006B5F23 66 8B 43 04  mov ax,[ebx+4]  /50 push   ; Param  = LoWord(nParam1)
        //   006B5F28 66 8B 43 0C  mov ax,[ebx+0xC]/50 push   ; Tag    = LoWord(nParam3)
        //   006B5F2D 6A 00 / 6A 00                            ; Series = 0 ; sMsg = nil
        //   006B5F31 8B 4B 08     mov ecx,[ebx+8]            ; Recog  = nParam2
        //   006B5F34 66 BA 99 0B  mov dx,0xB99
        //   006B5F3D FF 93 50 02 00 00  call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm2969(
            int recogParam2, ushort loParam1, ushort loParam3)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2969, recogParam2, loParam1, loParam3, 0),
                Array.Empty<byte>());

        // SM 2970 (0xB9A) — send [obj+0x250] @0x6B5F65 (RM-forward arm, empty body).
        //   006B5F48 66 8B 43 04  mov ax,[ebx+4]  /50 push   ; Param  = LoWord(nParam1)
        //   006B5F4D 66 8B 43 08  mov ax,[ebx+8]  /50 push   ; Tag    = LoWord(nParam2)
        //   006B5F52 66 8B 43 0C  mov ax,[ebx+0xC]/50 push   ; Series = LoWord(nParam3)
        //   006B5F57 6A 00                                    ; sMsg   = nil
        //   006B5F59 8B 4B 24     mov ecx,[ebx+0x24]         ; Recog  = BaseObject
        //   006B5F5C 66 BA 9A 0B  mov dx,0xB9A / 006B5F65 FF 93 50 02 00 00 call [ebx+0x250]
        // NOTE: a second 0xB9A site @0x6EB41C uses slot 0x254 with a body built by the
        // opaque per-object serializer `call [obj+0x34]` (0x6EB3EC) whose length is a
        // dyn-array Length (0x791F3C) — the same unmapped serializer sm-A fail-closed
        // for 3283/3313/3332. That variant is a documented gap; only the RM-forward
        // (empty-body) form is reproduced here.
        internal static (ClientPacket Header, byte[] Body) BuildSm2970(
            int recogBaseObject, ushort loParam1, ushort loParam2, ushort loParam3)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_2970, recogBaseObject, loParam1, loParam2, loParam3),
                Array.Empty<byte>());

        // SM 4349 (0x10FD) — send [obj+0x250] @0x6B5215 (RM-forward; nParam2 split).
        //   006B51F6 66 8B 43 02  mov ax,[ebx+2]   /50 push  ; Param  = wParam
        //   006B51FB 66 8B 43 08  mov ax,[ebx+8]   /50 push  ; Tag    = LoWord(nParam2)
        //   006B5200 8B 43 08 / C1 E8 10 / 50                ; Series = HiWord(nParam2)
        //   006B5207 6A 00                                    ; sMsg   = nil
        //   006B5209 8B 4B 04     mov ecx,[ebx+4]            ; Recog  = nParam1
        //   006B520C 66 BA FD 10  mov dx,0x10FD / 006B5215 FF 93 50 02 00 00 call [ebx+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4349(
            int recogParam1, ushort wParam, ushort loParam2, ushort hiParam2)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4349, recogParam1, wParam, loParam2, hiParam2),
                Array.Empty<byte>());

        // SM 4350 (0x10FE) — send [obj+0x250] @0x68980D (identical shape to SM 4349).
        //   00689801 8B 4B 04 mov ecx,[ebx+4] (Recog=nParam1); 00689804 66 BA FE 10 mov dx,0x10FE
        //   Param=wParam[rec+2], Tag=LoWord(nParam2)[rec+8], Series=HiWord(nParam2). Empty body.
        internal static (ClientPacket Header, byte[] Body) BuildSm4350(
            int recogParam1, ushort wParam, ushort loParam2, ushort hiParam2)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4350, recogParam1, wParam, loParam2, hiParam2),
                Array.Empty<byte>());

        // ============================================================
        // Empty-body status/result codes (all [obj+0x250], sMsg=nil).
        // ============================================================

        // SM 4035 (0xFC3) — send [obj+0x250], 12 sites in 0x6BF9xx-0x6BFCxx. All share:
        //   6A 00 (Param=0) / 6A {00|01|02} (Tag) / 6A 00 (Series=0) / 6A 00 (sMsg=nil)
        //   {33 C9 xor ecx / B9 01 00 00 00 mov ecx,1} (Recog=0|1)
        //   66 BA C3 0F mov dx,0xFC3 / FF 93 50 02 00 00 call [obj+0x250]
        //   e.g. @0x6BF9A7 (Tag=1,Recog=1); @0x6BFB24 (Tag=2,Recog=0); @0x6BFBE8 (Tag=0,Recog=0).
        internal static (ClientPacket Header, byte[] Body) BuildSm4035(int recog, ushort tag)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4035, recog, 0, tag, 0), Array.Empty<byte>());

        // SM 4038 (0xFC6) — send [obj+0x250] @0x746D3B / @0x746D56. Empty body, Recog=0,
        // Tag=Series=0; Param is a 0/1 flag off the global [[0x7D6938]] byte:
        //   006D6938!=0 path @0x746D28: 6A 01 push 1 (Param=1) ; 6A 00 x3 ; 33 C9 (Recog=0)
        //   else path    @0x746D43: 6A 00 x4 ; 33 C9 (Param=0)
        //   66 BA C6 0F mov dx,0xFC6 / FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4038(ushort param)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4038, 0, param, 0, 0), Array.Empty<byte>());

        // SM 4070 (0xFE6) — send [obj+0x250] @0x649072. Empty body. Register-call helper
        // func(eax=self→Recog, edx=self-obj, ecx=Param); Tag from the 1st stack arg:
        //   0064905E 51            push ecx           ; Param  = ecx arg (wire word)
        //   0064905F 66 8B 45 08   mov ax,[ebp+8] /50 ; Tag    = word[ebp+8]
        //   00649064 6A 00 / 6A 00                    ; Series = 0 ; sMsg = nil
        //   0064906E 8B CE         mov ecx,esi        ; Recog  = eax arg (esi=eax @0x64905C)
        //   00649068 66 BA E6 0F   mov dx,0xFE6 / 00649072 FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4070(
            int recog, ushort param, ushort tag)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4070, recog, param, tag, 0), Array.Empty<byte>());

        // SM 4117 (0x1015) — send [obj+0x250] @0x6E86D8 / @0x6E86F7 / @0x6E8727. Empty
        // body; header all-zero except Recog = zero-extended word[src+0x608]:
        //   6A 00 x4 (Param=Tag=Series=0 ; sMsg=nil)
        //   0F B7 8B 08 06 00 00  movzx ecx,word[ebx+0x608]  ; Recog = word[self+0x608]
        //   66 BA 15 10 mov dx,0x1015 / FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4117(ushort recog608)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4117, recog608, 0, 0, 0), Array.Empty<byte>());

        // SM 4205 (0x106D) — send [obj+0x250], 4 sites (0x654C3E/0x654F2E/0x654F6D/0x6F023A).
        // Empty body. SMS-auth reply: fail forms push Recog=-1 (or ecx,-1); the success
        // form @0x654F2E carries a validity countdown in Tag:
        //   00654F19 6A 00 / 68 08 07 00 00 push 0x708 / 6A 00 / 6A 00 ; Param=0,Tag=0x708,Series=0
        //   00654F24 33 C9 (Recog=0) ; 66 BA 6D 10 mov dx,0x106D / FF 96 50 02 00 00 call [+0x250]
        // (The external SMS gateway is trigger-side; the SM packet is a plain header.)
        internal static (ClientPacket Header, byte[] Body) BuildSm4205(int recog, ushort tag)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4205, recog, 0, tag, 0), Array.Empty<byte>());

        // SM 4206 (0x106E) — send [obj+0x250] @0x6F0496 (Recog=0) / @0x6F04F7 (Recog=-1).
        //   6A 00 x4 (empty) ; {33 C9 | 83 C9 FF} (Recog = 0 | -1)
        //   66 BA 6E 10 mov dx,0x106E / FF 96 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4206(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4206, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 4352 (0x1100) — send [obj+0x250] @0x6E616A (Param=0) / @0x6E6186 (Param=1).
        //   {6A 00|6A 01} 6A 00 6A 00 6A 00 (Param, Tag=0, Series=0, sMsg=nil)
        //   8B 4D FC  mov ecx,[ebp-4]  ; Recog = [ebp-4] (self object handle)
        //   66 BA 00 11 mov dx,0x1100 / FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm4352(int recog, ushort param)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4352, recog, param, 0, 0), Array.Empty<byte>());

        // ============================================================
        // String-bodied sends ([obj+0x250], 4th arg = sMsg). Return (Header, string):
        // the wire body is the AnsiString bytes the send splits, mirroring sm-2 BuildSm966.
        // ============================================================

        // SM 4455 (0x1167) — name notice, send [obj+0x250] @0x6A89E0. Recog=0:
        //   006A89B5 66 8B 45 FA mov ax,[ebp-6] /50 push ; Param = word[ebp-6]
        //   006A89BA 66 8B 45 F8 mov ax,[ebp-8] /50 push ; Tag   = word[ebp-8]
        //   006A89BF 6A 00                                ; Series = 0
        //   006A89C1 lea eax,[ebp-0xC]; mov edx,[ebp-4]; add edx,0x106; call 0x405774 ; name
        //   006A89D2 8B 45 F4 /50 push [ebp-0xC]          ; sMsg = name[player+0x106]
        //   006A89D6 33 C9 (Recog=0) ; 66 BA 67 11 dx=0x1167 / FF 93 50 02 00 00 call [+0x250]
        internal static (ClientPacket Header, string Msg) BuildSm4455(
            ushort param, ushort tag, string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4455, 0, param, tag, 0), charName ?? string.Empty);

        // SM 4456 (0x1168) — name notice, send [obj+0x250] @0x6A8AAB / @0x6A8AEA. Recog=0:
        //   Param = word[ebp-8] ; Tag = byte[ebp-5] (zero-extended) ; Series = 0
        //   sMsg = name[player+0x106] (lea/add 0x106/call 0x405774) ; 33 C9 (Recog=0)
        //   66 BA 68 11 mov dx,0x1168 / FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, string Msg) BuildSm4456(
            ushort param, byte tag, string charName)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_4456, 0, param, tag, 0), charName ?? string.Empty);

        // ------------------------------------------------------------------
        // FAIL-CLOSED (no builder fabricated; constant + evidence gap only).
        // Each has a real census send point, but its body/frame is not evaluable
        // from a mapped [0x250]/[0x254] slot without inventing bytes.
        //
        //  SM 3412 (0xD54) @0x6EE22C — mov dx,0xD54 then `call [obj+0xE0]`
        //      (0x6EE234), NOT a [0x250]/[0x254] send slot. The [+0xE0] virtual's
        //      frame is outside the two mapped send conventions -> not derivable.
        //  SM 4363 (0x110B) @0x767158 — same: mov dx,0x110B then `call [obj+0xE0]`
        //      (0x767160). Non-slot dispatch -> not derivable.
        //  SM 4032 (0xFC0) @0x746D18 slot 0x254 — Buf=[ebp-8],Len=[ebp-0xC] is a
        //      record from the [[0x7D6014]] table (CM 4125 worker 0x746C34); the
        //      table's 0x2B(43)-byte record format is undefined (matches
        //      NativeCmTailFailClosed.cs CM 4125 note).
        //  SM 4033 (0xFC1) @0x747362/@0x747380 slot 0x254 — Buf=[ebp-0x20],Len=0x20
        //      (32 bytes). The record is the state-0x36 spirit block copied from
        //      [self+0x5A8] (20 bytes @0x74733E) plus a computed dword; that record
        //      layout is not modeled (same unmapped [+0x5A8] family as 4037).
        //  SM 4037 (0xFC5) @0x6B71ED slot 0x254 — Buf=[ebp-0x1C],Len=0x18 (24 bytes)
        //      = dword[self+0x60C] then 20 bytes from [self+0x5A8]. Both fields are
        //      unmapped (matches NativeCmTailFailClosed.cs CM 4128 note).
        //  SM 4480 (0x1180) @0x7068A8 — sent via the group-broadcast wrapper
        //      `call 0x705954` (the same member-walk wrapper sm-1 used for SM 108),
        //      not a direct slot; the per-member Param/text args are set in the
        //      enclosing loop and are not evaluable at this call.
        //  SM 4614 (0x1206) @0x70212E.. — sent via wrapper `call 0x7059D0` with an
        //      8-byte Buf=[ebp+8]/Len=8, Series=1; the wrapper is not a direct
        //      [0x254] slot and its emitted frame is not reversed within scope.
        //  SM 4626 (0x1212) @0x6AE363 slot 0x254 — Buf=[ebp-0x1C],Len=edx is a paged
        //      list buffer (CM 4626 worker 0x6AE260, "分页列表查询"); element layout
        //      not derivable from the send site.
        //  SM 4646 (0x1226) @0x6FBC4C slot 0x254 — Buf=[ebp-4],Len=[esi+0x658]; body
        //      is a list the loop above builds in 0x18(24)-byte elements (prize list,
        //      CM 4646 worker 0x6FBB90); element layout not derivable.
        //  SM 4647 (0x1227) @0x6FB7FF slot 0x254 — Buf=[ebp-0x32],Len=0x18 (24 bytes)
        //      filled by call 0x69C514 (prize pre-check, CM 4647 worker 0x6FB6FC);
        //      the 24-byte record layout is not derivable from the send site.
        // ------------------------------------------------------------------
    }
}

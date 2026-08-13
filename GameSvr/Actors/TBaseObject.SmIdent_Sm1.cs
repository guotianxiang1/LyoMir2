using System;
using SystemModule;

namespace GameSvr
{
    // SM missing-ident batch 1/4 (ascending first quarter).
    //
    // Faithful send builders for native server->client idents that fire through a real send slot
    // in flat_image.bin (ImageBase 0x400000) but had no C# constant/builder. Each builder returns
    // the (ClientPacket Header, byte[] Body) pair exactly as the native site assembles it, mirroring
    // the reference builders BuildTimedAbilityClientState / BuildTimedAbilityListState (SM 3555/3554)
    // in TBaseObject.TimedAbility.cs.
    //
    // Frame convention recovered from the image (verified against SM 1202/1264/3554 exemplars):
    //   call [obj+0x250]:  push Param; push Tag; push Series; push sMsg;        ecx=Recog; dx=ident
    //   call [obj+0x254]:  push Param; push Tag; push Series; push Buf; push Len; ecx=Recog; dx=ident
    // MakeDefaultMsg(msg, Recog, Param, Tag, Series) builds the 12-byte TDefaultMessage header; the
    // trailing body is the sMsg string ([0x250]) or the (Buf,Len) buffer ([0x254]). A nil sMsg / a
    // (nil,0) buffer is an empty body.
    //
    // The trigger side (which RM tag / caller reaches each site, and the object fields feeding
    // Recog/Param) is NOT wired here on purpose: tonight's merge conflicts all come from several
    // agents editing the same dispatcher. These builders take the record/field-derived values as
    // parameters so they can be wired later without touching any shared method body.
    //
    // Idents whose body could not be proven from the image are fail-closed (no builder): see the
    // BLOCKED notes on SM_56, SM_554, SM_1233 in Grobal2.cs.
    public partial class TBaseObject
    {
        // SM 35 (0x23) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B48E7.
        //   006B48CA 66 8B 43 04           mov ax,[rec+4] / 50 push   ; Param  = LoWord(nParam1)
        //   006B48CF 66 8B 43 08           mov ax,[rec+8] / 50 push   ; Tag    = LoWord(nParam2)
        //   006B48D4 66 8B 43 02           mov ax,[rec+2] / 50 push   ; Series = wParam
        //   006B48D9 6A 00                 push 0                      ; sMsg   = nil -> empty body
        //   006B48DB 8B 4B 24              mov ecx,[rec+0x24]          ; Recog  = BaseObject
        //   006B48DE 66 BA 23 00           mov dx,0x23
        //   006B48E7 FF 93 50 02 00 00     call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm35(
            int recogBaseObject, ushort loParam1, ushort loParam2, ushort wParam)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_35, recogBaseObject, loParam1, loParam2, wParam),
                Array.Empty<byte>());

        // SM 37 (0x25) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B4745.
        //   006B4728 66 8B 43 04           mov ax,[rec+4] / 50 push   ; Param  = LoWord(nParam1)
        //   006B472D 66 8B 43 08           mov ax,[rec+8] / 50 push   ; Tag    = LoWord(nParam2)
        //   006B4732 66 8B 43 02           mov ax,[rec+2] / 50 push   ; Series = wParam
        //   006B4737 6A 00                 push 0                      ; sMsg   = nil -> empty body
        //   006B4739 8B 4B 24              mov ecx,[rec+0x24]          ; Recog  = BaseObject
        //   006B473C 66 BA 25 00           mov dx,0x25
        //   006B4745 FF 93 50 02 00 00     call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm37(
            int recogBaseObject, ushort loParam1, ushort loParam2, ushort wParam)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_37, recogBaseObject, loParam1, loParam2, wParam),
                Array.Empty<byte>());

        // SM 66 (0x42) — RM-dispatch forwarding arm, send [obj+0x254] @0x6B5E67 (Buf=nil, Len=0).
        //   006B5E48 66 8B 43 04           mov ax,[rec+4] / 50 push   ; Param  = LoWord(nParam1)
        //   006B5E4D 66 8B 43 08           mov ax,[rec+8] / 50 push   ; Tag    = LoWord(nParam2)
        //   006B5E52 66 8B 43 02           mov ax,[rec+2] / 50 push   ; Series = wParam
        //   006B5E57 6A 00 / 006B5E59 6A 00  push 0 ; push 0          ; Buf=nil ; Len=0 -> empty body
        //   006B5E5B 8B 4B 24              mov ecx,[rec+0x24]          ; Recog  = BaseObject
        //   006B5E5E 66 BA 42 00           mov dx,0x42
        //   006B5E67 FF 93 54 02 00 00     call [obj+0x254]
        internal static (ClientPacket Header, byte[] Body) BuildSm66(
            int recogBaseObject, ushort loParam1, ushort loParam2, ushort wParam)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_66, recogBaseObject, loParam1, loParam2, wParam),
                Array.Empty<byte>());

        // SM 70 (0x46) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B5D95.
        // Same record->frame shape as SM 35: Param=LoWord(nParam1) [rec+4], Tag=LoWord(nParam2)
        // [rec+8], Series=wParam [rec+2], Recog=BaseObject [rec+0x24], sMsg=nil (empty body).
        //   006B5D8C 66 BA 46 00 mov dx,0x46 / 006B5D95 FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm70(
            int recogBaseObject, ushort loParam1, ushort loParam2, ushort wParam)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_70, recogBaseObject, loParam1, loParam2, wParam),
                Array.Empty<byte>());

        // SM 71 (0x47) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B5DC9 (same shape as SM 35).
        //   006B5DC0 66 BA 47 00 mov dx,0x47 / 006B5DC9 FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm71(
            int recogBaseObject, ushort loParam1, ushort loParam2, ushort wParam)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_71, recogBaseObject, loParam1, loParam2, wParam),
                Array.Empty<byte>());

        // SM 72 (0x48) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B5DFD (same shape as SM 35).
        //   006B5DF4 66 BA 48 00 mov dx,0x48 / 006B5DFD FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm72(
            int recogBaseObject, ushort loParam1, ushort loParam2, ushort wParam)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_72, recogBaseObject, loParam1, loParam2, wParam),
                Array.Empty<byte>());

        // SM 73 (0x49) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B5E31 (same shape as SM 35).
        //   006B5E28 66 BA 49 00 mov dx,0x49 / 006B5E31 FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm73(
            int recogBaseObject, ushort loParam1, ushort loParam2, ushort wParam)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_73, recogBaseObject, loParam1, loParam2, wParam),
                Array.Empty<byte>());

        // SM 689 (0x2B1) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B4BA1. This arm reads a
        // different field set than SM 35: Recog is nParam1 [rec+4] and Series is word[rec+0xC].
        //   006B4B84 66 8B 43 02           mov ax,[rec+2]   / 50 push  ; Param  = wParam
        //   006B4B88 66 8B 43 08           mov ax,[rec+8]   / 50 push  ; Tag    = LoWord(nParam2)
        //   006B4B8E 66 8B 43 0C           mov ax,[rec+0xC] / 50 push  ; Series = word[rec+0xC]
        //   006B4B93 6A 00                 push 0                       ; sMsg   = nil -> empty body
        //   006B4B95 8B 4B 04              mov ecx,[rec+4]              ; Recog  = nParam1
        //   006B4B98 66 BA B1 02           mov dx,0x2B1
        //   006B4BA1 FF 93 50 02 00 00     call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm689(
            int recogParam1, ushort wParam, ushort loParam2, ushort series0C)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_689, recogParam1, wParam, loParam2, series0C),
                Array.Empty<byte>());

        // SM 959 (0x3BF) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B5761. Recog and Series
        // are hard 0 at this arm; only Param (wParam) and Tag (LoWord nParam1) come from the record.
        //   006B5748 66 8B 43 02           mov ax,[rec+2] / 50 push    ; Param  = wParam
        //   006B574D 66 8B 43 04           mov ax,[rec+4] / 50 push    ; Tag    = LoWord(nParam1)
        //   006B5752 6A 00 / 006B5754 6A 00  push 0 ; push 0           ; Series = 0 ; sMsg = nil
        //   006B5756 33 C9                 xor ecx,ecx                  ; Recog  = 0
        //   006B5758 66 BA BF 03           mov dx,0x3BF
        //   006B5761 FF 93 50 02 00 00     call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm959(ushort wParam, ushort loParam1)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_959, 0, wParam, loParam1, 0),
                Array.Empty<byte>());

        // SM 1201 (0x4B1) — RM-dispatch forwarding arm, send [obj+0x250] @0x6B4C41 (same field set as
        // SM 689: Recog=nParam1 [rec+4], Param=wParam, Tag=LoWord(nParam2), Series=word[rec+0xC]).
        //   006B4C24 66 8B 43 02 push wParam / 006B4C28 66 8B 43 08 push LoWord(nParam2)
        //   006B4C2E 66 8B 43 0C push word[rec+0xC] / 006B4C33 6A 00 push nil (empty body)
        //   006B4C35 8B 4B 04 mov ecx,[rec+4] (Recog) / 006B4C38 66 BA B1 04 mov dx,0x4B1
        //   006B4C41 FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1201(
            int recogParam1, ushort wParam, ushort loParam2, ushort series0C)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1201, recogParam1, wParam, loParam2, series0C),
                Array.Empty<byte>());

        // SM 539 (0x21B) — send [obj+0x250] @0x638A12 to a player looked up by name
        // ([0x7D6D50] global user manager -> 0x652784 find-by-name). The whole frame is 0 except
        // Recog, which is the caller's int argument (esi):
        //   00638A00 6A 00 6A 00 6A 00 6A 00   push 0 x4   ; Param=Tag=Series=0 ; sMsg=nil (empty body)
        //   00638A08 8B CE                      mov ecx,esi ; Recog = caller int arg
        //   00638A0C 66 BA 1B 02                mov dx,0x21B
        //   00638A12 FF 93 50 02 00 00          call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm539(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_539, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 543 (0x21F) — send [obj+0x250] @0x654BCD. Recog is the hard constant -6 (0xFFFFFFFA);
        // gated on [self+0xF4C]!=0. Empty body.
        //   00654BB8 6A 00 6A 00 6A 00 6A 00   push 0 x4   ; Param=Tag=Series=0 ; sMsg=nil
        //   00654BC0 B9 FA FF FF FF             mov ecx,0xFFFFFFFA ; Recog = -6
        //   00654BC5 66 BA 1F 02                mov dx,0x21F
        //   00654BCD FF 93 50 02 00 00          call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm543()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_543, -6, 0, 0, 0), Array.Empty<byte>());

        // SM 546 (0x222) — send [obj+0x250] @0x6E0B9D. Recog is the hard constant -2 (0xFFFFFFFE);
        // this is the else-branch of a duration handler. Empty body.
        //   006E0B88 6A 00 6A 00 6A 00 6A 00   push 0 x4   ; Param=Tag=Series=0 ; sMsg=nil
        //   006E0B90 B9 FE FF FF FF             mov ecx,0xFFFFFFFE ; Recog = -2
        //   006E0B95 66 BA 22 02                mov dx,0x222
        //   006E0B9D FF 93 50 02 00 00          call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm546()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_546, -2, 0, 0, 0), Array.Empty<byte>());

        // SM 551 (0x227) — send [obj+0x250] @0x786610. Param is the hard constant 2; Recog is the
        // caller-computed value (esi = imul eax,0xE10 @0x7865EE, the time delta added to [self+0xBD0]).
        //   007865FE 6A 02                      push 2      ; Param = 2
        //   00786600 6A 00 6A 00 6A 00          push 0 x3   ; Tag=Series=0 ; sMsg=nil (empty body)
        //   00786606 66 BA 27 02                mov dx,0x227
        //   0078660C 8B CE                      mov ecx,esi ; Recog = computed delta
        //   00786610 FF 93 50 02 00 00          call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm551(int recogDelta)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_551, recogDelta, 2, 0, 0), Array.Empty<byte>());

        // SM 951 (0x3B7) — send [obj+0x250] @0x6CF5EF. Recog is the caller-computed relationship id
        // ([ebp-0xC] = return of 0x600F6C); sent only when that value != 1. Empty body.
        //   006CF5DC 6A 00 6A 00 6A 00 6A 00   push 0 x4   ; Param=Tag=Series=0 ; sMsg=nil
        //   006CF5E4 8B 4D F4                   mov ecx,[ebp-0xC] ; Recog = computed id
        //   006CF5E7 66 BA B7 03                mov dx,0x3B7
        //   006CF5EF FF 93 50 02 00 00          call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm951(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_951, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 965 (0x3C5) — send [obj+0x250] @0x72737B. Recog is [group+0x40] (dword); rest 0.
        // Constant SM_965 already exists in Grobal2.cs. Empty body.
        //   00727368 6A 00 6A 00 6A 00 6A 00   push 0 x4   ; Param=Tag=Series=0 ; sMsg=nil
        //   00727370 8B C8                      mov ecx,eax ; Recog = [ebx+0x40]
        //   00727372 66 BA C5 03                mov dx,0x3C5
        //   0072737B FF 97 50 02 00 00          call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm965(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_965, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 108 (0x6C) — text notice broadcast. The caller (0x705F..) copies the source text to a
        // local, clamped to 80 bytes (min(len,0x50) via 0x40D580), then calls wrapper 0x705954 which
        // walks [self+0x30] and re-sends [player+0x250] to every member. Per member the frame is:
        //   007059A1 66 8B 45 14 push  ; Param  = word[ebp+0x14] (caller word[ebp-2])
        //   007059A6 66 8B 45 10 push  ; Tag    = 0
        //   007059AB 66 8B 45 0C push  ; Series = 0
        //   007059AC 8B 45 08    push  ; sMsg   = the clamped text (body)
        //   007059B0 8B 4D F4    mov ecx,[ebp-0xC] = Recog = 0 (caller xor ecx,ecx @0x705FBE)
        //   007059B3 dx = ident 0x6C ; 007059BB call [player+0x250]
        // The member walk and text source are trigger-side; this builder returns one recipient's
        // packet. Body = the sMsg bytes clamped to the native 80-byte cap.
        internal static (ClientPacket Header, byte[] Body) BuildSm108(ushort param, byte[] textBytes)
        {
            var body = textBytes ?? Array.Empty<byte>();
            if (body.Length > 0x50)
            {
                var clamped = new byte[0x50];
                Array.Copy(body, clamped, 0x50);
                body = clamped;
            }
            return (Grobal2.MakeDefaultMsg(Grobal2.SM_108, 0, param, 0, 0), body);
        }

        // ---- Activity/ranking cluster 0x6F0Exx..0x6F17xx (all [obj+0x250], empty body) ----
        // Each function gates on 0x6F0A24, may AddState via 0x6D3694 (state ids 0x139..0x143) into
        // [self+0x18C8], then sends its SM. The bodies are empty; only the header 5-tuple varies.

        // SM 1250 (0x4E2) — send @0x6F0A18. Recog hard -1, rest 0.
        //   006F0A05 6A00x4 push ; 006F0A0D 83 C9 FF or ecx,-1 (Recog) ; 006F0A10 66 BA E2 04 mov dx,0x4E2
        internal static (ClientPacket Header, byte[] Body) BuildSm1250()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1250, -1, 0, 0, 0), Array.Empty<byte>());

        // SM 1251 (0x4E3) — send @0x6D1666. Param hard 6, Recog=[self+0xA54], rest 0.
        //   006D1650 6A 06 push 6 (Param) ; 006D1658 8B 8E 54 0A 00 00 mov ecx,[self+0xA54] (Recog)
        //   006D165E 66 BA E3 04 mov dx,0x4E3 ; 006D1666 FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1251(int recogA54)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1251, recogA54, 6, 0, 0), Array.Empty<byte>());

        // SM 1252 (0x4E4) — send @0x654C10 (and @0x6D1604). Param hard 4, Recog=[self+0xA50], rest 0.
        //   00654BFA 6A 04 push 4 (Param) ; 00654C02 8B 8E 50 0A 00 00 mov ecx,[self+0xA50] (Recog)
        //   00654C08 66 BA E4 04 mov dx,0x4E4 ; 00654C10 FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1252(int recogA50)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1252, recogA50, 4, 0, 0), Array.Empty<byte>());

        // SM 1253 (0x4E5) — send @0x6F0F1C. Recog hard -2, Param 0; Tag/Series come from one packed
        // dword: Tag=HiWord (0x408D68 = shr eax,0x10), Series=LoWord.
        //   006F0F01 8B 06 mov eax,[rec] / E8 ..(0x408D68=HiWord) / 50 push   ; Tag = HiWord(dword[rec])
        //   006F0F09 66 8B 06 mov ax,word[rec] / 50 push                       ; Series = LoWord
        //   006F0F0D 6A 00 push 0 (sMsg) ; 006F0F0F B9 FE FF FF FF mov ecx,-2 (Recog)
        //   006F0F14 66 BA E5 04 mov dx,0x4E5 ; 006F0F1C FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1253(int packed)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1253, -2, 0,
                    (ushort)(packed >> 16), (ushort)packed),
                Array.Empty<byte>());

        // SM 1254 (0x4E6) — send @0x6F0F72. Recog hard -1, rest 0.
        //   006F0F5F 6A00x4 push ; 006F0F67 83 C9 FF or ecx,-1 ; 006F0F6A 66 BA E6 04 mov dx,0x4E6
        internal static (ClientPacket Header, byte[] Body) BuildSm1254()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1254, -1, 0, 0, 0), Array.Empty<byte>());

        // SM 1255 (0x4E7) — send @0x6F0EAE. Recog hard -1, rest 0.
        //   006F0E9B 6A00x4 push ; 006F0EA3 83 C9 FF or ecx,-1 ; 006F0EA6 66 BA E7 04 mov dx,0x4E7
        internal static (ClientPacket Header, byte[] Body) BuildSm1255()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1255, -1, 0, 0, 0), Array.Empty<byte>());

        // SM 1256 (0x4E8) — send @0x6F101D. Recog = caller int arg (edi=edx), rest 0.
        //   006F100B 6A00x4 push ; 006F1013 8B CF mov ecx,edi (Recog) ; 006F1015 66 BA E8 04 mov dx,0x4E8
        internal static (ClientPacket Header, byte[] Body) BuildSm1256(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1256, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 1257 (0x4E9) — send @0x6F10C8. Recog = [ebp-4] (caller local), rest 0.
        //   006F10B5 6A00x4 push ; 006F10BD 8B 4D FC mov ecx,[ebp-4] ; 006F10C0 66 BA E9 04 mov dx,0x4E9
        internal static (ClientPacket Header, byte[] Body) BuildSm1257(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1257, recog, 0, 0, 0), Array.Empty<byte>());

        // SM 1258 (0x4EA) — send @0x6F15A6. Param = a 0/1 result flag (bl); Recog = [ebp-4]; rest 0.
        //   006F1590 33 C0 / 8A C3 mov al,bl / 50 push  ; Param = flag(0/1)
        //   006F1595 6A 00 6A 00 6A 00 push 0 x3        ; Tag=Series=0 ; sMsg=nil
        //   006F159B 8B 4D FC mov ecx,[ebp-4] (Recog) ; 006F159E 66 BA EA 04 mov dx,0x4EA
        internal static (ClientPacket Header, byte[] Body) BuildSm1258(int recog, byte flag)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1258, recog, flag, 0, 0), Array.Empty<byte>());

        // SM 1259 (0x4EB) — send @0x6F1156. Recog hard -1, rest 0.
        //   006F1143 6A00x4 push ; 006F114B 83 C9 FF or ecx,-1 ; 006F114E 66 BA EB 04 mov dx,0x4EB
        internal static (ClientPacket Header, byte[] Body) BuildSm1259()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1259, -1, 0, 0, 0), Array.Empty<byte>());

        // SM 1260 (0x4EC) — send @0x6F11AE. Recog hard -1, rest 0.
        //   006F119B 6A00x4 push ; 006F11A3 83 C9 FF or ecx,-1 ; 006F11A6 66 BA EC 04 mov dx,0x4EC
        internal static (ClientPacket Header, byte[] Body) BuildSm1260()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1260, -1, 0, 0, 0), Array.Empty<byte>());

        // SM 1261 (0x4ED) — send @0x6F0E56. Recog hard -1, rest 0.
        //   006F0E43 6A00x4 push ; 006F0E4B 83 C9 FF or ecx,-1 ; 006F0E4E 66 BA ED 04 mov dx,0x4ED
        internal static (ClientPacket Header, byte[] Body) BuildSm1261()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1261, -1, 0, 0, 0), Array.Empty<byte>());

        // SM 1262 (0x4EE) — send @0x6F0FCA. Recog hard -1, rest 0.
        //   006F0FB7 6A00x4 push ; 006F0FBF 83 C9 FF or ecx,-1 ; 006F0FC2 66 BA EE 04 mov dx,0x4EE
        internal static (ClientPacket Header, byte[] Body) BuildSm1262()
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1262, -1, 0, 0, 0), Array.Empty<byte>());

        // SM 1263 (0x4EF) — send @0x6F1201. Param hard 5, Recog = caller int arg (edi=edx), rest 0.
        //   006F11EF 6A 05 push 5 (Param) ; 006F11F7 8B CF mov ecx,edi (Recog)
        //   006F11F9 66 BA EF 04 mov dx,0x4EF ; 006F1201 FF 93 50 02 00 00 call [obj+0x250]
        internal static (ClientPacket Header, byte[] Body) BuildSm1263(int recog)
            => (Grobal2.MakeDefaultMsg(Grobal2.SM_1263, recog, 5, 0, 0), Array.Empty<byte>());
    }
}

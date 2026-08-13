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
    }
}

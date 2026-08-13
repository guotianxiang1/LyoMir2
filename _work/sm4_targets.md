# SM missing batch 4/4 (census rank 71-105, second-highest 35) — target list

Authoritative census: `staging/_sm1_work/reconcile.txt` tail — 140 class-(c) idents
(native send-slot backtrack, no C# constant), ascending. My nominal window = rank
71-105. Already-handled entries in-window are skipped and back-filled by ascending
顺延 into rank 106+ so the batch is a full 35 NEW idents that I personally evidence.

Evidence base: `staging/_reunpack_work/flat_image.bin`, ImageBase 0x400000
(offset = VA - 0x400000). Send slots: [obj+0x250]=SendDefMessage (ret 0x10),
[obj+0x254]=SendSocket (ret 0x14). Frame push order Param,Tag,Series,(sMsg|Buf,Len);
ecx=Recog, dx=ident.

## SKIPPED in window rank 71-105 (already implemented / already fail-closed elsewhere)

| ident | rank | where handled |
|------:|-----:|---------------|
| 3003,3004,3007,3310,3312,3325,3340,3341,3367 | 73-87 | sm-A builders (TBaseObject.SmA.cs) |
| 3283,3291,3313,3324,3332,3452 | 77-89 | sm-A fail-closed (TBaseObject.SmA.cs) |
| 3009 | 76 | already sent — TPlayObject.NativeYbCredit.cs:91 MakeDefaultMsg(3009,0,0,0,0) |
| 3554,3555 | 90-91 | TimedAbility.cs BuildTimedAbilityListState/ClientState |
| 4331,4339,4340,4348 | 102-105 | TPlayObject.NativeScriptUiOpen.cs (SM_CLICK_OPEN_* + SendNativeScriptUiOpen) |

## MY 35 (ascending rank; send VA from capstone scan of flat_image.bin)

| # | rank | ident | hex | send VA(s) | slot |
|--:|-----:|------:|-----|-----------|------|
| 1 | 71 | 2969 | 0xB99 | 0x6B5F3D | 250 |
| 2 | 72 | 2970 | 0xB9A | 0x6B5F65 ; 0x6EB41C | 250 ; 254 |
| 3 | 88 | 3412 | 0xD54 | 0x6EE234 (call [obj+0xE0]) | — (non-slot) |
| 4 | 92 | 4032 | 0xFC0 | 0x746D18 | 254 |
| 5 | 93 | 4033 | 0xFC1 | 0x747362 ; 0x747380 | 254 |
| 6 | 94 | 4034 | 0xFC2 | 0x6BF7D7 (+8 more) | 250 |
| 7 | 95 | 4035 | 0xFC3 | 0x6BF9A7 (+11 more) | 250 |
| 8 | 96 | 4037 | 0xFC5 | 0x6B71ED | 254 |
| 9 | 97 | 4038 | 0xFC6 | 0x746D3B ; 0x746D56 | 250 |
| 10 | 98 | 4070 | 0xFE6 | 0x649072 | 250 |
| 11 | 99 | 4117 | 0x1015 | 0x6E86D8 ; 0x6E86F7 ; 0x6E8727 | 250 |
| 12 | 100 | 4205 | 0x106D | 0x654C3E ; 0x654F2E ; 0x654F6D ; 0x6F023A | 250 |
| 13 | 101 | 4206 | 0x106E | 0x6F0496 ; 0x6F04F7 | 250 |
| 14 | 106 | 4349 | 0x10FD | 0x6B5215 | 250 |
| 15 | 107 | 4350 | 0x10FE | 0x68980D | 250 |
| 16 | 109 | 4352 | 0x1100 | 0x6E616A ; 0x6E6186 | 250 |
| 17 | 112 | 4407 | 0x1137 | 0x6B60F2 | 250 |
| 18 | 113 | 4408 | 0x1138 | 0x6F3897 | 250 |
| 19 | 114 | 4409 | 0x1139 | 0x6F393A | 250 |
| 20 | 115 | 4410 | 0x113A | 0x6F387D | 250 |
| 21 | 116 | 4411 | 0x113B | 0x6F3920 | 250 |
| 22 | 117 | 4441 | 0x1159 | 0x6FF4D9 ; 0x6FF50D | 254 ; 250 |
| 23 | 118 | 4442 | 0x115A | 0x6FFE30 ; 0x6FFE5E | 254 ; 250 |
| 24 | 119 | 4443 | 0x115B | 0x700918 ; 0x700946 | 254 ; 250 |
| 25 | 120 | 4444 | 0x115C | 0x6FE929 ; 0x701181 | 250 |
| 26 | 121 | 4445 | 0x115D | 0x6FE865 ; 0x7010C5 | 250 |
| 27 | 122 | 4446 | 0x115E | 0x6F75EF | 250 |
| 28 | 123 | 4455 | 0x1167 | 0x6A89E0 | 250 |
| 29 | 124 | 4456 | 0x1168 | 0x6A8AAB ; 0x6A8AEA | 250 |
| 30 | 125 | 4457 | 0x1169 | 0x6A8C9F | 250 |
| 31 | 126 | 4458 | 0x116A | 0x6A8D22 | 250 |
| 32 | 127 | 4459 | 0x116B | 0x6A8DC2 | 250 |
| 33 | 132 | 4499 | 0x1193 | 0x6FBD25 | 250 |
| 34 | 135 | 4626 | 0x1212 | 0x6AE363 | 254 |
| 35 | 137 | 4646 | 0x1226 | 0x6FBC4C | 254 |

Disposition (build vs fail-closed) is decided per send site during evidence
extraction: build iff the frame 5-tuple AND body are evaluable at the send slot;
otherwise fail-closed (constant + evidence gap only).

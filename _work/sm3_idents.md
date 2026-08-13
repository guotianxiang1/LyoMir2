# SM missing-ident batch 3 (sm-3) — assigned slice: ascending #36..#70

Authoritative census: `staging/_sm1_work/classes.txt` CLASS (c) = 140 native wire-SM
idents that fire through a real send slot in `flat_image.bin` (ImageBase 0x400000)
yet have NO C# constant of any prefix.

Ascending order positions:
- #1..#35  (lowest 35)  = done by sm-1 (SmIdent_Sm1.cs) + sm-2b (SmIdent_Sm2.cs).
  Already-implemented set verified in master 29599421:
  35 37 66 70 71 72 73 108 539 543 546 551 689 917 924 925 951 959 965 966
  1107 1109 1201 1233 1250 1251 1252 1253 1254 1255 1256 1257 1258 1259 1260
  1261 1262 1263  (plus fail-closed 56, 554 registered as BLOCKED constants).
- #36..#70 (THIS SLICE, 35 idents) = 1264 1265 1726 1727 1729 1730 1731 1732 1733
  1734 1735 1736 1737 1738 2812 2813 2815 2830 2843 2850 2865 2878 2880 2881 2885
  2896 2897 2898 2951 2952 2956 2957 2958 2960 2968.
  None of these overlap the already-implemented set — full 35 are mine, no forward
  skip needed.

## Send-point VA + slot + planned disposition (from classes.txt `first=` + capstone)

| # | SM ident | hex | send VA | slot | disposition |
|---|----------|-----|---------|------|-------------|
| 36 | 1264 | 0x4F0 | 0x6F0A73 | [obj+0x250] | BUILD empty  Recog=0 Param=1 |
| 37 | 1265 | 0x4F1 | 0x6F1794 | [obj+0x250] | BUILD empty  Recog=ecx(arg) Param/Tag=word args |
| 38 | 1726 | 0x6BE | 0x6E3273 | [obj+0x250] | BUILD empty  Recog=edi(runtime) |
| 39 | 1727 | 0x6BF | 0x6E343A | [obj+0x250] | BUILD empty  Recog=1 |
| 40 | 1729 | 0x6C1 | 0x613925 | [obj+0x254] | FAIL-CLOSED  local 224B (8x28) container |
| 41 | 1730 | 0x6C2 | 0x6E39BC | [obj+0x250] | BUILD empty  Recog=edx(runtime) |
| 42 | 1731 | 0x6C3 | 0x6E3A0D | [obj+0x250] | BUILD empty  Recog=esi(runtime) |
| 43 | 1732 | 0x6C4 | 0x614AE8 | [obj+0x250] | BUILD empty  all 0 |
| 44 | 1733 | 0x6C5 | 0x6149C7 | [obj+0x250] | BUILD empty  Param=byte[self+0xF2] |
| 45 | 1734 | 0x6C6 | 0x6145F0 | [obj+0x250] | BUILD empty  Param=byte[self+ebx+0xEC] |
| 46 | 1735 | 0x6C7 | 0x61487F | [obj+0x250] | BUILD empty  all 0 |
| 47 | 1736 | 0x6C8 | 0x6144E4 | [obj+0x250] | BUILD empty  Param=byte[self+0xF3] |
| 48 | 1737 | 0x6C9 | 0x61478D | [obj+0x250] | BUILD empty  Recog/Param/Tag/Series=byte[self+0xEC..0xEF] |
| 49 | 1738 | 0x6CA | 0x6152EE | [obj+0x250] | BUILD empty  all 0 |
| 50 | 2812 | 0xAFC | 0x645320 | [obj+0x250] | BUILD string  sMsg=arg4; Recog/Param/Tag=args |
| 51 | 2813 | 0xAFD | 0x6B5D19 | [obj+0x250] | BUILD empty  RM arm Recog=BaseObject |
| 52 | 2815 | 0xAFF | 0x6D4ED7 | [obj+0x250] | BUILD string  sMsg=local; all-0 frame |
| 53 | 2830 | 0xB0E | 0x6B555D | [obj+0x254] | BUILD buf-forward  Buf=[rec+0x10] Len=word[rec+0x14] |
| 54 | 2843 | 0xB1B | 0x6DE6FA | [obj+0x250] | BUILD empty  Recog=6 |
| 55 | 2850 | 0xB22 | 0x6D30B7 | [obj+0x254] | FAIL-CLOSED  local count*20 dyn-array |
| 56 | 2865 | 0xB31 | 0x6E1D39 | [obj+0x250] | BUILD string  sMsg=local; Recog=self |
| 57 | 2878 | 0xB3E | 0x624AC6 | [obj+0x250] | BUILD string  sMsg=local; Recog=id |
| 58 | 2880 | 0xB40 | 0x6E598B | [obj+0x250] | BUILD empty  Recog=[ebp-8](runtime) |
| 59 | 2881 | 0xB41 | 0x6E5E10 | [obj+0x250] | BUILD empty  Param=ebx(runtime) |
| 60 | 2885 | 0xB45 | 0x744EF1 | [obj+0x254] | BUILD struct  20B (5 dwords, layout fully proven) |
| 61 | 2896 | 0xB50 | 0x6B5F8E | [obj+0x254] | BUILD buf-forward  Buf=[rec+0x10] Len=word[rec+0x14] |
| 62 | 2897 | 0xB51 | 0x6B5FC8 | [obj+0x254] | BUILD buf-forward  Buf=[rec+0x10] Len=word[rec+0x14] |
| 63 | 2898 | 0xB52 | 0x6B5FED | [obj+0x250] | BUILD empty  RM arm Recog=BaseObject |
| 64 | 2951 | 0xB87 | 0x6E5376 | [obj+0x250] | BUILD empty  Recog=self; Param/Tag=self fields |
| 65 | 2952 | 0xB88 | 0x6E5567 | [obj+0x250] | BUILD empty  Recog=self; Param=word local |
| 66 | 2956 | 0xB8C | 0x6E6AED | [obj+0x254] | FAIL-CLOSED  local count*24 record array |
| 67 | 2957 | 0xB8D | 0x6E6EE7 | [obj+0x250] | BUILD empty  all 0 |
| 68 | 2958 | 0xB8E | 0x6E6CF6 | [obj+0x250] | BUILD empty  Param=1 |
| 69 | 2960 | 0xB90 | 0x6B5ECE | [obj+0x250] | BUILD string  sMsg=[rec+0x10]; RM arm Recog=BaseObject |
| 70 | 2968 | 0xB98 | 0x6B5F18 | [obj+0x250] | BUILD empty  RM arm Recog=BaseObject Param=nParam1 |

Summary: 32 BUILD, 3 FAIL-CLOSED (1729, 2850, 2956 — local runtime-composed
variable-length record buffers whose bytes are not resolvable at the send slot).

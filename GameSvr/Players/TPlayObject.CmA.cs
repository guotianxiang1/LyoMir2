namespace GameSvr
{
    // CM batch-A: missing handlers from the ascending first quarter of the CM_ opcode
    // space (0..1086, 62 distinct idents). Cross-referenced every first-quarter CM_
    // constant against `case Grobal2.CM_*` in the whole tree; three had no handler:
    // CM_POWERBLOCK (0), CM_SELECTSERVER (104), CM_1005 (1005).
    //
    // All three are DEFAULT (native no-op) in the in-game CM dispatcher sub_6D7D68
    // (the same dispatcher NativeClientBodyLengthGate documents; ROOT 0x6D805F,
    // DEFAULT 0x6DBC2C). Evidence walked from the ident binary-search tree:
    //
    //   0x6D805C  8B 45 CC              mov   eax,[ebp-0x34]          ; TProcessMessage
    //   0x6D805F  0F B7 40 04           movzx eax,word [eax+4]        ; ident
    //   ...root compares 0xCD6 / 0x45C / 0x412 / 0x3F7...
    //   0x6D80A7  3D EC 03 00 00        cmp   eax,0x3EC (1004)
    //   0x6D80AC  7F 5D                 jg    0x6D810B                ; -> 1006..1014 table
    //   0x6D80AE  0F 84 86 0D 00 00     je    0x6D8E3A                ; ident 1004
    //   0x6D80B4  3D 21 02 00 00        cmp   eax,0x221 (545)
    //   0x6D80B9  7F 2B                 jg    0x6D80E6
    //   0x6D80BB  0F 84 F5 3A 00 00     je    0x6DBBB6                ; ident 545
    //   0x6D80C1  83 E8 50              sub   eax,0x50 (80)
    //   0x6D80C4  0F 84 74 0B 00 00     je    0x6D8C3E                ; ident 80
    //   0x6D80CA  48 / 0F 84 8E0B0000   dec/je 0x6D8C5F               ; ident 81
    //   0x6D80D1  48 / 0F 84 940B0000   dec/je 0x6D8C6C               ; ident 82
    //   0x6D80D8  83 E8 7B              sub   eax,0x7B
    //   0x6D80DB  0F 84 C1 0B 00 00     je    0x6D8CA2                ; ident 205
    //   0x6D80E1  E9 46 3B 00 00        jmp   0x6DBC2C                ; DEFAULT (all else)
    //
    // DEFAULT 0x6DBC2C: 33 C0 xor eax,eax (return False) / 5A 59 59 / 64 89 10 restore
    // SEH / E9 D5 00 00 00 jmp 0x6DBD0E (epilogue -> return with no reply, no side
    // effect, no log). i.e. drop the packet.
    //
    // Per §"DEFAULT 汇聚 = 原生 no-op 如实实现" each handler below is an exact no-op.
    // No params/fields/return codes exist to model (fail-closed: nothing fabricated).
    public partial class TPlayObject
    {
        // CM_POWERBLOCK = 0. ident 0 is not > 545 and not == 545 at 0x6D80B4, so it
        // enters the low sub-ladder at 0x6D80C1: sub 0x50 -> 0xFFFFFFB0, dec, dec,
        // sub 0x7B all stay non-zero, so it reaches 0x6D80E1 `E9 46 3B 00 00 jmp
        // 0x6DBC2C`. DEFAULT no-op.
        private void ClientNativeCm0PowerBlock()
        {
        }

        // CM_SELECTSERVER = 104. Same low sub-ladder at 0x6D80C1: sub 0x50 -> 24,
        // dec -> 23, dec -> 22, sub 0x7B -> 0xFFFFFF9B (non-zero), falls to
        // 0x6D80E1 `jmp 0x6DBC2C`. DEFAULT no-op. (104 is the login-gate select-server
        // opcode; the in-game dispatcher drops it. Char/server select proper is served
        // by DBSvr UserSocService, not by this in-game arm.)
        private void ClientNativeCm104SelectServer()
        {
        }

        // CM_1005 = 1005. At 0x6D80A7 `cmp eax,0x3EC (1004)` then 0x6D80AC
        // `7F 5D jg 0x6D810B`. At 0x6D810B `05 12 FC FF FF add eax,-0x3EE` makes
        // eax = ident-1006 = -1 (0xFFFFFFFF); `83 F8 08 cmp eax,8` then
        // `0F 87 13 3B 00 00 ja 0x6DBC2C` is taken (0xFFFFFFFF > 8 unsigned). The
        // jump table at 0x6D8120 (`FF 24 85 20 81 6D 00`) only covers idents
        // 1006..1014, so 1005 is excluded. DEFAULT no-op.
        private void ClientNativeCm1005()
        {
        }
    }
}

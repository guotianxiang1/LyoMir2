# SM missing batch 4/4 — final manifest (census rank 71-105 + 顺延 tail)

Authoritative census: `staging/_sm1_work/reconcile.txt` tail — 140 class-(c) idents
(native send-slot backtrack, no C# constant), ascending. Nominal window = rank
71-105. Because most of rank 71-105 was already handled by earlier passes, the batch
back-fills by ascending 顺延 across the full census tail (rank 71-140); every
genuinely-undone census ident in that range is now handled here (36 total:
26 built + 10 fail-closed). Nothing undone remains in rank 71-140.

Evidence base: `staging/_reunpack_work/flat_image.bin`, ImageBase 0x400000
(offset = VA - 0x400000). Slots: [obj+0x250]=SendDefMessage, [obj+0x254]=SendSocket.
Send VAs from `_work/smscan.py`; per-ident disasm evidence in the builders /
BLOCKED notes in `GameSvr/Actors/TBaseObject.SmIdent_Sm4.cs`.

## SKIPPED — already handled in rank 71-140 (not re-done)

| ident(s) | rank | handled by |
|----------|------|------------|
| 3003 3004 3007 3310 3312 3325 3340 3341 3367 | 73-87 | sm-A builders (TBaseObject.SmA.cs) |
| 3283 3291 3313 3324 3332 3452 | 77-89 | sm-A fail-closed (TBaseObject.SmA.cs) |
| 3009 | 76 | already sent — TPlayObject.NativeYbCredit.cs:91 |
| 3554 3555 | 90-91 | TimedAbility.cs BuildTimedAbilityListState/ClientState |
| 4034 | 94 | already sent — TPlayObject.NativeCmTailProtocol.cs:172 (SM_4034) |
| 4331 4339 4340 4348 4351 4361 | 102-110 | NativeScriptUiOpen.cs SM_CLICK_OPEN_* + SendNativeScriptUiOpen |
| 4441 4442 4443 4444 4445 4446 | 117-122 | m_sm_d constants + deliberate DEFERRED (Grobal2 BLOCKED-D5/D6) |
| 4469 4470 | 128-129 | slave-list NotifyNativeSlaveListChanged (TBaseObject.cs) |
| 4612 | 133 | social NativeSocialLoginPackets (SM_PENDING_NOTICE) |

## BUILT (26) — frame 5-tuple + body evaluable at the send slot

| # | rank | ident | hex | send VA | slot | Recog / Param / Tag / Series | body |
|--:|-----:|------:|-----|---------|------|------------------------------|------|
| 1 | 71 | 2969 | 0xB99 | 0x6B5F3D | 250 | nParam2 / lo(nParam1) / lo(nParam3) / 0 | empty |
| 2 | 72 | 2970 | 0xB9A | 0x6B5F65 | 250 | BaseObject / lo(nParam1) / lo(nParam2) / lo(nParam3) | empty |
| 3 | 95 | 4035 | 0xFC3 | 0x6BF9A7(+11) | 250 | 0\|1 / 0 / 0..2 / 0 | empty |
| 4 | 97 | 4038 | 0xFC6 | 0x746D3B/56 | 250 | 0 / 0\|1 / 0 / 0 | empty |
| 5 | 98 | 4070 | 0xFE6 | 0x649072 | 250 | eaxArg / ecxArg / w[ebp+8] / 0 | empty |
| 6 | 99 | 4117 | 0x1015 | 0x6E86D8(+2) | 250 | word[self+0x608] / 0 / 0 / 0 | empty |
| 7 | 100 | 4205 | 0x106D | 0x654F2E(+3) | 250 | 0\|-1 / 0 / 0\|0x708 / 0 | empty |
| 8 | 101 | 4206 | 0x106E | 0x6F0496/F7 | 250 | 0\|-1 / 0 / 0 / 0 | empty |
| 9 | 106 | 4349 | 0x10FD | 0x6B5215 | 250 | nParam1 / wParam / lo(nParam2) / hi(nParam2) | empty |
| 10 | 107 | 4350 | 0x10FE | 0x68980D | 250 | nParam1 / wParam / lo(nParam2) / hi(nParam2) | empty |
| 11 | 109 | 4352 | 0x1100 | 0x6E616A/86 | 250 | [ebp-4] / 0\|1 / 0 / 0 | empty |
| 12 | 112 | 4407 | 0x1137 | 0x6B60F2 | 250 | wParam / 0 / 0 / 0 | string (rec.text[+0x10]) |
| 13 | 113 | 4408 | 0x1138 | 0x6F3897 | 250 | inlay result / 0 / 0 / 0 | empty |
| 14 | 114 | 4409 | 0x1139 | 0x6F393A | 250 | inlay result / 0 / 0 / 0 | empty |
| 15 | 115 | 4410 | 0x113A | 0x6F387D | 250 | inlay result / 0 / 0 / 0 | empty |
| 16 | 116 | 4411 | 0x113B | 0x6F3920 | 250 | inlay result / 0 / 0 / 0 | empty |
| 17 | 123 | 4455 | 0x1167 | 0x6A89E0 | 250 | 0 / w[ebp-6] / w[ebp-8] / 0 | string (name) |
| 18 | 124 | 4456 | 0x1168 | 0x6A8AAB/EA | 250 | 0 / word / byte / 0 | string (name) |
| 19 | 125 | 4457 | 0x1169 | 0x6A8C9F | 250 | 0 / byte flag / 0 / 0 | empty |
| 20 | 126 | 4458 | 0x116A | 0x6A8D22 | 250 | 0 / byte flag / 0 / 0 | string (name) |
| 21 | 127 | 4459 | 0x116B | 0x6A8DC2 | 250 | 0 / byte flag / 0 / 0 | string (name) |
| 22 | 131 | 4496 | 0x1190 | 0x6FAD1B | 250 | esi result / 0 / 0 / 0 | empty |
| 23 | 132 | 4499 | 0x1193 | 0x6FBD25 | 250 | edxArg / 0 / 0 / 0 | string (ecxArg) |
| 24 | 136 | 4638 | 0x121E | 0x64E832 | 250 | 0 / 0 / 0 / 0 | empty |
| 25 | 139 | 4649 | 0x1229 | 0x6FBB5F | 250 | 0\|1 / 0 / 0 / 0 | empty |
| 26 | 140 | 4650 | 0x122A | 0x6FB610 | 250 | [ebp-4] / 0 / 0 / 0 | empty |

## FAIL-CLOSED (10) — constant + evidence gap only, no builder

| # | rank | ident | hex | send VA | slot | reason (body/frame not evaluable at a mapped slot) |
|--:|-----:|------:|-----|---------|------|----------------------------------------------------|
| 1 | 88 | 3412 | 0xD54 | 0x6EE234 | [obj+0xE0] | non-slot virtual dispatch (like sm-1's SM_554) |
| 2 | 92 | 4032 | 0xFC0 | 0x746D18 | 254 | Buf/Len = [[0x7D6014]] 43-byte table record, format undefined |
| 3 | 93 | 4033 | 0xFC1 | 0x747362 | 254 | 32-byte state-0x36 record from [self+0x5A8], unmapped |
| 4 | 96 | 4037 | 0xFC5 | 0x6B71ED | 254 | 24-byte body [self+0x60C]+[self+0x5A8], unmapped |
| 5 | 111 | 4363 | 0x110B | 0x767160 | [obj+0xE0] | non-slot virtual dispatch |
| 6 | 130 | 4480 | 0x1180 | 0x7068AF | wrapper 0x705954 | group-broadcast wrapper, per-member args in enclosing loop |
| 7 | 134 | 4614 | 0x1206 | 0x70214D | wrapper 0x7059D0 | wrapper send, 8-byte body, wrapper frame not reversed |
| 8 | 135 | 4626 | 0x1212 | 0x6AE363 | 254 | paged-list Buf/Len, element layout undefined |
| 9 | 137 | 4646 | 0x1226 | 0x6FBC4C | 254 | prize-list 0x18-byte elements, layout undefined |
| 10 | 138 | 4647 | 0x1227 | 0x6FB7FF | 254 | 24-byte body from 0x69C514, layout undefined |

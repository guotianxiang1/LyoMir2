# TEAM-08: Group Member Limit Gate

## Issue
The `nGroupMembersMax` config variable exists but is never used. The native protocol implementation correctly uses the hardcoded `NativeGroupMaxMembers = 11` constant everywhere, but an administrator might try to change the config value thinking it would increase the team size limit.

## Native Evidence
战神 discovery_group_channel_20260803.md row #7 documents the hard limit:

```
Native bound: Hard 0xB (11) everywhere in the binary
  6C3534: cmp dword [eax+0x44],0xB / jge  (capacity check, error -5)
  72732A: cmp dword [ebx+0x44],0xB / jge  (insert gate)
  726C32: cmp ecx,0xB / jne              (leader scan)
  727388: cmp esi,0xB / jne              (insert scan)
  727279: cmp ebx,0xB / jne              (broadcast scan)
```

The native binary allocates a fixed 11-slot array and the client has a fixed `11 × 54` byte buffer for member records. Exceeding 11 members would cause:
- Client memory buffer overflow
- Protocol desynchronization
- Undefined behavior

## Current State
- `NativeGroupMaxMembers = 11` constant defined in `TPlayObject.NativeGroupProtocol.cs:8`
- All protocol checks use this constant (lines 186-187, 362-364)
- Legacy handler `ClientAddGroupMember` uses it (line 1426 in `TPlayObject.Operate.cs`)
- Config `nGroupMembersMax = 10` is defined but never referenced in code
- Default value 10 is safe (< 11), but could be misconfigured

## Fix
Added audit tool `NativeGroupMaxMembersCheck` that:
1. Validates `nGroupMembersMax` does not exceed 10 (allowing for the leader in slot 0)
2. Verifies the protocol uses `NativeGroupMaxMembers` constant, not the config
3. Confirms the constant value is 11
4. Ensures legacy handlers also use the constant

## Verdict
SHIELD - No behavior change at default config (10 < 11), but the audit prevents future misconfiguration that would silently fail or cause client crashes.

## Files Modified
- `AuditTools/NativeGroupMaxMembersCheck/Program.cs` (new)
- `AuditTools/NativeGroupMaxMembersCheck/NativeGroupMaxMembersCheck.csproj` (new)
- `LyoMir2.sln` (added audit project)

## Recommendation
The unused `nGroupMembersMax` config could be:
1. Removed entirely (breaking change for existing configs)
2. Left as-is with audit protection (current approach)
3. Used to allow values ≤ 10 with `Math.Min(nGroupMembersMax, 10)` clamping

Current fix takes approach #2: keep the config for compatibility but ensure it can't break the native protocol.

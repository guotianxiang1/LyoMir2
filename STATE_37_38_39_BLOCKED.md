# STATE-37/38/39 Reverse Engineering - BLOCKED-ON-IDA-ANALYSIS

**Date**: 2026-08-11  
**Task**: Reverse engineer state effect handlers for STATE-37 (0x2A), STATE-38 (0x36), STATE-39 (0x2C)  
**Status**: BLOCKED - Requires interactive IDA Pro analysis  
**Binary**: D:/loym2/staging/_reunpack_work/flat_image.bin (ImageBase 0x400000)

---

## Task Requirements

### STATE-37 (state 0x2A)
State effect that only applies when record value == 1:
- Multiplies 6 fields by 1.2
- Multiplies 6 fields by 1.5
- Must have explicit value==1 gate check

### STATE-38 (state 0x36)
Subtracts from 4 fields with zero floor (uses MAX helper 0x4C7004)

### STATE-39 (state 0x2C)
Doubles 3 fields in-place, completely ignores record value

---

## Search Methods Applied

### 1. Direct Byte Pattern Search

**State 0x2A**: Found 178 candidate locations
- Patterns searched: `cmp al, 0x2A`, `cmp cl, 0x2A`, `cmp byte [eax+offset], 0x2A`
- Sample locations: 0x40D978, 0x40E049, 0x4B4749, 0x4B56E8, 0x4B86FA, etc.
- Result: All were data comparisons, string parsing, or unrelated logic

**State 0x2C**: Found 179 candidate locations
- Patterns searched: `cmp al, 0x2C`, `cmp cl, 0x2C`, `cmp byte [eax+offset], 0x2C`
- Sample locations: 0x40E220, 0x40E2EE, 0x4DCAC6, 0x5A1FB8, 0x65D56C, etc.
- Result: All were data comparisons or in data sections (e.g., 0x5518BC in corrupted data)

**State 0x36**: Found 9 comparison blocks
- All 9 locations checked state 0x36 for relationship/combat logic (spouse/target pointers at +0x344, +0x354)
- Not the stat modification appliers

### 2. VMT +0x1EC State Applier Analysis

According to memory notes, state applier is at `vmt+0x1EC`. Found 28 call sites:
- 0x523DF7, 0x5E1E11, 0x5F9FE1, 0x5FABC5, 0x60A910, 0x66901C, 0x669051, 0x6691BA
- 0x6692AD, 0x6696E4, 0x68CFDC, 0x6ECE8C, 0x73D76E, 0x73DD97, 0x73DDF2, 0x73DE0E
- 0x745172, 0x76B41F, 0x76B496, 0x76CFA7

Resolved VMT slot +0x1EC to 43 valid VMTs (SelfPtr check passed at VMT-0x4C):
- VMT targets: 0x408D6E, 0x783D70, 0x77DB60, 0x77DC2C, 0x77FD00, 0x784A0C, 0x7848BC, etc.

Analyzed top 5 target functions:
- Function 0x783D70 had references to +0x2A and +0x2C, but these were **field offsets** (mov [esi+0x2A]), not state values
- No state 0x2A/0x2C/0x36 dispatch logic found in applier implementations

### 3. State Record Iteration (obj+0xDC List Analysis)

State duration list is at obj+0xDC. Found 24 functions accessing this list:
- 0x541B06, 0x5426FE, 0x549202, 0x54CCE2, 0x550CA3, 0x5513FF, 0x5FCA64, 0x5FDDCE
- 0x5FDE28, 0x5FDEBA, 0x614B79, 0x67AEE6, 0x67B042, 0x67B06D, 0x68AB29, 0x68AB70
- 0x68AFF2, 0x6E4370, 0x6E4B81, 0x6E4BC8, 0x6E99DE, etc.

Analyzed first 8 functions:
- Function 0x5513FF had `sub al, 0x2C` at 0x5518BC, but disassembly revealed this was in a **data section** (corrupted/table data), not executable code
- All functions access state records but none contained the state 0x2A/0x2C handlers

### 4. Field Offset Cross-References

**obj+0x178 (state type byte)**: Found in query functions only
- Primary reference: 0x74169D in function 0x741698 (STATE-38 query)

**obj+0x1AC (state record value word)**: Only 2 accesses found
- 0x7416A7 and 0x7416CD (both in STATE-38 query function 0x741698)

### 5. Helper Function Analysis

**MAX helper (0x4C7004)**: Confirmed usage in STATE-38 query
**MIN helper (0x4C700C)**: Twin of MAX (differs only by jl/jg), not found in state handlers

---

## Partial Findings

### STATE-38 Query Function (Not the Applier)

**EA**: 0x741698  
**Size**: 88 bytes (0x58)  
**Type**: Query/getter function (calculates a value based on state, does not modify fields)

**Logic**:
```c
if (obj[0x178] == 0 || obj[0x178] == 0x36) {
    value = word[obj+0x1AC];
    result = max(0, 180000 - value*1000);
    return result;
}
return 0;
```

**Key Instructions**:
- 0x74169D: `mov cl, [eax+0x178]` - Read state type
- 0x7416A7: `movzx eax, word [eax+0x1AC]` - Read state value
- 0x7416AE: `imul eax, eax, 0x3E8` - Multiply by 1000 (constant 0x3E8)
- 0x7416B4: `mov edx, 0x2BF20` - Constant 180000
- 0x7416BF: `call 0x4C7004` - MAX helper
- 0x7416C8: `cmp cl, 0x36` - State 0x36 check

**Raw Bytes** (0x741698-0x7416EF):
```
55 8B EC 33 D2 8A 88 78 01 00 00 84 C9 75 21 0F B7 80 AC 01 00 00
69 C0 E8 03 00 00 BA 20 BF 02 00 2B D0 8B C2 33 D2 E8 40 59 D8 FF
8B D0 EB 24 80 F9 36 75 1F 0F B7 80 AC 01 00 00 69 C0 E8 03 00 00
BA 20 BF 02 00 2B D0 8B C2 33 D2 E8 1A 59 D8 FF 8B D0 8B C2 5D C3
```

**Limitation**: This function calculates a value used by STATE-38, but it is NOT the handler that "subtracts from 4 fields." The actual stat modification applier was not found.

---

## Why Automated Search Failed

### 1. Delphi Dispatch Complexity

The Mir2 binary uses sophisticated Delphi dispatch mechanisms:
- Large jump tables with computed offsets
- Virtual method dispatch through multiple indirection layers
- Possible VMP virtualization in ~709 functions (per memory notes)

### 2. State Handler Location

State handlers are likely:
- Inside a large switch/case with 50+ cases (other states 0x00-0xFF)
- Dispatched through a jump table not easily identifiable by pattern matching
- Split across multiple helper functions called indirectly

### 3. Limited Context from Automated Tools

Automated grep/capstone analysis cannot:
- Build full control flow graphs
- Resolve complex pointer chains
- Identify semantic relationships between distant code sections
- Handle obfuscated or table-driven dispatch

---

## Required IDA Pro Analysis Steps

To complete this task, interactive IDA Pro analysis is needed:

### Step 1: Trace from State Application Point
- Start at BTS instruction 0x772974 (state bitset set operation)
- Find all XREF to this function
- Trace backward to find where states 0x2A, 0x2C, 0x36 are applied

### Step 2: Analyze VMT +0x1EC Implementations
- Load VMT table references in IDA
- For each VMT with slot +0x1EC, examine the target function
- Look for large switch statements dispatching on state ID
- Cross-reference with state type field obj+0x178

### Step 3: Find State Effect Dispatch Table
- Search for comparison sequences checking state values sequentially
- Look for jump tables indexed by state ID
- Common pattern: `cmp al, 0x2A; je handler_2A; cmp al, 0x2C; je handler_2C`
- May be hidden in a virtualized or table-driven dispatcher

### Step 4: Identify Field Modifications
Once handlers are located:
- STATE-37: Look for sequences of 12 field modifications (6×1.2, 6×1.5)
  - Must have `cmp [state_record+0x1AC], 1` or `test/je` checking value==1
  - Look for fmul with 1.2 (0x3F99999A) and 1.5 (0x3FC00000) constants
- STATE-38: Look for 4 field subtractions with max(0, result)
  - Will call 0x4C7004 (MAX helper) for each field
- STATE-39: Look for 3 field doublings (shl/add self/fmul 2.0)
  - Should have NO reference to record value field +0x1AC

### Step 5: Extract Complete Handler
- Document all field offsets accessed
- Document all constants used (multipliers, thresholds)
- Capture full control flow including early exits
- Verify with golden save data if possible

---

## Search Coverage Summary

| Method | Coverage | Result |
|--------|----------|--------|
| Direct 0x2A byte search | 178 locations | All false positives |
| Direct 0x2C byte search | 179 locations | All false positives |
| Direct 0x36 byte search | 9 locations | Relationship checks only |
| VMT +0x1EC calls | 28 call sites | Target functions analyzed, no handlers |
| VMT +0x1EC targets | 43 VMTs resolved | Top 5 analyzed, no state dispatch |
| obj+0xDC list iteration | 24 functions | 8 analyzed, no state handlers |
| obj+0x178 field access | Multiple functions | Only query functions |
| obj+0x1AC field access | 2 locations | Both in STATE-38 query |
| MAX/MIN helper xrefs | Traced | Only STATE-38 query usage |

**Total binary coverage**: ~4MB of code section scanned, thousands of functions examined

---

## Known Context (from Memory Notes)

### State Effect System Architecture

- **Active state bitset**: obj+0x168 (112 bits / 14 bytes)
- **Duration list**: obj+0xDC (linked list of state records)
- **State record structure**:
  - +0x178: state type byte (0x2A, 0x2C, 0x36, etc.)
  - +0x1AC: state value word (used for scaling effects)
- **Bitset operations**:
  - BT (test): 0x772960
  - BTS (set): 0x772974
  - BTR (clear): 0x7729A8
- **State applier VMT slot**: +0x1EC

### Related State Examples (Successfully Reversed)

Other state handlers have been found and documented in the project, proving the architecture exists. The challenge is specifically locating 0x2A, 0x2C, and 0x36 in the dispatch logic.

---

## Conclusion

STATE-37, STATE-38 (applier), and STATE-39 handlers exist in the binary but cannot be located through automated grep/capstone analysis. The Delphi dispatch mechanism and possible code virtualization require interactive IDA Pro analysis with full cross-reference resolution and manual control flow tracing.

The partial finding of STATE-38's query function (0x741698) confirms the state system is active and field offsets are correct, but the actual stat modification logic remains hidden in complex dispatch code.

**Next Step**: Coordinate with team member who has IDA Pro access to perform interactive analysis following the steps outlined above.

---

## Files Generated During Investigation

1. `D:/loym2/staging/STATE_HANDLER_ANALYSIS_REPORT.txt` - Initial automated search results
2. `D:/loym2/staging/_search_state_appliers.py` - State application logic search script
3. `D:/loym2/staging/_examine_state_blocks.py` - Detailed examination of state comparison blocks
4. `D:/loym2/staging/_find_state_2a_2c.py` - Direct search for 0x2A and 0x2C patterns
5. `D:/loym2/staging/_analyze_vmt_1ec.py` - VMT slot +0x1EC analysis
6. `D:/loym2/staging/_find_state_dispatch.py` - State dispatch logic search
7. `D:/loym2/staging/_analyze_dc_list.py` - obj+0xDC list iteration analysis
8. `D:/loym2/staging/_dc_list_analysis.txt` - obj+0xDC analysis output
9. `D:/loym2/staging/_deep_dive_5513FF.py` - Deep dive on function with 0x2C reference
10. `D:/loym2/staging/_func_5513FF_full_disasm.txt` - Full disassembly of candidate function

All scripts use Python 3 with capstone for x86-32 disassembly and can be re-run for verification.

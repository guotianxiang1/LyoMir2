using System;
using System.Runtime.CompilerServices;

namespace GameSvr.StatusEffects
{
    /// <summary>
    /// Native body-state subsystem data structures.
    ///
    /// Specification: D:/loym2/staging/status_effects_spec_20260810.md
    ///
    /// STRUCTURE:
    /// - 112-bit presence bitset at obj+0x168 (14 bytes usable)
    /// - 18-byte state records in singly-linked list at obj+0xDC
    /// - Valid state id range: 0x00..0x6F (112 states)
    ///
    /// CRITICAL FIXES from spec contradictions:
    /// - NO status-time ARRAY (m_wStatusTimeArr is WRONG model)
    /// - Element is ONE BIT, not a word
    /// - Duration lives in heap record, not parallel array slot
    /// - Expiry is COUNTDOWN (ms subtract), not absolute deadline
    /// </summary>
    public class NativeBodyState
    {
        // §1.1 The presence bitset - obj+0x168, 112 bits = 14 bytes
        // EA: 0x772968 bt dword ptr [eax + 0x168], edx
        private readonly uint[] _presenceBits = new uint[4]; // 4 x 32 = 128 bits (14 usable)

        // §1.2 The state record list - obj+0xDC
        // EA: 0x773170 mov edx, dword ptr [esi + 0xdc]
        private StateRecord _headRecord = null;

        // §3.2 Expiry walker interval gate - obj+0xE0
        // EA: 0x772FE4 sub eax, dword ptr [ebx + 0xe0]
        private uint _lastWalkTick = 0;

        /// <summary>
        /// State record node - 18 bytes (allocator 0x764E00)
        /// </summary>
        private class StateRecord
        {
            // +0x00: byte - caller flag (stack param [ebp+8])
            // EA: 0x77310F mov byte ptr [eax], dl
            public byte CallerFlag;

            // +0x01: byte - state id
            // EA: 0x773164 mov byte ptr [eax+1], bl
            public byte StateId;

            // +0x02: dword - remaining ms; -1 = permanent
            // EA: 0x77315E mov dword ptr [eax+2], edx
            public int RemainingMs;

            // +0x06: dword - last-serviced tick
            // EA: 0x7731B3 mov dword ptr [edx+6], eax
            public uint LastServicedTick;

            // +0x0A: dword - value / level
            // EA: 0x77316A mov dword ptr [eax+0xA], edi
            public int Value;

            // +0x0E: dword - next pointer
            // EA: 0x773176 mov dword ptr [eax+0xE], edx
            public StateRecord Next;
        }

        /// <summary>
        /// HasState - EA 0x772960
        ///
        /// Raw bytes:
        /// 772960  80FA6F           cmp   dl, 0x6f
        /// 772963  770A             ja    0x77296f    ; out of range -> skip bt
        /// 772965  83E27F           and   edx, 0x7f
        /// 772968  0FA39068010000   bt    dword ptr [eax + 0x168], edx
        /// 77296F  0F92C0           setb  al
        /// 772972  C3               ret
        ///
        /// Valid range: 0x00..0x6F (112 states)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasState(byte stateId)
        {
            // Cap check: dl > 0x6F skips the bt -> unspecified result
            // Native returns unspecified CF for out-of-range, we return false
            if (stateId > 0x6F)
                return false;

            // Mask to 7 bits (and edx, 0x7f)
            int bitIndex = stateId & 0x7F;

            // bt instruction: test bit at index
            int dwordIndex = bitIndex >> 5;  // div 32
            int bitOffset = bitIndex & 0x1F; // mod 32

            return (_presenceBits[dwordIndex] & (1u << bitOffset)) != 0;
        }

        /// <summary>
        /// SetState - EA 0x772974
        ///
        /// Raw bytes:
        /// 772974  8BC6             mov   eax, esi
        /// 772976  8BCB             mov   ecx, ebx
        /// 772978  8A4B01           mov   cl, byte ptr [ebx + 1]
        /// 77297B  8A01             mov   al, byte ptr [ecx]
        /// 77297D  84C0             test  al, al
        /// 77297F  7404             je    0x772987
        /// 772981  2C2D             sub   al, 0x2d
        /// 772983  750C             jne   0x772993
        /// 772987  33D2             xor   edx, edx
        /// 772989  8BC6             mov   eax, esi
        /// 77298B  8B08             mov   ecx, dword ptr [eax]
        /// 77298D  FF91D8010000     call  dword ptr [ecx + 0x1d8]  ; pre-hook for 0 or 0x2D
        /// 772993  80FB6F           cmp   bl, 0x6f
        /// 772996  770A             ja    0x7729a2
        /// 772998  83E37F           and   ebx, 0x7f
        /// 77299B  0FAB9E68010000   bts   dword ptr [esi + 0x168], ebx
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetState(byte stateId)
        {
            // Cap check: hard no-op above 0x6F
            if (stateId > 0x6F)
                return;

            // Mask to 7 bits
            int bitIndex = stateId & 0x7F;

            // bts instruction: set bit at index
            int dwordIndex = bitIndex >> 5;
            int bitOffset = bitIndex & 0x1F;

            _presenceBits[dwordIndex] |= (1u << bitOffset);
        }

        /// <summary>
        /// ClearState - EA 0x7729A8
        ///
        /// Raw bytes:
        /// 7729A8  8BC6             mov   eax, esi
        /// 7729AA  8A4301           mov   al, byte ptr [ebx + 1]
        /// 7729AD  80FB6F           cmp   bl, 0x6f
        /// 7729B0  770A             ja    0x7729bc
        /// 7729B2  83E37F           and   ebx, 0x7f
        /// 7729B5  0FB39E68010000   btr   dword ptr [esi + 0x168], ebx
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearState(byte stateId)
        {
            // Cap check: hard no-op above 0x6F
            if (stateId > 0x6F)
                return;

            // Mask to 7 bits
            int bitIndex = stateId & 0x7F;

            // btr instruction: clear bit at index
            int dwordIndex = bitIndex >> 5;
            int bitOffset = bitIndex & 0x1F;

            _presenceBits[dwordIndex] &= ~(1u << bitOffset);
        }

        /// <summary>
        /// Get the raw 14-byte bitset for protocol message 657 (SM_CHARSTATUSCHANGED)
        ///
        /// §27: sub_7729C4 sends only the 16-byte bitset + word[obj+0x274]
        /// EA: 0x773194 算出的每状态秒数被丢弃
        /// </summary>
        public void GetPresenceBitset(byte[] buffer, int offset)
        {
            if (buffer == null || buffer.Length < offset + 14)
                throw new ArgumentException("Buffer too small for 14-byte bitset");

            // Copy 14 bytes (112 bits)
            Buffer.BlockCopy(_presenceBits, 0, buffer, offset, 14);
        }

        /// <summary>
        /// FindRecord by state id - EA 0x773B98
        ///
        /// Raw bytes:
        /// 773B98  8B82DC000000     mov  eax, dword ptr [edx + 0xdc]  ; head
        /// 773B9E  85C0             test eax, eax
        /// 773BA0  7412             je   0x773bb4                     ; null -> not found
        /// 773BA2  8A4001           mov  al, byte ptr [eax + 1]        ; state id
        /// 773BA5  3AC1             cmp  al, cl
        /// 773BA7  7407             je   0x773bb0                     ; found
        /// 773BA9  8B400E           mov  eax, dword ptr [eax + 0xe]    ; next
        /// 773BAC  85C0             test eax, eax
        /// 773BAE  75F2             jne  0x773ba2                     ; loop
        /// 773BB0  C3               ret                               ; eax = record or null
        /// </summary>
        private StateRecord FindRecord(byte stateId)
        {
            StateRecord current = _headRecord;
            while (current != null)
            {
                if (current.StateId == stateId)
                    return current;
                current = current.Next;
            }
            return null;
        }

        /// <summary>
        /// ApplyState - vmt+0x1EC = EA 0x7730D0, ret 8
        ///
        /// Signature (Delphi register convention):
        ///   eax      = Self
        ///   dl       = stateId
        ///   ecx      = durationMs      ; -1 = PERMANENT
        ///   [ebp+8]  = byte  -> record[+0x00] (callerFlag)
        ///   [ebp+0xC]= dword -> record[+0x0A] (value/level)
        ///
        /// Flow from §3.1:
        /// 1. Call gate vmt+0x1E8; false aborts silently
        /// 2. Find existing record
        /// 3. If exists:
        ///    - Overwrite callerFlag unconditionally
        ///    - If new value > old: write duration & value, fire StateGained
        ///    - Else if new value == old: refresh duration only if longer
        /// 4. If not exists: alloc, link at head, fire StateGained
        /// 5. Notify client + timestamp
        ///
        /// CRITICAL NATIVE BEHAVIOR (not a C# bug):
        /// When new value > old, duration is written UNCONDITIONALLY at 0x773121
        /// -> stronger-but-shorter application SHORTENS remaining time
        /// Only equal-value path is monotonic (longer duration only)
        /// </summary>
        public void ApplyState(byte stateId, int durationMs, byte callerFlag, int value,
            System.Func<byte, bool> applyGate, System.Action<byte, int> stateGainedCallback,
            System.Action<byte, int> notifyClientCallback, System.Func<uint> getTickCount)
        {
            // Step 1: Gate check (vmt+0x1E8)
            // EA: 0x7730E9 call dword ptr [ecx+0x1E8]
            // EA: 0x7730F3 test al,al / je 0x7731B6  -> abort if false
            if (applyGate != null && !applyGate(stateId))
                return; // Silent abort

            // Step 2: Find existing record
            // EA: 0x7730FB call 0x773B98
            StateRecord existing = FindRecord(stateId);

            if (existing != null)
            {
                // Step 3: Existing record path
                // EA: 0x77310F mov byte ptr [eax], dl  - overwrite callerFlag unconditionally
                existing.CallerFlag = callerFlag;

                int oldValue = existing.Value;
                // EA: 0x773114 mov eax, dword ptr [eax+0xA]
                // EA: 0x773117 cmp edi, eax

                if (value > oldValue)
                {
                    // New value GREATER: write both duration and value, fire StateGained
                    // EA: 0x773119 jle 0x773136
                    // EA: 0x773121 mov [eax+2],edx  - duration
                    // EA: 0x773127 mov [eax+0xA],edi - value
                    existing.RemainingMs = durationMs;
                    existing.Value = value;

                    // Fire StateGained (vmt+0x60)
                    // EA: 0x77312B call dword ptr [ecx+0x60]
                    stateGainedCallback?.Invoke(stateId, value);
                }
                else if (value == oldValue)
                {
                    // Equal value: refresh duration only if LONGER (monotonic)
                    // EA: 0x773136 cmp edi,eax / 0x773138 jne skip
                    // EA: 0x77313D mov eax,[eax+2] / 0x773140 cmp eax,[ebp-4]
                    // EA: 0x773143 jge skip
                    if (durationMs > existing.RemainingMs)
                    {
                        // EA: 0x77314B mov [eax+2],edx
                        existing.RemainingMs = durationMs;
                    }
                }
                // else: new value < old -> NO UPDATE (native behavior)

                // Update timestamp for notification path
                // EA: 0x7731B3 mov dword ptr [edx+6], eax
                existing.LastServicedTick = getTickCount();
            }
            else
            {
                // Step 4: New record path
                // EA: 0x773150+ alloc via 0x764E00
                var newRecord = new StateRecord
                {
                    CallerFlag = callerFlag,
                    StateId = stateId,
                    RemainingMs = durationMs,
                    Value = value,
                    LastServicedTick = getTickCount(),
                    Next = _headRecord  // EA: 0x773176 mov dword ptr [eax+0xE], edx
                };

                // Link at head
                // EA: 0x77317C mov dword ptr [esi+0xdc], eax
                _headRecord = newRecord;

                // Set the bit
                SetState(stateId);

                // Fire StateGained (vmt+0x60)
                // EA: 0x773189 call dword ptr [ecx+0x60]
                stateGainedCallback?.Invoke(stateId, value);
            }

            // Step 5: Notify client
            // EA: 0x77318C+
            // 7731A1  8A5001           mov  dl, byte ptr [eax + 1]    ; state id
            // 7731A8  FF5314           call dword ptr [ebx + 0x14]    ; notify(state, seconds, 1)
            if (notifyClientCallback != null)
            {
                // Convert ms to seconds via SIGNED idiv
                // EA: 0x773191 mov eax, dword ptr [eax + 2]  ; remaining ms
                // EA: 0x773194 B9E8030000       mov ecx, 0x3e8
                // EA: 0x773199 99               cdq
                // EA: 0x77319A F7F9             idiv ecx
                // Permanent (-1) notifies seconds = 0
                int seconds = durationMs == -1 ? 0 : durationMs / 1000;
                notifyClientCallback(stateId, seconds);
            }
        }

        /// <summary>
        /// RemoveState by id - EA 0x7731C0
        ///
        /// Signature: eax=Self, dl=state -> al = removed?
        ///
        /// Flow from §4.3:
        /// 1. HasState guard - return false if not present
        /// 2. ClearState (clear the bit)
        /// 3. Walk obj+0xDC comparing byte[node+1]
        /// 4. Unlink (head case or mid case)
        /// 5. Fire vmt+0x5C (StateLost)
        /// 6. Free node
        /// 7. Return true
        ///
        /// Raw bytes:
        /// 7731D8  E883F7FFFF       call 0x772960             ; HasState
        /// 7731F2  E8B1F7FFFF       call 0x7729a8             ; ClearState
        /// 7731FE  3A5D01           cmp  bl, byte ptr [eax+1] ; compare state id
        /// 773211  895108           mov  dword ptr [ecx+8], edx ; mid unlink: prev->next = cur->next
        /// 77321B  8996DC000000     mov  dword ptr [esi+0xdc], edx ; head unlink
        /// 773227  FF575C           call dword ptr [edi+0x5c]  ; StateLost
        /// 77322C  E8DFD1C8FF       call 0x764e10             ; free
        /// </summary>
        public bool RemoveState(byte stateId, System.Action<byte> stateLostCallback)
        {
            // Step 1: HasState guard
            // EA: 0x7731D8 call 0x772960
            if (!HasState(stateId))
                return false;

            // Step 2: ClearState (clear the bit)
            // EA: 0x7731F2 call 0x7729a8
            ClearState(stateId);

            // Step 3-4: Walk list and unlink
            // EA: 0x7731FE cmp bl, byte ptr [eax+1]
            StateRecord prev = null;
            StateRecord current = _headRecord;

            while (current != null)
            {
                if (current.StateId == stateId)
                {
                    // Found: unlink
                    if (prev == null)
                    {
                        // Head case
                        // EA: 0x77321B mov dword ptr [esi+0xdc], edx
                        _headRecord = current.Next;
                    }
                    else
                    {
                        // Mid case
                        // EA: 0x773211 mov dword ptr [ecx+8], edx
                        // Note: +8 in the disasm is relative to a different base register
                        prev.Next = current.Next;
                    }

                    // Step 5: Fire StateLost
                    // EA: 0x773227 call dword ptr [edi+0x5c]
                    stateLostCallback?.Invoke(stateId);

                    // Step 6: Free (C# GC handles this, no explicit free needed)
                    // EA: 0x77322C call 0x764e10

                    // Step 7: Return true
                    return true;
                }

                prev = current;
                current = current.Next;
            }

            // Not found in list (bit was set but no record - shouldn't happen)
            return false;
        }

        /// <summary>
        /// GetStateLevel - EA 0x773BEC
        ///
        /// Returns record[+0x0A] (value/level), or 0 if no record
        ///
        /// Raw bytes:
        /// 773BEC  8BC1             mov  eax, ecx
        /// 773BF3  E8A0FFFFFF       call 0x773b98             ; FindRecord
        /// 773BF8  85C0             test eax, eax
        /// 773BFA  7411             je   0x773c0d             ; null -> return 0
        /// 773BFC  8B400A           mov  eax, dword ptr [eax + 0xa]
        /// 773C10  33C0             xor  eax, eax             ; null path
        ///
        /// Used for: states 0x10(×2), 0x1E(×4), 0x2E, 0x2F, 0x39, 0x47(×2), 0x4B, 0x4C, 0x4D
        /// </summary>
        public int GetStateLevel(byte stateId)
        {
            StateRecord record = FindRecord(stateId);
            if (record == null)
                return 0;

            // EA: 0x773BFC mov eax, dword ptr [eax + 0xa]
            return record.Value;
        }

        /// <summary>
        /// Get remaining duration in milliseconds for a state
        /// Returns -1 for permanent, 0 if not found
        /// </summary>
        public int GetStateRemainingMs(byte stateId)
        {
            StateRecord record = FindRecord(stateId);
            if (record == null)
                return 0;

            return record.RemainingMs;
        }

        /// <summary>
        /// Count total active states (for debugging/auditing)
        /// </summary>
        public int GetActiveStateCount()
        {
            int count = 0;
            StateRecord current = _headRecord;
            while (current != null)
            {
                count++;
                current = current.Next;
            }
            return count;
        }

        /// <summary>
        /// Countdown per-record expiry test - vmt+0x58 = EA 0x7730AC
        ///
        /// Returns true if expired (should be removed)
        ///
        /// Raw bytes from §3.3:
        /// 7730AC  53               push ebx
        /// 7730AD  33DB             xor  ebx, ebx                    ; result = false
        /// 7730AF  8BC2             mov  eax, edx                    ; eax = record
        /// 7730B1  837802FF         cmp  dword ptr [eax + 2], -1      ; permanent?
        /// 7730B5  7413             je   0x7730ca                    ; -> never expires
        /// 7730B7  8BD1             mov  edx, ecx                    ; edx = now
        /// 7730B9  2B5006           sub  edx, dword ptr [eax + 6]     ; edx = now - lastTick
        /// 7730BC  295002           sub  dword ptr [eax + 2], edx     ; remaining -= elapsed
        /// 7730BF  894806           mov  dword ptr [eax + 6], ecx     ; lastTick = now
        /// 7730C2  83780200         cmp  dword ptr [eax + 2], 0
        /// 7730C6  7F02             jg   0x7730ca                    ; remaining > 0 -> alive
        /// 7730C8  B301             mov  bl, 1                       ; expired
        /// 7730CA  8BC3             mov  eax, ebx
        /// 7730CD  C3               ret
        ///
        /// RULING: COUNTDOWN (not absolute deadline)
        /// - record[+0x02] is remaining milliseconds, decremented by plain integer subtract
        /// - NO fdiv 86400.0, NO fadd of a date
        /// - Expiry test is <= 0 (signed jg taken means alive)
        /// - -1 short-circuits before any arithmetic (permanent never expires)
        /// </summary>
        private bool TestExpiry(StateRecord record, uint nowTick)
        {
            // Permanent state never expires
            // EA: 0x7730B1 cmp dword ptr [eax + 2], -1
            // EA: 0x7730B5 je 0x7730ca
            if (record.RemainingMs == -1)
                return false;

            // Calculate elapsed time
            // EA: 0x7730B9 sub edx, dword ptr [eax + 6]
            uint elapsed = nowTick - record.LastServicedTick;

            // Decrement remaining time
            // EA: 0x7730BC sub dword ptr [eax + 2], edx
            record.RemainingMs -= (int)elapsed;

            // Update last serviced tick
            // EA: 0x7730BF mov dword ptr [eax + 6], ecx
            record.LastServicedTick = nowTick;

            // Check if expired (remaining <= 0)
            // EA: 0x7730C2 cmp dword ptr [eax + 2], 0
            // EA: 0x7730C6 jg 0x7730ca  (alive if remaining > 0)
            return record.RemainingMs <= 0;
        }

        /// <summary>
        /// Expiry walker - EA 0x772FD0
        ///
        /// Interval and comparison from §3.2:
        /// 772FDB  E86053C9FF       call 0x408340                    ; now
        /// 772FE4  2B83E0000000     sub  eax, dword ptr [ebx + 0xe0] ; now - lastWalkTick
        /// 772FEA  3DF4010000       cmp  eax, 0x1f4                  ; 500
        /// 772FEF  0F82AF000000     jb   0x7730a4                    ; skip if BELOW 500
        /// 772FF5  89B3E0000000     mov  dword ptr [ebx + 0xe0], esi ; latch = now
        ///
        /// CRITICAL CONTRACT:
        /// - Interval constant = 0x1F4 = 500 ms
        /// - Branch is jb (below), so body runs when elapsed >= 500
        /// - Greater-OR-EQUAL, NOT strictly-greater
        /// - Latch is hard reset to now (mov, not add 500)
        /// - Residue discarded, cannot catch up after stall
        /// - Effective period is >= 500 ms, drifting with tick jitter
        ///
        /// Walk is two-phase:
        /// 1. Walk list, call vmt+0x58 per record, collect expired nodes
        /// 2. For each expired: fire vmt+0x5C (StateLost), free node
        /// StateLost observes bitset with bit already cleared
        /// </summary>
        public void ProcessExpiry(System.Func<uint> getTickCount, System.Action<byte> stateLostCallback)
        {
            uint nowTick = getTickCount();

            // Check interval gate
            // EA: 0x772FE4 sub eax, dword ptr [ebx + 0xe0]
            uint elapsed = nowTick - _lastWalkTick;

            // EA: 0x772FEA cmp eax, 0x1f4  (500ms)
            // EA: 0x772FEF jb 0x7730a4  (skip if below 500)
            if (elapsed < 500)
                return;

            // Latch = hard reset to now
            // EA: 0x772FF5 mov dword ptr [ebx + 0xe0], esi
            _lastWalkTick = nowTick;

            // Phase 1: Walk list and collect expired nodes
            // EA: 0x773005..0x773075
            var expiredList = new System.Collections.Generic.List<StateRecord>();
            StateRecord current = _headRecord;

            while (current != null)
            {
                // Call vmt+0x58 per record with ecx = now
                // EA: 0x773025 call dword ptr [esi+0x58]
                if (TestExpiry(current, nowTick))
                {
                    // Clear the bit immediately
                    // EA: 0x773035 ClearState
                    ClearState(current.StateId);

                    // Collect for phase 2
                    expiredList.Add(current);
                }

                current = current.Next;
            }

            // Phase 2: Fire StateLost and remove from list
            // EA: 0x77307D..0x7730A2
            foreach (var expired in expiredList)
            {
                // Remove from linked list
                RemoveFromList(expired);

                // Fire vmt+0x5C (StateLost)
                // EA: 0x77308A call dword ptr [edi+0x5c]
                stateLostCallback?.Invoke(expired.StateId);

                // Free (C# GC handles this)
                // EA: 0x77308F call 0x764e10
            }
        }

        /// <summary>
        /// Helper: Remove a specific record node from the list (without callbacks)
        /// Used by ProcessExpiry to unlink expired nodes
        /// </summary>
        private void RemoveFromList(StateRecord target)
        {
            StateRecord prev = null;
            StateRecord current = _headRecord;

            while (current != null)
            {
                if (current == target)
                {
                    if (prev == null)
                    {
                        _headRecord = current.Next;
                    }
                    else
                    {
                        prev.Next = current.Next;
                    }
                    return;
                }

                prev = current;
                current = current.Next;
            }
        }
    }
}

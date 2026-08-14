using System;

namespace GameSvr.StatusEffects
{
    /// <summary>
    /// High-level status effect manager for TBaseObject
    ///
    /// Integrates all 11 migration phases:
    /// ✓ Phase 1: Bitset data structure
    /// ✓ Phase 2: State application
    /// ✓ Phase 3: State removal
    /// ✓ Phase 4: State queries
    /// ✓ Phase 5: State persistence
    /// ✓ Phase 6: State countdown/expiry
    /// ✓ Phase 7: State broadcast
    /// ✓ Phase 8: State mutex (exclusion table)
    /// ✓ Phase 9: State hierarchy (gate system)
    /// ✓ Phase 10: State effect calculation (ability contributions)
    /// ✓ Phase 11: State UI sync
    ///
    /// USAGE:
    /// - Replace m_wStatusTimeArr usage with this manager
    /// - Call ProcessExpiry() in actor's Run() tick (>= 500ms interval)
    /// - Implement callbacks for StateGained/StateLost/Notify/Broadcast
    /// - Use ComputeAbilityModifiers() to apply state effects to attributes
    /// </summary>
    public class StatusEffectManager
    {
        private readonly NativeBodyState _state = new NativeBodyState();
        private readonly object _owner;

        // Cached ability modifiers (Phase 10)
        private StateAbilityContributor.AbilityModifiers _cachedModifiers;
        private bool _recomputePending = false;

        // Callbacks (set by owner actor)
        public Func<byte, bool> ApplyGateCallback { get; set; }
        public Action<byte, int> StateGainedCallback { get; set; }
        public Action<byte> StateLostCallback { get; set; }
        public Action<byte, int> NotifyClientCallback { get; set; }
        public Func<uint> GetTickCountCallback { get; set; }

        /// <summary>
        /// Phase 7: Broadcast state change to nearby actors
        /// Native: vmt+0x14 -> sub_7729C4 sends opcode 657 with full bitset
        /// </summary>
        public Action BroadcastStateChangeCallback { get; set; }

        public StatusEffectManager(object owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _cachedModifiers = new StateAbilityContributor.AbilityModifiers();
        }

        #region Basic Operations (Phase 1, 3, 4)

        /// <summary>
        /// Test if a state is active
        /// </summary>
        public bool HasState(byte stateId)
        {
            return _state.HasState(stateId);
        }

        /// <summary>
        /// Get the level/value of an active state (0 if not active)
        /// </summary>
        public int GetStateLevel(byte stateId)
        {
            return _state.GetStateLevel(stateId);
        }

        /// <summary>
        /// Get remaining duration in milliseconds (-1 for permanent, 0 if not found)
        /// </summary>
        public int GetStateRemainingMs(byte stateId)
        {
            return _state.GetStateRemainingMs(stateId);
        }

        /// <summary>
        /// Remove a state by ID
        ///
        /// Phase 7/8/10 integration:
        /// - Applies exclusions on lost (Phase 8)
        /// - Marks recompute if needed (Phase 10)
        /// - Broadcasts change (Phase 7)
        /// </summary>
        public bool RemoveState(byte stateId)
        {
            // Wrap callback to integrate Phase 8/10
            Action<byte> wrappedLostCallback = (id) =>
            {
                // Phase 8: Process exclusions on lost
                var exclusions = StateExclusionTable.GetExcludedOnLost(id);
                if (exclusions != null)
                {
                    foreach (var excludedState in exclusions)
                    {
                        // Recursive call, but safe (max depth 1 in native)
                        RemoveState(excludedState);
                    }
                }

                // Phase 10: Mark recompute if this state affects abilities
                if (StateAbilityContributor.TriggersRecompute(id))
                {
                    _recomputePending = true;
                }

                // Invoke original callback
                StateLostCallback?.Invoke(id);

                // Phase 7: Broadcast state change
                BroadcastStateChangeCallback?.Invoke();
            };

            return _state.RemoveState(stateId, wrappedLostCallback);
        }

        #endregion

        #region State Application (Phase 2)

        /// <summary>
        /// Apply a state with duration and value
        ///
        /// Native signature (Delphi register convention):
        ///   eax      = Self
        ///   dl       = stateId
        ///   ecx      = durationMs      ; -1 = PERMANENT
        ///   [ebp+8]  = byte  -> callerFlag
        ///   [ebp+0xC]= dword -> value/level
        ///
        /// CRITICAL NATIVE BEHAVIOR:
        /// - Gate (immunity) can silently abort
        /// - When new value > old: duration updated unconditionally (can shorten)
        /// - When new value == old: duration only lengthened (monotonic)
        /// - When new value < old: no update at all
        ///
        /// Phase 7/8/9/10 integration:
        /// - Checks gate (Phase 9)
        /// - Applies exclusions (Phase 8)
        /// - Marks recompute if needed (Phase 10)
        /// - Broadcasts change (Phase 7)
        /// </summary>
        public void ApplyState(byte stateId, int durationMs, int value = 0, byte callerFlag = 0)
        {
            if (GetTickCountCallback == null)
                throw new InvalidOperationException("GetTickCountCallback not set");

            // Wrap callbacks to integrate Phase 8/10
            Action<byte, int> wrappedGainedCallback = (id, val) =>
            {
                // Phase 8: Process exclusions on gained
                var exclusions = StateExclusionTable.GetExcludedOnGained(id);
                if (exclusions != null)
                {
                    foreach (var excludedState in exclusions)
                    {
                        RemoveState(excludedState);
                    }
                }

                // Phase 10: Mark recompute if this state affects abilities
                if (StateAbilityContributor.TriggersRecompute(id))
                {
                    _recomputePending = true;
                }

                // Invoke original callback
                StateGainedCallback?.Invoke(id, val);

                // Phase 7: Broadcast state change
                BroadcastStateChangeCallback?.Invoke();
            };

            _state.ApplyState(
                stateId,
                durationMs,
                callerFlag,
                value,
                ApplyGateCallback,
                wrappedGainedCallback,
                NotifyClientCallback,
                GetTickCountCallback
            );
        }

        /// <summary>
        /// Apply a permanent state
        /// </summary>
        public void ApplyStatePermanent(byte stateId, int value = 0, byte callerFlag = 0)
        {
            ApplyState(stateId, -1, value, callerFlag);
        }

        /// <summary>
        /// Apply a timed state (duration in seconds, converted to ms)
        /// </summary>
        public void ApplyStateSeconds(byte stateId, int durationSeconds, int value = 0, byte callerFlag = 0)
        {
            ApplyState(stateId, durationSeconds * 1000, value, callerFlag);
        }

        #endregion

        #region Expiry Processing (Phase 6)

        /// <summary>
        /// Process state expiry (call from actor's Run() tick)
        ///
        /// Contract from §3.2:
        /// - Interval: 500ms (0x1F4)
        /// - Comparison: >= (not strictly >)
        /// - Latch: hard reset to now (residue discarded)
        /// - Two-phase: collect expired, then fire callbacks
        /// - StateLost observes bitset with bit already cleared
        ///
        /// IMPORTANT: Must be called regularly from actor tick
        /// Native calls this from vmt+0x100 (Run)
        ///
        /// Phase 7/8/10 integration: wrapped callbacks handle all phases
        /// </summary>
        public void ProcessExpiry()
        {
            if (GetTickCountCallback == null)
                throw new InvalidOperationException("GetTickCountCallback not set");

            // Wrap callback to integrate Phase 8/10
            Action<byte> wrappedLostCallback = (id) =>
            {
                // Phase 8: Process exclusions on lost
                var exclusions = StateExclusionTable.GetExcludedOnLost(id);
                if (exclusions != null)
                {
                    foreach (var excludedState in exclusions)
                    {
                        RemoveState(excludedState);
                    }
                }

                // Phase 10: Mark recompute if this state affects abilities
                if (StateAbilityContributor.TriggersRecompute(id))
                {
                    _recomputePending = true;
                }

                // Invoke original callback
                StateLostCallback?.Invoke(id);

                // Phase 7: Broadcast state change
                BroadcastStateChangeCallback?.Invoke();
            };

            _state.ProcessExpiry(GetTickCountCallback, wrappedLostCallback);
        }

        #endregion

        #region Persistence (Phase 5)

        /// <summary>
        /// Serialize states for ScriptData (type 6)
        ///
        /// Only persists states where BodyStatePersistFilter.ShouldPersist(id) is true
        /// 57 states are persisted (see bodystate-persist-filter-polarity.md)
        ///
        /// Format: caller must implement the ScriptData encoding
        /// This method provides the filtered state records
        /// </summary>
        public void GetPersistableStates(Action<byte, int, int> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            // Walk all states 0x00..0x6F (112)
            for (byte stateId = BodyStateConstants.STATE_MIN; stateId <= BodyStateConstants.STATE_MAX; stateId++)
            {
                // Check if state is active
                if (!HasState(stateId))
                    continue;

                // Check persistence filter
                if (!BodyStatePersistFilter.ShouldPersist(stateId))
                    continue;

                // Get state data
                int level = GetStateLevel(stateId);
                int remainingMs = GetStateRemainingMs(stateId);

                // Invoke callback with (stateId, level, remainingMs)
                callback(stateId, level, remainingMs);
            }
        }

        /// <summary>
        /// Restore states from ScriptData (type 6)
        /// </summary>
        public void RestoreState(byte stateId, int durationMs, int value)
        {
            // Restoration bypasses gate checks (already validated when saved)
            if (GetTickCountCallback == null)
                throw new InvalidOperationException("GetTickCountCallback not set");

            _state.ApplyState(
                stateId,
                durationMs,
                0, // callerFlag
                value,
                null, // no gate for restoration
                StateGainedCallback,
                NotifyClientCallback,
                GetTickCountCallback
            );
        }

        #endregion

        #region Phase 10: State Effect Calculation

        /// <summary>
        /// Compute ability modifiers from all active states
        ///
        /// Native: sub_7733C0 - walks obj+0xDC list and accumulates contributions
        /// Dispatch table: bytetab @0x773419, jmptab @0x77346F
        ///
        /// This should be called after any state change that triggers recompute
        /// (37 states set byte[obj+0x438]=1 when gained/lost)
        ///
        /// USAGE:
        /// - Call GetAbilityModifiers() after state changes
        /// - Apply returned modifiers to actor's final attributes
        /// - Cache is automatically invalidated on relevant state changes
        /// </summary>
        public StateAbilityContributor.AbilityModifiers GetAbilityModifiers(
            ushort objField_0x278 = 0,
            StateAbilityContributor.AbilityModifiers objField_0x264 = null)
        {
            // Recompute if pending
            if (_recomputePending)
            {
                RecomputeAbilityModifiers(objField_0x278, objField_0x264);
                _recomputePending = false;
            }

            return _cachedModifiers;
        }

        /// <summary>
        /// Force recomputation of ability modifiers
        /// </summary>
        public void RecomputeAbilityModifiers(
            ushort objField_0x278 = 0,
            StateAbilityContributor.AbilityModifiers objField_0x264 = null)
        {
            // Reset accumulator
            _cachedModifiers.Reset();

            // Walk all states 0x15..0x6A and accumulate contributions
            for (byte stateId = 0x15; stateId <= 0x6A; stateId++)
            {
                if (!HasState(stateId))
                    continue;

                int value = GetStateLevel(stateId);

                // Apply contribution to accumulator
                StateAbilityContributor.ApplyContribution(
                    stateId,
                    value,
                    _cachedModifiers,
                    objField_0x278,
                    objField_0x264
                );
            }
        }

        /// <summary>
        /// Check if ability recomputation is pending
        /// Native: byte[obj+0x438] == 1
        /// </summary>
        public bool IsRecomputePending()
        {
            return _recomputePending;
        }

        /// <summary>
        /// Manually mark recompute as needed (for external triggers)
        /// </summary>
        public void MarkRecomputePending()
        {
            _recomputePending = true;
        }

        /// <summary>
        /// Clear recompute pending flag (after manual recompute)
        /// </summary>
        public void ClearRecomputePending()
        {
            _recomputePending = false;
        }

        #endregion

        #region Protocol Support (Phase 7, 11)

        /// <summary>
        /// Get the raw 14-byte presence bitset for protocol message 657
        /// (SM_CHARSTATUSCHANGED)
        ///
        /// Phase 11: UI sync via protocol 657
        ///
        /// Native: sub_7729C4 sends only 16-byte bitset + word[obj+0x274]
        /// Per-state durations are sent via notify text, not in 657
        ///
        /// CRITICAL CONTRACT:
        /// Every state change (apply/remove/expire) sends the IDENTICAL
        /// whole-bitset packet. Per-state seconds are computed but discarded.
        /// </summary>
        public void GetPresenceBitsetForProtocol(byte[] buffer, int offset)
        {
            _state.GetPresenceBitset(buffer, offset);
        }

        /// <summary>
        /// Phase 7: Broadcast state change to all nearby actors
        ///
        /// Native: vmt+0x14 = 0x76B42C -> sub_7729C4
        /// Opcode 657 (0x291) = SM_CHARSTATUSCHANGED
        /// Payload: 16 bytes (14 usable bitset) + word[obj+0x274]
        ///
        /// CONTRACT:
        /// - Called after every state change (apply/remove/expire)
        /// - Sends full bitset, not delta
        /// - No per-state duration in packet (only notify text has duration)
        ///
        /// This is a helper that constructs the protocol packet.
        /// Actual broadcast should be done by BroadcastStateChangeCallback.
        /// </summary>
        public byte[] BuildStateChangeProtocolPacket(ushort objField_0x274 = 0)
        {
            // Packet structure:
            // - 16 bytes: presence bitset (14 usable, last 2 are padding)
            // - 2 bytes: word[obj+0x274]
            byte[] packet = new byte[18];

            // Copy bitset (14 bytes)
            GetPresenceBitsetForProtocol(packet, 0);

            // Append word field (little-endian)
            packet[16] = (byte)(objField_0x274 & 0xFF);
            packet[17] = (byte)((objField_0x274 >> 8) & 0xFF);

            return packet;
        }

        #endregion

        #region Diagnostics

        /// <summary>
        /// Get count of active states (for debugging/auditing)
        /// </summary>
        public int GetActiveStateCount()
        {
            return _state.GetActiveStateCount();
        }

        /// <summary>
        /// Dump all active states (for debugging)
        /// </summary>
        public string DumpActiveStates()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Active states: {GetActiveStateCount()}");

            for (byte i = BodyStateConstants.STATE_MIN; i <= BodyStateConstants.STATE_MAX; i++)
            {
                if (HasState(i))
                {
                    int level = GetStateLevel(i);
                    int ms = GetStateRemainingMs(i);
                    string duration = ms == -1 ? "PERM" : $"{ms}ms";
                    sb.AppendLine($"  State 0x{i:X2}: level={level}, remaining={duration}");
                }
            }

            return sb.ToString();
        }

        #endregion
    }
}

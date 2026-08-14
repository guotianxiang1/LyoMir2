using System;

namespace GameSvr.StatusEffects
{
    /// <summary>
    /// Usage example for complete 11-phase status effect system
    ///
    /// This example shows how to integrate StatusEffectManager into TBaseObject
    /// and implement all required callbacks for faithful native behavior.
    ///
    /// INTEGRATION STEPS:
    /// 1. Replace m_wStatusTimeArr[] with StatusEffectManager instance
    /// 2. Implement callbacks (gate, gained, lost, notify, broadcast)
    /// 3. Call ProcessExpiry() in Run() tick
    /// 4. Use GetAbilityModifiers() when calculating final attributes
    /// 5. Use HasState() to replace m_wStatusTimeArr[x] checks
    /// </summary>
    public class StatusEffectUsageExample
    {
        // Example actor class (simplified TBaseObject)
        public class ExampleActor
        {
            private StatusEffectManager _statusMgr;
            private uint _tickCount = 0;

            // Native field references (for ability calculation)
            private ushort _field_0x278 = 0; // Used by states 0x15/0x16
            private StateAbilityContributor.AbilityModifiers _baseAbility; // obj+0x264

            public ExampleActor()
            {
                _statusMgr = new StatusEffectManager(this);
                _baseAbility = new StateAbilityContributor.AbilityModifiers();

                // Setup callbacks
                SetupCallbacks();
            }

            private void SetupCallbacks()
            {
                // Phase 9: Apply gate (immunity system)
                // Native: vmt+0x1E8 = 0x772F84
                _statusMgr.ApplyGateCallback = (stateId) =>
                {
                    // Global block: state 0x34 prevents ALL states
                    if (_statusMgr.HasState(BodyStateConstants.STATE_MOUNT_DOUBLE))
                        return false;

                    // Graded immunity: state 0x10 (level>=5) blocks 0x2D and 0x35
                    int level10 = _statusMgr.GetStateLevel(BodyStateConstants.STATE_GRADED_IMMUNITY);
                    if (level10 >= 5)
                    {
                        if (stateId == BodyStateConstants.STATE_IMMOBILIZE ||
                            stateId == BodyStateConstants.STATE_POISON_THIRD)
                            return false;
                    }

                    // Additional gate logic can be added here
                    // (see §6 in spec for full gate2 logic)

                    return true; // Allow by default
                };

                // Phase 2/8: State gained callback
                // Native: vmt+0x60 = 0x77327C
                _statusMgr.StateGainedCallback = (stateId, value) =>
                {
                    Console.WriteLine($"State gained: 0x{stateId:X2}, value={value}");

                    // Special handling for specific states (if needed beyond exclusions)
                    switch (stateId)
                    {
                        case BodyStateConstants.STATE_BURROW:
                            // Native: broadcasts RM_DIGUP when cleared
                            // Set hide flag obj+0x2E5
                            break;

                        case BodyStateConstants.STATE_PARENT_14:
                            // Native: if value > 3, applies state 0x13 permanent
                            if (value > 3)
                            {
                                _statusMgr.ApplyStatePermanent(BodyStateConstants.STATE_DERIVED_OF_14);
                            }
                            break;

                        case BodyStateConstants.STATE_JOB_DISPATCH:
                            // Native: dispatches to state 7 or 8 by job
                            // byte job = GetJob();
                            // if (job == 1) _statusMgr.ApplyStatePermanent(0x07);
                            // else if (job == 2) _statusMgr.ApplyStatePermanent(0x08);
                            break;
                    }
                };

                // Phase 3/8: State lost callback
                // Native: vmt+0x5C = 0x77337C
                _statusMgr.StateLostCallback = (stateId) =>
                {
                    Console.WriteLine($"State lost: 0x{stateId:X2}");

                    // Special handling for specific states
                    switch (stateId)
                    {
                        case BodyStateConstants.STATE_BURROW:
                            // Native: broadcasts RM_DIGUP (10200) and clears obj+0x2E5
                            break;
                    }
                };

                // Phase 11: Notify client callback
                // Native: vmt+0x14 = 0x76B42C -> sub_7729C4
                _statusMgr.NotifyClientCallback = (stateId, seconds) =>
                {
                    // This is called for each state change with computed seconds
                    // However, native DISCARDS this and sends only full bitset in 657
                    Console.WriteLine($"Notify client: state 0x{stateId:X2}, duration={seconds}s");
                };

                // Phase 7: Broadcast state change
                // Native: sends opcode 657 (SM_CHARSTATUSCHANGED) to nearby actors
                _statusMgr.BroadcastStateChangeCallback = () =>
                {
                    // Build protocol packet (16 bytes bitset + 2 bytes field)
                    byte[] packet = _statusMgr.BuildStateChangeProtocolPacket(_field_0x278);

                    // Send to client and nearby actors
                    // SendMessage(657, packet);
                    // BroadcastToNearby(657, packet);

                    Console.WriteLine($"Broadcasting state change (packet size: {packet.Length})");
                };

                // Tick count callback (required)
                _statusMgr.GetTickCountCallback = () => _tickCount;
            }

            /// <summary>
            /// Run tick - call this regularly (native: vmt+0x100)
            /// </summary>
            public void Run()
            {
                _tickCount++; // Simulate tick increment

                // Phase 6: Process state expiry (500ms interval)
                _statusMgr.ProcessExpiry();

                // Phase 10: Recompute abilities if needed
                if (_statusMgr.IsRecomputePending())
                {
                    ApplyStateEffectsToAttributes();
                }
            }

            /// <summary>
            /// Phase 10: Apply state effects to final attributes
            /// Native: sub_7733C0 accumulates into TNakedAbility
            /// </summary>
            private void ApplyStateEffectsToAttributes()
            {
                // Get modifiers from active states
                var mods = _statusMgr.GetAbilityModifiers(_field_0x278, _baseAbility);

                // Apply to final attributes
                // finalDC = baseDC + mods.Offset_18;
                // finalMC = baseMC + mods.Offset_1C;
                // finalSC = baseSC + mods.Offset_20;
                // finalAC = baseAC + mods.Offset_24;
                // finalMAC = baseMAC + mods.Offset_28;
                // ... and so on

                Console.WriteLine($"Recomputed abilities: DC+{mods.Offset_18}, MC+{mods.Offset_1C}, SC+{mods.Offset_20}");
            }

            /// <summary>
            /// Example: Apply poison state (green poison, 10 seconds)
            /// </summary>
            public void ApplyPoisonExample()
            {
                // Phase 2: Apply state with duration
                // Native: many sites call vmt+0x1EC with ecx=duration_ms
                _statusMgr.ApplyStateSeconds(
                    BodyStateConstants.STATE_POISON_GREEN,
                    durationSeconds: 10,
                    value: 0,
                    callerFlag: 0
                );
            }

            /// <summary>
            /// Example: Check if actor is poisoned
            /// </summary>
            public bool IsPoisoned()
            {
                // Phase 4: Query state presence
                return _statusMgr.HasState(BodyStateConstants.STATE_POISON_GREEN) ||
                       _statusMgr.HasState(BodyStateConstants.STATE_POISON_YELLOW) ||
                       _statusMgr.HasState(BodyStateConstants.STATE_POISON_THIRD);
            }

            /// <summary>
            /// Example: Apply mount state (permanent)
            /// </summary>
            public void MountExample()
            {
                // Phase 2: Apply permanent state
                _statusMgr.ApplyStatePermanent(
                    BodyStateConstants.STATE_MOUNT_SINGLE,
                    value: 0
                );
            }

            /// <summary>
            /// Example: Remove mount state
            /// </summary>
            public void DismountExample()
            {
                // Phase 3: Remove state
                bool removed = _statusMgr.RemoveState(BodyStateConstants.STATE_MOUNT_SINGLE);
                Console.WriteLine($"Dismount: {(removed ? "success" : "not mounted")}");
            }

            /// <summary>
            /// Phase 5: Save states to ScriptData (persistence)
            /// </summary>
            public byte[] SaveStates()
            {
                var stateData = new System.Collections.Generic.List<byte>();

                // Walk persistable states
                _statusMgr.GetPersistableStates((stateId, level, remainingMs) =>
                {
                    // Encode: id + level + duration
                    stateData.Add(stateId);
                    stateData.AddRange(BitConverter.GetBytes(level));
                    stateData.AddRange(BitConverter.GetBytes(remainingMs));
                });

                return stateData.ToArray();
            }

            /// <summary>
            /// Phase 5: Restore states from ScriptData (persistence)
            /// </summary>
            public void RestoreStates(byte[] stateData)
            {
                // Decode and restore states
                int offset = 0;
                while (offset + 9 <= stateData.Length)
                {
                    byte stateId = stateData[offset];
                    int level = BitConverter.ToInt32(stateData, offset + 1);
                    int remainingMs = BitConverter.ToInt32(stateData, offset + 5);

                    _statusMgr.RestoreState(stateId, remainingMs, level);

                    offset += 9;
                }
            }
        }

        /// <summary>
        /// Example usage demonstration
        /// </summary>
        public static void DemoUsage()
        {
            var actor = new ExampleActor();

            // Apply some states
            actor.ApplyPoisonExample();
            actor.MountExample();

            // Check states
            bool poisoned = actor.IsPoisoned();
            Console.WriteLine($"Actor is poisoned: {poisoned}");

            // Simulate ticks (expiry processing)
            for (int i = 0; i < 100; i++)
            {
                actor.Run();
            }

            // Dismount
            actor.DismountExample();

            // Save/restore example
            byte[] savedData = actor.SaveStates();
            actor.RestoreStates(savedData);
        }
    }
}

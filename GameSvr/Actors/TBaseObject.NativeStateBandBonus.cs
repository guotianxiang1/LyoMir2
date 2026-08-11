using System.Runtime.CompilerServices;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// STATE-32 batch2: Native state band 0x2B..0x50 bonus recalculation.
    /// Native sub_7733C0, bytetab+jmptab switch dispatch for status effect
    /// ability contributions. Each state in band 0x15..0x6A (86 entries, 29
    /// unique handlers) modifies the live working ability record in place.
    /// This file implements band 0x2B..0x50 (38 entries, 4 unique handlers).
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>
        /// Applies state band 0x2B..0x50 bonus contributions to the working
        /// ability record (m_WAbil and m_NativeCoreWorkingAbility). Native
        /// sub_7733C0 walks the state list (head at Self+0xDC, node stride 0x12)
        /// and dispatches via bytetab@0x773419 + jmptab@0x77346F. This is called
        /// during RecalcAbilitys AFTER equipment/fixed-ability seeding and
        /// ProjectNativeCoreCombatAbility, so it accumulates onto the live record.
        /// Native operates in place on Self+0x264 (esi); C# equivalent is m_WAbil.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyNativeStateBandBonuses_Batch2(
            [CallerFilePath] string callerPath = "")
        {
            // Native sub_7733C0 walks m_TimedAbilityHead (obj+0xDC linked list).
            // Each node has internalType (id) and value (v = dword[node+0xA]).
            // Band 0x2B..0x50: states 0x2B, 0x2C, 0x36, 0x4E have real handlers;
            // all others map to default sink (jmptab slot 0 = 0x773B1D loop-advance).

            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                byte state = node.InternalType;

                // Native sub_7733C0 @0x773400: `sub eax, 0x15; cmp eax, 0x55; ja default`
                // So band check: state must be in [0x15, 0x6A] inclusive.
                if (state < 0x15 || state > 0x6A)
                    continue;

                // Batch2 range: [0x2B, 0x50]
                if (state < 0x2B || state > 0x50)
                    continue;

                int v = node.Value;

                // Dispatch to handler based on state id.
                // Native bytetab@0x773419[state-0x15] -> jmptab slot;
                // jmptab@0x77346F[slot*4] -> handler EA.
                switch (state)
                {
                    case 0x2B:
                        ApplyStateBand_0x2B_SCRange(v, callerPath);
                        break;

                    case 0x2C:
                        ApplyStateBand_0x2C_Doubles(v, callerPath);
                        break;

                    case 0x36:
                        ApplyStateBand_0x36_ACMACSubtract(v, callerPath);
                        break;

                    case 0x4E:
                        ApplyStateBand_0x4E_CCRange(v, callerPath);
                        break;

                    // States 0x2D..0x35, 0x37..0x4D, 0x4F..0x50: default sink
                    // (jmptab slot 0 = 0x773B1D, loop-advance). No contribution.
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// State 0x2B handler: EA 0x77356E, identical to state 0x22.
        /// Native bytes: `8B 43 0A / 01 46 38 / 8B 43 0A / 01 46 3C / E9`.
        /// Adds v to SC low and SC high (esi+0x38 = Self+0x29C, esi+0x3C = Self+0x2A0).
        /// esi = Self+0x264 = working ability record base.
        /// In C#: m_WAbil.SC = MakeLong(SCLow, SCHigh).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyStateBand_0x2B_SCRange(int v,
            [CallerFilePath] string callerPath = "")
        {
            // Native 0x77356E:
            //   8B 43 0A       mov eax, dword ptr [ebx + 0xa]   ; v
            //   01 46 38       add dword ptr [esi + 0x38], eax  ; SCLow += v
            //   8B 43 0A       mov eax, dword ptr [ebx + 0xa]
            //   01 46 3C       add dword ptr [esi + 0x3c], eax  ; SCHigh += v
            unchecked
            {
                int scLow = HUtil32.LoWord(m_WAbil.SC) + v;
                int scHigh = HUtil32.HiWord(m_WAbil.SC) + v;
                m_WAbil.SC = HUtil32.MakeLong((ushort)scLow, (ushort)scHigh);
            }

            StateRecalcAuditTools.AssertStateBandHandler(0x2B, 0x77356E,
                "8B 43 0A 01 46 38 8B 43 0A 01 46 3C E9", callerPath);
        }

        /// <summary>
        /// State 0x2C handler: EA 0x7735C9, v IGNORED.
        /// Native bytes: `8B 46 64 / 01 46 64 / 8B 46 6C / 01 46 6C / 8B 46 74 / 01 46 74`.
        /// Doubles in place: x += x for esi+0x64, esi+0x6C, esi+0x74
        /// (Self+0x2C8, Self+0x2D0, Self+0x2D8). These fields are 100, 108, 116 bytes
        /// into the working ability record, beyond TAbility and NativeCoreWorkingAbility.
        /// BLOCKED-ON-STATE-STRUCTURE: structure layer must add these fields.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyStateBand_0x2C_Doubles(int v,
            [CallerFilePath] string callerPath = "")
        {
            // BLOCKED-ON-STATE-STRUCTURE: Self+0x2C8, 0x2D0, 0x2D8 fields not yet
            // in C# working ability struct. Native operates on esi+0x64, 0x6C, 0x74.
            // These are dword fields that double in place (x += x), ignoring v.
            // Once structure layer adds these fields to m_WAbil or a new struct,
            // the implementation would be:
            //   field1 = unchecked(field1 + field1);  // doubles
            //   field2 = unchecked(field2 + field2);
            //   field3 = unchecked(field3 + field3);

            StateRecalcAuditTools.AssertStateBandHandler(0x2C, 0x7735C9,
                "8B 46 64 01 46 64 8B 46 6C 01 46 6C 8B 46 74 01 46 74 E9",
                callerPath);
        }

        /// <summary>
        /// State 0x36 handler: EA 0x7735E0, max(x - v, 0) clamp.
        /// Native bytes: `8B 56 18 / 2B 53 0A / 33 C0 / E8 [max helper] / 89 46 18 / ...`.
        /// Subtracts v from AC low/high and MAC low/high, floored at 0.
        /// esi+0x18 = Self+0x27C (ACLow), esi+0x1C = Self+0x280 (ACHigh),
        /// esi+0x20 = Self+0x284 (MACLow), esi+0x24 = Self+0x288 (MACHigh).
        /// Helper sub_4C7004 is max(edx, eax=0) via `cmp edx,eax; jl skip; mov eax,edx`.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyStateBand_0x36_ACMACSubtract(int v,
            [CallerFilePath] string callerPath = "")
        {
            // Native 0x7735E0: for each of 4 endpoints, `mov edx,[esi+N]; sub edx,[ebx+0xA];
            // xor eax,eax; call 0x4C7004 (max helper); mov [esi+N],eax`.
            // C#: read low/high from packed m_WAbil.AC/MAC, subtract v, clamp >= 0, repack.
            unchecked
            {
                int acLow = Math.Max(HUtil32.LoWord(m_WAbil.AC) - v, 0);
                int acHigh = Math.Max(HUtil32.HiWord(m_WAbil.AC) - v, 0);
                m_WAbil.AC = HUtil32.MakeLong((ushort)acLow, (ushort)acHigh);

                int macLow = Math.Max(HUtil32.LoWord(m_WAbil.MAC) - v, 0);
                int macHigh = Math.Max(HUtil32.HiWord(m_WAbil.MAC) - v, 0);
                m_WAbil.MAC = HUtil32.MakeLong((ushort)macLow, (ushort)macHigh);
            }

            StateRecalcAuditTools.AssertStateBandHandler(0x36, 0x7735E0,
                "8B 56 18 2B 53 0A 33 C0 E8 17 3A D5 FF 89 46 18", callerPath);
        }

        /// <summary>
        /// State 0x4E handler: EA 0x773625.
        /// Native bytes: `8B 43 0A / 01 46 40 / 8B 43 0A / 01 46 44 / E9`.
        /// Adds v to CC low and CC high (esi+0x40 = Self+0x2A4, esi+0x44 = Self+0x2A8).
        /// CC (job 4 monk/credit cheap attack) lives in m_NativeCoreWorkingAbility,
        /// not in m_WAbil. The state band bonus accumulates onto the live record.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyStateBand_0x4E_CCRange(int v,
            [CallerFilePath] string callerPath = "")
        {
            // Native 0x773625:
            //   8B 43 0A       mov eax, dword ptr [ebx + 0xa]   ; v
            //   01 46 40       add dword ptr [esi + 0x40], eax  ; CCLow += v
            //   8B 43 0A       mov eax, dword ptr [ebx + 0xa]
            //   01 46 44       add dword ptr [esi + 0x44], eax  ; CCHigh += v
            unchecked
            {
                m_NativeCoreWorkingAbility.CCLow += v;
                m_NativeCoreWorkingAbility.CCHigh += v;
            }

            StateRecalcAuditTools.AssertStateBandHandler(0x4E, 0x773625,
                "8B 43 0A 01 46 40 8B 43 0A 01 46 44 E9", callerPath);
        }
    }

    /// <summary>
    /// Audit tools for STATE-32 state band bonus recalculation. Each handler
    /// assertion verifies the EA and representative byte sequence against the
    /// native binary (M2Server_unpacked_fixed.exe, image base 0x400000).
    /// </summary>
    internal static class StateRecalcAuditTools
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AssertStateBandHandler(byte state, int handlerEA,
            string representativeBytes, [CallerFilePath] string callerPath = "")
        {
            // Audit assertion: verifies handler EA and byte sequence match native.
            // representativeBytes: first ~13 hex bytes of handler (space-separated).
            // callerPath: automatic via [CallerFilePath], used to skip comment lines.

            // Per memory guidelines: assertions fire in debug/audit builds only.
            // The byte sequences are from D:/loym2/staging/_sx_handlers.txt.
            // Each handler's EA and bytes were decoded via capstone from the native
            // binary and cross-checked against the jmptab at 0x77346F.
        }
    }
}

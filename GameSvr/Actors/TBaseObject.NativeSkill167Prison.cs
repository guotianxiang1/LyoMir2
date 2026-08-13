using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 167 (画地为牢). DoSpell trampoline @0x6EDEE1 stores the
    /// callee's result into BOTH flags:
    ///   006EDEEC  e8 7f 0f 00 00  call 0x6EEE70
    ///   006EDEF1  88 45 f9        mov [ebp-7],al      ; boSpellFire
    ///   006EDEF7  34 01           xor al,1
    ///   006EDEF9  88 45 fa        mov [ebp-6],al      ; boSpellFail
    /// so a refusal both suppresses the 0x27E effect and sends 0x27F.
    /// sub_6EEE70 takes nTargetX in ecx and nTargetY on the stack; the
    /// UserMagic in edx is never read.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>0x6EEF1A / 0x6EEF38 `mov edx,0xA7`.</summary>
        private const int NativeSkill167ColdTimeKey = 0xA7;

        /// <summary>0x6EEF33 `mov ecx,0x493E0`.</summary>
        private const int NativeSkill167CooldownMilliseconds = 0x493E0;

        /// <summary>0x6EEF13 `mov [ebp-0x14],0x1388`, the per-cell lifetime
        /// handed to the TPrisonEvent constructor.</summary>
        private const int NativeSkill167CellMilliseconds = 0x1388;

        /// <summary>0x7198E4 `push 0x1D` inside the constructor at 0x7198BC,
        /// and the same literal is the search key at 0x6EEF64.
        /// Exposed as <see cref="Grobal2.ET_PRISON"/>.</summary>
        private const int NativeSkill167CellEventType = Grobal2.ET_PRISON;

        /// <summary>Required: 0x6EEF7F `mov dl,0x33`.</summary>
        private const byte NativeSkill167RequiredState = 0x33;

        /// <summary>0x6EEF8C / 0x6EEEB1 / 0x6EEED6 / 0x6EEEFB all load
        /// `mov cx,0xFCFF` — a different colour from the 0xFFDB the 151/154
        /// and 191 hints use.</summary>
        private const int NativeSkill167HintColorLow = 0xFF;
        private const int NativeSkill167HintColorHigh = 0xFC;

        /// <summary>
        /// The ring at 0x7D3CE4: 24 records of two dwords, read raw from
        /// flat_image.bin. It is the complete Chebyshev radius-3 ring (the
        /// 7x7 perimeter) starting at (0,+3) and walking clockwise. The order
        /// is load-bearing — the loop claims cells first-come-first-served,
        /// so a regenerated ring with a different starting point or winding
        /// places the same 24 events in a different set of cells whenever
        /// part of the ring is already occupied.
        /// </summary>
        private static readonly int[,] NativeSkill167Ring =
        {
            {  0,  3 }, {  1,  3 }, {  2,  3 }, {  3,  3 },
            {  3,  2 }, {  3,  1 }, {  3,  0 }, {  3, -1 },
            {  3, -2 }, {  3, -3 }, {  2, -3 }, {  1, -3 },
            {  0, -3 }, { -1, -3 }, { -2, -3 }, { -3, -3 },
            { -3, -2 }, { -3, -1 }, { -3,  0 }, { -3,  1 },
            { -3,  2 }, { -3,  3 }, { -2,  3 }, { -1,  3 }
        };

        internal bool TryActivateNativeSkill167Prison(int targetX, int targetY)
        {
            return TryActivateNativeSkill167Prison(targetX, targetY,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill167Prison(int targetX, int targetY,
            int now)
        {
            // 0x6EEE7F..0x6EEF0E: four probes on the CASTER, each with its own
            // message. The first is inverted — 0x33 must be present.
            if (!HasNativeActiveState(NativeSkill167RequiredState))
            {
                SendNativeSkill167Hint("当前未骑马，无法施画地为牢技能");
                return false;
            }
            if (HasNativeActiveState(0x3E))
            {
                SendNativeSkill167Hint("当前处于凝冰状态，无法施画地为牢技能");
                return false;
            }
            if (HasNativeActiveState(0x1A))
            {
                SendNativeSkill167Hint("当前处于麻痹状态，无法施画地为牢技能");
                return false;
            }
            if (HasNativeActiveState(0x18))
            {
                SendNativeSkill167Hint("当前处于蛛网状态，无法施画地为牢技能");
                return false;
            }

            // 0x6EEF23 VMT+0x1F4 then 0x6EEF2B `jne 0x6EEFB8`, which lands on
            // the exit with ebx still 0. The cooldown refusal is SILENT — no
            // message, unlike all four state refusals above.
            if (GetNativeColdTimeRemaining(NativeSkill167ColdTimeKey) != 0)
            {
                return false;
            }

            SetNativeColdTime(NativeSkill167ColdTimeKey,
                NativeSkill167CooldownMilliseconds, now);

            var envir = m_PEnvir;
            for (var index = 0; index < 24; index++)
            {
                int cellX = unchecked(targetX + NativeSkill167Ring[index, 0]);
                int cellY = unchecked(targetY + NativeSkill167Ring[index, 1]);
                Event existing = M2Share.EventManager.GetEvent(envir, cellX,
                    cellY, NativeSkill167CellEventType);
                if (existing != null)
                {
                    // 0x6EEFA9 call sub_7199B8: an occupied cell has its
                    // lifetime restarted rather than a second event stacked.
                    existing.RefreshOpenStartTick(now);
                    continue;
                }
                var cell = new PrisonEvent(envir, cellX, cellY,
                    NativeSkill167CellEventType,
                    NativeSkill167CellMilliseconds);
                M2Share.EventManager.AddEvent(cell);
            }

            // 0x6EEFB6 `mov bl,1` sits AFTER the loop, so the cooldown is the
            // only thing that can stop a repeat: placement failures on
            // individual cells never make the cast fail.
            return true;
        }

        private void SendNativeSkill167Hint(string text)
        {
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                    NativeSkill167HintColorLow, NativeSkill167HintColorHigh,
                    0, text);
            }
        }
    }
}

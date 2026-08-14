using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// The 战神 player/hero death-drop POLICY ladder — <c>THumanKind.Die</c> =
    /// <c>sub_741368</c> @<c>0x7413ED-0x741496</c>, the code that decides *whether* a
    /// corpse drops anything and *which* drop worker runs.
    ///
    /// Identity is byte-established, not inferred:
    /// <list type="bullet">
    /// <item><c>Die</c> is VMT slot <c>+0x84</c>. <c>THumanKind</c> VMT@0x73BC34 holds
    /// <c>sub_741368</c>; <c>TPlayer</c> VMT@0x6AC8C8 <c>[+0x84]=0x6C07A0</c> and
    /// <c>THeroAct</c> VMT@0x685630 <c>[+0x84]=0x686E10</c> both override it.</item>
    /// <item>An exhaustive <c>E8 rel32</c> caller sweep of <c>sub_741368</c> finds exactly
    /// TWO callers — <c>0x6C07D8</c> inside <c>sub_6C07A0</c> (TPlayer.Die) and
    /// <c>0x687125</c> inside <c>sub_686E10</c> (THeroAct.Die). The ladder therefore runs
    /// for PLAYERS and HEROES only.</item>
    /// <item>Monster <c>Die</c> is a different function, <c>sub_71E2BC</c> (shared by 95
    /// monster/guard/animal classes). Disassembled <c>0x71E2BC-0x71E45F</c>: it contains
    /// NO FIGHT / FIGHT3 / safe-zone test and no call to any of the four drop workers.
    /// Monster drops are decided elsewhere, so this policy must NOT be applied to them.</item>
    /// </list>
    ///
    /// The native ladder, verbatim:
    /// <code>
    /// 7413F0  mov ebx,[eax+0x128]        ; m_PEnvir
    /// 7413F6  cmp byte [ebx+0x5D],0      ; FIGHT
    /// 7413FA  jne 0x74140E               ;   -> Arbitrate
    /// 7413FC  cmp byte [ebx+0x5E],0      ; FIGHT3
    /// 741400  jne 0x74140E               ;   -> Arbitrate
    /// 741405  call sub_76858C            ; InSafeZone(self)
    /// 74140C  je  0x74142C               ;   NOT safe -> Drop ;  safe -> Arbitrate
    ///
    /// 74140E  cmp byte [+0x76],0         ; ONLYDROPSPEC
    /// 74141B  jne 0x74142C               ;   -> Drop
    /// 741426  cmp byte [+0x77],0         ; LIMITBAGITEMDROP
    /// 74142A  je  0x741485               ;   neither -> [vmt+0x21C]  (empty)
    ///                                    ;   set     -> Drop
    ///
    /// 741435  cmp byte [+0x8C],0         ; OLDSKY/NEWSKY/MULSKY tri-state
    /// 74143C  jne 0x741470               ;   nonzero -> [vmt+0x21C]  (empty)
    /// 74143E  cmp byte [+0x76],0 / 741447 call sub_740300   ; ONLYDROPSPEC  (exclusive)
    /// 74144E  cmp byte [+0x77],0 / 741457 call sub_748D48   ; LIMITBAGITEMDROP (exclusive)
    /// 741461  call sub_73FC70            ; equip worker  ([+0x4C0] container)
    /// 741469  call sub_740078            ; bag   worker  ([+0x508] = m_ItemList)
    /// </code>
    ///
    /// <c>[vmt+0x21C]</c> is the "drop nothing" leaf. It resolves per class:
    /// <c>THumanKind</c>/<c>THeroAct</c> -> <c>sub_741620</c> (<c>55 8B EC 5D C2 08 00</c>
    /// = push ebp; mov ebp,esp; pop ebp; ret 8 — a genuinely empty virtual), while
    /// <c>TPlayer</c>/<c>TGdMsgGMAgent</c> -> <c>sub_6EB8CC</c>, a thunk that forwards its
    /// two stack args and tail-calls <c>sub_741620</c> (@0x6EB8D8). Either way: no drop.
    ///
    /// Map-flag offsets are from the parser <c>sub_774D98</c> (token -> <c>mov byte
    /// [ebx+d],v</c>): <c>SAFE</c>+0x5C, <c>FIGHT</c>+0x5D, <c>FIGHT3</c>+0x5E,
    /// <c>ONLYDROPSPEC</c>+0x76 (@0x775ADC), <c>LIMITBAGITEMDROP</c>+0x77 (@0x775B10),
    /// and the <c>+0x8C</c> tri-state <c>OLDSKY</c>=1 (@0x774FCE) / <c>NEWSKY</c>=2
    /// (@0x775003) / <c>MULSKY</c>=3 (@0x775033) — which is exactly C#'s
    /// <c>TMapFlag.SceneType</c>.
    /// </summary>
    internal static class NativeDeathDropPolicy
    {
        /// <summary>Which native leaf of <c>sub_741368</c> a death resolves to.</summary>
        internal enum Outcome
        {
            /// <summary><c>0x741470</c> / <c>0x741485</c> — <c>[vmt+0x21C]</c>, drop nothing.</summary>
            DropNothing = 0,

            /// <summary><c>0x74145E-0x741469</c> — <c>sub_73FC70</c> then <c>sub_740078</c>.</summary>
            NormalEquipThenBag = 1,

            /// <summary><c>0x741447</c> — <c>sub_740300</c> only, then <c>jmp 0x741498</c>.</summary>
            OnlyDropSpecWorker = 2,

            /// <summary><c>0x741457</c> — <c>sub_748D48</c> only, then <c>jmp 0x741498</c>.</summary>
            LimitBagItemDropWorker = 3,
        }

        /// <summary>
        /// <c>sub_741368</c> @0x7413F6-0x741496 evaluated over the map flags and the
        /// safe-zone predicate. <paramref name="inSafeZone"/> is the caller's
        /// <c>sub_76858C</c> result (C# <c>InNativeSafeZone12()</c>).
        /// </summary>
        internal static Outcome Resolve(TMapFlag flag, bool inSafeZone)
        {
            // A null map has no flags to read; native would have faulted on
            // [eax+0x128] == nil, so the C# equivalent of "no map" is no drop.
            if (flag == null) return Outcome.DropNothing;

            // 0x7413F6 / 0x7413FC / 0x741405 — the three legs that route to arbitration.
            var arbitrate = flag.boFightZone || flag.boFight3Zone || inSafeZone;

            if (arbitrate)
            {
                // 0x741417: ONLYDROPSPEC re-enables dropping on a FIGHT/FIGHT3/safe map.
                // 0x741426: so does LIMITBAGITEMDROP. Neither => 0x741485 (empty leaf).
                if (!flag.boONLYDROPSPEC && !flag.boLIMITBAGITEMDROP)
                {
                    return Outcome.DropNothing;
                }
            }

            // 0x741435: any non-zero sky tri-state suppresses the drop entirely
            // (the native test is `cmp byte [+0x8C],0 / jne`, NOT `== 1`, so NEWSKY(2)
            // and MULSKY(3) suppress it too).
            if (flag.SceneType != 0)
            {
                return Outcome.DropNothing;
            }

            // 0x74143E / 0x74144E — each special flag selects its OWN exclusive worker
            // and jumps past the normal pair; ONLYDROPSPEC is tested first.
            if (flag.boONLYDROPSPEC) return Outcome.OnlyDropSpecWorker;
            if (flag.boLIMITBAGITEMDROP) return Outcome.LimitBagItemDropWorker;

            return Outcome.NormalEquipThenBag;
        }
    }
}

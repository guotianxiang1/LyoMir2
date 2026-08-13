using System.Collections.Generic;

namespace GameSvr.Services
{
    /// <summary>
    /// 战神 hands its CM dispatcher the wire body LENGTH as a fourth parameter, and 39 of the
    /// 311 real handlers open with a test on it. C# had no carrier for that value at all, so
    /// every one of those gates was silently absent and a malformed short packet reached code
    /// that expects a body.
    ///
    /// Dispatcher signature (sub_6D7D68 prologue, the only call site is 0x6B1B36):
    ///   0x6D7D7E  89 4D F8              mov  [ebp-8],  ecx    ; ECX = body pointer (nil when len &lt;= 0)
    ///   0x6D7D81  8B DA                 mov  ebx, edx         ; EDX = the 12-byte header record
    ///   0x6D7D83  89 45 FC              mov  [ebp-4],  eax    ; EAX = self (TPlayObject)
    ///   0x6D7D86  8B 75 08              mov  esi, [ebp+8]     ; [ebp+8] = body length (4th param)
    ///   0x6D7D97  89 5D CC              mov  [ebp-0x34], ebx
    ///   0x6D7DA8  0F B7 FE              movzx edi, si         ; EDI = zero-extended low word of it
    /// and the caller builds that fourth parameter out of the queued packet node:
    ///   0x6B1B11  0F B7 73 08           movzx esi, word [node+8]   ; TOTAL wire length
    ///   0x6B1B15  83 EE 0C              sub  esi, 0x0C             ; minus the 12-byte header
    ///   0x6B1B18  85 F6 / 7E 0B         test esi,esi / jle 0x6B1B27
    ///   0x6B1B1C  8B 43 04 / 83 C0 0C   mov eax,[node+4] / add eax,0x0C  ; body = buffer + 12
    ///   0x6B1B27  33 C0                 xor eax,eax                ; ... or nil when there is none
    ///   0x6B1B2C  56                    push esi                   ; -> [ebp+8]
    ///   0x6B1B36  E8 2D 62 02 00        call 0x6D7D68
    ///
    /// So the value is exactly `wireTotalLength - 12`, i.e. the byte count GameGate handed over
    /// after the header — which in C# is `payload.Length` at the GateService ingress. Because
    /// every gate reads it through `si` or through `movzx edi,si`, they all see the LOW 16 BITS,
    /// which is why <see cref="Evaluate"/> masks with 0xFFFF before comparing.
    ///
    /// Every failing arm lands on the same code: 0x6DBC2C is `33 C0 / 5A / 59 / 59 /
    /// 64 89 10 / E9 D5 00 00 00 -> 0x6DBD0E`, and the `jae`-style arms fall through to a
    /// byte-identical copy of it. Failure is therefore always "drop the packet, return False" —
    /// no reply, no side effect, no log.
    /// </summary>
    public static class NativeClientBodyLengthGate
    {
        public enum GateKind
        {
            /// <summary>`66 85 F6 test si,si` + `0F 86 jbe default` — body must be non-empty.</summary>
            NonEmpty,
            /// <summary>`cmp si/edi,K` + `jb`-style reject — body must be at least K bytes.</summary>
            AtLeast,
            /// <summary>`83 FF 28 cmp edi,0x28` + `0F 85 jne default` — body must be exactly K bytes.</summary>
            Exactly,
        }

        public readonly struct Rule
        {
            public Rule(GateKind kind, int bound, int gateVa, string gateBytes, string text)
            {
                Kind = kind;
                Bound = bound;
                GateVa = gateVa;
                GateBytes = gateBytes;
                Text = text;
            }

            public GateKind Kind { get; }
            public int Bound { get; }
            /// <summary>VA of the `cmp`/`test` that reads the length.</summary>
            public int GateVa { get; }
            /// <summary>Bytes of the `cmp`/`test` + the conditional jump that follows it.</summary>
            public string GateBytes { get; }
            public string Text { get; }
        }

        private static Rule NonEmpty(int va, string bytes) =>
            new(GateKind.NonEmpty, 1, va, bytes, "test si,si / jbe 0x6DBC2C");

        private static Rule AtLeast(int bound, int va, string bytes, string text) =>
            new(GateKind.AtLeast, bound, va, bytes, text);

        // ident -> gate. Produced by walking the dispatch tree from 0x6D805C and scanning every
        // one of the 311 real handler labels for a read of esi/si/edi/di that happens before any
        // write to them; 64 handlers touch the value, and these 39 turn it into a branch.
        private static readonly Dictionary<int, Rule> s_rules = new()
        {
            // ---- body must be non-empty (16 idents) --------------------------------------
            [1011] = NonEmpty(0x6D8F14, "66 85 F6 / 0F 86 0F 2D 00 00"),   // CM_MERCHANTDLGSELECT
            [1014] = NonEmpty(0x6D8F94, "66 85 F6 / 0F 86 8F 2C 00 00"),   // CM_USERBUYITEM
            [1015] = NonEmpty(0x6D8FC8, "66 85 F6 / 0F 86 5B 2C 00 00"),   // CM_USERGETDETAILITEM
            [1020] = NonEmpty(0x6D9072, "66 85 F6 / 0F 86 B1 2B 00 00"),   // CM_CREATEGROUP
            [1021] = NonEmpty(0x6D909C, "66 85 F6 / 0F 86 87 2B 00 00"),   // CM_ADDGROUPMEMBER
            [1022] = NonEmpty(0x6D90C6, "66 85 F6 / 0F 86 5D 2B 00 00"),   // CM_DELGROUPMEMBER
            [1026] = NonEmpty(0x6D914B, "66 85 F6 / 0F 86 D8 2A 00 00"),   // CM_DEALADDITEM
            [1027] = NonEmpty(0x6D916D, "66 85 F6 / 0F 86 B6 2A 00 00"),   // CM_DEALDELITEM
            [1034] = NonEmpty(0x6D924A, "66 85 F6 / 0F 86 D9 29 00 00"),   // CM_USERMAKEDRUGITEM
            [1043] = NonEmpty(0x6D9284, "66 85 F6 / 0F 86 9F 29 00 00"),   // CM_ADJUST_BONUS
            [1048] = NonEmpty(0x6D92F6, "66 85 F6 / 0F 86 2D 29 00 00"),   // CM_DOSHOP
            [1061] = NonEmpty(0x6D9579, "66 85 F6 / 0F 86 AA 26 00 00"),   // (not implemented in C#)
            [1080] = NonEmpty(0x6D95D6, "66 85 F6 / 0F 86 4D 26 00 00"),   // (not implemented in C#)
            [3030] = NonEmpty(0x6DA1B1, "66 85 F6 / 0F 86 72 1A 00 00"),   // CM_SAY
            [4616] = NonEmpty(0x6DB79D, "66 85 F6 / 0F 86 86 04 00 00"),   // CM_FIND_CORPS_BYNAME
            [4617] = NonEmpty(0x6DB7C7, "66 85 F6 / 0F 86 5C 04 00 00"),   // CM_FIND_GILD_BYNAME

            // ---- body must hold the fixed-width prefix the handler dereferences -----------
            // 3306 reads `mov ecx,[body]` at 0x6DAB56 right after the gate, hence 4.
            [3306] = AtLeast(4, 0x6DAB39, "66 83 FE 04 / 0F 82 E9 10 00 00", "cmp si,4 / jb 0x6DBC2C"),
            // 1350 hands the body to sub_6F09C4, which reads [body+0x18] and [body+0x1C].
            [1350] = AtLeast(0x20, 0x6DAC8E, "83 FF 20 / 0F 82 95 0F 00 00", "cmp edi,0x20 / jb 0x6DBC2C"),
            [1355] = AtLeast(0x0C, 0x6DAD08, "83 FF 0C / 0F 82 1B 0F 00 00", "cmp edi,0xC / jb 0x6DBC2C"),

            // ---- the 64-bit id families: two dwords read straight out of the body ---------
            // 4535/4536 reject with `jb 0x6DBC2C`; the rest use `jae <body>` and fall through
            // into a byte-identical copy of 0x6DBC2C. Same observable behaviour either way.
            [4522] = AtLeast(8, 0x6DB3BC, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB3CE"),
            [4525] = AtLeast(8, 0x6DB3FD, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB40F"),
            [4527] = AtLeast(8, 0x6DB44B, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB45D"),
            [4528] = AtLeast(8, 0x6DB472, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB484"),
            [4529] = AtLeast(8, 0x6DB499, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB4AB"),
            [4535] = AtLeast(8, 0x6DB51D, "83 FF 08 / 0F 82 06 07 00 00", "cmp edi,8 / jb 0x6DBC2C"),
            [4536] = AtLeast(8, 0x6DB53E, "83 FF 08 / 0F 82 E5 06 00 00", "cmp edi,8 / jb 0x6DBC2C"),
            [4540] = AtLeast(8, 0x6DB5B7, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB5C9"),
            [4560] = AtLeast(8, 0x6DB5DE, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB5F0"),
            [4567] = AtLeast(8, 0x6DB69A, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB6AC"),
            [4568] = AtLeast(8, 0x6DB6C1, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB6D3"),
            [4569] = AtLeast(8, 0x6DB6E8, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB6FA"),
            [4572] = AtLeast(8, 0x6DB745, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB757"),
            [4573] = AtLeast(8, 0x6DB7F1, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB803"),
            [4574] = AtLeast(8, 0x6DB824, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB836"),
            [4576] = AtLeast(8, 0x6DB866, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB878"),
            [4578] = AtLeast(8, 0x6DB8EB, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB8FD"),
            [4579] = AtLeast(8, 0x6DB912, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB924"),
            [4611] = AtLeast(8, 0x6DB771, "83 FF 08 / 73 0D", "cmp edi,8 / jae 0x6DB783"),

            // ---- the one exact-width frame ------------------------------------------------
            // 0x6DAEE2.. reads ShortString@+0, ShortString@+0x10, dword@+0x20, dword@+0x24 = 0x28.
            [3410] = new Rule(GateKind.Exactly, 0x28, 0x6DAED9,
                "83 FF 28 / 0F 85 4A 0D 00 00", "cmp edi,0x28 / jne 0x6DBC2C"),
        };

        public static IReadOnlyDictionary<int, Rule> Rules => s_rules;

        public static bool TryGetRule(int ident, out Rule rule) => s_rules.TryGetValue(ident, out rule);

        /// <summary>
        /// True when 战神 would let this ident through with this many body bytes. Idents with no
        /// gate always pass. `bodyLength` is the wire byte count after the 12-byte header, i.e.
        /// the same quantity the native caller computes at 0x6B1B11-0x6B1B15.
        /// </summary>
        public static bool Allows(int ident, int bodyLength)
        {
            if (!s_rules.TryGetValue(ident, out var rule)) return true;
            return Evaluate(rule, bodyLength);
        }

        public static bool Evaluate(Rule rule, int bodyLength)
        {
            // Native never sees more than the low word: the `si` gates compare the 16-bit
            // register and the `edi` gates compare `movzx edi,si`. A 0x10000-byte body would
            // read as 0 in both. GameGate caps frames long before that, but the mask keeps the
            // model exact instead of accidentally-correct.
            var w = bodyLength & 0xFFFF;
            return rule.Kind switch
            {
                GateKind.NonEmpty => w != 0,
                GateKind.Exactly => w == rule.Bound,
                _ => w >= rule.Bound,
            };
        }
    }
}

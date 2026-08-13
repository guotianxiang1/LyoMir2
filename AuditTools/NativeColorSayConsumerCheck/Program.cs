// 彩色文字 (ColorSay) CONSUMER audit -- sub_6C9354, the say-path half.
//
// The granter and the rec-0xD5 persistence are covered by NativeColorSayCheck.
// This audit covers what makes the feature observable, and specifically the four
// facts a naive port gets wrong.
//
// EVIDENCE (战神 flat image, ImageBase 0x400000):
//
//   6C93FF  cmp dword [esi+0xBD4],0     ; the gate is the COUNTDOWN, not the tier
//   6C9406  jne 0x6C9442                ; -> coloured route
//   6C9429  push 0xFF00                 ; plain colour token
//   6C9432  mov cx,0x28                 ; plain ident = 40
//   6C9442  mov al,byte [esi+0xB86]     ; the tier
//   6C9448  cmp al,1 / 6C944C mov ax,0xFFF5
//   6C9454  cmp al,2 / 6C9456 mov ax,0xFFFA
//   6C945C  mov ax,0xFF01               ; EVERY other tier, tier 0 INCLUDED
//   6C9485  mov cx,0x69                 ; coloured ident = 105
//
// IDENT 105 IS A REAL DEDICATED OPCODE, not a reused constant. Exhaustive
// immediate scan over the whole image:
//   mov cx,0x69 (66 b9 69 00) : 1 hit -> 0x6C9485
//   mov dx,0x69 (66 ba 69 00) : 1 hit -> 0x6B4B51 (the RM 10033 handler)
//   mov ax,0x69 (66 b8 69 00) : 0 hits
//
// COLOURED SAY BYPASSES BLOCK-PUBLIC-CHAT, proven twice independently:
//   ident 40  filtered at 0x6B4A63 `test byte [eax+0xB9C],2 / jne` AND again per
//             recipient at 0x6DC092 (reached via 0x6DC07E `sub dx,0x28 / je`)
//   ident 105 filtered by NEITHER -- 0x6B4B3C has no 0xB9C test, and sub_6DC068's
//             ident ladder only knows {0x28,0x66,0x68} = {40,102,104}
//             (0x6DC07E / 0x6DC084 `sub dx,0x3E` / 0x6DC08A `sub dx,2`), so 105
//             falls through to the deliver exit at 0x6DC0BB.
//
// THE COLOUR IS AN OPAQUE 16-BIT TOKEN server-side. Every hop moves all 16 bits
// atomically (0x769B39 / 0x7652FA / 0x765E99 / 0x6B4B3C / 0x6D7C75); an
// image-wide scan for `shr ax,8` (66 c1 e8 08) returns 2 hits total and
// `and ax,0xFF` (66 25 ff 00) returns 1, none on this path. So the FColor/BColor
// split is a CLIENT-side reading; this audit asserts the C# split round-trips
// rather than asserting the server understands the halves.

using System.Reflection;
using System.Text;

namespace NativeColorSayConsumerCheck
{
    internal static class Program
    {
        private static int _assertions;
        private static readonly List<string> Failures = new();

        private static void True(bool condition, string what)
        {
            _assertions++;
            if (!condition) Failures.Add(what);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            _assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                Failures.Add($"{what}: expected={expected} actual={actual}");
        }

        private static void NotEqual<T>(T unexpected, T actual, string what)
        {
            _assertions++;
            if (EqualityComparer<T>.Default.Equals(unexpected, actual))
                Failures.Add($"{what}: must not be {unexpected}");
        }

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                PrepareRuntimeConfig();
                InitializeRuntime();
                CheckIdentConstants();
                CheckColourConstants();
                CheckTierSelectHasNoTierZeroGuard();
                CheckGateReadsCountdownNotTier();
                CheckColourTokenSplitRoundTrips();
                CheckConsumerIsWiredIntoTheSayPath();
                CheckColourHearBypassesChatShield();
                CheckPlainRouteStillMatchesNative();
            }
            catch (Exception ex)
            {
                Failures.Add("unexpected exception: " + ex);
            }

            if (Failures.Count == 0)
            {
                Console.WriteLine(
                    $"AUDIT_PASS NativeColorSayConsumerCheck {_assertions} assertions");
                Console.WriteLine(
                    "  gate 0x6C93FF reads the COUNTDOWN obj+0xBD4, not the tier "
                    + "obj+0xB86 (which nothing ever clears)");
                Console.WriteLine(
                    "  ident 40 -> 105 on the coloured route; 105 is dedicated "
                    + "(only 2 immediates image-wide) and is NOT chat-shielded");
                Console.WriteLine(
                    "  tier 1/2 -> 0xFFF5/0xFFFA, EVERY other tier incl. 0 -> "
                    + "0xFF01 (no tier-0 guard at 0x6C9442)");
                return 0;
            }

            Console.WriteLine("AUDIT_FAIL NativeColorSayConsumerCheck");
            foreach (var failure in Failures) Console.WriteLine("  - " + failure);
            return 1;
        }

        // The GameSvr static ctor reads config files relative to the process
        // directory; the audit OutputPath sits outside the repo, so seed minimal
        // stubs before touching any GameSvr type.
        private static void PrepareRuntimeConfig()
        {
            var directory = AppContext.BaseDirectory;
            File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
            File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
            File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
            var share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
            Directory.CreateDirectory(share);
            File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
                "[PlayerLevelExp]");
            File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
        }

        private static void InitializeRuntime()
        {
            GameSvr.M2Share.g_Config = new GameSvr.GameSvrConfig();
            GameSvr.M2Share.ObjectManager = new GameSvr.ObjectManager();
            GameSvr.M2Share.ProcessMsgCriticalSection = new object();
            GameSvr.M2Share.ProcessHumanCriticalSection = new object();
            GameSvr.M2Share.LogMsgCriticalSection = new object();
            GameSvr.M2Share.LogStringList = new System.Collections.ArrayList();
        }

        // ---------- reflection plumbing ----------

        private static Type PlayObjectType => typeof(GameSvr.TPlayObject);

        private static int ConstInt(string name)
        {
            var field = PlayObjectType.GetField(name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (field == null) throw new MissingFieldException(name);
            return Convert.ToInt32(field.GetRawConstantValue());
        }

        private static GameSvr.TPlayObject NewPlayer() =>
            (GameSvr.TPlayObject)Activator.CreateInstance(
                PlayObjectType, nonPublic: true);

        private static FieldInfo Field(string name)
        {
            var field = PlayObjectType.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic
                                    | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(name);
            return field;
        }

        private static MethodInfo Method(string name)
        {
            var method = PlayObjectType.GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Public
                                       | BindingFlags.Instance);
            if (method == null) throw new MissingMethodException(name);
            return method;
        }

        private static ushort ColorForTier(byte tier) =>
            (ushort)PlayObjectType.GetMethod("NativeColorSayColorForTier",
                    BindingFlags.NonPublic | BindingFlags.Public
                                           | BindingFlags.Static)!
                .Invoke(null, new object[] { tier })!;

        private static string SourceOf(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { RepoRoot() }
                .Concat(parts).ToArray()));

        // ---------- checks ----------

        private static void CheckIdentConstants()
        {
            // 0x6C9432 mov cx,0x28 / 0x6C9485 mov cx,0x69
            Equal(40, SystemModule.Grobal2.SM_HEAR,
                "plain say ident is 0x28 = 40 (0x6C9432)");
            Equal(105, SystemModule.Grobal2.SM_COLORHEAR,
                "coloured say ident is 0x69 = 105 (0x6C9485)");
            NotEqual(SystemModule.Grobal2.SM_HEAR,
                SystemModule.Grobal2.SM_COLORHEAR,
                "the ident MUST change on the coloured route -- reusing SM_HEAR "
                + "would also silently re-enable the chat shield");
        }

        private static void CheckColourConstants()
        {
            Equal((ushort)0xFF00, (ushort)ConstInt("NativeSayColorPlain"),
                "0x6C9429 push 0xFF00");
            Equal((ushort)0xFFF5, (ushort)ConstInt("NativeSayColorTier1"),
                "0x6C944C mov ax,0xFFF5");
            Equal((ushort)0xFFFA, (ushort)ConstInt("NativeSayColorTier2"),
                "0x6C9456 mov ax,0xFFFA");
            Equal((ushort)0xFF01, (ushort)ConstInt("NativeSayColorTierOther"),
                "0x6C945C mov ax,0xFF01");

            // the three tier tokens must be mutually distinct, else the feature is
            // invisible even with the right ident
            var tokens = new[] { 0xFFF5, 0xFFFA, 0xFF01 };
            Equal(3, tokens.Distinct().Count(),
                "the three tier colours must be distinct");

            // and none of them may equal the plain token
            foreach (var t in tokens)
                NotEqual((ushort)0xFF00, (ushort)t,
                    $"tier colour 0x{t:X4} must differ from the plain 0xFF00");
        }

        private static void CheckTierSelectHasNoTierZeroGuard()
        {
            Equal((ushort)0xFFF5, ColorForTier(1), "tier 1 -> 0xFFF5 (0x6C944C)");
            Equal((ushort)0xFFFA, ColorForTier(2), "tier 2 -> 0xFFFA (0x6C9456)");

            // THE trap: 0x6C9442 tests 1 then 2 and falls through. There is no
            // tier-0 case and no range check, so tier 0 is a COLOUR, not "plain".
            Equal((ushort)0xFF01, ColorForTier(0),
                "tier 0 falls into the else leg and yields 0xFF01 -- native has NO "
                + "tier-0 guard, so a port that treats 0 as 'no colour' diverges");
            NotEqual((ushort)0xFF00, ColorForTier(0),
                "tier 0 must NOT map to the plain 0xFF00 token");

            // every other byte value, including the granter's byte-wraparound
            // results (0x786843 `sub al,0x16` wraps), lands on the same leg
            foreach (byte tier in new byte[] { 3, 4, 0x16, 0x80, 0xEA, 0xFF })
                Equal((ushort)0xFF01, ColorForTier(tier),
                    $"tier {tier} -> 0xFF01 (no clamp, no range check at 0x6C9442)");

            // exhaustive: exactly two byte values are special, no more
            var special = Enumerable.Range(0, 256)
                .Where(t => ColorForTier((byte)t) != 0xFF01).ToArray();
            Equal(2, special.Length,
                "exactly two tier values (1 and 2) may be special across the whole "
                + "byte domain");
            True(special.SequenceEqual(new[] { 1, 2 }),
                "the two special tiers must be 1 and 2");
        }

        private static void CheckGateReadsCountdownNotTier()
        {
            var isActive = Method("NativeColorSayIsActive");
            var current = Method("NativeColorSayCurrentColor");

            // tier set but countdown zero -> PLAIN. This is the sticky-tier case:
            // obj+0xB86 has no clearer anywhere in the image (exhaustive scan of
            // displacement 86 0B 00 00 finds 4 instructions, 2 reads 2 writes),
            // so a tier survives expiry and relogin. Gating on the tier instead of
            // the countdown would colour speech forever.
            var expired = NewPlayer();
            Field("m_btNativeColorSayTier").SetValue(expired, (byte)2);
            Field("m_nNativeThirdBuffSeconds").SetValue(expired, 0);
            Equal(false, (bool)isActive.Invoke(expired, null)!,
                "countdown 0 with a live tier byte is INACTIVE (0x6C93FF tests "
                + "obj+0xBD4, not obj+0xB86)");
            Equal((ushort)0xFF00, (ushort)current.Invoke(expired, null)!,
                "an expired buff speaks with the plain token even though the tier "
                + "byte still says 2");

            // countdown set but tier zero -> COLOURED, with the else-leg token
            var zeroTier = NewPlayer();
            Field("m_btNativeColorSayTier").SetValue(zeroTier, (byte)0);
            Field("m_nNativeThirdBuffSeconds").SetValue(zeroTier, 1);
            Equal(true, (bool)isActive.Invoke(zeroTier, null)!,
                "a non-zero countdown is ACTIVE regardless of tier");
            Equal((ushort)0xFF01, (ushort)current.Invoke(zeroTier, null)!,
                "active + tier 0 -> 0xFF01, not plain");

            // 0x6C93FF is `cmp ...,0 / jne`, i.e. ANY non-zero value activates --
            // including a negative one, which the DB load can leave behind if the
            // range check is bypassed. Not `> 0`.
            var negative = NewPlayer();
            Field("m_nNativeThirdBuffSeconds").SetValue(negative, -1);
            Equal(true, (bool)isActive.Invoke(negative, null)!,
                "0x6C9406 is `jne`, so a NEGATIVE countdown also activates -- a "
                + "`> 0` test would diverge");
        }

        private static void CheckColourTokenSplitRoundTrips()
        {
            // The server never splits the token (proven negative: no `and ax,0xFF`
            // / `shr ax,8` / byte store on the path). C# has to split it to reach
            // its byte-pair wire API, so assert the split is lossless.
            foreach (var token in new ushort[] { 0xFF00, 0xFFF5, 0xFFFA, 0xFF01 })
            {
                var fColor = unchecked((byte)(token & 0xFF));
                var bColor = unchecked((byte)(token >> 8));
                Equal(token,
                    (ushort)SystemModule.HUtil32.MakeWord(fColor, bColor),
                    $"0x{token:X4} must survive the split/repack round trip");
                // BColor is 0xFF in all four -- the shared "transparent background"
                // slot. If a token ever loses it, the split is wrong.
                Equal((byte)0xFF, bColor,
                    $"0x{token:X4} high byte is 0xFF in all four native constants");
            }

            // and the tier-1/2 low bytes are the distinguishing palette indices
            Equal((byte)0xF5, unchecked((byte)(0xFFF5 & 0xFF)), "tier 1 FColor 245");
            Equal((byte)0xFA, unchecked((byte)(0xFFFA & 0xFF)), "tier 2 FColor 250");
            Equal((byte)0x01, unchecked((byte)(0xFF01 & 0xFF)), "else FColor 1");
            Equal((byte)0x00, unchecked((byte)(0xFF00 & 0xFF)), "plain FColor 0");
        }

        private static void CheckConsumerIsWiredIntoTheSayPath()
        {
            // A dormant consumer is worthless: the say path must actually consult
            // the gate, on a non-comment line, and must send the coloured ident.
            var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "GameSvr",
                "Players", "TPlayObject.Chat.cs"));
            var gateLine = -1;
            var sendLine = -1;
            var plainLine = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*") ||
                    code.StartsWith("/*")) continue;
                if (code.Contains("NativeColorSayIsActive()")) gateLine = i;
                if (gateLine >= 0 && sendLine < 0 &&
                    code.Contains("Grobal2.RM_COLORHEAR")) sendLine = i;
                if (code.Contains("Grobal2.RM_HEAR,")) plainLine = i;
            }

            True(gateLine >= 0,
                "ProcessSayMsg must consult NativeColorSayIsActive() on a live line "
                + "-- a commented-out call does not count");
            True(sendLine > gateLine,
                "the coloured send must sit inside the gate's branch");
            True(plainLine > sendLine,
                "the coloured branch must come BEFORE the plain RM_HEAR send, "
                + "mirroring 0x6C9406's early divert");

            // The gate condition must be the call ALONE. `&& false`, `&& x` or a
            // negation all leave the call textually present while making the branch
            // unreachable or conditional on something native does not test -- the
            // exact hole a call-anywhere scan walks through.
            if (gateLine >= 0)
            {
                var cond = lines[gateLine].Trim();
                Equal("if (NativeColorSayIsActive())", cond,
                    "the divert must be gated on NativeColorSayIsActive() ALONE "
                    + "(0x6C93FF/0x6C9406 test only obj+0xBD4) -- no extra "
                    + "conjunct, no negation");
            }

            // Both sends inside the branch must carry the coloured RM, and the
            // colour bytes must come from the split, not from the config defaults.
            if (gateLine >= 0 && plainLine > gateLine)
            {
                var branch = lines.Skip(gateLine).Take(plainLine - gateLine)
                    .Select(l => l.TrimStart())
                    .Where(l => !l.StartsWith("//")).ToArray();
                var sends = branch.Where(l => l.Contains("SendMsg(")
                                             || l.Contains("SendRefMsg(")).ToArray();
                Equal(2, sends.Length,
                    "the coloured branch has exactly two sends (the self-only "
                    + "filtered case and the broadcast case)");
                foreach (var send in sends)
                {
                    True(send.Contains("Grobal2.RM_COLORHEAR"),
                        "every send inside the coloured branch must use "
                        + "RM_COLORHEAR -- reusing RM_HEAR silently restores the "
                        + "chat shield and the wrong ident: " + send);
                    True(!send.Contains("btHearMsgFColor"),
                        "the coloured branch must not fall back to the plain "
                        + "config colour: " + send);
                    True(send.Contains("fColor, bColor"),
                        "the coloured branch must pass the split token: " + send);
                }

                // and the split itself must keep both halves live
                var split = string.Join("\n", branch);
                True(split.Contains("(byte)(color & 0xFF)"),
                    "the low byte must come from the token");
                True(split.Contains("(byte)(color >> 8)"),
                    "the high byte must come from the token -- hardcoding it loses "
                    + "the 0xFF transparent-background slot every native constant "
                    + "carries");
            }

            // The message-side case must exist AND be a live case label on the RM
            // constant -- a renumbered/unreachable label leaves the RM undelivered.
            var msgLines = File.ReadAllLines(Path.Combine(RepoRoot(), "GameSvr",
                "Players", "TPlayObject.Message.cs"));
            var caseLabels = 0;
            var emitLine = -1;
            var lastCaseLine = -1;
            for (var i = 0; i < msgLines.Length; i++)
            {
                var code = msgLines[i].TrimStart();
                if (code.StartsWith("//")) continue;
                if (code == "case Grobal2.RM_COLORHEAR:")
                {
                    caseLabels++;
                    lastCaseLine = i;
                }
                if (code.Contains("Grobal2.SM_COLORHEAR")) emitLine = i;
            }

            Equal(2, caseLabels,
                "RM_COLORHEAR needs TWO live case labels in ProcessMsg: one to "
                + "enter the say-message group and one to select its packet");
            True(emitLine > 0,
                "the translation must emit SM_COLORHEAR (105), not SM_HEAR");
            True(lastCaseLine >= 0 && emitLine > lastCaseLine,
                "the SM_COLORHEAR emit must sit under a live RM_COLORHEAR case "
                + "label, not merely somewhere in the file");
        }

        private static void CheckColourHearBypassesChatShield()
        {
            // The shield test must be scoped to RM_HEAR alone. Native proves the
            // asymmetry twice (0x6B4A63 vs the absent test at 0x6B4B3C; and
            // sub_6DC068's {40,102,104} ident ladder), so if a porter widens the
            // C# shield test to cover RM_COLORHEAR the feature silently regains a
            // filter native does not have.
            var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "GameSvr",
                "Players", "TPlayObject.Message.cs"));
            var shieldLines = new List<string>();
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//")) continue;
                if (code.Contains("m_dwChatShieldMask & 0x02"))
                {
                    // gather the whole condition, which spans two lines
                    shieldLines.Add(lines[Math.Max(0, i - 1)].Trim() + " "
                                    + code);
                }
            }

            True(shieldLines.Count > 0,
                "the block-public-chat test must still exist for RM_HEAR");
            foreach (var cond in shieldLines)
            {
                True(cond.Contains("RM_HEAR"),
                    "the shield test must be scoped to RM_HEAR");
                True(!cond.Contains("RM_COLORHEAR"),
                    "RM_COLORHEAR must NOT be subject to the block-public-chat "
                    + "bit -- 0x6B4B3C has no obj+0xB9C test and sub_6DC068 does "
                    + "not recognise ident 105");
            }
        }

        private static void CheckPlainRouteStillMatchesNative()
        {
            // Regression guard: the plain token must remain reachable and must
            // still equal 0xFF00 once packed from the config bytes, which is what
            // 0x6C9429 pushes. If someone retunes the config defaults the plain
            // route stops matching native.
            GameSvr.M2Share.g_Config = new GameSvr.GameSvrConfig();
            var packed = (ushort)SystemModule.HUtil32.MakeWord(
                GameSvr.M2Share.g_Config.btHearMsgFColor,
                GameSvr.M2Share.g_Config.btHearMsgBColor);
            Equal((ushort)0xFF00, packed,
                "the config's default hear colour must pack to 0xFF00, the token "
                + "0x6C9429 pushes on the plain route");

            var plain = NewPlayer();
            Field("m_nNativeThirdBuffSeconds").SetValue(plain, 0);
            Equal((ushort)0xFF00,
                (ushort)Method("NativeColorSayCurrentColor").Invoke(plain, null)!,
                "an unbuffed player speaks with 0xFF00");
        }

                private static string RepoRoot()
        {
            return AuditRepoRoot.Resolve();
        }
    }
}

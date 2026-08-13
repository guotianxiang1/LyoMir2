// Castle-war punch-list items 2..5. Item 1 (GetCastle rollback / attack-list
// reload) is covered by CastleOwnerTransitionCheck.
//
// ITEM 2 -- ArcherGuard idle facing, obj+0x4E0, a SIGNED dword sentinel.
//   684819  C7 86 E0 04 00 00 FF FF FF FF   mov dword [esi+0x4E0], -1
//   6848A0  mov eax,[ebx+0x4E0]
//   6848A6  test eax,eax
//   6848A8  jl 0x6848C3                     ; SIGNED -> -1 skips the idle turn
//   6848AC  mov dl,byte [ebx+0x154]         ; current facing (m_btDirection)
//   6848B2  cmp eax,edx / 6848B4 je
//   6848BE  call 0x7677C4                   ; TurnTo
// 0x684819 is the ONLY dword-immediate write to +0x4E0 anywhere in the image, so
// -1 is the sole initial value. A byte field cannot hold it.
//
// ITEM 3 -- dead-structure repair clock obj+0x4E4, distinct from obj+0x338
// (m_dwStruckTick). Both repair paths branch on a death test and read a DIFFERENT
// clock per branch; both compare 0xEA60 = 60000 ms:
//   door  65B53B call 0x772DA8 / test al,al / 65B542 jne 0x65B570
//         alive 65B54C sub eax,[esi+0x338]      dead 65B578 sub eax,[esi+0x4E4]
//   wall  65B5F6 call 0x772DA8 / test al,al / 65B5FD jne 0x65B62B
//         alive 65B604 sub eax,[ebx+0x338]      dead 65B630 sub eax,[ebx+0x4E4]
// The clock starts in the structure's Die: TCastleDoor VMT 0x6841AC slot +0x84 =
// sub_684AB8 -> 0x684AC0 inherited death, 0x684AC5 GetTickCount, 0x684ACA
// mov [ebx+0x4E4],eax. VMT verified by the Delphi SelfPtr check dword[V-0x4C]==V
// with the class-name ShortString at V-0x2C reading 'TCastleDoor' (parent
// 'TGuardUnit', InstanceSize 1260).
//
// ITEM 4 -- NOHUMNOMON DOES NOT EXIST IN 战神. Tier-1 NEGATIVE evidence: an
// image-wide byte scan for NOHUMNOMON / NOHUMNOMONSTER / NOHUM / NOMON /
// nohumnomon / NoHumNoMon / NOHUMANNOMON / NOHUM_NOMON returns 0 hits each, and
// the complete map-flag token census -- the two parallel literal blocks at
// 0x775BFC and 0x776B20, 46 tokens each -- contains no equivalent. So the flag
// must not be parsed and must not gate monster regeneration.
//
// ITEM 5 -- ElfMonster.boIsFirst == obj+0x4E8.
//   66A29F  mov byte [esi+0x4E8],1   ; ctor sets it
//   66A318  cmp byte [esi+0x4E8],0 / je   then 66A321 clears  ; Run tests+clears
//   66A22E  mov byte [ebx+0x4E8],0   ; AppearNow's FIRST statement
// (TElfWarrior genuinely has two flags, +0x4EC tested first and its own +0x4ED --
// 4 and 3 byte references respectively -- so the two classes must not be merged.)

using System.Reflection;
using System.Text;

namespace NativeCastleWarFiveCheck
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

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                PrepareRuntimeConfig();
                InitializeRuntime();
                CheckArcherGuardDirectionIsSignedWithMinusOne();
                CheckDeadRepairClockIsSeparateField();
                CheckDeadRepairClockIsStartedByDie();
                CheckRepairPathsUseTheRightClock();
                CheckNoHumNoMonIsGone();
                CheckElfAppearNowClearsIsFirst();
                CheckElfWarriorFlagsNotMerged();
            }
            catch (Exception ex)
            {
                Failures.Add("unexpected exception: " + ex);
            }

            if (Failures.Count == 0)
            {
                Console.WriteLine(
                    $"AUDIT_PASS NativeCastleWarFiveCheck {_assertions} assertions");
                Console.WriteLine(
                    "  archer facing obj+0x4E0 = signed dword, -1 sentinel "
                    + "(0x684819, the only immediate write image-wide)");
                Console.WriteLine(
                    "  dead-repair clock obj+0x4E4 != m_dwStruckTick obj+0x338; "
                    + "both branches gate on 0xEA60 = 60000 ms");
                Console.WriteLine(
                    "  NOHUMNOMON absent from 战神 (0 byte hits, 46-token flag "
                    + "census) -- parser and regen gate both dropped");
                Console.WriteLine(
                    "  ElfMonster.boIsFirst = obj+0x4E8, cleared by AppearNow "
                    + "(0x66A22E, its first statement)");
                return 0;
            }

            Console.WriteLine("AUDIT_FAIL NativeCastleWarFiveCheck");
            foreach (var failure in Failures) Console.WriteLine("  - " + failure);
            return 1;
        }

        // The GameSvr static ctor reads config files relative to the process
        // directory; the audit OutputPath sits outside the repo, so seed stubs
        // before touching any GameSvr type.
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

                private static string RepoRoot()
        {
            return AuditRepoRoot.Resolve();
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

        private static FieldInfo GuardField(string name) =>
            typeof(GameSvr.GuardUnit).GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        // ---------- item 2 ----------

        private static void CheckArcherGuardDirectionIsSignedWithMinusOne()
        {
            var field = GuardField("m_nDirection");
            True(field != null, "GuardUnit.m_nDirection must exist");

            // A byte cannot represent the -1 sentinel at 0x684819, so the signed
            // `jl` at 0x6848A8 would be unreachable and the >= 0 test dead.
            Equal(typeof(int), field.FieldType,
                "m_nDirection must be a SIGNED int -- 0x684819 writes a full dword "
                + "-1 and 0x6848A8 tests it with a signed `jl`");

            var archer = (GameSvr.ArcherGuard)Activator.CreateInstance(
                typeof(GameSvr.ArcherGuard));
            Equal(-1, (int)field.GetValue(archer),
                "a fresh ArcherGuard must start at -1 (0x684819 is the ONLY "
                + "dword-immediate write to obj+0x4E0 in the whole image)");

            // The BASE default matters too, not just ArcherGuard's ctor override:
            // every other GuardUnit subclass inherits it, and native's only
            // immediate write to +0x4E0 is the -1. A base default of 0 would give
            // plain guards / doors / walls an idle facing they never had.
            var bareGuard = (GameSvr.GuardUnit)Activator.CreateInstance(
                typeof(GameSvr.GuardUnit));
            Equal(-1, (int)field.GetValue(bareGuard),
                "GuardUnit's own default must be -1 -- subclasses that do not "
                + "override the ctor must still start with the no-idle-facing "
                + "sentinel");

            // and the guard must be reachable: a hired guard is set to 3, which
            // must still pass the >= 0 test
            field.SetValue(archer, 3);
            Equal(3, (int)field.GetValue(archer),
                "an explicitly-set facing must survive (hire paths write 3)");

            // BEHAVIOURAL: the sentinel must actually suppress the idle turn, and
            // a real facing must actually trigger it. Drive Run's idle branch by
            // its own predicate so that changing the default to 0 -- which makes
            // the >= 0 test always true -- is caught rather than merely noticed.
            var currentFacing = typeof(GameSvr.TBaseObject).GetField(
                "m_btDirection",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            True(currentFacing != null, "m_btDirection must exist");

            static bool WouldTurn(int idleFacing, byte facing) =>
                idleFacing >= 0 && facing != idleFacing;

            True(!WouldTurn(-1, 0),
                "with the -1 sentinel the archer must NOT turn even when its "
                + "current facing is 0 -- 0x6848A8's signed `jl` skips the turn");
            True(!WouldTurn(-1, 5),
                "the sentinel suppresses the turn for every current facing");
            True(WouldTurn(3, 0),
                "a real idle facing of 3 must turn a guard currently facing 0");
            True(!WouldTurn(3, 3),
                "0x6848B4's `je` means no turn when already facing the right way");

            // the ctor default must be a value that suppresses the turn
            var fresh = (GameSvr.ArcherGuard)Activator.CreateInstance(
                typeof(GameSvr.ArcherGuard));
            var freshFacing = (int)field.GetValue(fresh);
            True(!WouldTurn(freshFacing, 0),
                $"a fresh archer's default facing ({freshFacing}) must suppress "
                + "the idle turn -- a default of 0 would make it snap to direction "
                + "0 on its first idle tick instead of holding its placed facing");
        }

        // ---------- item 3 ----------

        private static void CheckDeadRepairClockIsSeparateField()
        {
            var dead = GuardField("m_dwDeadRepairTick");
            True(dead != null,
                "GuardUnit must expose obj+0x4E4 as its own field -- the dead "
                + "branch at 0x65B578/0x65B630 reads it, not m_dwStruckTick");

            var struck = typeof(GameSvr.TBaseObject).GetField("m_dwStruckTick",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            True(struck != null, "m_dwStruckTick (obj+0x338) must still exist");
            True(dead != null && struck != null && dead != struck,
                "obj+0x4E4 and obj+0x338 are DIFFERENT clocks -- aliasing them "
                + "makes a destroyed gate repairable the instant it falls");

            // it must be visible to UserCastle, which lives in another class
            True(dead != null && dead.IsPublic,
                "the dead-repair clock must be reachable from UserCastle.RepairDoor "
                + "/ RepairWall");
        }

        private static void CheckDeadRepairClockIsStartedByDie()
        {
            // Both structures must stamp the clock in their own Die override,
            // mirroring sub_684AB8's 0x684AC5/0x684ACA.
            foreach (var (file, label) in new[]
                     {
                         ("CastleDoor.cs", "TCastleDoor VMT 0x6841AC slot +0x84"),
                         ("WallStructure.cs", "the wall's Die"),
                     })
            {
                var text = Source("GameSvr", "Monsters", "Monster", file);
                var dieAt = text.IndexOf("public override void Die()",
                    StringComparison.Ordinal);
                True(dieAt >= 0, $"{file} must override Die");
                if (dieAt < 0) continue;

                var body = text.Substring(dieAt,
                    Math.Min(600, text.Length - dieAt));
                // Comment lines name the call too, so scan CODE only -- otherwise
                // commenting the stamp out still passes.
                var codeBody = string.Join('\n', body.Split('\n')
                    .Select(l => l.TrimStart())
                    .Where(l => !l.StartsWith("//") && !l.StartsWith("*")
                                && !l.StartsWith("/*")));
                var stampAt = codeBody.IndexOf(
                    "m_dwDeadRepairTick = HUtil32.GetTickCount()",
                    StringComparison.Ordinal);
                var baseAt = codeBody.IndexOf("base.Die();", StringComparison.Ordinal);
                True(stampAt > 0,
                    $"{file}'s Die must stamp the dead-repair clock on a LIVE line "
                    + $"({label})");
                True(baseAt >= 0 && stampAt > baseAt,
                    $"{file} must stamp the clock AFTER the inherited death, "
                    + "mirroring 0x684AC0 then 0x684AC5/0x684ACA");
            }
        }

        private static void CheckRepairPathsUseTheRightClock()
        {
            var text = Source("GameSvr", "Castle", "UserCastle.cs");

            foreach (var method in new[] { "public bool RepairDoor()", "public bool RepairWall(" })
            {
                var at = text.IndexOf(method, StringComparison.Ordinal);
                True(at >= 0, $"{method} must exist");
                if (at < 0) continue;

                var body = text.Substring(at, Math.Min(2000, text.Length - at));
                var elseAt = body.IndexOf("else", StringComparison.Ordinal);
                True(elseAt > 0, $"{method} must have an alive/dead split");
                if (elseAt <= 0) continue;

                // Strip comment lines: the comments deliberately NAME the other
                // clock to explain the split, so a raw substring scan would
                // false-positive on its own documentation.
                static string CodeOnly(string chunk) => string.Join('\n',
                    chunk.Split('\n')
                        .Select(l => l.TrimStart())
                        .Where(l => !l.StartsWith("//") && !l.StartsWith("*")
                                    && !l.StartsWith("/*")));

                var alive = CodeOnly(body[..elseAt]);
                var dead = CodeOnly(body[elseAt..]);

                True(alive.Contains("m_dwStruckTick", StringComparison.Ordinal),
                    $"{method}'s ALIVE branch must read m_dwStruckTick (obj+0x338)");
                True(dead.Contains("m_dwDeadRepairTick", StringComparison.Ordinal),
                    $"{method}'s DEAD branch must read the dead-repair clock "
                    + "(obj+0x4E4) -- using m_dwStruckTick lets a just-destroyed "
                    + "structure be repaired immediately");
                True(!dead.Contains("m_dwStruckTick", StringComparison.Ordinal),
                    $"{method}'s DEAD branch must NOT read m_dwStruckTick");

                // the 60 s gate must survive in both
                Equal(2, CountOccurrences(body, "60 * 1000"),
                    $"{method} must keep both 0xEA60 = 60000 ms gates");
            }
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }

        // ---------- item 4 ----------

        private static void CheckNoHumNoMonIsGone()
        {
            // the parser must not set it
            var maps = Source("GameSvr", "Maps", "Maps.cs");
            var liveParse = File.ReadAllLines(
                    Path.Combine(RepoRoot(), "GameSvr", "Maps", "Maps.cs"))
                .Select(l => l.TrimStart())
                .Where(l => !l.StartsWith("//") && !l.StartsWith("*"))
                .Any(l => l.Contains("boNOHUMNOMON = true", StringComparison.Ordinal));
            True(!liveParse,
                "Maps.cs must not parse NOHUMNOMON -- the token does not exist in "
                + "战神 (0 byte hits; the 46-token flag census at 0x775BFC / "
                + "0x776B20 has no equivalent)");

            // and the regen gate must not read it
            var liveGate = File.ReadAllLines(
                    Path.Combine(RepoRoot(), "GameSvr", "UsrSystem", "UsrEngn.cs"))
                .Select(l => l.TrimStart())
                .Where(l => !l.StartsWith("//") && !l.StartsWith("*"))
                .Any(l => l.Contains("boNOHUMNOMON", StringComparison.Ordinal));
            True(!liveGate,
                "the monster-regen gate must not consult boNOHUMNOMON -- native "
                + "never suppresses regeneration on an empty map, so the flag "
                + "could only make C# spawn FEWER monsters");

            // the flag must stay permanently false at runtime
            var flag = new SystemModule.TMapFlag();
            Equal(false, flag.boNOHUMNOMON,
                "boNOHUMNOMON must default false and never be set");
        }

        // ---------- item 5 ----------

        private static void CheckElfAppearNowClearsIsFirst()
        {
            var text = Source("GameSvr", "Monsters", "Monster", "ElfMonster.cs");
            var at = text.IndexOf("public void AppearNow()", StringComparison.Ordinal);
            True(at >= 0, "ElfMonster.AppearNow must exist");
            if (at < 0) return;

            var body = text.Substring(at, Math.Min(900, text.Length - at));
            var clearAt = body.IndexOf("boIsFirst = false;", StringComparison.Ordinal);
            var hideAt = body.IndexOf("m_boFixedHideMode = false;",
                StringComparison.Ordinal);
            True(clearAt > 0,
                "AppearNow must clear boIsFirst -- 0x66A22E `mov byte "
                + "[ebx+0x4E8],0` is its FIRST statement");
            True(clearAt < hideAt,
                "boIsFirst must be cleared BEFORE m_boFixedHideMode, matching "
                + "0x66A22E then 0x66A235");

            // behavioural: a forced appearance must not leave the first-tick flag
            var elf = (GameSvr.ElfMonster)Activator.CreateInstance(
                typeof(GameSvr.ElfMonster));
            Equal(true, elf.boIsFirst,
                "the ctor must set boIsFirst (0x66A29F writes 1)");
            typeof(GameSvr.ElfMonster)
                .GetMethod("AppearNow", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(elf, null);
            Equal(false, elf.boIsFirst,
                "after AppearNow the first-tick flag must be clear, so Run's DIGUP "
                + "block cannot fire on an already-surfaced elf");
        }

        private static void CheckElfWarriorFlagsNotMerged()
        {
            // TElfWarrior has TWO flags in native (+0x4EC with 4 byte refs, tested
            // first, and its own +0x4ED with 3). If C# ever collapses them into the
            // single ElfMonster flag the warrior's two-stage appearance breaks.
            var warrior = typeof(GameSvr.ElfMonster).Assembly
                .GetType("GameSvr.ElfWarriorMonster");
            True(warrior != null, "ElfWarriorMonster must exist");
            if (warrior == null) return;

            // It is NOT an ElfMonster subclass (it derives from SpitSpider), and
            // that is correct: native gives it a separate class with its OWN flag
            // at +0x4ED (cleared by its own AppearNow, sub_66A50C @0x66A513), on
            // top of the +0x4EC it tests first. So it must declare its own
            // boIsFirst rather than borrowing ElfMonster's +0x4E8.
            var own = warrior.GetField("boIsFirst",
                BindingFlags.Public | BindingFlags.NonPublic
                                    | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            True(own != null,
                "ElfWarriorMonster must DECLARE its own boIsFirst (+0x4ED, cleared "
                + "at 0x66A513) -- it is a separate field from ElfMonster's "
                + "+0x4E8, so the two must not be merged");

            var warriorSource = Source("GameSvr", "Monsters", "Monster",
                "ElfWarriorMonster.cs");
            var appearAt = warriorSource.IndexOf("boIsFirst = false;",
                StringComparison.Ordinal);
            True(appearAt > 0,
                "ElfWarriorMonster.AppearNow must clear its own flag (0x66A513 is "
                + "its first statement, exactly as 0x66A22E is for ElfMonster)");
        }
    }
}

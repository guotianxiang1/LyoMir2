// 天地合一 (AllowGroupReCall) 持久化审计
//
// 战神真相（逐字节）：
//   obj+0xBA4 <-> rec+0x0D7，单字节，域 {0,1}
//     enc  sub_6B0FF0  0x6B11D4 mov al,[ebx+0xBA4] / 0x6B11DA mov [esi+0xD7],al
//          （SAVE 里 esi 已在 0x6B100C `lea esi,[eax+8]` 预偏移，故 [esi+N] 即 rec N）
//     dec  sub_6AFD7C  0x6B00FB mov al,[eax+0xD7]  / 0x6B0104 mov [edx+0xBA4],al
//   身份判据（不是 btAllowGroup）：
//     GM 开关 sub_622820  0x623993 xor byte [eax+0xBA4],1   -> 域只可能 {0,1}
//                        0x62399D cmp byte [eax+0xBA4],0
//                        0x6239A6 mov cx,0xFFDB             -> FColor=0xDB 绿
//     消费者 sub_7274B4   0x72750C cmp byte [ebx+0xBA4],0 / je 0x72753A(拒绝)
//                        0x7275A4 '无法对 '  0x7275B4 ' 使用天地合一'
//   邻居 rec+0x0DE <-> obj+0xBA5 是另一个标志（enc 0x6B1234 / dec 0x6B01B8），
//   已由共享 codec 建模；曾有一次 0xDE->0xD7 的改动是错的并已回退。
//
// 本审计守两件事：
//   1. rec+0x0D7 的 clone-carry 往返（Restore/Persist 对称、宽度、域）。
//   2. **回归门**：UsrEngn 不得在 RestoreNativeUnmappedScalars 之后再把
//      HumData.boAllowGroupReCall 赋回 m_boAllowGroupReCall —— 共享 codec 不建模
//      rec+0x0D7，该 DTO 成员恒 false，那行会在每次登录静默清掉开关。
//      这正是本轮修掉的真 bug，静态断言防止它被"顺手恢复"。

using System.Reflection;
using System.Text;

namespace NativeAllowGroupReCallPersistCheck
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
                // Constructing a TPlayObject touches M2Share's static ctor, which
                // reads config files relative to the output directory. Same shape as
                // NativeKillExpDecayCheck.
                PrepareRuntimeConfig();
                InitializeRuntime();

                CheckOffsetConstant();
                CheckRoundTrip();
                CheckDomainIsBooleanized();
                CheckPersistIsAuthoritative();
                CheckShortRecordContract();
                CheckNeighbourNotConflated();
                CheckNoPostRestoreClobber();
                CheckRestoreOrderingInUsrEngn();
                CheckPersistIsWired();
            }
            catch (Exception ex)
            {
                Failures.Add("unexpected exception: " + ex);
            }

            if (Failures.Count == 0)
            {
                Console.WriteLine(
                    $"AUDIT_PASS NativeAllowGroupReCallPersistCheck {_assertions} assertions");
                return 0;
            }

            Console.WriteLine("AUDIT_FAIL NativeAllowGroupReCallPersistCheck");
            foreach (var f in Failures) Console.WriteLine("  - " + f);
            return 1;
        }

        // ---------- runtime bring-up ----------

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
            // Everything in this repo is `namespace GameSvr` regardless of folder,
            // so GameSvrConfig is NOT under GameSvr.Configs.
            GameSvr.M2Share.g_Config = new GameSvr.GameSvrConfig();
            GameSvr.M2Share.ObjectManager = new GameSvr.ObjectManager();
            GameSvr.M2Share.ProcessMsgCriticalSection = new object();
            GameSvr.M2Share.ProcessHumanCriticalSection = new object();
            GameSvr.M2Share.LogMsgCriticalSection = new object();
            GameSvr.M2Share.LogStringList = new System.Collections.ArrayList();
        }

        // ---------- reflection plumbing ----------

        private static Type PlayObjectType =>
            typeof(GameSvr.TPlayObject);

        private static int ConstInt(string name)
        {
            var field = PlayObjectType.GetField(name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (field == null) throw new MissingFieldException(name);
            return (int)field.GetRawConstantValue();
        }

        private static GameSvr.TPlayObject NewPlayer()
        {
            var player = (GameSvr.TPlayObject)Activator.CreateInstance(
                PlayObjectType, nonPublic: true);
            SetRecord(player, new byte[RecordSize]);
            return player;
        }

        private static int RecordSize => DBSvr.Core.NativeHumanDataCodec.DataRecordSize;

        private static void SetRecord(GameSvr.TPlayObject player, byte[] raw)
        {
            PlayObjectType.GetField("m_NativeHumanData",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(player, raw);
        }

        private static byte[] GetRecord(GameSvr.TPlayObject player) =>
            (byte[])PlayObjectType.GetField("m_NativeHumanData",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(player);

        private static bool GetToggle(GameSvr.TPlayObject player) =>
            (bool)player.GetType().GetField("m_boAllowGroupReCall",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(player);

        private static void SetToggle(GameSvr.TPlayObject player, bool value) =>
            player.GetType().GetField("m_boAllowGroupReCall",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(player, value);

        private static void Restore(GameSvr.TPlayObject player) =>
            PlayObjectType.GetMethod("RestoreNativeUnmappedScalars",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Invoke(player, null);

        private static bool Persist(GameSvr.TPlayObject player) =>
            (bool)PlayObjectType.GetMethod("PersistNativeUnmappedScalars",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .Invoke(player, null);

        // ---------- checks ----------

        private static void CheckOffsetConstant()
        {
            // enc 0x6B11DA `mov byte [esi+0xD7],al`
            Equal(0x00D7, ConstInt("NativeAllowGroupReCallOffset"),
                "rec offset must be 0xD7 per enc 0x6B11DA / dec 0x6B0104");
        }

        private static void CheckRoundTrip()
        {
            foreach (var value in new[] { false, true })
            {
                var player = NewPlayer();
                SetToggle(player, value);
                True(Persist(player), "persist must succeed on a full-length record");
                var raw = GetRecord(player);
                Equal((byte)(value ? 1 : 0), raw[ConstInt("NativeAllowGroupReCallOffset")],
                    $"persisted byte for toggle={value}");

                var reloaded = NewPlayer();
                SetRecord(reloaded, raw);
                SetToggle(reloaded, !value);   // prove Restore actually assigns
                Restore(reloaded);
                Equal(value, GetToggle(reloaded), $"round-trip toggle={value}");
            }
        }

        private static void CheckDomainIsBooleanized()
        {
            // 0x6B0104 stores the byte raw; the only writer is the xor at 0x623993 so
            // native never produces anything but 0/1. A corrupted or foreign byte must
            // still decode as "on" rather than throwing or truncating to false.
            var offset = ConstInt("NativeAllowGroupReCallOffset");
            foreach (byte raw in new byte[] { 1, 2, 0x7F, 0x80, 0xFF })
            {
                var player = NewPlayer();
                var record = GetRecord(player);
                record[offset] = raw;
                Restore(player);
                True(GetToggle(player), $"non-zero record byte 0x{raw:X2} must restore as on");
            }

            var zeroPlayer = NewPlayer();
            SetToggle(zeroPlayer, true);
            Restore(zeroPlayer);
            True(!GetToggle(zeroPlayer), "zero record byte must restore as off");
        }

        private static void CheckPersistIsAuthoritative()
        {
            // Native rebuilds the whole frame from the live object every save
            // (sub_6B6510 zero-fills, then sub_6B0FF0 writes each slot), so the LIVE
            // value must win over whatever byte was loaded.
            var offset = ConstInt("NativeAllowGroupReCallOffset");
            var player = NewPlayer();
            GetRecord(player)[offset] = 1;      // stale "on" from the load
            SetToggle(player, false);           // player switched it off in-session
            Persist(player);
            Equal((byte)0, GetRecord(player)[offset],
                "live value must overwrite the stale loaded byte");
        }

        private static void CheckShortRecordContract()
        {
            // A record too short to hold the block cannot be patched; persist then
            // reports success only when every field it owns is still at its default,
            // so a real value is never silently dropped.
            var player = NewPlayer();
            SetRecord(player, new byte[8]);
            SetToggle(player, false);
            True(Persist(player), "short record with default toggle reports success");

            SetToggle(player, true);
            True(!Persist(player),
                "short record with the toggle ON must report failure, not drop it");

            // Restore must not throw on a short record either.
            Restore(player);
        }

        private static void CheckNeighbourNotConflated()
        {
            // rec+0x0DE <-> obj+0xBA5 is a DIFFERENT flag handled by the shared codec.
            // Writing our toggle must never touch it, and vice versa.
            const int neighbour = 0x00DE;
            var player = NewPlayer();
            SetToggle(player, true);
            Persist(player);
            Equal((byte)0, GetRecord(player)[neighbour],
                "persisting 0xD7 must not write the 0xDE neighbour");

            var reader = NewPlayer();
            GetRecord(reader)[neighbour] = 1;
            Restore(reader);
            True(!GetToggle(reader),
                "a set 0xDE neighbour must not be read as the 0xD7 toggle");
        }

        // ---------- static source gates ----------

                private static string RepoRoot()
        {
            return AuditRepoRoot.Resolve();
        }

        private static string[] UsrEngnLines() => File.ReadAllLines(
            Path.Combine(RepoRoot(), "GameSvr", "UsrSystem", "UsrEngn.cs"));

        private static void CheckNoPostRestoreClobber()
        {
            // THE REGRESSION GATE. `m_boAllowGroupReCall = HumData.boAllowGroupReCall`
            // must not exist as live code: the shared codec does not model rec+0x0D7,
            // so that DTO member is always false and the assignment wiped the toggle
            // on every login.
            var offenders = new List<int>();
            var lines = UsrEngnLines();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var code = line.TrimStart();
                if (code.StartsWith("//")) continue;   // the explanatory comment is fine
                if (line.Contains("m_boAllowGroupReCall") &&
                    line.Contains("HumData.boAllowGroupReCall"))
                {
                    offenders.Add(i + 1);
                }
            }

            Equal(0, offenders.Count,
                "UsrEngn must not assign HumData.boAllowGroupReCall onto the live field "
                + "(offending line(s): " + string.Join(",", offenders) + ")");
        }

        private static void CheckRestoreOrderingInUsrEngn()
        {
            // The restore has to run AFTER the DTO block it supersedes; if someone
            // moves it above the assignments, other fields in the same module regress.
            var lines = UsrEngnLines();
            var restoreLine = -1;
            var lastDtoLine = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("RestoreNativeUnmappedScalars()")) restoreLine = i;
                if (lines[i].Contains("PlayObject.m_nBodyLuckLevel =") &&
                    lines[i].Contains("HumData")) lastDtoLine = i;
            }

            True(restoreLine > 0, "RestoreNativeUnmappedScalars call site must exist");
            True(lastDtoLine > 0, "the DTO assignment block must still be present");
            True(restoreLine > lastDtoLine,
                "RestoreNativeUnmappedScalars must run AFTER the DTO assignments "
                + $"(restore@{restoreLine + 1} dto@{lastDtoLine + 1})");
        }

        private static void CheckPersistIsWired()
        {
            var lines = UsrEngnLines();
            var wired = lines.Any(l => l.Contains("PersistNativeUnmappedScalars()")
                                       && !l.TrimStart().StartsWith("//"));
            True(wired, "PersistNativeUnmappedScalars must be called from the save path");
        }
    }
}

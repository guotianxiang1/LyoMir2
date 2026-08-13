// GILD-27: Audit tool proving guild war CreateTime is persisted, loaded and consumed.
//
// The bug:
//   NativeCorpsService._gildRelations was Dictionary<(ulong,ulong), byte> — it held
//   ONLY the relation byte. LoadGildRelations (NativeCorpsStore.cs) likewise selected
//   only GildID1,GildID2,Relation. CreateTime was written to the DB on INSERT
//   (NativeGildMySqlStore.InsertGildRelationSql) but never read back and never kept
//   in memory, so nothing could compute a war deadline: wars never expired.
//
// The fix:
//   1. GildRelations/_gildRelations carry (byte Relation, DateTime CreateTime)
//   2. LoadGildRelations SELECTs CreateTime and stores it
//   3. Both relation writers (DeclareWar -> type 2, union accept -> type 1) stamp
//      DateTime.Now into the in-memory tuple AND pass it to the DB insert
//   4. ExpireGildWars(durationMs) removes wars past CreateTime+duration and is
//      ticked from GameServer.ProcessPhase4_SlowerExecute
//
// Native anchor for the expiry rule itself: the file-based twin
// AssociationManager.Run() (Associations/AssociationManager.cs) drops a war when
// (GetTickCount()-dwWarTick) > dwWarTime, with dwWarTime = g_Config.dwGuildWarTime.
// The MySQL path stores an absolute timestamp instead of a tick, so the equivalent
// predicate is CreateTime+dwGuildWarTime <= now.
//
// Every check below is fail-closed: a missing/unreadable source file is a FAIL, not
// a skip (AppContext.BaseDirectory points at bin/ and has no .cs — a classic silent
// false-green). Source lines are stripped of // comments before matching so that a
// commented-out call site cannot satisfy a wiring assertion.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace GildWarCreateTimeCheck
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Console.WriteLine("[GILD-27] Audit: guild war CreateTime persistence + expiry");
            Console.WriteLine(new string('=', 70));

            var service = ReadSource("GameSvr/Services/NativeCorpsService.cs");
            var store = ReadSource("GameSvr/Services/NativeCorpsStore.cs");
            var codec = ReadSource("GameSvr/Services/NativeCorpsWireCodec.cs");
            var expiry = ReadSource("GameSvr/Services/NativeGildWarExpiry.cs");
            var server = ReadSource("GameSvr/GameServer.cs");
            var gildSql = ReadSource("GameSvr/Services/NativeGildMySqlStore.cs");

            // ---- shape: the tuple actually reaches both dictionaries -------------
            Assert("snapshot GildRelations is keyed to (Relation, CreateTime)",
                codec, s => Has(s,
                    "Dictionary<(ulong First, ulong Second), (byte Relation, DateTime CreateTime)>",
                    "GildRelations"));

            Assert("_gildRelations field is keyed to (Relation, CreateTime)",
                service, s => Has(s,
                    "Dictionary<(ulong First, ulong Second), (byte Relation, DateTime CreateTime)>",
                    "_gildRelations"));

            Assert("Unavailable ctor builds the tuple dictionary",
                service, s => Has(s,
                    "new Dictionary<(ulong, ulong), (byte, DateTime)>()"));

            AssertFalse("no byte-only relation dictionary survives anywhere",
                new[] { service, codec },
                s => Has(s, "Dictionary<(ulong First, ulong Second), byte>")
                     || Has(s, "Dictionary<(ulong, ulong), byte>"));

            // ---- load: CreateTime is selected and stored ------------------------
            Assert("LoadGildRelations SELECTs CreateTime",
                store, s => Has(s, "SELECT GildID1,GildID2,Relation,CreateTime"));

            Assert("LoadGildRelations reads column 3 as DateTime",
                store, s => Has(s, "reader.GetDateTime(3)"));

            Assert("LoadGildRelations stores (relation, createTime)",
                store, s => Has(s, "TryAdd(key, (relation, createTime))"));

            // ---- write: both relation types stamp a timestamp -------------------
            // These used to require the literal DateTime.Now at both sinks. GILD-04 hoisted
            // the union timestamp into a local (RemoveGildRelationLocked, then one
            // `var unionTime = DateTime.Now` feeding both writers) so the in-memory tuple and
            // the gildrelation row can no longer straddle a millisecond boundary and disagree.
            // The literal rejected that improvement, so pin the property instead: whatever the
            // timestamp expression is, both sinks must receive the same one.
            Assert("DeclareWar stamps the war in memory and persists the same instant",
                service, s => StampsSameInstant(s, "GildHostile"));

            Assert("union accept stamps the union in memory and persists the same instant",
                service, s => StampsSameInstant(s, "GildUnion"));

            Assert("InsertGildRelationFailSafe takes createTime and binds it (no inline NOW)",
                service, s => Has(s,
                    "InsertGildRelationFailSafe(\n            (ulong First, ulong Second) relationKey, int relation, DateTime createTime)".Replace("\n            ", " "))
                    || (Has(s, "int relation, DateTime createTime)")
                        && Has(s, "relation,\n                        createTime, out var error)".Replace("\n                        ", " "))));

            Assert("gildrelation INSERT still carries a bound CreateTime param",
                gildSql, s => Has(s,
                    "INSERT INTO gamedata.gildrelation(GildID1,GildID2,Relation,")
                    && Has(s, "CreateTime) VALUES(@g1,@g2,@relation,@created)"));

            // ---- consume: expiry exists, is correct, and is ticked --------------
            Assert("NativeGildWarExpiry.GetExpired takes the tuple dictionary",
                expiry, s => Has(s,
                    "Dictionary<(ulong First, ulong Second), (byte Relation, DateTime CreateTime)> relations"));

            Assert("expiry only drops hostile relations (unions never expire)",
                expiry, s => Has(s, "pair.Value.Relation != GildHostile")
                             && Has(s, "continue"));

            Assert("expiry deadline is CreateTime + duration",
                expiry, s => Has(s, "pair.Value.CreateTime.AddMilliseconds(durationMs)"));

            // Teardown goes through the shared RemoveGildRelationLocked (native
            // delete_relation sub_5E90A4, which drops the map entry AND the row);
            // both halves are pinned here so neither can be lost in the helper.
            Assert("ExpireGildWars tears the relation down through the shared helper",
                service, s => Has(s, "internal void ExpireGildWars(int durationMs)")
                              && InBlock(s, "internal void ExpireGildWars",
                                  "RemoveGildRelationLocked(relationKey)"));

            Assert("the shared teardown removes in memory and pushes the DB DELETE",
                service, s => InBlock(s, "private void RemoveGildRelationLocked",
                                  "_gildRelations.Remove(relationKey)")
                              && InBlock(s, "private void RemoveGildRelationLocked",
                                  "DeleteGildRelationFailSafe(relationKey)"));

            Assert("ExpireGildWars calls the shared expiry helper",
                service, s => InBlock(s, "internal void ExpireGildWars",
                    "NativeGildWarExpiry.GetExpired(_gildRelations, now, durationMs)"));

            Assert("ExpireGildWars takes the service lock",
                service, s => InBlock(s, "internal void ExpireGildWars", "lock (_sync)"));

            Assert("Phase4 ticks ExpireGildWars with the configured war time",
                server, s => InBlock(s, "private void ProcessPhase4_SlowerExecute",
                    "M2Share.CorpsService.ExpireGildWars(M2Share.g_Config.dwGuildWarTime)"));

            Console.WriteLine(new string('=', 70));
            Console.WriteLine($"Result: {_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        // ---- harness ---------------------------------------------------------

        private static void Assert(string name, string source,
            Func<string, bool> predicate)
        {
            if (source == null)
            {
                Fail(name + "  [source file missing]");
                return;
            }
            bool ok;
            try
            {
                ok = predicate(source);
            }
            catch (Exception ex)
            {
                Fail(name + "  [predicate threw: " + ex.Message + "]");
                return;
            }
            if (ok) Pass(name); else Fail(name);
        }

        private static void AssertFalse(string name, IEnumerable<string> sources,
            Func<string, bool> forbidden)
        {
            var all = sources.ToArray();
            if (all.Any(s => s == null))
            {
                Fail(name + "  [source file missing]");
                return;
            }
            if (all.Any(forbidden)) Fail(name); else Pass(name);
        }

        private static void Pass(string name)
        {
            Console.WriteLine("[PASS] " + name);
            _passed++;
        }

        private static void Fail(string name)
        {
            Console.WriteLine("[FAIL] " + name);
            _failed++;
        }

        // Whitespace-insensitive substring match: the repo wraps long expressions
        // across lines, so both needle and haystack are collapsed before compare.
        private static bool Has(string source, params string[] needles) =>
            needles.All(n => Collapse(source).Contains(Collapse(n)));

        // Both relation sinks -- the in-memory tuple and the DB insert -- must be handed the
        // same timestamp expression, so a reload cannot produce a deadline that differs from
        // the one the live map is using.
        private static bool StampsSameInstant(string source, string relation)
        {
            var collapsed = Collapse(source);
            var memory = Regex.Match(collapsed,
                @"_gildRelations\[relationKey\] = \(" + relation
                + @", (?<stamp>[A-Za-z0-9_.]+)\)");
            if (!memory.Success) return false;
            return Regex.IsMatch(collapsed,
                @"InsertGildRelationFailSafe\(relationKey, " + relation + ", "
                + Regex.Escape(memory.Groups["stamp"].Value) + @"\)");
        }

        private static string Collapse(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length);
            var pendingSpace = false;
            foreach (var ch in value)
            {
                if (char.IsWhiteSpace(ch)) { pendingSpace = sb.Length > 0; continue; }
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // Confine a match to one method body so a hit elsewhere in the file cannot
        // satisfy a wiring assertion. Brace-counts from the signature.
        private static bool InBlock(string source, string signature, string needle)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return false;
            var open = source.IndexOf('{', start);
            if (open < 0) return false;
            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return Has(source.Substring(open, i - open + 1), needle);
                }
            }
            return false;
        }

        // Strip // comments (keeping string literals intact is unnecessary here: no
        // asserted needle contains "//") so commented-out code cannot pass a check.
        private static string StripLineComments(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            foreach (var line in source.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    sb.Append('\n');
                    continue;
                }
                var idx = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(idx >= 0 ? line.Substring(0, idx) : line).Append('\n');
            }
            return sb.ToString();
        }

        // [CallerFilePath] anchors on this source file: AppContext.BaseDirectory is
        // bin/ and holds no .cs, which would turn every scan into a silent SKIP.
        private static string ReadSource(string relativeFromRepoRoot,
            [CallerFilePath] string thisFile = null)
        {
            var dir = Path.GetDirectoryName(thisFile);
            if (dir == null) return null;
            var path = Path.GetFullPath(Path.Combine(dir, "..", "..",
                relativeFromRepoRoot.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(path) ? StripLineComments(File.ReadAllText(path)) : null;
        }
    }
}

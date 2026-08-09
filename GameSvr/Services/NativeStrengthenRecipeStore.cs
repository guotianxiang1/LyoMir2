using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SystemModule;

namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Loader/store for the equipment-synthesis recipe config that backs CM_STRENGTHEN_EQUIP_QUEST 4465
    // (read-only query) and the CM_STRENGTHEN_EQUIP 4466 front-half. This is the C# port of the native
    // synthesis-manager config (dword_7DC210) that the original loads from `config\SuperEquipSmeltNew.txt`
    // via sub_7548D8. It is DORMANT by default: the gate SupportsStrengthenRecipes is OFF, so the live
    // 4465 case keeps its fail-closed stub until the loader is proven (see the ASSUMED list below).
    //
    // Plan + evidence: staging/cm_strengthen_recipe_loader_plan_20260801.md,
    //   strengthen_equip_quest_4465_readonly_20260731.md, strengthen_equip_exec_4466_evidence_20260731.md.
    //
    // Config file (ships on disk): Share/config/SuperEquipSmeltNew.txt (GBK, TAB-delimited). Row shape:
    //   <resultName> \t <baseName> \t <param> \t [<requiredMaterials>]  [<weight>:<opt>|<opt>|...]
    //   '#' lines are comments; blank lines separate tiers. Some rows have an empty column before <param>.
    //
    // Native structure this mirrors (from the dormant models):
    //   dword_7DC210 mgr : [mgr+0x24] recipe list (name-indexed via sub_49F5F4), [mgr+0x28] unit-price[key-1].
    //   recipe : +0x08 key (word) ; +0x04 {weight,resultId} list ; +0x0A weight-total (Random bound).
    //
    // idat-CONFIRMED (sub_7548D8 @0x007548D8 dumped 2026-08-01, recipe_out.txt/recipe_out2.txt):
    //   record 0x60 bytes: +0x00 result(str,col1), +0x04 base(str,col2), +0x0C int(col3), option
    //     strings +0x30.., option ints +0x44.., same-base chain-next +0x5C. Delimiters {TAB, SPACE};
    //     '#' AND ';' comment lines skipped. Bracket groups split on '['/']' then ':' then '|'/'\'.
    //   (a) LOOKUP KEY = base name (col2): the loader inserts keyed by col2 (Add vtbl+0x3C); reader
    //       sub_60F504 does sub_49F5F4(mgr[+0x24], name). This store's base-name keying is correct.
    //       NOTE: same-base recipes CHAIN via record+0x5C; this store is last-wins (a simplification —
    //       fine iff bases are unique, which they are in the shipped file).
    //   (b) COL3 -> record+0x0C as an INT (Str_ToInt); it is NOT the material bit-flag. (Whether the
    //       4465/4466 handler's "recipe key" is this field vs another needs the handler field-read map.)
    //   (c) PRICE: the loader does NOT populate the price array — sub_60F55C reads mgr[+0x28][key-1]
    //       (8-byte stride) from a DIFFERENT source (not SuperEquipSmeltNew.txt). Still unknown; 4465 is
    //       read-only and does not need it, 4466 does.
    //   (d) Per-material "look" word body + vtbl+0x254 buffered send still deferred (gated handler).
    //
    // *** CONFIG-LINKED-BLOCKED — this file is NOT the 4465/4466 recipe source (idat-confirmed 2026-08-01) ***
    //   Full trace (recipe_out*.txt / link_out*.txt): sub_7548D8 loads SuperEquipSmeltNew.txt into
    //   (*off_7D5D6C)[+0x3C4][+4] — a SEPARATE "super smelt" container. The 4465/4466 handlers read
    //   dword_7DC210[+0x24], where dword_7DC210 is the synthesis QUEUE MANAGER (class off_60F3AC; vtbl
    //   sub_6102E0 process / sub_60F404 ctor). Its constructor sub_60F404 does `mov [obj+0x24], newContainer`
    //   — it creates the recipe container EMPTY, and NO native config-file loader ever populates it
    //   (dword_7DC210 has only readers + ctor as xrefs). The 4465/4466 recipes are added at RUNTIME
    //   (the PAS-driven strengthen path; C# equivalent = ScriptSystem/PasEngine/PasApiBridge.NativeStrengthenEquip).
    //   => SuperEquipSmeltNew.txt is the wrong source for 4465/4466. SupportsStrengthenRecipes MUST stay
    //   OFF; wiring 4465 to this store would return WRONG (read-only) recipe data. 4465 is config-linked-
    //   blocked, parked like the RandSeed-gated features. This parser is still correct FOR the SuperEquipSmelt
    //   system (UserEngine[+0x3C4]) should that ever be modeled; it is simply not the 4465 recipe table.


    //
    // Fail-closed everywhere: a missing/unreadable/garbled file leaves the store empty; the gated handler
    // then returns false and the live case behaves exactly as today. No recipe data is invented.
    // ------------------------------------------------------------------------------------------------

    /// <summary>One parsed synthesis recipe row (names kept verbatim; ids resolved lazily at use time).</summary>
    public sealed class NativeStrengthenRecipe
    {
        /// <summary>col1 — the synthesized output item name (tier N+1).</summary>
        public string ResultName { get; init; }
        /// <summary>col2 — the base/主材料 item name (tier N); the ASSUMED lookup key.</summary>
        public string BaseName { get; init; }
        /// <summary>col3 — ASSUMED recipe key (word[recipe+0x08]); may be the material bit-flag (see (b)).</summary>
        public int Param { get; init; }
        /// <summary>ASSUMED alias for the recipe key used by the model (== Param pending (b)).</summary>
        public int Key => Param;
        /// <summary>[requiredMaterials] — the 必备材料 names ('|'-separated in the file).</summary>
        public IReadOnlyList<string> RequiredMaterials { get; init; }
        /// <summary>weight of the result group (the '&lt;n&gt;:' prefix) — maps to weight-total (+0x0A).</summary>
        public int ResultWeight { get; init; }
        /// <summary>weighted result-option names — maps to the {weight,resultId} list (+0x04).</summary>
        public IReadOnlyList<string> ResultOptions { get; init; }
    }

    public sealed class NativeStrengthenRecipeStore
    {
        /// <summary>
        /// Feature gate. Default OFF and MUST STAY OFF permanently.
        ///
        /// *** idat 2026-08-02 (staging update_clothes_4637_ida_work/wf2_out.txt) — DEFINITIVELY DEAD ***
        /// The 4465/4466 CM handlers are never even reached in the original: sub_6D7D68 cases 4465/4466
        /// enqueue onto the synthesis manager dword_7DC210 via sub_6103DC, but that enqueue is gated on
        /// `[mgr+0x10] != 0`, which is NEVER set (ctor sub_60F404 leaves it 0; dword_7DC210 has NO writer
        /// xref anywhere; sub_610E88 is a finalizer, not a setter). The queue is permanently disabled, so
        /// sub_60F5C0 / sub_60F7AC never run and native sends the client NOTHING for 4465/4466. Combined
        /// with the empty, never-loaded recipe table [mgr+0x24] (this store's file feeds a DIFFERENT
        /// container, UserEngine[+0x3C4]), the whole feature is inert. The live cases are therefore silent
        /// no-ops (TPlayObject.Message.cs). Enabling this gate would emit a response the original never
        /// sends — do not.
        /// </summary>
        public static bool SupportsStrengthenRecipes { get; set; } = false;

        /// <summary>Native config path (relative to the server root).</summary>
        public const string ConfigRelativePath = @"Share\config\SuperEquipSmeltNew.txt";

        private static readonly NativeStrengthenRecipeStore _shared = new NativeStrengthenRecipeStore();
        /// <summary>Process-wide store the gated handler and the reload hook share.</summary>
        public static NativeStrengthenRecipeStore Shared => _shared;

        // ASSUMED key = base name (a); OrdinalIgnoreCase mirrors the native name lookup being case-loose.
        private readonly Dictionary<string, NativeStrengthenRecipe> _byKeyName =
            new Dictionary<string, NativeStrengthenRecipe>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Number of recipes currently loaded (0 => dormant; the gated handler falls back).</summary>
        public int Count => _byKeyName.Count;

        /// <summary>Recipe lookup by the ASSUMED key (base item name). False when absent/empty.</summary>
        public bool TryGetRecipe(string name, out NativeStrengthenRecipe recipe)
        {
            recipe = null;
            if (string.IsNullOrEmpty(name)) return false;
            return _byKeyName.TryGetValue(name, out recipe);
        }

        /// <summary>Build the default native config path from the server root (null-safe).</summary>
        public static string DefaultConfigPath(string rootPath) =>
            string.IsNullOrEmpty(rootPath)
                ? null
                : Path.Combine(rootPath, "Share", "config", "SuperEquipSmeltNew.txt");

        /// <summary>
        /// Load/replace the store from a GBK, TAB-delimited SuperEquipSmeltNew.txt. Fail-safe: a
        /// missing/unreadable/garbled file leaves the store empty and returns 0 (never throws).
        /// </summary>
        public int Load(string configFilePath)
        {
            _byKeyName.Clear();
            try
            {
                if (string.IsNullOrEmpty(configFilePath) || !File.Exists(configFilePath))
                    return 0;
                var bytes = File.ReadAllBytes(configFilePath);
                var text = HUtil32.GbkEncoding.GetString(bytes);
                foreach (var raw in text.Split('\n'))
                {
                    var recipe = ParseLine(raw);
                    if (recipe == null) continue;
                    // (a) ASSUMED lookup key = base name; last row for a given base wins.
                    if (!string.IsNullOrEmpty(recipe.BaseName))
                        _byKeyName[recipe.BaseName] = recipe;
                }
            }
            catch
            {
                _byKeyName.Clear();
                return 0;
            }
            return _byKeyName.Count;
        }

        /// <summary>Reload hook for @loadSuperSmelt / ReloadComposeConfig — loads the shared store.</summary>
        public static int Reload(string rootPath) => _shared.Load(DefaultConfigPath(rootPath));

        private static readonly Regex BracketRx = new Regex(@"\[([^\]]*)\]", RegexOptions.Compiled);

        /// <summary>
        /// Parse one already-decoded config line. Returns null for comment ('#') / blank / malformed
        /// lines. Pure and side-effect-free so the audit can exercise it with plain ASCII fixtures.
        /// </summary>
        public static NativeStrengthenRecipe ParseLine(string line)
        {
            if (line == null) return null;
            line = line.Replace("\r", string.Empty).Trim();
            // sub_7548D8 skips '#' and ';' comment lines.
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') return null;

            // sub_7548D8 tokenizes with sub_4C6BA4 whose delimiter set is {TAB, SPACE}; consecutive
            // delimiters collapse (empty tokens dropped), matching StringSplitOptions.RemoveEmptyEntries.
            var cols = line.Split(new[] { '\t', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 2) return null;

            string resultName = cols[0].Trim();
            string baseName = cols[1].Trim();
            if (resultName.Length == 0 || baseName.Length == 0) return null;

            // (b) col3 param: first pure-int column at index >= 2 (some rows have an empty column first).
            int param = 0;
            for (int i = 2; i < cols.Length; i++)
            {
                var t = cols[i].Trim();
                if (t.Length > 0 && int.TryParse(t, out var p)) { param = p; break; }
            }

            // Bracket groups over the tail: first plain [..] = required materials; the first "<n>:.." = weighted results.
            var required = new List<string>();
            int resultWeight = 0;
            var options = new List<string>();
            bool gotRequired = false;

            var tail = cols.Length > 2 ? string.Join(" ", cols, 2, cols.Length - 2) : string.Empty;
            foreach (Match m in BracketRx.Matches(tail))
            {
                var inner = m.Groups[1].Value.Trim();
                if (inner.Length == 0) continue;
                int colon = inner.IndexOf(':');
                if (colon > 0 && int.TryParse(inner.Substring(0, colon).Trim(), out var w))
                {
                    resultWeight = w;
                    foreach (var opt in inner.Substring(colon + 1).Split('|'))
                        if (opt.Trim().Length > 0) options.Add(opt.Trim());
                }
                else if (!gotRequired)
                {
                    foreach (var mat in inner.Split('|'))
                        if (mat.Trim().Length > 0) required.Add(mat.Trim());
                    gotRequired = true;
                }
            }

            return new NativeStrengthenRecipe
            {
                ResultName = resultName,
                BaseName = baseName,
                Param = param,
                RequiredMaterials = required,
                ResultWeight = resultWeight,
                ResultOptions = options,
            };
        }
    }
}

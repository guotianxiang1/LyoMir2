using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>
    /// Magic ID to human-readable name mapping.
    ///
    /// Source: SpellsDef.cs constants
    /// Purpose: Unblock "~36 spell id" BLOCKED item from session-state-0804
    ///
    /// Note: Many IDs use generic names (SKILL_43, SKILL_140, etc.) because
    /// their actual game names are not present in the binary constants.
    /// Full names would require extraction from game data files or client strings.
    /// </summary>
    public static class MagicIdNames
    {
        private static readonly Dictionary<int, string> _idToName = new Dictionary<int, string>
        {
            { 1, "Fireball" },                  // SKILL_FIREBALL
            { 2, "Healing" },                   // SKILL_HEALLING
            { 3, "One Sword" },                 // SKILL_ONESWORD
            { 4, "Il Kwang" },                  // SKILL_ILKWANG
            { 5, "Fireball2" },                 // SKILL_FIREBALL2
            { 6, "Am Youn Sul (Poison)" },      // SKILL_AMYOUNSUL
            { 7, "Ye Do" },                     // SKILL_YEDO
            { 8, "Fire Wind" },                 // SKILL_FIREWIND
            { 9, "Fire" },                      // SKILL_FIRE
            { 10, "Shoot Lighten" },            // SKILL_SHOOTLIGHTEN
            { 11, "Lightning" },                // SKILL_LIGHTENING
            { 12, "Er Gum" },                   // SKILL_ERGUM
            { 13, "Fire Charm" },               // SKILL_FIRECHARM
            { 14, "Hang Ma Jin Bub" },          // SKILL_HANGMAJINBUB
            { 15, "De Ji Won Ho" },             // SKILL_DEJIWONHO
            { 16, "Holy Shield" },              // SKILL_HOLYSHIELD
            { 17, "Skeleton" },                 // SKILL_SKELLETON
            { 18, "Cloak (Invisibility)" },     // SKILL_CLOAK
            { 19, "Big Cloak" },                // SKILL_BIGCLOAK
            { 20, "Taming" },                   // SKILL_TAMMING
            { 21, "Space Move (Teleport)" },    // SKILL_SPACEMOVE
            { 22, "Earth Fire" },               // SKILL_EARTHFIRE
            { 23, "Fire Boom (Explosion)" },    // SKILL_FIREBOOM
            { 24, "Light Flower" },             // SKILL_LIGHTFLOWER
            { 25, "Ban Wol (Half Moon)" },      // SKILL_BANWOL
            { 26, "Fire Sword" },               // SKILL_FIRESWORD
            { 27, "Moo Te Bo" },                // SKILL_MOOTEBO
            { 28, "Show HP" },                  // SKILL_SHOWHP
            { 29, "Big Healing" },              // SKILL_BIGHEALLING
            { 30, "Sin Su (Divine Beast)" },    // SKILL_SINSU
            { 31, "Shield" },                   // SKILL_SHIELD
            { 32, "Kill Undead" },              // SKILL_KILLUNDEAD
            { 33, "Snow Wind" },                // SKILL_SNOWWIND
            { 34, "Cross Moon" },               // SKILL_CROSSMOON
            { 35, "Wind Te Bo" },               // SKILL_WINDTEBO
            { 36, "U Enhancer" },               // SKILL_UENHANCER
            { 37, "Energy Repulsor" },          // SKILL_ENERGYREPULSOR
            { 38, "Twin Blade" },               // SKILL_TWINBLADE
            { 39, "Group De Ding" },            // SKILL_GROUPDEDING
            { 40, "Un Am Youn Sul" },           // SKILL_UNAMYOUNSUL
            { 41, "Angel" },                    // SKILL_ANGEL
            { 42, "Group Lightning" },          // SKILL_GROUPLIGHTENING
            { 43, "Skill 43" },                 // SKILL_43 (unknown name)
            { 44, "Skill 44" },                 // SKILL_44 (unknown name)
            { 45, "Skill 45" },                 // SKILL_45 (unknown name)
            { 46, "Skill 46" },                 // SKILL_46 (unknown name)
            { 47, "Skill 47" },                 // SKILL_47 (unknown name)
            { 48, "Group Am Youn Sul" },        // SKILL_GROUPAMYOUNSUL
            { 49, "Skill 49" },                 // SKILL_49 (unknown name)
            { 50, "Ma Be" },                    // SKILL_MABE
            { 51, "Skill 51" },                 // SKILL_51 (unknown name)
            { 52, "Skill 52" },                 // SKILL_52 (unknown name)
            { 53, "Skill 53" },                 // SKILL_53 (unknown name)
            { 54, "Skill 54" },                 // SKILL_54 (unknown name)
            { 55, "Skill 55" },                 // SKILL_55 (unknown name)
            { 56, "Red Ban Wol" },              // SKILL_REDBANWOL
            { 57, "Skill 57" },                 // SKILL_57 (unknown name)
            { 58, "Skill 58" },                 // SKILL_58 (unknown name)
            { 59, "Skill 59" },                 // SKILL_59 (unknown name)
            { 140, "Skill 140" },               // SKILL_140 (unknown name)
            { 141, "Skill 141" },               // SKILL_141 (unknown name)
            { 145, "Skill 145" },               // SKILL_145 (unknown name)
            { 146, "Skill 146" },               // SKILL_146 (unknown name)
            { 149, "Skill 149" },               // SKILL_149 (unknown name)
            { 150, "Skill 150" },               // SKILL_150 (unknown name)
            { 152, "Skill 152" },               // SKILL_152 (unknown name)
            { 153, "Skill 153" },               // SKILL_153 (unknown name)
            { 161, "Skill 161" },               // SKILL_161 (unknown name)
            { 162, "Skill 162" },               // SKILL_162 (unknown name)
            { 169, "Skill 169" },               // SKILL_169 (unknown name)
            { 170, "Skill 170" },               // SKILL_170 (unknown name)
            { 171, "Skill 171" },               // SKILL_171 (unknown name)
            { 172, "Skill 172" },               // SKILL_172 (unknown name)
            { 173, "Skill 173" },               // SKILL_173 (unknown name)
            { 174, "Skill 174" },               // SKILL_174 (unknown name)
            { 179, "Skill 179" },               // SKILL_179 (unknown name)
            { 180, "Skill 180" },               // SKILL_180 (unknown name)
            { 291, "Skill 291" },               // SKILL_291 (unknown name)
            { 292, "Skill 292" },               // SKILL_292 (unknown name)
            { 293, "Skill 293" },               // SKILL_293 (unknown name)
            { 294, "Skill 294" },               // SKILL_294 (unknown name)
            { 295, "Skill 295" },               // SKILL_295 (unknown name)
            { 296, "Skill 296" },               // SKILL_296 (unknown name)
            { 297, "Skill 297" },               // SKILL_297 (unknown name)
            { 298, "Skill 298" },               // SKILL_298 (unknown name)
            { 299, "Skill 299" },               // SKILL_299 (unknown name)
            { 300, "Skill 300" },               // SKILL_300 (unknown name)
            { 301, "Skill 301" },               // SKILL_301 (unknown name)
            { 302, "Skill 302" },               // SKILL_302 (unknown name)
            { 303, "Skill 303" },               // SKILL_303 (unknown name)
            { 304, "Skill 304" },               // SKILL_304 (unknown name)
            { 305, "Skill 305" },               // SKILL_305 (unknown name)
            { 306, "Skill 306" },               // SKILL_306 (unknown name)
            { 307, "Skill 307" },               // SKILL_307 (unknown name)
            { 308, "Skill 308" },               // SKILL_308 (unknown name)
            { 309, "Skill 309" },               // SKILL_309 (unknown name)
            { 310, "Skill 310" },               // SKILL_310 (unknown name)
            { 311, "Skill 311" },               // SKILL_311 (unknown name)
            { 312, "Skill 312" },               // SKILL_312 (unknown name)
            { 313, "Skill 313" },               // SKILL_313 (unknown name)
            { 314, "Skill 314" },               // SKILL_314 (unknown name)
            { 315, "Skill 315" },               // SKILL_315 (unknown name)
            { 316, "Skill 316" },               // SKILL_316 (unknown name)
            { 317, "Skill 317" },               // SKILL_317 (unknown name)
            { 318, "Skill 318" },               // SKILL_318 (unknown name)
            { 319, "Skill 319" },               // SKILL_319 (unknown name)
            { 320, "Skill 320" },               // SKILL_320 (unknown name)
        };

        /// <summary>
        /// Get the human-readable name for a magic ID.
        /// </summary>
        /// <param name="magicId">Magic ID constant</param>
        /// <returns>Human-readable name, or "Unknown Magic {id}" if not mapped</returns>
        public static string GetName(int magicId)
        {
            return _idToName.TryGetValue(magicId, out var name)
                ? name
                : $"Unknown Magic {magicId}";
        }

        /// <summary>
        /// Check if a magic ID is known/registered.
        /// </summary>
        public static bool IsKnown(int magicId)
        {
            return _idToName.ContainsKey(magicId);
        }

        /// <summary>
        /// Get all registered magic IDs.
        /// </summary>
        public static IEnumerable<int> GetAllIds()
        {
            return _idToName.Keys;
        }

        /// <summary>
        /// Total count of registered magic IDs.
        /// </summary>
        public static int Count => _idToName.Count;
    }
}

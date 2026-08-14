using System.IO;
using System.Reflection;
using SystemModule;

namespace GameSvr.PasEngine
{
    /// <summary>
    /// PasScriptHost 扩展方法占位符
    /// </summary>
    public static class PasScriptHostExtensions
    {
        public static bool TryCallMonsterMain(this PasScriptHost host, TBaseObject animal, TPlayObject player)
        {
            // TODO: 从相关 commit 提取完整实现
            return false;
        }

        /// <summary>
        /// Get the environment path (Envir directory) from the PasScriptHost.
        /// Uses reflection to access the private _envirPath field.
        /// </summary>
        public static string GetEnvirPath(this PasScriptHost host)
        {
            if (host == null) return null;

            var field = typeof(PasScriptHost).GetField("_envirPath",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(host) as string;
        }

        /// <summary>
        /// Resolve a script path by name. Attempts to find the script in standard directories.
        /// </summary>
        public static string ResolveScriptPath(this PasScriptHost host, string scriptName)
        {
            if (host == null || string.IsNullOrWhiteSpace(scriptName))
                return null;

            var envirPath = host.GetEnvirPath();
            if (string.IsNullOrEmpty(envirPath))
                return null;

            // Try common script locations
            var candidates = new[]
            {
                Path.Combine(envirPath, "Market_Def", "QuestDiary", $"{scriptName}.pas"),
                Path.Combine(envirPath, "Market_Def", $"{scriptName}.pas"),
                Path.Combine(envirPath, "QuestDiary", $"{scriptName}.pas"),
                Path.Combine(envirPath, $"{scriptName}.pas")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }
    }
}

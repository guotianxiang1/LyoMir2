namespace GameSvr.PasEngine
{
    internal static class PasIncludeResolver
    {
        public static string Resolve(string includeName, string baseDirectory, string envirDirectory)
        {
            if (string.IsNullOrWhiteSpace(includeName)) return null;

            var normalizedName = includeName.Replace('/', Path.DirectorySeparatorChar);
            var searchDirectories = new[]
            {
                baseDirectory,
                Path.Combine(envirDirectory, "CommonScripts"),
                envirDirectory,
                Path.Combine(envirDirectory, "PsNpcscripts")
            };

            foreach (var directory in searchDirectories)
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                var path = Path.GetFullPath(Path.Combine(directory, normalizedName));
                if (File.Exists(path)) return path;
            }

            return null;
        }
    }
}

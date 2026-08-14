using SystemModule;
using SystemModule.Common;

namespace GameSvr.Configs
{
    /// <summary>
    /// Line-oriented loader for <c>config\BufferConf.txt</c> (sub_749BE4 @0x00749BE4).
    /// </summary>
    internal static class NativeBufferConf
    {
        internal const uint LoaderEa = 0x00749BE4;
        internal const string RelativePath = @"config\BufferConf.txt";
        internal const string LineErrorPrefix =
            "[Error]:BufferConf配置出错, line-";

        internal static bool TryLoad(string shareBaseDirectory, out int loadedLines,
            out string error)
        {
            loadedLines = 0;
            error = string.Empty;
            var path = Path.GetFullPath(Path.Combine(shareBaseDirectory, RelativePath));
            if (!File.Exists(path))
                return true;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, HUtil32.GbkEncoding);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            for (var lineNumber = 1; lineNumber <= lines.Length; lineNumber++)
            {
                var line = lines[lineNumber - 1].Trim();
                if (line.Length == 0 || line[0] == ';')
                    continue;
                if (line[0] == '#')
                    continue;
                if (!TryParseLine(line, out var parseError))
                {
                    M2Share.ErrorMessage(LineErrorPrefix + lineNumber);
                    error = parseError;
                    return false;
                }
                loadedLines++;
            }
            return true;
        }

        private static bool TryParseLine(string line, out string error)
        {
            error = string.Empty;
            var parts = line.Split(';');
            if (parts.Length < 5)
            {
                error = "expected at least 5 semicolon-separated fields";
                return false;
            }
            for (var index = 0; index < 5; index++)
            {
                if (string.IsNullOrWhiteSpace(parts[index]))
                {
                    error = "empty field at index " + index;
                    return false;
                }
            }
            return true;
        }
    }
}

using System.Text;

namespace GameSvr
{
    public enum NativeDynamicRoomDynamicNpcScriptRole
    {
        HiddenController,
        ConfiguredVisible
    }

    public sealed class NativeDynamicRoomDynamicNpcScriptBinding
    {
        public NativeDynamicRoomDynamicNpcScriptBinding(
            NativeDynamicRoomDefinition definition,
            NativeDynamicRoomDynamicNpcScriptRole role,
            NativeDynamicRoomConfiguredNpcDefinition configuredNpc,
            string scriptFileName, string scriptPath, bool hasScript,
            int scriptByteLength, string firstLine)
        {
            Definition = definition;
            Role = role;
            ConfiguredNpc = configuredNpc;
            ScriptFileName = scriptFileName;
            ScriptPath = scriptPath;
            HasScript = hasScript;
            ScriptByteLength = scriptByteLength;
            FirstLine = firstLine;
        }

        public NativeDynamicRoomDefinition Definition { get; }
        public NativeDynamicRoomDynamicNpcScriptRole Role { get; }
        public NativeDynamicRoomConfiguredNpcDefinition ConfiguredNpc { get; }
        public string ScriptFileName { get; }
        public string ScriptPath { get; }
        public bool HasScript { get; }
        public int ScriptByteLength { get; }
        public string FirstLine { get; }
    }

    public static class NativeDynamicRoomDynamicNpcScriptBindingPlanner
    {
        public static bool TryPlanBindings(
            IEnumerable<NativeDynamicRoomDefinition> definitions,
            string envirDirectory, int currentServerIndex,
            out IReadOnlyList<NativeDynamicRoomDynamicNpcScriptBinding> bindings,
            out IReadOnlyList<string> errors)
        {
            var planned = new List<NativeDynamicRoomDynamicNpcScriptBinding>();
            var diagnostics = new List<string>();
            bindings = Array.Empty<NativeDynamicRoomDynamicNpcScriptBinding>();
            errors = Array.Empty<string>();

            if (currentServerIndex < 0)
                diagnostics.Add($"invalid current server index: {currentServerIndex}");
            if (definitions == null)
                diagnostics.Add("dynamic room definitions are null");
            if (string.IsNullOrWhiteSpace(envirDirectory)
                || !Directory.Exists(envirDirectory))
                diagnostics.Add($"Envir directory not found: {envirDirectory}");
            if (diagnostics.Count > 0)
            {
                errors = diagnostics;
                return false;
            }

            var scriptDirectory = Path.GetFullPath(Path.Combine(
                envirDirectory, "DynRoomScripts"));
            if (!Directory.Exists(scriptDirectory))
            {
                errors = new[] { $"DynRoomScripts directory not found: {scriptDirectory}" };
                return false;
            }

            var seenControllerFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    diagnostics.Add("dynamic room definition is null");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(definition.RoomName))
                {
                    diagnostics.Add("dynamic room definition has an empty room name");
                    continue;
                }
                if (definition.ConfiguredNpcs == null)
                {
                    diagnostics.Add($"room {definition.RoomName}: configured NPC definitions are null");
                    continue;
                }
                var controllerFileName = BuildDynamicNpcScriptFileName(definition);
                if (seenControllerFiles.Add(controllerFileName))
                {
                    PlanBinding(definition,
                        NativeDynamicRoomDynamicNpcScriptRole.HiddenController,
                        null, controllerFileName, scriptDirectory, planned, diagnostics);
                }
                else
                {
                    diagnostics.Add($"room {definition.RoomName}: duplicate dynamic NPC script binding: {controllerFileName}");
                }

                foreach (var configuredNpc in definition.ConfiguredNpcs)
                {
                    if (configuredNpc == null)
                    {
                        diagnostics.Add($"room {definition.RoomName}: configured NPC is null");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(configuredNpc.ScriptName))
                    {
                        diagnostics.Add($"room {definition.RoomName}: configured NPC has an empty script name");
                        continue;
                    }

                    PlanBinding(definition,
                        NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible,
                        configuredNpc,
                        BuildConfiguredNpcScriptFileName(definition, configuredNpc),
                        scriptDirectory, planned, diagnostics);
                }
            }

            if (diagnostics.Count > 0)
            {
                errors = diagnostics;
                return false;
            }

            bindings = planned;
            return true;
        }

        public static string BuildDynamicNpcScriptFileName(
            NativeDynamicRoomDefinition definition)
        {
            return definition == null ? string.Empty : $"DNpc_{definition.RoomName}.pas";
        }

        public static string BuildConfiguredNpcScriptFileName(
            NativeDynamicRoomDefinition definition,
            NativeDynamicRoomConfiguredNpcDefinition configuredNpc)
        {
            return definition == null || configuredNpc == null
                ? string.Empty
                : $"{configuredNpc.ScriptName}-{definition.RoomName}.pas";
        }

        private static void PlanBinding(NativeDynamicRoomDefinition definition,
            NativeDynamicRoomDynamicNpcScriptRole role,
            NativeDynamicRoomConfiguredNpcDefinition configuredNpc,
            string fileName, string scriptDirectory,
            List<NativeDynamicRoomDynamicNpcScriptBinding> planned,
            List<string> diagnostics)
        {
            var subject = role == NativeDynamicRoomDynamicNpcScriptRole.HiddenController
                ? $"room {definition.RoomName}: hidden controller"
                : $"room {definition.RoomName}: configured NPC {configuredNpc.NpcName}";
            if (!IsSafeFileName(fileName))
            {
                diagnostics.Add($"{subject} has unsafe dynamic NPC script file name: {fileName}");
                return;
            }

            var scriptPath = Path.GetFullPath(Path.Combine(scriptDirectory, fileName));
            if (!IsUnderDirectory(scriptDirectory, scriptPath))
            {
                diagnostics.Add($"{subject} dynamic NPC script path escapes DynRoomScripts: {fileName}");
                return;
            }

            if (!File.Exists(scriptPath))
            {
                planned.Add(new NativeDynamicRoomDynamicNpcScriptBinding(
                    definition, role, configuredNpc, fileName, scriptPath,
                    false, 0, string.Empty));
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(scriptPath);
            }
            catch (IOException ex)
            {
                diagnostics.Add($"{subject} dynamic NPC script could not be read: {fileName} ({ex.Message})");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnostics.Add($"{subject} dynamic NPC script could not be read: {fileName} ({ex.Message})");
                return;
            }
            if (bytes.Length <= 0)
            {
                diagnostics.Add($"{subject} dynamic NPC script is empty: {fileName}");
                return;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var text = Encoding.GetEncoding(936).GetString(bytes);
            if (string.IsNullOrWhiteSpace(text))
            {
                diagnostics.Add($"{subject} dynamic NPC script has no GBK text: {fileName}");
                return;
            }

            planned.Add(new NativeDynamicRoomDynamicNpcScriptBinding(
                definition, role, configuredNpc, fileName, scriptPath,
                true, bytes.Length, FirstNonEmptyLine(text)));
        }

        private static bool IsSafeFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName)
                && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && fileName.IndexOfAny(new[] { '/', '\\' }) < 0
                && !Path.IsPathRooted(fileName);
        }

        private static bool IsUnderDirectory(string directory, string path)
        {
            var root = directory.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmptyLine(string text)
        {
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n')
                         .Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }
            return string.Empty;
        }
    }
}

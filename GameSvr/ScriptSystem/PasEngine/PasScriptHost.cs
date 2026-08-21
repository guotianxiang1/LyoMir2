using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using GameSvr;
using SystemModule;

namespace GameSvr.PasEngine
{
    public enum NpcPasScriptResolutionKind
    {
        Legacy,
        ExactDynamic,
        DynamicUnavailable
    }

    public sealed class NpcPasScriptResolution
    {
        internal NpcPasScriptResolution(NpcPasScriptResolutionKind kind,
            IReadOnlyList<string> scriptPaths,
            NativeDynamicRoomPasScriptBindingHandle dynamicBindingHandle)
        {
            Kind = kind;
            ScriptPaths = scriptPaths ?? Array.Empty<string>();
            DynamicBindingHandle = dynamicBindingHandle;
        }

        public NpcPasScriptResolutionKind Kind { get; }
        public IReadOnlyList<string> ScriptPaths { get; }
        public string ScriptPath => ScriptPaths.Count == 0 ? null : ScriptPaths[0];
        public NativeDynamicRoomPasScriptBindingHandle DynamicBindingHandle { get; }
    }

    public sealed class NpcPasScriptInteractionHandle
    {
        internal NpcPasScriptInteractionHandle(PasScriptHost owner,
            TPlayObject player, NormNpc npc,
            NativeDynamicRoomPasScriptBindingHandle dynamicBindingHandle)
        {
            Owner = owner;
            Player = player;
            Npc = npc;
            DynamicBindingHandle = dynamicBindingHandle;
        }

        internal PasScriptHost Owner { get; }
        internal TPlayObject Player { get; }
        internal NormNpc Npc { get; }
        internal NativeDynamicRoomPasScriptBindingHandle DynamicBindingHandle
            { get; }
    }

    public sealed class PasTaskMetadata
    {
        public PasTaskMetadata(int taskId, int taskType, string title, int picId)
        {
            TaskId = taskId;
            TaskType = taskType;
            Title = title ?? string.Empty;
            PicId = picId;
        }

        public int TaskId { get; }
        public int TaskType { get; }
        public string Title { get; }
        public int PicId { get; }
    }

    /// <summary>
    /// Manages loading, caching, and execution of .pas script files.
    /// Supports hot-reload, persistent NPC script state, Compiler.inc definitions,
    /// OnInitialize lifecycle, and PsNpcScript.txt mapping.
    /// </summary>
    public class PasScriptHost
    {
        private readonly string _envirPath;
        private readonly string _commonScriptsPath;
        private readonly string _itemScriptsPath;
        private readonly string _taskScriptsPath;
        private readonly string _monsterScriptsPath;
        private readonly PasApiBridge _api;
        private readonly NativeDynamicRoomPasScriptRouteTable _dynamicNpcRoutes;
        private readonly NativeDynamicRoomRuntime _dynamicRoomRuntime;
        private readonly ConcurrentDictionary<string, PasProgram> _cache;
        private readonly ConcurrentDictionary<string, DateTime> _fileTimestamps;

        // Persistent NPC script state: one serialized interpreter per live NPC.
        private readonly ConcurrentDictionary<int, NpcScriptState> _npcStates = new();
        private readonly ConcurrentDictionary<int, NpcInteractionBinding>
            _npcInteractionBindings = new();
        private readonly object _taskScriptsLock = new();
        private readonly List<TaskScriptState> _taskScripts = new();
        private readonly Dictionary<int, TaskScriptState> _taskScriptsById = new();
        private bool _taskScriptsLoaded;
        private readonly object _monsterScriptsLock = new();
        private readonly Dictionary<string, string> _monsterScriptPaths =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, MonsterScriptState> _monsterStates = new();
        private bool _monsterScriptsLoaded;
        private bool _compilerIncLoaded;
        private readonly object _compilerIncLock = new();
        private readonly HashSet<string> _scriptDefines = new(StringComparer.OrdinalIgnoreCase);

        private class NpcScriptState
        {
            public NormNpc Npc;
            public string ScriptPath;
            public PasProgram Program;
            public PasInterpreter Interpreter;
            public NativeDynamicRoomPasScriptBindingHandle DynamicBindingHandle;
            public bool Initialized;
            public readonly object SyncRoot = new();
        }

        private sealed class NpcInteractionBinding
        {
            public TPlayObject Player;
            public NormNpc Npc;
            public NativeDynamicRoomPasScriptBindingHandle DynamicBindingHandle;
        }

        private sealed class TaskScriptState
        {
            public string ScriptName;
            public string ScriptPath;
            public PasProgram Program;
            public PasInterpreter Interpreter;
            public PasTaskMetadata Metadata;
            public readonly object SyncRoot = new();
        }

        private sealed class MonsterScriptState
        {
            public TBaseObject Animal;
            public string ScriptPath;
            public PasProgram Program;
            public PasInterpreter Interpreter;
            public bool Initialized;
            public readonly object SyncRoot = new();
        }

        private sealed class PreprocessorFrame
        {
            public bool ParentActive;
            public bool ConditionActive;
            public bool ElseSeen;
            public bool Active => ParentActive && ConditionActive;
        }

        private sealed class PreprocessedSource
        {
            private readonly StringBuilder _text = new();

            public List<PasSourceLine> Lines { get; } = new();
            public string Text => _text.ToString();

            public void AppendLine(string text, string sourceFile, int sourceLine)
            {
                _text.AppendLine(text);
                Lines.Add(new PasSourceLine(sourceFile, sourceLine));
            }

            public void Append(PreprocessedSource source)
            {
                if (source == null) return;
                _text.Append(source._text);
                Lines.AddRange(source.Lines);
            }
        }

        public PasScriptHost(string envirPath)
            : this(envirPath, null, null)
        {
        }

        public PasScriptHost(string envirPath,
            NativeDynamicRoomPasScriptRouteTable dynamicNpcRoutes)
            : this(envirPath, dynamicNpcRoutes, null)
        {
        }

        public PasScriptHost(string envirPath,
            NativeDynamicRoomPasScriptRouteTable dynamicNpcRoutes,
            NativeDynamicRoomRuntime dynamicRoomRuntime)
        {
            _envirPath = envirPath;
            _commonScriptsPath = Path.Combine(envirPath, "CommonScripts");
            _itemScriptsPath = Path.Combine(envirPath, "PsItemScript");
            _taskScriptsPath = Path.Combine(envirPath, "PsTaskList");
            _monsterScriptsPath = Path.Combine(envirPath, "MonScript");
            _api = new PasApiBridge();
            _dynamicNpcRoutes = dynamicNpcRoutes;
            _dynamicRoomRuntime = dynamicRoomRuntime;
            _cache = new ConcurrentDictionary<string, PasProgram>(StringComparer.OrdinalIgnoreCase);
            _fileTimestamps = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            LoadScriptDefines();
        }

        public PasApiBridge Api => _api;

        public int LoadMonsterScripts()
        {
            lock (_monsterScriptsLock)
            {
                if (_monsterScriptsLoaded) return _monsterScriptPaths.Count;

                _monsterScriptPaths.Clear();
                var configPath = Path.Combine(_envirPath, "monScript.txt");
                if (!File.Exists(configPath))
                {
                    _monsterScriptsLoaded = true;
                    return 0;
                }

                foreach (var rawLine in PasScriptTextReader.ReadAllLines(configPath))
                {
                    var monsterName = rawLine.Trim();
                    if (monsterName.Length == 0 || monsterName[0] == ';') continue;
                    if (_monsterScriptPaths.ContainsKey(monsterName))
                    {
                        LogMonsterMessage(
                            $"[PasEngine] Duplicate monster script entry rejected: {monsterName}");
                        continue;
                    }

                    var scriptPath = ResolveMonsterScriptPath(monsterName);
                    if (scriptPath == null)
                    {
                        LogMonsterMessage($"[PasEngine] Monster script not found: {monsterName}");
                        continue;
                    }

                    _monsterScriptPaths.Add(monsterName, scriptPath);
                }

                _monsterScriptsLoaded = true;
                return _monsterScriptPaths.Count;
            }
        }

        public bool TryInitializeMonsterScript(TBaseObject animal)
        {
            if (!TryGetMonsterScriptState(animal, out var state)) return false;

            return TryInitializeMonsterScriptState(animal, state);
        }

        private bool TryInitializeMonsterScriptState(TBaseObject animal,
            MonsterScriptState state)
        {
            lock (state.SyncRoot)
            {
                if (state.Initialized) return true;
                try
                {
                    using var context = _api.PushAnimalContext(null, animal);
                    var procedure = FindProcedure(state.Program, "OnInitialize");
                    if (procedure != null)
                        state.Interpreter.ExecuteProcedure(procedure.Name);
                    return true;
                }
                catch (Exception ex)
                {
                    LogMonsterMessage(
                        $"[PasEngine] Monster script initialize failed {animal.m_sCharName}: {ex.Message}");
                    return false;
                }
                finally
                {
                    // Native attaches one script object at spawn and runs initialization once.
                    state.Initialized = true;
                }
            }
        }

        /// <summary>
        /// Native sub_67DC40 walks the manager's script-attached monster list and
        /// calls TAnimal.LoadScript (sub_71F240) for each entry. The native loader
        /// releases the old script object, reloads that animal's existing path, and
        /// initializes the replacement; it does not reread monScript.txt.
        /// </summary>
        public int ReloadActiveMonsterScripts()
        {
            var snapshot = _monsterStates.ToArray();
            var reloaded = 0;
            var allSucceeded = true;
            var reloadedPrograms = new Dictionary<string, PasProgram>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in snapshot)
            {
                var oldState = entry.Value;
                lock (oldState.SyncRoot)
                {
                    if (!_monsterStates.TryGetValue(entry.Key, out var current) ||
                        !ReferenceEquals(current, oldState))
                    {
                        allSucceeded = false;
                        continue;
                    }

                    if (!File.Exists(oldState.ScriptPath))
                    {
                        LogMonsterMessage(
                            $"[Exception]:TAnimal.LoadScript:{oldState.ScriptPath}: 不存在");
                        RemoveMonsterState(entry.Key, oldState);
                        allSucceeded = false;
                        continue;
                    }

                    if (!reloadedPrograms.TryGetValue(oldState.ScriptPath,
                            out var program))
                    {
                        Invalidate(oldState.ScriptPath);
                        program = GetOrLoadProgram(oldState.ScriptPath);
                        if (program != null)
                            reloadedPrograms.Add(oldState.ScriptPath, program);
                    }
                    if (program == null)
                    {
                        RemoveMonsterState(entry.Key, oldState);
                        allSucceeded = false;
                        continue;
                    }

                    var replacement = new MonsterScriptState
                    {
                        Animal = oldState.Animal,
                        ScriptPath = oldState.ScriptPath,
                        Program = program,
                        Interpreter = CreateInterpreter(program)
                    };
                    if (!_monsterStates.TryUpdate(entry.Key, replacement, oldState) ||
                        !TryInitializeMonsterScriptState(replacement.Animal, replacement))
                    {
                        allSucceeded = false;
                        continue;
                    }

                    reloaded++;
                }
            }

            if (allSucceeded)
                LogMonsterMessage($"成功刷新怪物脚本{snapshot.Length}个");
            return reloaded;
        }

        public bool TryCallAfterScatterItems(TBaseObject animal, TPlayObject player,
            IReadOnlyList<KeyValuePair<string, string>> scatteredItems)
        {
            if (animal == null || player == null || animal.ObjectId <= 0 ||
                !_monsterStates.TryGetValue(animal.ObjectId, out var state))
                return false;

            var procedure = FindProcedure(state.Program, "AfterScatterItems");
            if (procedure == null) return false;

            var count = scatteredItems?.Count ?? 0;
            var keys = new PasArray(0, count - 1, "string");
            var values = new PasArray(0, count - 1, "string");
            for (var index = 0; index < count; index++)
            {
                keys[index] = PasValue.FromString(scatteredItems[index].Key);
                values[index] = PasValue.FromString(scatteredItems[index].Value);
            }

            lock (state.SyncRoot)
            {
                try
                {
                    using var context = _api.PushAnimalContext(player, animal);
                    state.Interpreter.ExecuteProcedure(procedure.Name, new List<PasValue>
                    {
                        PasValue.FromArray(keys),
                        PasValue.FromArray(values)
                    });
                    return true;
                }
                catch (Exception ex)
                {
                    LogMonsterMessage(
                        $"[PasEngine] Monster script callback failed {animal.m_sCharName}:AfterScatterItems - {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// The monster-side @main entry, reached from CM_CLICKNPC when the packet's Tag is 1.
        /// 0x6B8B5A `83 7D FC 01 cmp dword[ebp-4],1` / 0x6B8B5E `0F 85 8B 00 00 00 jne 0x6B8BEF`
        /// picks the arm and 0x6B8BA2 `E8 9D 78 06 00 call 0x720444` runs
        ///   0x720449  B9 60 04 72 00  mov ecx,0x720460     ; literal "@main", declen 5
        ///   0x72044E  E8 15 00 00 00  call 0x720468
        /// which dispatches through the script object at monster+0x4D0
        /// (0x7204BE `8B 80 D0 04 00 00` then 0x7204CA `FF 56 44 call [vmt+0x44]`).
        ///
        /// That offset is what tells the two CM_CLICKNPC arms apart: the NPC arm's script object
        /// is at npc+0x570 (0x63DCF2 `8B 80 70 05 00 00`), a different class. The owning unit is
        /// the monster one — its literals are 0x71EBDC "monScript\", 0x71F338
        /// "[Exception]:TAnimal.LoadScript:", 0x71E8A4 "怪物名: " and 0x720158
        /// "@AfterScatterItems", the callback modelled directly above.
        ///
        /// Native runs a GM-only diagnostic when the script call returns False
        /// (0x7204D0 `80 7D F7 00 cmp byte[ebp-9],0` / 0x7204DA `cmp byte[player+0x675],3 / jbe`,
        /// then a four-part concat of 0x7205B4 "[ExecScript Fail]: " and monster+0x4CC). That is
        /// not reproduced here: the identity of monster+0x4CC and the argument order of the
        /// concat helper 0x405890 are both unproven, and the line is invisible below GM level 4.
        /// </summary>
        public bool TryCallMonsterMain(TBaseObject animal, TPlayObject player)
        {
            if (animal == null || animal.ObjectId <= 0 ||
                !_monsterStates.TryGetValue(animal.ObjectId, out var state))
                return false;

            var procedure = FindProcedure(state.Program, "main");
            if (procedure == null) return false;

            lock (state.SyncRoot)
            {
                try
                {
                    using var context = _api.PushAnimalContext(player, animal);
                    state.Interpreter.ExecuteProcedure(procedure.Name);
                    return true;
                }
                catch (Exception ex)
                {
                    LogMonsterMessage(
                        $"[PasEngine] Monster script callback failed {animal.m_sCharName}:main - {ex.Message}");
                    return false;
                }
            }
        }

        public void ClearMonsterScriptState(int objectId)
        {
            if (objectId > 0) _monsterStates.TryRemove(objectId, out _);
        }

        private void RemoveMonsterState(int objectId,
            MonsterScriptState expectedState)
        {
            if (expectedState == null) return;
            ((ICollection<KeyValuePair<int, MonsterScriptState>>)_monsterStates)
                .Remove(new KeyValuePair<int, MonsterScriptState>(objectId,
                    expectedState));
        }

        private string ResolveMonsterScriptPath(string monsterName)
        {
            if (string.IsNullOrWhiteSpace(monsterName) || monsterName is "." or ".." ||
                monsterName.EndsWith(".", StringComparison.Ordinal) ||
                monsterName.EndsWith(" ", StringComparison.Ordinal) ||
                monsterName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;

            try
            {
                var root = Path.GetFullPath(_monsterScriptsPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(Path.Combine(root, monsterName + ".pas"));
                return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                       candidate.EndsWith(".pas", StringComparison.OrdinalIgnoreCase) &&
                       File.Exists(candidate)
                    ? candidate
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetMonsterScriptState(TBaseObject animal, out MonsterScriptState state)
        {
            state = null;
            if (animal == null || animal.ObjectId <= 0 || string.IsNullOrWhiteSpace(animal.m_sCharName))
                return false;

            LoadMonsterScripts();
            string scriptPath;
            lock (_monsterScriptsLock)
            {
                if (!_monsterScriptPaths.TryGetValue(animal.m_sCharName, out scriptPath))
                    return false;
            }

            LoadCompilerInc();
            var program = GetOrLoadProgram(scriptPath);
            if (program == null) return false;

            while (true)
            {
                if (_monsterStates.TryGetValue(animal.ObjectId, out var existing) &&
                    ReferenceEquals(existing.Animal, animal) &&
                    string.Equals(existing.ScriptPath, scriptPath, StringComparison.OrdinalIgnoreCase) &&
                    ReferenceEquals(existing.Program, program))
                {
                    state = existing;
                    return true;
                }

                var replacement = new MonsterScriptState
                {
                    Animal = animal,
                    ScriptPath = scriptPath,
                    Program = program,
                    Interpreter = CreateInterpreter(program)
                };
                if (existing == null)
                {
                    if (_monsterStates.TryAdd(animal.ObjectId, replacement))
                    {
                        state = replacement;
                        return true;
                    }
                }
                else if (_monsterStates.TryUpdate(animal.ObjectId, replacement, existing))
                {
                    state = replacement;
                    return true;
                }
            }
        }

        private static void LogMonsterMessage(string message)
        {
            try
            {
                M2Share.MainOutMessage(message);
            }
            catch (TypeInitializationException)
            {
                System.Diagnostics.Debug.WriteLine(message);
            }
        }

        public int LoadTaskScripts()
        {
            lock (_taskScriptsLock)
            {
                if (_taskScriptsLoaded) return _taskScripts.Count;

                _taskScripts.Clear();
                _taskScriptsById.Clear();
                LoadCompilerInc();

                var configPath = Path.Combine(_taskScriptsPath, "PsTaskConfig.txt");
                if (!File.Exists(configPath))
                {
                    LogTaskMessage($"[PasEngine] Task config not found: {configPath}");
                    _taskScriptsLoaded = true;
                    return 0;
                }

                foreach (var rawLine in PasScriptTextReader.ReadAllText(configPath)
                             .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    var scriptName = rawLine.Trim();
                    if (scriptName.Length == 0 || scriptName[0] == ';') continue;

                    TryLoadTaskScript(scriptName);
                }

                _taskScriptsLoaded = true;
                return _taskScripts.Count;
            }
        }

        public IReadOnlyList<PasTaskMetadata> GetTaskScripts()
        {
            LoadTaskScripts();
            lock (_taskScriptsLock)
            {
                return _taskScripts.Select(state => state.Metadata).ToArray();
            }
        }

        public bool TryGetTaskState(int taskId, TPlayObject player, out int state)
        {
            state = 0;
            if (!TryInvokeTask(taskId, player, "GetTaskState", null, out var result)) return false;
            state = result.AsInt();
            return true;
        }

        public bool TryGetTaskDetail(int taskId, TPlayObject player, out string detail)
        {
            detail = string.Empty;
            if (!TryInvokeTask(taskId, player, "GetTaskDetail", null, out var result)) return false;
            detail = result.AsString();
            return true;
        }

        public bool TryGetTaskProgress(int taskId, TPlayObject player, out string progress)
        {
            progress = string.Empty;
            if (!TryInvokeTask(taskId, player, "GetTaskProgress", null, out var result)) return false;
            progress = result.AsString();
            return true;
        }

        public bool TryDoTaskCommand(int taskId, TPlayObject player, string value, out bool handled)
        {
            handled = false;
            var args = new List<PasValue> { PasValue.FromString(value ?? string.Empty) };
            if (!TryInvokeTask(taskId, player, "DoTaskCommand", args, out var result)) return false;
            handled = result.AsBool();
            return true;
        }

        private void TryLoadTaskScript(string scriptName)
        {
            var taskRoot = Path.GetFullPath(_taskScriptsPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string scriptPath;
            try
            {
                scriptPath = Path.GetFullPath(Path.Combine(taskRoot, scriptName + ".pas"));
            }
            catch (Exception ex)
            {
                LogTaskMessage($"[PasEngine] Invalid task script name '{scriptName}': {ex.Message}");
                return;
            }

            if (!scriptPath.StartsWith(taskRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(scriptPath))
            {
                LogTaskMessage($"[PasEngine] Task script not found: {scriptName}");
                return;
            }

            try
            {
                var program = GetOrLoadProgram(scriptPath);
                if (program == null) return;

                var interpreter = CreateInterpreter(program);
                int taskId;
                int taskType;
                string title;
                int picId = 0;
                using (var context = _api.PushContext(null, null))
                {
                    taskId = interpreter.ExecuteProcedure("GetTaskID").AsInt();
                    taskType = interpreter.ExecuteProcedure("GetTaskType").AsInt();
                    title = interpreter.ExecuteProcedure("GetTaskTitle").AsString();
                    if (taskType == 20 && FindProcedure(program, "GetTaskPicId") != null)
                        picId = interpreter.ExecuteProcedure("GetTaskPicId").AsInt();
                }

                if (_taskScriptsById.ContainsKey(taskId))
                {
                    LogTaskMessage(
                        $"[PasEngine] Duplicate task id {taskId} rejected: {scriptName}");
                    return;
                }

                var state = new TaskScriptState
                {
                    ScriptName = scriptName,
                    ScriptPath = scriptPath,
                    Program = program,
                    Interpreter = interpreter,
                    Metadata = new PasTaskMetadata(taskId, taskType, title, picId)
                };
                _taskScripts.Add(state);
                _taskScriptsById.Add(taskId, state);
            }
            catch (Exception ex)
            {
                LogTaskMessage($"[PasEngine] Task script load failed {scriptName}: {ex.Message}");
            }
        }

        private bool TryInvokeTask(int taskId, TPlayObject player, string functionName,
            List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            if (player == null) return false;

            LoadTaskScripts();
            TaskScriptState state;
            lock (_taskScriptsLock)
            {
                if (!_taskScriptsById.TryGetValue(taskId, out state)) return false;
            }

            lock (state.SyncRoot)
            {
                try
                {
                    using var context = _api.PushContext(player, null);
                    result = state.Interpreter.ExecuteProcedure(functionName,
                        args ?? new List<PasValue>());
                    return true;
                }
                catch (Exception ex)
                {
                    LogTaskMessage(
                        $"[PasEngine] Task script error {state.ScriptName}:{functionName} - {ex.Message}");
                    result = PasValue.Nil;
                    return false;
                }
            }
        }

        private static void LogTaskMessage(string message)
        {
            try
            {
                M2Share.MainOutMessage(message);
            }
            catch (TypeInitializationException)
            {
                System.Diagnostics.Debug.WriteLine(message);
            }
        }

        public NpcPasScriptResolution ResolveNpcScript(NormNpc npc)
        {
            if (npc == null)
            {
                return new NpcPasScriptResolution(
                    NpcPasScriptResolutionKind.Legacy,
                    Array.Empty<string>(), null);
            }

            if (_dynamicNpcRoutes != null)
            {
                var routeState = _dynamicNpcRoutes.ResolveCurrent(npc,
                    out var handle, out var exactPath);
                if (routeState == NativeDynamicRoomPasScriptRouteState.ExactCurrent)
                {
                    return new NpcPasScriptResolution(
                        NpcPasScriptResolutionKind.ExactDynamic,
                        new[] { exactPath }, handle);
                }
                if (routeState == NativeDynamicRoomPasScriptRouteState
                        .DynamicUnavailableOrStale)
                {
                    return new NpcPasScriptResolution(
                        NpcPasScriptResolutionKind.DynamicUnavailable,
                        Array.Empty<string>(), null);
                }
            }

            var scriptName = (npc as Merchant)?.m_sScript ?? npc.m_sCharName;
            string[] candidates =
            {
                scriptName + "-" + npc.m_sMapName,
                scriptName,
                npc.m_sCharName + "-" + npc.m_sMapName,
                npc.m_sCharName
            };
            var paths = new List<string>(candidates.Length);
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var path = FindScriptFile(candidate);
                if (path != null) paths.Add(path);
            }
            return new NpcPasScriptResolution(
                NpcPasScriptResolutionKind.Legacy, paths.AsReadOnly(), null);
        }

        private NpcPasScriptResolution ResolveNpcInteractionScript(
            NormNpc npc, TPlayObject player)
        {
            if (npc == null) return DynamicNpcUnavailable();
            if (player == null || player.ObjectId <= 0
                || !_npcInteractionBindings.TryGetValue(player.ObjectId,
                    out var interaction)
                || !ReferenceEquals(interaction.Player, player)
                || !ReferenceEquals(interaction.Npc, npc))
            {
                var current = ResolveNpcScript(npc);
                return current.Kind == NpcPasScriptResolutionKind.ExactDynamic
                    ? DynamicNpcUnavailable()
                    : current;
            }

            if (interaction.DynamicBindingHandle == null)
            {
                var current = ResolveNpcScript(npc);
                return current.Kind == NpcPasScriptResolutionKind.Legacy
                    ? current
                    : DynamicNpcUnavailable();
            }

            if (_dynamicNpcRoutes != null
                && _dynamicNpcRoutes.ValidateExpected(npc,
                    interaction.DynamicBindingHandle, out var exactPath))
            {
                return new NpcPasScriptResolution(
                    NpcPasScriptResolutionKind.ExactDynamic,
                    new[] { exactPath }, interaction.DynamicBindingHandle);
            }
            return DynamicNpcUnavailable();
        }

        private NpcPasScriptResolution ResolveNpcInteractionScript(
            NpcPasScriptInteractionHandle expectedInteraction)
        {
            if (expectedInteraction == null
                || !ReferenceEquals(expectedInteraction.Owner, this)
                || expectedInteraction.Player == null
                || expectedInteraction.Npc == null
                || expectedInteraction.Player.m_boGhost
                || M2Share.ObjectManager == null
                || !ReferenceEquals(M2Share.ObjectManager.Get(
                    expectedInteraction.Player.ObjectId),
                    expectedInteraction.Player))
                return DynamicNpcUnavailable();

            if (expectedInteraction.DynamicBindingHandle == null)
            {
                var current = ResolveNpcScript(expectedInteraction.Npc);
                return current.Kind == NpcPasScriptResolutionKind.Legacy
                    ? current
                    : DynamicNpcUnavailable();
            }

            if (_dynamicNpcRoutes != null
                && _dynamicNpcRoutes.ValidateExpected(expectedInteraction.Npc,
                    expectedInteraction.DynamicBindingHandle,
                    out var exactPath))
            {
                return new NpcPasScriptResolution(
                    NpcPasScriptResolutionKind.ExactDynamic,
                    new[] { exactPath },
                    expectedInteraction.DynamicBindingHandle);
            }
            return DynamicNpcUnavailable();
        }

        public NpcPasScriptInteractionHandle CaptureNpcInteraction(
            TPlayObject player, NormNpc npc)
        {
            TryCaptureNpcInteraction(player, npc, out var interaction,
                out _);
            return interaction;
        }

        public bool TryCaptureNpcInteraction(TPlayObject player, NormNpc npc,
            out NpcPasScriptInteractionHandle interaction,
            out NpcPasScriptResolutionKind kind)
        {
            interaction = null;
            var resolution = ResolveNpcInteractionScript(npc, player);
            kind = resolution.Kind;
            if (resolution.Kind == NpcPasScriptResolutionKind
                    .DynamicUnavailable
                || player == null || npc == null)
                return false;

            interaction = new NpcPasScriptInteractionHandle(this, player, npc,
                resolution.DynamicBindingHandle);
            return true;
        }

        private NpcInteractionBinding BindNpcInteraction(TPlayObject player,
            NormNpc npc, NpcPasScriptResolution resolution)
        {
            if (player == null || player.ObjectId <= 0 || npc == null
                || resolution == null
                || resolution.Kind == NpcPasScriptResolutionKind
                    .DynamicUnavailable)
                return null;

            var interaction = new NpcInteractionBinding
            {
                Player = player,
                Npc = npc,
                DynamicBindingHandle = resolution.DynamicBindingHandle
            };
            _npcInteractionBindings[player.ObjectId] = interaction;
            return interaction;
        }

        private NpcInteractionBinding GetNpcInteraction(TPlayObject player,
            NormNpc npc)
        {
            if (player == null || player.ObjectId <= 0 || npc == null
                || !_npcInteractionBindings.TryGetValue(player.ObjectId,
                    out var interaction)
                || !ReferenceEquals(interaction.Player, player)
                || !ReferenceEquals(interaction.Npc, npc))
                return null;
            return interaction;
        }

        private void RemoveNpcInteraction(NpcInteractionBinding interaction)
        {
            if (interaction?.Player == null) return;
            ((ICollection<KeyValuePair<int, NpcInteractionBinding>>)
                    _npcInteractionBindings)
                .Remove(new KeyValuePair<int, NpcInteractionBinding>(
                    interaction.Player.ObjectId, interaction));
        }

        private static NpcPasScriptResolution DynamicNpcUnavailable()
        {
            return new NpcPasScriptResolution(
                NpcPasScriptResolutionKind.DynamicUnavailable,
                Array.Empty<string>(), null);
        }

        public string FindItemScriptFile(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName) || itemName is "." or ".." ||
                itemName.EndsWith(".", StringComparison.Ordinal) ||
                itemName.EndsWith(" ", StringComparison.Ordinal) ||
                itemName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;

            var itemRoot = Path.GetFullPath(_itemScriptsPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(itemRoot, itemName + ".pas"));
            if (!candidate.StartsWith(itemRoot, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(candidate) ? candidate : null;
        }

        public bool TryPreloadItemScript(string itemName,
            out string scriptPath, out string error)
        {
            error = string.Empty;
            scriptPath = FindItemScriptFile(itemName);
            if (scriptPath == null) return false;

            LoadCompilerInc();
            return GetOrLoadProgram(scriptPath, out error) != null;
        }

        public bool TryCallItemProcedure(string scriptPath, string procedureName,
            TPlayObject player, TUserItem item, out PasValue result)
        {
            result = PasValue.Nil;
            if (player == null || item == null || string.IsNullOrWhiteSpace(scriptPath) ||
                string.IsNullOrWhiteSpace(procedureName))
                return false;

            var fullPath = scriptPath;
            try
            {
                var itemRoot = Path.GetFullPath(_itemScriptsPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                fullPath = Path.GetFullPath(scriptPath);
                if (!fullPath.StartsWith(itemRoot, StringComparison.OrdinalIgnoreCase) ||
                    !fullPath.EndsWith(".pas", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(fullPath))
                    return false;
                LoadCompilerInc();
                var program = GetOrLoadProgram(fullPath);
                var procedure = program == null ? null : FindProcedure(program, procedureName);
                if (procedure == null) return false;
                return TryInvokeWithInterpreter(fullPath, program, player, null,
                    false, null,
                    interpreter => interpreter.ExecuteProcedure(procedure.Name),
                    out result, item);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[PasEngine] Item script error in {fullPath}:{procedureName} - {ex.Message}");
                return false;
            }
        }

        public bool TryCallNpcProcedure(NormNpc npc,
            IReadOnlyList<string> procedureNames, TPlayObject player,
            out PasValue result, params PasValue[] args)
        {
            result = PasValue.Nil;
            if (npc == null || procedureNames == null
                || procedureNames.Count == 0)
                return false;

            return TryCallResolvedNpcProcedure(
                ResolveNpcInteractionScript(npc, player), npc,
                procedureNames, player, null, out result, args);
        }

        public bool TryCallNpcProcedure(
            NpcPasScriptInteractionHandle expectedInteraction,
            IReadOnlyList<string> procedureNames, out PasValue result,
            params PasValue[] args)
        {
            result = PasValue.Nil;
            if (expectedInteraction == null || procedureNames == null
                || procedureNames.Count == 0
                || !ReferenceEquals(expectedInteraction.Owner, this))
                return false;

            return TryCallResolvedNpcProcedure(
                ResolveNpcInteractionScript(expectedInteraction),
                expectedInteraction.Npc, procedureNames,
                expectedInteraction.Player, null, out result, args);
        }

        public bool TryCallNpcItemProcedure(NormNpc npc, string procedureName,
            TPlayObject player, TUserItem item, out PasValue result,
            params PasValue[] args)
        {
            result = PasValue.Nil;
            if (npc == null || player == null || item == null
                || string.IsNullOrWhiteSpace(procedureName))
                return false;

            return TryCallResolvedNpcProcedure(
                ResolveNpcInteractionScript(npc, player), npc,
                new[] { procedureName }, player, item, out result, args);
        }

        private bool TryCallResolvedNpcProcedure(
            NpcPasScriptResolution resolution, NormNpc npc,
            IReadOnlyList<string> procedureNames, TPlayObject player,
            TUserItem item, out PasValue result, PasValue[] args)
        {
            result = PasValue.Nil;
            if (resolution == null
                || resolution.Kind == NpcPasScriptResolutionKind
                    .DynamicUnavailable)
                return false;

            foreach (var scriptPath in resolution.ScriptPaths)
            {
                try
                {
                    LoadCompilerInc();
                    var program = GetOrLoadProgram(scriptPath);
                    if (program == null) continue;
                    foreach (var procedureName in procedureNames)
                    {
                        if (string.IsNullOrWhiteSpace(procedureName)) continue;
                        var procedure = FindProcedure(program, procedureName);
                        if (procedure == null) continue;
                        return TryInvokeWithInterpreter(scriptPath, program,
                            player, npc, false, null,
                            interpreter => interpreter.ExecuteProcedure(
                                procedure.Name, new List<PasValue>(args
                                    ?? Array.Empty<PasValue>())), out result,
                            item, resolution.DynamicBindingHandle);
                    }
                }
                catch (Exception ex)
                {
                    M2Share.MainOutMessage(
                        $"[PasEngine] NPC procedure error in {scriptPath} - {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Set the execution context for the current script call.
        /// </summary>
        public void SetContext(TPlayObject player, NormNpc npc)
        {
            _api.CurrentPlayer = player;
            _api.CurrentNpc = npc;
            _api.CurrentAnimal = null;
            _api.CurrentItem = null;
        }

        /// <summary>
        /// Load and execute a procedure from a .pas file.
        /// </summary>
        /// <param name="pasFilePath">Absolute path to the .pas file</param>
        /// <param name="procedureName">Procedure name to call (without leading underscore)</param>
        /// <param name="args">Optional arguments</param>
        public void CallProcedure(string pasFilePath, string procedureName, params PasValue[] args)
        {
            CallProcedure(pasFilePath, procedureName, _api.CurrentPlayer, _api.CurrentNpc, args);
        }

        public bool TryInitializeYanshen(TPlayObject player)
        {
            var plugin = M2Share.PluginManager?.GetPlugin("YanshenCompat");
            if (plugin?.State != GameSvr.Plugins.PluginState.Running) return false;

            // A6 登录播种（战神 DLL 0x100CE4EA，this=玩家）：登录初始化路径对玩家
            // S 银行灌种一次，早于 shipped initys()。幂等（哨兵 S(1,49)==1314 时整段
            // 跳过），且与 RunQuest.pas 是否存在无关——原生只要眼神 DLL 在场就播种，
            // 故置于插件门内、脚本文件门外。仅在眼神插件运行时播种，避免污染
            // 非眼神场景下的 S(1,1..150) 脚本变量。
            player?.YanshenSeedLoginSVars();

            var scriptPath = Path.Combine(_envirPath, "PsMapQuest", "RunQuest.pas");
            return File.Exists(scriptPath) &&
                   TryCallProcedure(scriptPath, "initys", player, null);
        }

        public void CallProcedure(string pasFilePath, string procedureName,
            TPlayObject player, NormNpc npc, params PasValue[] args)
        {
            try
            {
                LoadCompilerInc();
                var program = GetOrLoadProgram(pasFilePath);
                if (program == null) return;
                _ = TryInvokeWithInterpreter(pasFilePath, program, player, npc,
                    false, null,
                    interpreter => interpreter.ExecuteProcedure(procedureName,
                        new List<PasValue>(args ?? Array.Empty<PasValue>())),
                    out _);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[PasEngine] Error in {pasFilePath}:{procedureName} - {ex.Message}");
            }
        }

        public bool TryCallProcedure(string pasFilePath, string procedureName,
            TPlayObject player, NormNpc npc, params PasValue[] args)
        {
            return TryCallProcedure(pasFilePath, procedureName, player, npc,
                out _, args);
        }

        public bool TryCallProcedure(string pasFilePath, string procedureName,
            TPlayObject player, NormNpc npc, out PasValue result,
            params PasValue[] args)
        {
            result = PasValue.Nil;
            if (string.IsNullOrWhiteSpace(pasFilePath) ||
                string.IsNullOrWhiteSpace(procedureName) || !File.Exists(pasFilePath))
                return false;

            try
            {
                LoadCompilerInc();
                var program = GetOrLoadProgram(pasFilePath);
                var procedure = program == null ? null : FindProcedure(program, procedureName);
                if (procedure == null) return false;

                return TryInvokeWithInterpreter(pasFilePath, program, player,
                    npc, false, null,
                    interpreter => interpreter.ExecuteProcedure(procedure.Name,
                        new List<PasValue>(args ?? Array.Empty<PasValue>())),
                    out result);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage(
                    $"[PasEngine] Error in {pasFilePath}:{procedureName} - {ex.Message}");
                return false;
            }
        }

        public bool TryCallYbShopProcedure(TPlayObject player,
            string procedureName, out PasValue result, params PasValue[] args)
        {
            var scriptPath = Path.Combine(_envirPath, "YBShop",
                "YBShopScript.pas");
            return TryCallProcedure(scriptPath, procedureName, player, null,
                out result, args);
        }

        public bool TryCallTaskDispatchProcedure(TPlayObject player,
            string procedureName, out PasValue result, params PasValue[] args)
        {
            var scriptPath = Path.Combine(_envirPath, "PsMapQuest",
                "TaskDispatch.pas");
            return TryCallProcedure(scriptPath, procedureName, player, null,
                out result, args);
        }

        /// <summary>
        /// Execute a script file's main entry point.
        /// Typical script: program Mir2; begin domain; end.
        /// </summary>
        public void ExecuteMain(string pasFilePath)
        {
            try
            {
                LoadCompilerInc();
                var program = GetOrLoadProgram(pasFilePath);
                if (program == null) return;

                var player = _api.CurrentPlayer;
                var npc = _api.CurrentNpc;
                if (!TryValidateNpcInvocation(npc, pasFilePath, null)) return;
                using var context = _api.PushContext(player, npc);
                var interpreter = CreateInterpreter(program);
                interpreter.ExecuteMain();
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[PasEngine] Main execute error in {pasFilePath} - {ex.Message}");
            }
        }

        public bool HasLabelHandler(string pasFilePath, string label)
        {
            var program = GetOrLoadProgram(pasFilePath);
            if (program == null) return false;

            var labelCall = ParseScriptLabel(label);
            if (labelCall.Label.Equals("@main", StringComparison.OrdinalIgnoreCase) ||
                labelCall.Label.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                labelCall.Label.Equals("_main", StringComparison.OrdinalIgnoreCase) ||
                labelCall.Label.Equals("@exit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var procName = labelCall.Label;
            if (procName.StartsWith("@")) procName = procName.Substring(1);
            if (!procName.StartsWith("_")) procName = "_" + procName;
            if (FindProcedure(program, procName) != null) return true;

            var alternateName = labelCall.Label;
            if (alternateName.StartsWith("@")) alternateName = alternateName.Substring(1);
            return FindProcedure(program, alternateName) != null;
        }

        public bool TryCallScriptLabel(string scriptName, string label, TPlayObject player, NormNpc npc = null)
        {
            if (npc != null
                && ResolveNpcScript(npc).Kind != NpcPasScriptResolutionKind
                    .Legacy)
                return TryCallNpcLabel(npc, label, player, out _, out _);

            var pasFilePath = FindScriptFile(scriptName);
            if (pasFilePath == null || !HasLabelHandler(pasFilePath, label))
            {
                return false;
            }

            return TryCallLabelCore(pasFilePath, label, player, npc, null,
                out _);
        }

        public bool TryCallNpcLabel(NormNpc npc, string label,
            TPlayObject player, out PasValue result, out bool scriptFound)
        {
            result = PasValue.Nil;
            scriptFound = false;
            if (npc == null || string.IsNullOrWhiteSpace(label)) return false;

            var previousInteraction = GetNpcInteraction(player, npc);
            var resolution = ResolveNpcScript(npc);
            if (resolution.Kind == NpcPasScriptResolutionKind.DynamicUnavailable)
            {
                RemoveNpcInteraction(previousInteraction);
                return false;
            }
            scriptFound = resolution.ScriptPaths.Count > 0;
            foreach (var scriptPath in resolution.ScriptPaths)
            {
                if (!HasLabelHandler(scriptPath, label)) continue;
                var interaction = BindNpcInteraction(player, npc, resolution);
                // Native click handler sub_6B8B28 writes player+0xCD8 AFTER the
                // talk vcall (0x6B8BA7 / 0x6B8C48), and never writes 0. GotoLable
                // 0x63DC98 itself does not touch +0xCD8. Do not bind or clear
                // m_NPC here: a label miss would otherwise drop a binding native
                // keeps, and a pre-call write would let Give's audit (0x6DF341
                // cmp [edi+0xCD8],0) see the new NPC during @main.
                var invoked = TryCallLabelCore(scriptPath, label, player, npc,
                    resolution.DynamicBindingHandle, out result);
                if (!invoked)
                {
                    RemoveNpcInteraction(interaction);
                }
                else if (ParseScriptLabel(label).Label.Equals("@exit",
                             StringComparison.OrdinalIgnoreCase))
                {
                    RemoveNpcInteraction(interaction);
                }
                return invoked;
            }
            RemoveNpcInteraction(previousInteraction);
            return false;
        }

        /// <summary>
        /// Execute a specific procedure by name from a .pas file.
        /// This is the primary entry point for NPC interactions.
        /// Handles: Compiler.inc definitions, OnInitialize lifecycle, persistent NPC state.
        /// </summary>
        public PasValue CallLabel(string pasFilePath, string label, TPlayObject player, NormNpc npc)
        {
            _ = TryCallLabelCore(pasFilePath, label, player, npc, null,
                out var result);
            return result;
        }

        private bool TryCallLabelCore(string pasFilePath, string label,
            TPlayObject player, NormNpc npc,
            NativeDynamicRoomPasScriptBindingHandle expectedDynamicBinding,
            out PasValue result)
        {
            result = PasValue.Nil;
            var labelCall = ParseScriptLabel(label);

            try
            {
                LoadCompilerInc();
                var program = GetOrLoadProgram(pasFilePath);
                if (program == null) return false;
                return TryInvokeWithInterpreter(pasFilePath, program, player,
                    npc, false, null,
                    interpreter => ExecuteLabel(interpreter, program, pasFilePath, label,
                        labelCall, player, npc), out result, null,
                    expectedDynamicBinding);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[PasEngine] Error in {pasFilePath}:{label} - {ex.Message}");
            }

            return false;
        }

        private PasValue ExecuteLabel(PasInterpreter interpreter, PasProgram program,
            string pasFilePath, string label, ScriptLabelCall labelCall,
            TPlayObject player, NormNpc npc)
        {
            var isExit = labelCall.Label.Equals("@exit", StringComparison.OrdinalIgnoreCase);
            var procName = labelCall.Label;
            if (procName.StartsWith("@")) procName = procName.Substring(1);
            if (!procName.StartsWith("_")) procName = "_" + procName;

            var proc = FindProcedure(program, procName);
            if (proc != null)
            {
                LogLabelDispatch(pasFilePath, label, proc.Name, labelCall.RawArgs);
                return interpreter.ExecuteProcedure(proc.Name, BuildLabelArgs(proc, labelCall));
            }

            var alternateName = labelCall.Label;
            if (alternateName.StartsWith("@")) alternateName = alternateName.Substring(1);
            var alternate = FindProcedure(program, alternateName);
            if (alternate != null)
            {
                LogLabelDispatch(pasFilePath, label, alternate.Name, labelCall.RawArgs);
                return interpreter.ExecuteProcedure(alternate.Name, BuildLabelArgs(alternate, labelCall));
            }

            if (labelCall.Label.Equals("@main", StringComparison.OrdinalIgnoreCase))
            {
                interpreter.ExecuteMain();
                return PasValue.Nil;
            }

            if (isExit)
            {
                player?.SendMsg(npc, Grobal2.RM_MERCHANTDLGCLOSE, 0,
                    npc?.ObjectId ?? 0, 0, 0, string.Empty);
                return PasValue.Nil;
            }

            if (IsBuiltInMerchantLabel(labelCall.Label))
            {
                LogLabelBuiltin(pasFilePath, label);
                return PasValue.Nil;
            }

            LogLabelMissing(pasFilePath, label);
            var beforeFallbackDialogSeq = player?.MerchantDialogSeq ?? 0;
            foreach (var fallback in new[] { "_main", "domain", "_domain" })
            {
                if (FindProcedure(program, fallback) == null) continue;
                var value = interpreter.ExecuteProcedure(fallback);
                SendMissingLabelFallbackIfNoDialog(player, npc, label, beforeFallbackDialogSeq);
                return value;
            }

            interpreter.ExecuteMain();
            SendMissingLabelFallbackIfNoDialog(player, npc, label, beforeFallbackDialogSeq);
            return PasValue.Nil;
        }

        public PasValue CallInputDialog(string pasFilePath, int inputType, string inputStr, bool inputOk, TPlayObject player, NormNpc npc)
        {
            _ = TryCallInputDialogCore(pasFilePath, inputType, inputStr,
                inputOk, player, npc, null, out var result);
            return result;
        }

        public bool TryCallNpcInputDialog(NormNpc npc, int inputType,
            string inputStr, bool inputOk, TPlayObject player,
            out PasValue result)
        {
            result = PasValue.Nil;
            if (npc == null) return false;
            var resolution = ResolveNpcInteractionScript(npc, player);
            if (resolution.Kind == NpcPasScriptResolutionKind.DynamicUnavailable
                || resolution.ScriptPaths.Count == 0)
                return false;
            return TryCallInputDialogCore(resolution.ScriptPath, inputType,
                inputStr, inputOk, player, npc,
                resolution.DynamicBindingHandle, out result);
        }

        private bool TryCallInputDialogCore(string pasFilePath, int inputType,
            string inputStr, bool inputOk, TPlayObject player, NormNpc npc,
            NativeDynamicRoomPasScriptBindingHandle expectedDynamicBinding,
            out PasValue result)
        {
            result = PasValue.Nil;
            var procName = "P" + inputType;

            try
            {
                LoadCompilerInc();

                var program = GetOrLoadProgram(pasFilePath);
                if (program == null) return false;

                var proc = FindProcedure(program, procName);
                if (proc == null) return false;

                return TryInvokeWithInterpreter(pasFilePath, program, player,
                    npc, inputOk, inputStr,
                    interpreter => interpreter.ExecuteProcedure(proc.Name),
                    out result, null, expectedDynamicBinding);
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[PasEngine] InputDialog error in {pasFilePath}:{procName} - {ex.Message}");
            }

            return false;
        }

        private void LoadScriptDefines()
        {
            lock (_compilerIncLock)
            {
                _scriptDefines.Clear();
                _scriptDefines.Add("VER150");

                var path = Path.Combine(_commonScriptsPath, "Compiler.inc");
                if (!File.Exists(path)) return;

                foreach (var rawLine in PasScriptTextReader.ReadAllText(path)
                             .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) ||
                        line.StartsWith(";", StringComparison.Ordinal))
                        continue;

                    var tokenLength = 0;
                    while (tokenLength < line.Length &&
                           (char.IsLetterOrDigit(line[tokenLength]) || line[tokenLength] == '_'))
                        tokenLength++;
                    if (tokenLength == 0 || char.IsDigit(line[0])) continue;

                    var remainder = line.Substring(tokenLength).TrimStart();
                    if (remainder.Length == 0 || remainder.StartsWith("//", StringComparison.Ordinal))
                        _scriptDefines.Add(line.Substring(0, tokenLength));
                }
            }
        }

        /// <summary>Load the enabled symbols from the native Compiler.inc list.</summary>
        private void LoadCompilerInc()
        {
            if (_compilerIncLoaded) return;
            lock (_compilerIncLock)
            {
                if (_compilerIncLoaded) return;
                try
                {
                    var path = Path.Combine(_commonScriptsPath, "Compiler.inc");
                    if (!File.Exists(path)) return;
                    LoadScriptDefines();
                }
                catch (Exception ex)
                {
                    M2Share.MainOutMessage($"[PasEngine] Failed to load Compiler.inc: {ex.Message}");
                }
                finally
                {
                    _compilerIncLoaded = true;
                }
            }
        }

        private PasInterpreter CreateInterpreter(PasProgram program)
        {
            return new PasInterpreter(program, _api);
        }

        private NpcScriptState GetOrCreateNpcState(NormNpc npc,
            string scriptPath, PasProgram program,
            NativeDynamicRoomPasScriptBindingHandle dynamicBindingHandle)
        {
            var npcId = npc.ObjectId;
            while (true)
            {
                if (_npcStates.TryGetValue(npcId, out var existing) &&
                    ReferenceEquals(existing.Npc, npc) &&
                    string.Equals(existing.ScriptPath, scriptPath, StringComparison.OrdinalIgnoreCase) &&
                    ReferenceEquals(existing.Program, program) &&
                    ReferenceEquals(existing.DynamicBindingHandle,
                        dynamicBindingHandle))
                    return existing;

                var replacement = new NpcScriptState
                {
                    Npc = npc,
                    ScriptPath = scriptPath,
                    Program = program,
                    Interpreter = CreateInterpreter(program),
                    DynamicBindingHandle = dynamicBindingHandle
                };

                if (existing == null)
                {
                    if (_npcStates.TryAdd(npcId, replacement)) return replacement;
                }
                else if (_npcStates.TryUpdate(npcId, replacement, existing))
                {
                    return replacement;
                }
            }
        }

        private bool TryInvokeWithInterpreter(string scriptPath,
            PasProgram program,
            TPlayObject player, NormNpc npc, bool inputOk, string inputStr,
            Func<PasInterpreter, PasValue> invocation, out PasValue result,
            TUserItem item = null,
            NativeDynamicRoomPasScriptBindingHandle expectedDynamicBinding = null)
        {
            result = PasValue.Nil;
            // ValidateExpected is a lifecycle snapshot. The future production
            // runtime connection must also hold its reentrant activation gate.
            if (!TryValidateNpcInvocation(npc, scriptPath,
                    expectedDynamicBinding))
                return false;

            if (npc == null || npc.ObjectId <= 0)
            {
                using var context = _api.PushItemContext(player, npc, inputOk, inputStr, item);
                result = invocation(CreateInterpreter(program));
                return true;
            }

            var state = GetOrCreateNpcState(npc, scriptPath, program,
                expectedDynamicBinding);
            try
            {
                lock (state.SyncRoot)
                {
                    if (!TryValidateNpcInvocation(npc, scriptPath,
                            expectedDynamicBinding))
                        return false;
                    using var context = _api.PushItemContext(player, npc,
                        inputOk, inputStr, item);
                    EnsureInitialized(state);
                    if (!TryValidateNpcInvocation(npc, scriptPath,
                            expectedDynamicBinding))
                        return false;
                    result = invocation(state.Interpreter);
                    return true;
                }
            }
            catch
            {
                RemoveNpcState(npc.ObjectId, state);
                throw;
            }
        }

        private bool TryValidateNpcInvocation(NormNpc npc, string scriptPath,
            NativeDynamicRoomPasScriptBindingHandle expectedDynamicBinding)
        {
            if (npc == null)
                return expectedDynamicBinding == null;
            if (_dynamicNpcRoutes == null)
                return expectedDynamicBinding == null;

            var routeState = _dynamicNpcRoutes.ResolveCurrent(npc,
                out var currentHandle, out _);
            if (routeState == NativeDynamicRoomPasScriptRouteState.NotDynamic)
                return expectedDynamicBinding == null;
            if (routeState != NativeDynamicRoomPasScriptRouteState.ExactCurrent
                || expectedDynamicBinding == null
                || !ReferenceEquals(currentHandle, expectedDynamicBinding)
                || !_dynamicNpcRoutes.ValidateExpected(npc,
                    expectedDynamicBinding, out var exactPath))
                return false;

            return SameScriptPath(scriptPath, exactPath);
        }

        private static bool SameScriptPath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left)
                || string.IsNullOrWhiteSpace(right))
                return false;
            try
            {
                return string.Equals(Path.GetFullPath(left),
                    Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or NotSupportedException
                                       or PathTooLongException)
            {
                return false;
            }
        }

        private void RemoveNpcState(int npcId, NpcScriptState expectedState)
        {
            if (expectedState == null) return;
            ((ICollection<KeyValuePair<int, NpcScriptState>>)_npcStates)
                .Remove(new KeyValuePair<int, NpcScriptState>(npcId,
                    expectedState));
        }

        private void EnsureInitialized(NpcScriptState state)
        {
            if (state.Initialized) return;
            var procedure = FindProcedure(state.Program, "OnInitialize");
            if (procedure != null)
                state.Interpreter.ExecuteProcedure(procedure.Name);
            state.Initialized = true;
        }

        /// <summary>Clear NPC state (called when NPC unloads/resets).</summary>
        public void ClearNpcState(int npcId)
        {
            _npcStates.TryRemove(npcId, out _);
        }

        public void ClearNpcState(NormNpc npc)
        {
            if (npc == null) return;
            if (_npcStates.TryGetValue(npc.ObjectId, out var state)
                && ReferenceEquals(state.Npc, npc))
                RemoveNpcState(npc.ObjectId, state);

            foreach (var entry in _npcInteractionBindings)
            {
                if (ReferenceEquals(entry.Value.Npc, npc))
                    RemoveNpcInteraction(entry.Value);
            }
        }

        /// <summary>
        /// Get NPC interaction limit value (Delphi @GetLimitValue hook).
        /// Script function returns integer limit for the given player-NPC interaction.
        /// </summary>
        public int GetLimitValue(NormNpc npc, TPlayObject player)
        {
            if (npc == null || player == null) return 0;
            var resolution = ResolveNpcScript(npc);
            if (resolution.Kind == NpcPasScriptResolutionKind.DynamicUnavailable
                || resolution.ScriptPaths.Count == 0)
                return 0;
            return TryCallLabelCore(resolution.ScriptPath, "@GetLimitValue",
                player, npc, resolution.DynamicBindingHandle, out var result)
                ? result.AsInt()
                : 0;
        }

        // ===== PsNpcScript.txt / PsNpcScriptEx.txt / PsMapQuest.txt Parsing =====
        // ZhanShen PsNpcScript format:
        // ScriptName MapName X Y NpcName Dir Appr Castle AutoTime
        // PsMapQuest: ten native fields; map is field 0 and script is field 8.

        private ConcurrentDictionary<string, string> _npcScriptMap; // NPC name → script path
        private ConcurrentDictionary<string, List<MapQuestEntry>> _mapQuestMap; // map → quest entries
        private TimedNpcScriptEntry[] _timedNpcScripts = Array.Empty<TimedNpcScriptEntry>();

        private sealed class TimedNpcScriptEntry
        {
            public string ScriptName;
            public string ScriptPath;
            public string MapName;
            public short X;
            public short Y;
            public string NpcName;
            public int IntervalSeconds;
            public long LastExecuteTick;
            public int NpcObjectId;
        }

        private sealed class MapQuestEntry
        {
            public string MapName;
            public int VariableGroup;
            public int VariableIndex;
            public int UpperBound;
            public string MonsterName;
            public int MonsterCount;
            public string ItemName;
            public int ItemCount;
            public string ScriptFile;
            public string ScriptPath;
            public bool IsGroup;
            public PasProgram Program;
            public PasInterpreter Interpreter;
            public readonly object SyncRoot = new();
        }

        public int TimedNpcScriptCount => _timedNpcScripts.Length;

        /// <summary>Parse PsNpcScript.txt + PsNpcScriptEx.txt to build NPC→script mapping (Delphi compatible).</summary>
        public void LoadNpcScriptMap()
        {
            _npcScriptMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var timedScripts = new List<TimedNpcScriptEntry>();
            var files = new[] { "PsNpcScript.txt", "PsNpcScriptEx.txt" };
            foreach (var fileName in files)
            {
                var path = Path.Combine(_envirPath, fileName);
                if (!File.Exists(path)) continue;
                try
                {
                    foreach (var line in PasScriptTextReader.ReadAllLines(path))
                    {
                        var parts = SplitNpcScriptLine(line);
                        if (parts.Length != 9) continue;

                        var scriptName = CleanNpcScriptToken(parts[0]);
                        var mapName = CleanNpcScriptToken(parts[1]);
                        var npcName = CleanNpcScriptToken(parts[4]);
                        var scriptPath = ResolveNpcScriptPath(scriptName, mapName);
                        if (scriptPath == null) continue;

                        AddNpcScriptMapping(scriptName, scriptPath);
                        AddNpcScriptMapping(scriptName + "-" + mapName, scriptPath);
                        AddNpcScriptMapping(npcName, scriptPath);
                        AddNpcScriptMapping(npcName + "-" + mapName, scriptPath);

                        if (int.TryParse(parts[8], out var autoTime) && autoTime > 0 &&
                            short.TryParse(parts[2], out var x) && short.TryParse(parts[3], out var y))
                        {
                            timedScripts.Add(new TimedNpcScriptEntry
                            {
                                ScriptName = scriptName,
                                ScriptPath = scriptPath,
                                MapName = mapName,
                                X = x,
                                Y = y,
                                NpcName = npcName,
                                IntervalSeconds = autoTime,
                                LastExecuteTick = Environment.TickCount64
                            });
                        }
                    }
                }
                catch (Exception ex) { M2Share.MainOutMessage($"[PasEngine] Failed to parse {fileName}: {ex.Message}"); }
            }
            _timedNpcScripts = timedScripts.ToArray();
        }

        private static string CleanNpcScriptToken(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value.Substring(1, value.Length - 2).Trim();
            return value;
        }

        private static string[] SplitNpcScriptLine(string line)
        {
            line = (line ?? "").Trim();
            if (line.Length == 0 || line.StartsWith(";")) return Array.Empty<string>();

            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line.Substring(0, comment).Trim();
            if (line.Length == 0) return Array.Empty<string>();

            var parts = new List<string>();
            var token = new StringBuilder();
            var quote = '\0';
            foreach (var ch in line)
            {
                if (quote != '\0')
                {
                    token.Append(ch);
                    if (ch == quote) quote = '\0';
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    token.Append(ch);
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (token.Length > 0)
                    {
                        parts.Add(token.ToString());
                        token.Clear();
                    }
                    continue;
                }

                token.Append(ch);
            }

            if (token.Length > 0)
                parts.Add(token.ToString());
            return parts.ToArray();
        }

        private void AddNpcScriptMapping(string name, string scriptPath)
        {
            name = CleanNpcScriptToken(name);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(scriptPath)) return;
            if (!IsPasScriptPath(scriptPath) || !File.Exists(scriptPath)) return;
            _npcScriptMap[name] = scriptPath;
        }

        private static bool IsPasScriptPath(string path)
        {
            return string.Equals(Path.GetExtension(path), ".pas", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveNpcScriptPath(string scriptName, string mapName)
        {
            scriptName = CleanNpcScriptToken(scriptName);
            mapName = CleanNpcScriptToken(mapName);
            if (string.IsNullOrWhiteSpace(scriptName)) return null;

            var names = new List<string>();
            if (!string.IsNullOrWhiteSpace(mapName))
                names.Add(scriptName + "-" + mapName);
            names.Add(scriptName);

            foreach (var name in names)
            {
                var existing = ResolveExistingScriptPath(name);
                if (existing != null) return existing;
            }

            return null;
        }

        private string ResolveExistingScriptPath(string scriptName)
        {
            scriptName = CleanNpcScriptToken(scriptName);
            if (string.IsNullOrWhiteSpace(scriptName)) return null;

            if (Path.IsPathRooted(scriptName) && IsPasScriptPath(scriptName) && File.Exists(scriptName))
                return scriptName;

            var hasExtension = !string.IsNullOrEmpty(Path.GetExtension(scriptName));
            string[] baseDirs = {
                Path.Combine(_envirPath, "PsNpcscripts"),
                _commonScriptsPath,
                _envirPath
            };

            foreach (var dir in baseDirs)
            {
                var path = Path.Combine(dir, scriptName);
                if (hasExtension && IsPasScriptPath(path) && File.Exists(path)) return path;
                if (!hasExtension)
                {
                    var pasPath = path + ".pas";
                    if (File.Exists(pasPath)) return pasPath;
                }
            }

            return null;
        }

        /// <summary>Get script path for an NPC by name (from PsNpcScript.txt mapping).</summary>
        public string GetNpcScriptFile(string npcName)
        {
            if (_npcScriptMap == null) LoadNpcScriptMap();
            return _npcScriptMap != null && _npcScriptMap.TryGetValue(npcName, out var path) ? path : null;
        }

        /// <summary>Parse PsMapQuest.txt to build map→quest mappings (Delphi compatible).</summary>
        public void LoadMapQuestMap()
        {
            var mapQuestMap = new ConcurrentDictionary<string, List<MapQuestEntry>>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(_envirPath, "PsMapQuest.txt");
            if (!File.Exists(path))
            {
                _mapQuestMap = mapQuestMap;
                return;
            }
            try
            {
                foreach (var line in PasScriptTextReader.ReadAllLines(path))
                {
                    var parts = SplitNpcScriptLine(line);
                    if (parts.Length != 10) continue;

                    var mapName = CleanNpcScriptToken(parts[0]);
                    var scriptFile = CleanNpcScriptToken(parts[8]);
                    if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(scriptFile)) continue;

                    var variableGroup = ParseInt(parts[1]);
                    var variableIndex = ParseInt(parts[2]);
                    var scriptPath = ResolveMapQuestScriptPath(scriptFile);
                    if (variableGroup <= 0 || variableIndex <= 0 || scriptPath == null) continue;

                    var entry = new MapQuestEntry
                    {
                        MapName = mapName,
                        VariableGroup = variableGroup,
                        VariableIndex = variableIndex,
                        UpperBound = ParseInt(parts[3]),
                        MonsterName = NormalizeMapQuestKey(parts[4]),
                        MonsterCount = ParseInt(parts[5]),
                        ItemName = NormalizeMapQuestKey(parts[6]),
                        ItemCount = ParseInt(parts[7]),
                        ScriptFile = scriptFile,
                        ScriptPath = scriptPath,
                        IsGroup = parts[9].Equals("GROUP", StringComparison.OrdinalIgnoreCase)
                    };
                    if (!mapQuestMap.TryGetValue(mapName, out var entries))
                    {
                        entries = new List<MapQuestEntry>();
                        mapQuestMap[mapName] = entries;
                    }
                    entries.Add(entry);
                }
            }
            catch (Exception ex) { M2Share.MainOutMessage($"[PasEngine] Failed to parse PsMapQuest.txt: {ex.Message}"); }
            ValidateMapQuestDuplicateIndices(mapQuestMap);
            _mapQuestMap = mapQuestMap;
        }

        private static void ValidateMapQuestDuplicateIndices(
            ConcurrentDictionary<string, List<MapQuestEntry>> mapQuestMap)
        {
            foreach (var pair in mapQuestMap)
            {
                var seen = new HashSet<int>();
                foreach (var entry in pair.Value)
                {
                    var key = (entry.VariableGroup << 16) | entry.VariableIndex;
                    if (!seen.Add(key))
                    {
                        M2Share.MainOutMessage(NativeAntiCheatHostRuntime.TaskListErrorPrefix
                            + " PsMapQuest duplicate id map=" + pair.Key
                            + " group=" + entry.VariableGroup
                            + " index=" + entry.VariableIndex);
                    }

                    if (!File.Exists(entry.ScriptPath))
                    {
                        M2Share.MainOutMessage(NativeAntiCheatHostRuntime.TaskListErrorPrefix
                            + " " + NativeAntiCheatHostRuntime.TaskListLoadFailureMessage
                            + " " + entry.ScriptPath);
                    }
                }
            }
        }

        /// <summary>Get quest scripts for a map (returns list of (questName, scriptPath) pairs).</summary>
        public List<(string questName, string scriptPath)> GetMapQuestScripts(string mapName)
        {
            if (_mapQuestMap == null) LoadMapQuestMap();
            var result = new List<(string, string)>();
            if (_mapQuestMap != null && _mapQuestMap.TryGetValue(mapName, out var entries))
            {
                foreach (var e in entries)
                {
                    result.Add((e.ScriptFile, e.ScriptPath));
                }
            }
            return result;
        }

        private string ResolveMapQuestScriptPath(string scriptFile)
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(_envirPath, "PsMapQuest"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fileName = scriptFile.EndsWith(".pas", StringComparison.OrdinalIgnoreCase)
                    ? scriptFile
                    : scriptFile + ".pas";
                var candidate = Path.GetFullPath(Path.Combine(root, fileName));
                return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                       candidate.EndsWith(".pas", StringComparison.OrdinalIgnoreCase) &&
                       File.Exists(candidate)
                    ? candidate
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeMapQuestKey(string value)
        {
            value = CleanNpcScriptToken(value);
            return value == "*" ? string.Empty : value;
        }

        /// <summary>Dispatch native PsMapQuest monster-kill records for one player.</summary>
        public int ProcessMapQuestKill(string mapName, TPlayObject player, string monsterName, bool grouped)
        {
            if (player == null || string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(monsterName))
                return 0;
            if (_mapQuestMap == null) LoadMapQuestMap();
            if (_mapQuestMap == null || !_mapQuestMap.TryGetValue(mapName, out var entries))
                return 0;

            var executed = 0;
            foreach (var entry in entries)
            {
                if (grouped && !entry.IsGroup) continue;
                if (entry.MonsterName.Length == 0 || entry.ItemName.Length != 0) continue;
                if (!string.Equals(entry.MonsterName, monsterName, StringComparison.OrdinalIgnoreCase)) continue;

                // VariableGroup comes from configuration, so it can be 0, and group 0 of
                // the V bank is not in the dictionary - computing the flat key here read
                // zero for every such entry regardless of what the script had stored.
                var currentValue = player.TryGetScriptVar('V', entry.VariableGroup,
                    entry.VariableIndex, out var storedValue)
                    ? storedValue
                    : 0;
                if (currentValue < -1 || currentValue >= entry.UpperBound) continue;

                try
                {
                    LoadCompilerInc();
                    var program = GetOrLoadProgram(entry.ScriptPath);
                    if (program == null) continue;
                    lock (entry.SyncRoot)
                    {
                        if (!ReferenceEquals(entry.Program, program))
                        {
                            entry.Program = program;
                            entry.Interpreter = CreateInterpreter(program);
                        }

                        using var context = _api.PushContext(player, null);
                        entry.Interpreter.ExecuteMain();
                    }
                    executed++;
                }
                catch (Exception ex)
                {
                    M2Share.MainOutMessage(
                        $"[PasEngine] Map quest error map={mapName} monster={monsterName} script={entry.ScriptFile}: {ex.Message}");
                }
            }
            return executed;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }

        // ===== CallOut/CallOutEx Timer System (Delphi compatible) =====

        private readonly List<DeferredCall> _deferredCalls = new();
        private readonly object _deferredLock = new();

        private sealed class DeferredCall
        {
            public long ExecuteAtTick;
            public string ScriptPath;
            public string ProcName;
            public TPlayObject Player;
            public NormNpc Npc;
            public int PlayerId;
            public int NpcId;
            public NativeDynamicRoomPasScriptBindingHandle DynamicBindingHandle;
            public bool RequiresPlayer;
            public bool RequiresNpc;
            public bool IsSingleSlot;
            public bool IsNamedSlot;
        }

        /// <summary>Schedule an independent deferred script call.</summary>
        public void ScheduleCall(string scriptPath, string procName, TPlayObject player, NormNpc npc, int delayMs)
        {
            ScheduleCallCore(scriptPath, procName, player, npc, delayMs, false, false);
        }

        /// <summary>
        /// Schedule the native player timer. CallOut owns one slot; CallOutEx owns
        /// one slot per procedure name and resets an existing matching slot.
        /// </summary>
        public void SchedulePlayerCall(string scriptPath, string procName,
            TPlayObject player, NormNpc npc, int delayMs, bool extended)
        {
            if (player == null || delayMs <= 0) return;
            ScheduleCallCore(scriptPath, procName, player, npc, delayMs,
                !extended, extended);
        }

        private void ScheduleCallCore(string scriptPath, string procName,
            TPlayObject player, NormNpc npc, int delayMs, bool singleSlot, bool namedSlot)
        {
            if (!TryResolveDeferredRoute(scriptPath, npc,
                    out var resolvedScriptPath, out var dynamicBindingHandle))
                return;

            lock (_deferredLock)
            {
                DeferredCall call = null;
                if (singleSlot)
                {
                    call = _deferredCalls.FirstOrDefault(entry =>
                        entry.IsSingleSlot && entry.PlayerId == player.ObjectId
                        && ReferenceEquals(entry.Player, player));
                }
                else if (namedSlot)
                {
                    call = _deferredCalls.FirstOrDefault(entry =>
                        entry.IsNamedSlot && entry.PlayerId == player.ObjectId &&
                        ReferenceEquals(entry.Player, player) &&
                        string.Equals(entry.ProcName, procName, StringComparison.OrdinalIgnoreCase));
                }

                if (call == null)
                {
                    call = new DeferredCall();
                    _deferredCalls.Add(call);
                }

                call.ExecuteAtTick = Environment.TickCount64 + Math.Max(0, delayMs);
                call.ScriptPath = resolvedScriptPath;
                call.ProcName = procName;
                call.Player = player;
                call.Npc = npc;
                call.PlayerId = player?.ObjectId ?? 0;
                call.NpcId = npc?.ObjectId ?? 0;
                call.DynamicBindingHandle = dynamicBindingHandle;
                call.RequiresPlayer = player != null;
                call.RequiresNpc = npc != null;
                call.IsSingleSlot = singleSlot;
                call.IsNamedSlot = namedSlot;
            }
        }

        private bool TryResolveDeferredRoute(string scriptPath, NormNpc npc,
            out string resolvedScriptPath,
            out NativeDynamicRoomPasScriptBindingHandle dynamicBindingHandle)
        {
            resolvedScriptPath = scriptPath;
            dynamicBindingHandle = null;
            if (npc == null) return true;

            var resolution = ResolveNpcScript(npc);
            if (resolution.Kind == NpcPasScriptResolutionKind
                    .DynamicUnavailable)
                return false;
            if (resolution.Kind != NpcPasScriptResolutionKind.ExactDynamic)
                return true;
            if (_dynamicRoomRuntime == null
                || resolution.DynamicBindingHandle == null
                || string.IsNullOrWhiteSpace(resolution.ScriptPath))
                return false;

            resolvedScriptPath = resolution.ScriptPath;
            dynamicBindingHandle = resolution.DynamicBindingHandle;
            return true;
        }

        public int CancelDeferredCallsForObject(int objectId)
        {
            if (objectId <= 0) return 0;
            // Natively there is nothing to evict: the current-NPC binding is the inline
            // player field +0xCD8 and the CallOut slot is the four inline fields
            // +0xCE8/+0xCEC/+0xCF0/+0xCF4, so both die with the object itself. C# holds the
            // equivalent state in _npcInteractionBindings, keyed by ObjectId — and ObjectId
            // is reused across relog, so an entry for a disposed player stayed resident
            // forever (the ReferenceEquals(interaction.Player, player) guard in
            // GetNpcInteraction / ResolveNpcInteractionScript made it unreachable but never
            // freed). Dropping it on the same disposal hook that already cancels this
            // object's deferred calls reproduces native's "dies with the object" lifetime
            // without adding any clear native does not have.
            EvictNpcInteractionBindings(objectId);
            lock (_deferredLock)
            {
                return _deferredCalls.RemoveAll(call =>
                    call.PlayerId == objectId || call.NpcId == objectId);
            }
        }

        /// <summary>
        /// Drop the NPC-interaction binding owned by, or pointing at, a disposed object.
        /// Native equivalent: the inline player fields +0xCD8 / +0xCE8..+0xCF4 are freed
        /// with the player record; no explicit clear exists (no write of 0 to +0xCD8
        /// anywhere in the image).
        /// </summary>
        private void EvictNpcInteractionBindings(int objectId)
        {
            if (objectId <= 0) return;
            if (_npcInteractionBindings.TryRemove(objectId, out _)) { }
            foreach (var entry in _npcInteractionBindings)
            {
                if (entry.Value?.Npc != null && entry.Value.Npc.ObjectId == objectId)
                    RemoveNpcInteraction(entry.Value);
            }
        }

        /// <summary>Process pending deferred calls (call from game loop). Returns number executed.</summary>
        public int ProcessDeferredCalls()
        {
            var now = Environment.TickCount64;
            var ready = new List<DeferredCall>();
            var objectManager = M2Share.ObjectManager;
            lock (_deferredLock)
            {
                for (int i = _deferredCalls.Count - 1; i >= 0; i--)
                {
                    var call = _deferredCalls[i];
                    if (call.ExecuteAtTick <= now)
                    {
                        ready.Add(call);
                        _deferredCalls.RemoveAt(i);
                    }
                }
            }
            // Native fires ready CallOutEx timers in DESCENDING LIST INDEX, never in
            // due-time order. The player tick sub_6B2D38 walks the list at [player+0x7F0]
            // with ebx from Count-1 down to 0 (0x6B3A87 `mov ebx,[eax+8]` / 0x6B3A8A
            // `dec ebx`, loop back-edge 0x6B3B23 `dec ebx` / 0x6B3B24 `cmp ebx,-1`), and
            // dispatches in that same pass (0x6B3AEE `call [esi+0x44]` / 0x6B3AFD
            // sub_63DC98) — there is no collection phase and no sort anywhere. Sorting the
            // ready set by ExecuteAtTick re-ordered the side effects of two timers coming
            // due in the same tick, so the collection order above (already descending
            // index) IS the execution order and must be preserved verbatim.
            var executed = 0;
            foreach (var call in ready)
            {
                try
                {
                    if (!HasExactDeferredActors(call, objectManager)) continue;
                    if (call.DynamicBindingHandle != null)
                    {
                        if (_dynamicRoomRuntime != null
                            && _dynamicRoomRuntime.TryExecuteExpectedPas(
                                call.Npc, call.DynamicBindingHandle,
                                exactScriptPath =>
                                    TryExecuteDynamicDeferredProcedure(call,
                                        exactScriptPath)))
                            executed++;
                        continue;
                    }

                    if (call.Npc != null
                        && ResolveNpcScript(call.Npc).Kind
                        != NpcPasScriptResolutionKind.Legacy)
                        continue;
                    CallProcedure(call.ScriptPath, call.ProcName,
                        call.Player, call.Npc);
                    executed++;
                }
                catch (Exception ex)
                {
                    M2Share.MainOutMessage($"[PasEngine] Deferred call error {call.ScriptPath}:{call.ProcName} - {ex.Message}");
                }
            }
            return executed;
        }

        private static bool HasExactDeferredActors(DeferredCall call,
            ObjectManager objectManager)
        {
            if (call.RequiresPlayer
                && (call.Player == null || call.PlayerId <= 0
                    || call.Player.m_boGhost || objectManager == null
                    || !ReferenceEquals(objectManager.Get(call.PlayerId),
                        call.Player)))
                return false;
            if (call.RequiresNpc
                && (call.Npc == null || call.NpcId <= 0
                    || call.Npc.m_boGhost || objectManager == null
                    || !ReferenceEquals(objectManager.Get(call.NpcId),
                        call.Npc)))
                return false;
            return true;
        }

        private bool TryExecuteDynamicDeferredProcedure(DeferredCall call,
            string exactScriptPath)
        {
            if (!SameScriptPath(call.ScriptPath, exactScriptPath)
                || string.IsNullOrWhiteSpace(call.ProcName))
                return false;

            LoadCompilerInc();
            var program = GetOrLoadProgram(exactScriptPath);
            var procedure = program == null
                ? null
                : FindProcedure(program, call.ProcName);
            if (procedure == null) return false;
            return TryInvokeWithInterpreter(exactScriptPath, program,
                call.Player, call.Npc, false, null,
                interpreter => interpreter.ExecuteProcedure(procedure.Name),
                out _, null, call.DynamicBindingHandle);
        }

        /// <summary>Check if there are any pending deferred calls.</summary>
        public int PendingCallCount { get { lock (_deferredLock) return _deferredCalls.Count; } }

        /// <summary>
        /// Execute the auto-run procedure (Execute) for timed scripts.
        /// </summary>
        public bool CallExecute(string pasFilePath, TPlayObject player, NormNpc npc)
        {
            LoadCompilerInc();
            var program = GetOrLoadProgram(pasFilePath);
            if (program == null) return false;

            var proc = FindProcedure(program, "Execute") ?? FindProcedure(program, "_execute");
            if (proc == null) return false;

            return TryInvokeWithInterpreter(pasFilePath, program, player, npc,
                false, null,
                interpreter => interpreter.ExecuteProcedure(proc.Name),
                out _);
        }

        public int ProcessAutoScripts()
        {
            var executed = 0;
            var now = Environment.TickCount64;
            foreach (var entry in _timedNpcScripts)
            {
                var wasUnresolved = entry.NpcObjectId <= 0;
                var npc = ResolveTimedNpc(entry);
                if (npc == null) continue;
                if (wasUnresolved)
                {
                    entry.LastExecuteTick = now;
                    continue;
                }

                if (now - entry.LastExecuteTick < entry.IntervalSeconds * 1000L) continue;
                entry.LastExecuteTick = now;
                try
                {
                    if (CallExecute(entry.ScriptPath, null, npc)) executed++;
                }
                catch (Exception ex)
                {
                    M2Share.MainOutMessage($"[PasEngine] AutoTime error {entry.ScriptName}: {ex.Message}");
                }
            }
            return executed;
        }

        private static NormNpc ResolveTimedNpc(TimedNpcScriptEntry entry)
        {
            if (entry.NpcObjectId > 0)
            {
                var cached = M2Share.ObjectManager.Get(entry.NpcObjectId) as NormNpc;
                if (IsTimedNpcMatch(cached, entry)) return cached;
                entry.NpcObjectId = 0;
            }

            var merchants = M2Share.UserEngine?.SnapshotMerchants();
            if (merchants == null) return null;
            for (var index = 0; index < merchants.Length; index++)
            {
                var merchant = merchants[index];
                if (!IsTimedNpcMatch(merchant, entry)) continue;
                if (merchant.m_nCurrX != entry.X || merchant.m_nCurrY != entry.Y) continue;
                entry.NpcObjectId = merchant.ObjectId;
                return merchant;
            }
            return null;
        }

        private static bool IsTimedNpcMatch(NormNpc npc, TimedNpcScriptEntry entry)
        {
            if (npc == null || npc.m_boGhost) return false;
            if (!string.Equals(npc.m_sMapName, entry.MapName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(npc.m_sCharName, entry.NpcName, StringComparison.OrdinalIgnoreCase)) return false;
            return npc is Merchant merchant &&
                string.Equals(merchant.m_sScript, entry.ScriptName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Load (or reload from cache) a .pas file.
        /// </summary>
        /// <summary>Resolve {$I filename} recursively and honor nested {$IFDEF}/{$IFNDEF} blocks.</summary>
        private string PreprocessIncludes(string source, string baseDir,
            HashSet<string> visited)
        {
            return PreprocessIncludesWithSourceMap(source, baseDir, visited, null).Text;
        }

        private PreprocessedSource PreprocessIncludesWithSourceMap(string source, string baseDir,
            HashSet<string> visited, string sourceFile)
        {
            var output = new PreprocessedSource();
            if (string.IsNullOrEmpty(source)) return output;
            baseDir ??= _envirPath;
            visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var stack = new List<PreprocessorFrame>();
            var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var directive = TryGetPreprocessorDirective(line);
                if (directive != null)
                {
                    HandlePreprocessorDirective(directive, baseDir, visited, stack, output);
                    continue;
                }

                if (IsPreprocessorActive(stack))
                    output.AppendLine(line, sourceFile, lineIndex + 1);
            }

            return output;
        }

        private static string TryGetPreprocessorDirective(string line)
        {
            var trimmed = line?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            if (!trimmed.StartsWith("{$", StringComparison.Ordinal))
                return null;
            var closeAt = trimmed.IndexOf('}');
            if (closeAt < 2) return null;
            var trailing = trimmed.Substring(closeAt + 1).TrimStart();
            if (trailing.Length > 0 && !trailing.StartsWith("//", StringComparison.Ordinal)) return null;
            return trimmed.Substring(2, closeAt - 2).Trim();
        }

        private void HandlePreprocessorDirective(string directive, string baseDir,
            HashSet<string> visited, List<PreprocessorFrame> stack,
            PreprocessedSource output)
        {
            if (directive.StartsWith("IFDEF", StringComparison.OrdinalIgnoreCase))
            {
                var name = directive.Substring(5).Trim();
                stack.Add(new PreprocessorFrame
                {
                    ParentActive = IsPreprocessorActive(stack),
                    ConditionActive = _scriptDefines.Contains(name)
                });
                return;
            }

            if (directive.StartsWith("IFNDEF", StringComparison.OrdinalIgnoreCase))
            {
                var name = directive.Substring(6).Trim();
                stack.Add(new PreprocessorFrame
                {
                    ParentActive = IsPreprocessorActive(stack),
                    ConditionActive = !_scriptDefines.Contains(name)
                });
                return;
            }

            if (directive.Equals("ELSE", StringComparison.OrdinalIgnoreCase) || directive.StartsWith("ELSE ", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count == 0) return;
                var frame = stack[stack.Count - 1];
                if (frame.ElseSeen) return;
                frame.ElseSeen = true;
                frame.ConditionActive = !frame.ConditionActive;
                return;
            }

            if (directive.StartsWith("ENDIF", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                return;
            }

            if (!IsPreprocessorActive(stack)) return;

            if (directive.StartsWith("I ", StringComparison.OrdinalIgnoreCase) ||
                directive.StartsWith("INCLUDE ", StringComparison.OrdinalIgnoreCase))
            {
                var includeName = directive.Substring(directive.IndexOf(' ') + 1).Trim().Trim('\'', '"');
                var includePath = ResolveIncludePath(includeName, baseDir);
                if (includePath == null)
                {
                    throw new FileNotFoundException($"Include not found: {includeName} (from {baseDir})");
                }

                if (!visited.Add(includePath))
                    return;

                var includeSource = PasScriptTextReader.ReadAllText(includePath);
                output.Append(PreprocessIncludesWithSourceMap(includeSource,
                    Path.GetDirectoryName(includePath), visited, includePath));
            }
        }

        private static bool IsPreprocessorActive(List<PreprocessorFrame> stack)
        {
            return stack.Count == 0 || stack[stack.Count - 1].Active;
        }

        private string ResolveIncludePath(string includeName, string baseDir)
        {
            return PasIncludeResolver.Resolve(includeName, baseDir, _envirPath);
        }
        private PasProgram GetOrLoadProgram(string pasFilePath) =>
            GetOrLoadProgram(pasFilePath, out _);

        private PasProgram GetOrLoadProgram(string pasFilePath,
            out string error)
        {
            error = string.Empty;
            if (!File.Exists(pasFilePath))
            {
                // Try common scripts directory
                var altPath = Path.Combine(_commonScriptsPath, Path.GetFileName(pasFilePath));
                if (File.Exists(altPath))
                    pasFilePath = altPath;
                else
                {
                    error = $"File not found: {pasFilePath}";
                    M2Share.MainOutMessage($"[PasEngine] File not found: {pasFilePath}");
                    return null;
                }
            }

            var lastWrite = File.GetLastWriteTimeUtc(pasFilePath);

            // Check cache freshness
            if (_cache.TryGetValue(pasFilePath, out var cached) &&
                _fileTimestamps.TryGetValue(pasFilePath, out var cachedTime) &&
                cachedTime >= lastWrite)
            {
                return cached;
            }

            // Load and parse
            string source = null;
            try
            {
                source = PasScriptTextReader.ReadAllText(pasFilePath);
                // Resolve {$I filename} includes (recursive)
                var preprocessed = PreprocessIncludesWithSourceMap(source,
                    Path.GetDirectoryName(pasFilePath), new HashSet<string>(), pasFilePath);
                source = preprocessed.Text;
                var lexer = new PasLexer(source, pasFilePath, preprocessed.Lines);

                // Register common defines
                lexer.SetDefines(_scriptDefines);

                var parser = new PasParser(lexer, Path.GetDirectoryName(pasFilePath));
                var program = parser.Parse();

                _cache[pasFilePath] = program;
                _fileTimestamps[pasFilePath] = lastWrite;

                return program;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                var sourceLines = source?.Split('\n') ?? new string[0];
                int.TryParse(System.Text.RegularExpressions.Regex.Match(ex.Message, @"line (\d+)").Groups[1].Value, out var errLine);
                var ctx = errLine > 0 && errLine <= sourceLines.Length ? sourceLines[errLine - 1].Trim() : "?";
                M2Share.MainOutMessage($"[PasEngine] Failed to load {Path.GetFileName(pasFilePath)} line {errLine}: [{ctx}] - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clear a single script from cache (hot-reload).
        /// </summary>
        public void Invalidate(string pasFilePath)
        {
            _cache.TryRemove(pasFilePath, out _);
            _fileTimestamps.TryRemove(pasFilePath, out _);
        }

        /// <summary>
        /// Clear all cached scripts.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
            _fileTimestamps.Clear();
            _npcStates.Clear();
            _monsterStates.Clear();
            lock (_monsterScriptsLock)
            {
                _monsterScriptPaths.Clear();
                _monsterScriptsLoaded = false;
            }
            lock (_taskScriptsLock)
            {
                _taskScripts.Clear();
                _taskScriptsById.Clear();
                _taskScriptsLoaded = false;
            }
            _compilerIncLoaded = false;
            LoadScriptDefines();
            M2Share.MainOutMessage("[PasEngine] Script cache cleared.");
        }

        private PasProcDecl FindProcedure(PasProgram program, string name)
        {
            return program.Procedures.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private readonly struct ScriptLabelCall
        {
            public ScriptLabelCall(string label, string rawArgs, string[] args)
            {
                Label = label;
                RawArgs = rawArgs;
                Args = args;
            }

            public string Label { get; }
            public string RawArgs { get; }
            public string[] Args { get; }
            public bool HasArgs => RawArgs.Length > 0 || Args.Length > 0;
        }

        private static ScriptLabelCall ParseScriptLabel(string label)
        {
            label = (label ?? string.Empty).Trim();
            var splitAt = label.IndexOf('~');
            if (splitAt < 0)
                return new ScriptLabelCall(label, string.Empty, Array.Empty<string>());

            var labelName = label.Substring(0, splitAt);
            var rawArgs = splitAt + 1 < label.Length ? label.Substring(splitAt + 1) : string.Empty;
            var args = rawArgs.Length == 0 ? new[] { string.Empty } : rawArgs.Split('~');
            return new ScriptLabelCall(labelName, rawArgs, args);
        }

        private static List<PasValue> BuildLabelArgs(PasProcDecl proc, ScriptLabelCall labelCall)
        {
            var result = new List<PasValue>();
            if (proc.Parameters.Count == 0 || !labelCall.HasArgs)
                return result;

            if (proc.Parameters.Count == 1)
            {
                result.Add(ToLabelArg(labelCall.RawArgs, proc.Parameters[0].TypeName));
                return result;
            }

            for (var i = 0; i < proc.Parameters.Count; i++)
            {
                var arg = i < labelCall.Args.Length ? labelCall.Args[i] : string.Empty;
                result.Add(ToLabelArg(arg, proc.Parameters[i].TypeName));
            }
            return result;
        }

        private static PasValue ToLabelArg(string value, string typeName)
        {
            var type = (typeName ?? string.Empty).Trim().ToLowerInvariant();
            if (type is "integer" or "int" or "smallint" or "longint" or "shortint" or "byte" or "word" or "cardinal")
                return PasValue.FromInt(int.TryParse(value, out var intValue) ? intValue : 0);
            if (type is "boolean" or "bool")
                return PasValue.FromBool(value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
            if (type is "double" or "single" or "extended" or "real")
                return PasValue.FromDouble(double.TryParse(value, out var dblValue) ? dblValue : 0);
            return PasValue.FromString(value);
        }

        [System.Diagnostics.Conditional("GAMESVR_SCRIPT_TRACE")]
        private static void LogLabelDispatch(string pasFilePath, string label, string procName, string rawArgs)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PasLabel] {Path.GetFileName(pasFilePath)} {label} -> {procName}({rawArgs})");
        }

        [System.Diagnostics.Conditional("GAMESVR_SCRIPT_TRACE")]
        private static void LogLabelMissing(string pasFilePath, string label)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PasLabelMissing] {Path.GetFileName(pasFilePath)} {label}");
        }

        [System.Diagnostics.Conditional("GAMESVR_SCRIPT_TRACE")]
        private static void LogLabelBuiltin(string pasFilePath, string label)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PasLabelBuiltin] {Path.GetFileName(pasFilePath)} {label}");
        }

        private static bool IsBuiltInMerchantLabel(string label)
        {
            var value = (label ?? string.Empty).Trim();
            if (value.StartsWith("~"))
                value = value.Substring(1);

            return value.Equals(M2Share.sOFFLINEMSG, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sSL_SENDMSG, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sSUPERREPAIR, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sREPAIR, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sBUY, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sSELL, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sMAKEDURG, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sPRICES, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sSTORAGE, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sGETBACK, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sGETNEXTPAGE, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sGETPREVIOUSPAGE, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sUPGRADENOW, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sGETBACKUPGNOW, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sGETMARRY, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sGETMASTER, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sEXIT, StringComparison.OrdinalIgnoreCase)
                || value.Equals(M2Share.sBACK, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(M2Share.sUSEITEMNAME, StringComparison.OrdinalIgnoreCase);
        }

        private static void SendMissingLabelFallbackIfNoDialog(TPlayObject player, NormNpc npc, string label, int beforeDialogSeq)
        {
            if (player == null || npc == null || player.MerchantDialogSeq != beforeDialogSeq)
                return;

            player.SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, npc.ObjectId, 0, 0,
                npc.m_sCharName + "/该功能暂未配置。\\ \\<返回/@main>");
        }

        /// <summary>
        /// Find the PAS script file for a given NPC script name.
        /// Complete search order matching 战神 PAS behavior:
        ///   1. PsNpcscripts/{name}.pas
        ///   2. CommonScripts/{name}.pas
        ///   3. PsMapQuest/{name}.pas
        ///   4. MonScript/{name}.pas
        ///   5. PsItemScript/{name}.pas
        ///   6. DynRoomScripts/{name}.pas
        ///   7. PsFamousScripts/{name}.pas
        ///   8. PsTaskList/{name}.pas
        ///   9. PsNpcscripts subdirectories
        /// </summary>
        public string FindScriptFile(string scriptName)
        {
            scriptName = CleanNpcScriptToken(scriptName);
            if (string.IsNullOrWhiteSpace(scriptName)) return null;

            if (_npcScriptMap == null) LoadNpcScriptMap();
            if (_npcScriptMap != null &&
                _npcScriptMap.TryGetValue(scriptName, out var mappedPath) &&
                File.Exists(mappedPath))
                return mappedPath;

            var directPath = ResolveExistingScriptPath(scriptName);
            if (directPath != null) return directPath;

            string[] searchDirs = {
                "PsNpcscripts",
                "CommonScripts",
                "PsMapQuest",
                "MonScript",
                "PsItemScript",
                "DynRoomScripts",
                "PsFamousScripts",
                "PsTaskList",
                "PsMapQuest/TaskDispatch",
                "PsMapQuest/HelperQuest",
                "PsMapQuest/RunMailQuest",
                "PsMapQuest/LogonQuest",
                "PsMapQuest/LogoutQuest",
                "PsMapQuest/RunQuest",
            };

            string[] extensions = { ".pas" };

            foreach (var dir in searchDirs)
            {
                foreach (var ext in extensions)
                {
                    var path = Path.Combine(_envirPath, dir, scriptName + ext);
                    if (File.Exists(path)) return path;
                    // Also check using / separators
                    path = Path.Combine(_envirPath, dir.Replace('/', Path.DirectorySeparatorChar), scriptName + ext);
                    if (File.Exists(path)) return path;
                }
            }

            // Search subdirectories of PsNpcscripts
            var psDir = Path.Combine(_envirPath, "PsNpcscripts");
            if (Directory.Exists(psDir))
            {
                foreach (var subDir in Directory.GetDirectories(psDir))
                {
                    foreach (var ext in extensions)
                    {
                        var path = Path.Combine(subDir, scriptName + ext);
                        if (File.Exists(path)) return path;
                    }
                }
            }

            // Search subdirectories of PsMapQuest
            var mqDir = Path.Combine(_envirPath, "PsMapQuest");
            if (Directory.Exists(mqDir))
            {
                foreach (var subDir in Directory.GetDirectories(mqDir))
                {
                    foreach (var ext in extensions)
                    {
                        var path = Path.Combine(subDir, scriptName + ext);
                        if (File.Exists(path)) return path;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Find All script files in a directory recursively.
        /// Used for batch loading or hot-reload.
        /// </summary>
        public List<string> FindAllScriptFiles(string subDir = "")
        {
            var result = new List<string>();
            string[] searchDirs = { "PsNpcscripts", "CommonScripts", "PsMapQuest", "MonScript", "PsItemScript", "DynRoomScripts" };
            foreach (var dir in searchDirs)
            {
                var fullDir = string.IsNullOrEmpty(subDir)
                    ? Path.Combine(_envirPath, dir)
                    : Path.Combine(_envirPath, dir, subDir);
                if (Directory.Exists(fullDir))
                {
                    result.AddRange(Directory.GetFiles(fullDir, "*.pas", SearchOption.AllDirectories));
                }
            }
            return result;
        }

        /// <summary>
        /// Check if any cached script file has been modified (for hot-reload polling).
        /// </summary>
        public List<string> GetModifiedFiles()
        {
            var modified = new List<string>();
            foreach (var kvp in _fileTimestamps)
            {
                if (File.Exists(kvp.Key))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(kvp.Key);
                    if (lastWrite > kvp.Value)
                        modified.Add(kvp.Key);
                }
            }
            return modified;
        }

        /// <summary>
        /// Reload all modified scripts.
        /// </summary>
        public void HotReload()
        {
            var modified = GetModifiedFiles();
            foreach (var file in modified)
            {
                Invalidate(file);
                GetOrLoadProgram(file);
                M2Share.MainOutMessage($"[PasEngine] Hot-reloaded: {Path.GetFileName(file)}");
            }
        }
    }
}

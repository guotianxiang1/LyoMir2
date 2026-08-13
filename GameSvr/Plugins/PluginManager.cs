using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GameSvr;

namespace GameSvr.Plugins
{
    public enum YanshenMyJsonKind
    {
        Role,
        SkillConfig,
        SkillExt,
        MonsterSkillExt,
        DropRate,
        GuaranteedDrop,
    }

    /// <summary>
    /// Plugin Manager — manages third-party plugins loaded into the M2Server process.
    ///
    /// Based on the "眼神插件" (Yanshen Plugin) reverse-engineering analysis:
    /// - The 2.0.7 sample injects through a top-level libmySQL.dll proxy that
    ///   forwards the native MySQL exports and loads ys/yanshen2.0.7.dll
    /// - It uses a "tunnel" protocol: encoding custom commands as !!!!-prefixed strings
    ///   passed through M2Server's existing Player.GetBagItemCount() API
    /// - 41+ custom commands: element system, custom damage, control skills,
    ///   pet system, item operations, DB operations, etc.
    ///
    /// This manager provides:
    /// 1. Plugin DLL loading/unloading management
    /// 2. Configuration management (JSON-based, replacing MyJson directory)
    /// 3. Plugin status monitoring (memory usage, hook count, error count)
    /// 4. Compatible !!!! command protocol for script compatibility
    /// 5. Hot-reload support for plugin configurations
    /// </summary>
    public class PluginManager
    {
        private static readonly Encoding NativeConfigEncoding = Encoding.GetEncoding(
            936,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        /// <summary>Suffix native appends to a feature name inside the MyJson subsystem documents.</summary>
        private const string SubsystemToggleSuffix = "_是否勾选";

        private readonly ConcurrentDictionary<string, PluginInfo> _plugins = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _pluginDir;
        private readonly string _configDir;
        private readonly string _nativeConfigPath;
        private readonly string _myJsonDir;
        private readonly object _nativeConfigLock = new();
        private readonly object _itemConfigLock = new();
        private readonly object _myJsonConfigLock = new();
        private Dictionary<string, object> _nativeConfig;

        // MyJson cached data
        private Dictionary<string, object> _skillConfig;
        private Dictionary<string, object> _skillExtConfig;
        private Dictionary<string, object> _monSkillExtConfig;
        private Dictionary<string, object> _roleConfig;
        private Dictionary<string, object> _dropRateConfig;
        private Dictionary<string, object> _guaranteedDropConfig;
        private Dictionary<string, object> _itemConfig;
        private RecycleConfigSnapshot _recycleConfig;

        public PluginManager(string baseDir, string runtimeDir = null)
        {
            if (string.IsNullOrWhiteSpace(baseDir)) throw new ArgumentException("Plugin base directory is required", nameof(baseDir));

            var pluginBaseDir = Path.GetFullPath(baseDir);
            var nativeRuntimeDir = Path.GetFullPath(runtimeDir ?? Path.GetDirectoryName(pluginBaseDir) ?? pluginBaseDir);
            _pluginDir = Path.Combine(pluginBaseDir, "Plugins");
            _configDir = Path.Combine(pluginBaseDir, "Plugins", "Config");
            _nativeConfigPath = Path.Combine(nativeRuntimeDir, "config.json");
            _myJsonDir = Path.Combine(nativeRuntimeDir, "MyJson");
            LoadNativeConfig();
        }

        public string NativeConfigPath => _nativeConfigPath;

        // ===== Native Config (config.json with GBK encoding) =====

        /// <summary>
        /// Load the original config.json (GS1\config.json) with GBK encoding.
        /// This is the original yanshen plugin configuration format with Chinese key names.
        /// </summary>
        public void LoadNativeConfig()
        {
            if (!File.Exists(_nativeConfigPath))
            {
                lock (_nativeConfigLock)
                    _nativeConfig ??= new Dictionary<string, object>(StringComparer.Ordinal);
                M2Share.MainOutMessage($"[PluginManager] Native config not found: {_nativeConfigPath}");
                return;
            }
            try
            {
                var json = File.ReadAllText(_nativeConfigPath, NativeConfigEncoding);
                var loaded = DeserializeConfig(json);
                lock (_nativeConfigLock)
                    _nativeConfig = loaded;
                M2Share.MainOutMessage($"[PluginManager] Loaded native config: {loaded.Count} keys from {_nativeConfigPath}");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[PluginManager] Failed to load native config: {ex.Message}");
                lock (_nativeConfigLock)
                    _nativeConfig ??= new Dictionary<string, object>(StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// Get a value from the native config.json by Chinese key name.
        /// Returns 0 for \"0\", the raw value otherwise, or default if key not found.
        /// </summary>
        public object GetNativeConfigValue(string key)
        {
            lock (_nativeConfigLock)
                return _nativeConfig != null && _nativeConfig.TryGetValue(key, out var val) ? val : null;
        }

        /// <summary>
        /// Set a value in the native config dictionary (in-memory only until SaveNativeConfig is called).
        /// </summary>
        public void SetNativeConfigValue(string key, object value)
        {
            lock (_nativeConfigLock)
            {
                _nativeConfig ??= new Dictionary<string, object>(StringComparer.Ordinal);
                _nativeConfig[key] = NormalizeConfigValue(value);
            }
        }

        /// <summary>
        /// Save the native config dictionary back to config.json with GBK encoding.
        /// </summary>
        public void SaveNativeConfig()
        {
            if (!TrySaveNativeConfig(out var error))
                M2Share.MainOutMessage($"[PluginManager] Failed to save native config: {error}");
        }

        public bool TrySaveNativeConfig(out string error)
        {
            lock (_nativeConfigLock)
            {
                if (_nativeConfig == null)
                {
                    error = "configuration is not loaded";
                    return false;
                }

                if (!TryWriteNativeConfig(_nativeConfig, out error))
                    return false;
            }

            M2Share.MainOutMessage($"[PluginManager] Saved native config: {_nativeConfigPath}");
            return true;
        }

        /// <summary>
        /// Persist a complete set of edits, then publish the same snapshot to live readers.
        /// A failed write leaves both the file and the active configuration unchanged.
        /// </summary>
        public bool ApplyNativeConfigChanges(IReadOnlyDictionary<string, object> changes, out string error)
        {
            if (changes == null)
            {
                error = "changes cannot be null";
                return false;
            }

            lock (_nativeConfigLock)
            {
                var candidate = _nativeConfig != null
                    ? new Dictionary<string, object>(_nativeConfig, StringComparer.Ordinal)
                    : new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (var (key, value) in changes)
                    candidate[key] = NormalizeConfigValue(value);

                if (!TryWriteNativeConfig(candidate, out error))
                    return false;

                _nativeConfig = candidate;
            }

            M2Share.MainOutMessage($"[PluginManager] Saved and applied native config: {_nativeConfigPath}");
            return true;
        }

        /// <summary>Return a stable snapshot; callers cannot mutate the live dictionary.</summary>
        public Dictionary<string, object> GetNativeConfig()
        {
            lock (_nativeConfigLock)
                return _nativeConfig != null
                    ? new Dictionary<string, object>(_nativeConfig, StringComparer.Ordinal)
                    : new Dictionary<string, object>(StringComparer.Ordinal);
        }

        private bool TryWriteNativeConfig(IReadOnlyDictionary<string, object> config, out string error)
        {
            var tempPath = _nativeConfigPath + ".tmp";
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
                var json = JsonSerializer.Serialize(config, options).Replace("\n", "\r\n", StringComparison.Ordinal);
                var directory = Path.GetDirectoryName(_nativeConfigPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(tempPath, json, NativeConfigEncoding);
                File.Move(tempPath, _nativeConfigPath, true);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                error = ex.Message;
                return false;
            }
        }

        private static Dictionary<string, object> DeserializeConfig(
            string json, bool allowLegacyTrailingBrace = false)
        {
            using var document = ParseConfigDocument(json, allowLegacyTrailingBrace);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("configuration root must be a JSON object");

            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                result[property.Name] = NormalizeConfigValue(property.Value);
            return result;
        }

        private static JsonDocument ParseConfigDocument(
            string json, bool allowLegacyTrailingBrace)
        {
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException) when (allowLegacyTrailingBrace)
            {
                // The distributed 2.07/2.08 drop-rate file has one redundant final '}'.
                var trimmed = json.TrimEnd();
                if (trimmed.Length == 0 || trimmed[^1] != '}') throw;
                return JsonDocument.Parse(trimmed[..^1]);
            }
        }

        public static object NormalizeConfigValue(object value)
        {
            if (value is not JsonElement element) return value;

            return element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => NormalizeConfigValue(property.Value),
                    StringComparer.Ordinal),
                JsonValueKind.Array => element.EnumerateArray().Select(item => NormalizeConfigValue(item)).ToList(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
                JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText(),
            };
        }

        // ===== MyJson File Integration (GBK encoding) =====

        public string GetMyJsonConfigPath(YanshenMyJsonKind kind) => kind switch
        {
            YanshenMyJsonKind.Role => Path.Combine(_myJsonDir, "roles", "config.json"),
            YanshenMyJsonKind.SkillConfig => Path.Combine(_myJsonDir, "skills", "config.json"),
            YanshenMyJsonKind.SkillExt => Path.Combine(_myJsonDir, "skills", "skillext.json"),
            YanshenMyJsonKind.MonsterSkillExt => Path.Combine(_myJsonDir, "skills", "monskillext.json"),
            YanshenMyJsonKind.DropRate => Path.Combine(_myJsonDir, "眼神爆率.json"),
            YanshenMyJsonKind.GuaranteedDrop => Path.Combine(_myJsonDir, "全区可爆.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown MyJson kind"),
        };

        /// <summary>
        /// Reload one independent MyJson document. A failed read or parse leaves the
        /// last valid in-memory snapshot unchanged.
        /// </summary>
        public bool ReloadMyJsonConfig(YanshenMyJsonKind kind, out string error)
        {
            lock (_myJsonConfigLock)
            {
                try
                {
                    var path = GetMyJsonConfigPath(kind);
                    if (!File.Exists(path))
                        throw new FileNotFoundException("MyJson configuration was not found", path);

                    var json = File.ReadAllText(path, NativeConfigEncoding);
                    var candidate = DeserializeConfig(
                        json, allowLegacyTrailingBrace: kind == YanshenMyJsonKind.DropRate);
                    SetMyJsonConfig(kind, candidate);
                    error = null;
                    M2Share.MainOutMessage(
                        $"[PluginManager] Loaded {Path.GetRelativePath(_myJsonDir, path)}: " +
                        $"{candidate.Count} entries");
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    M2Share.MainOutMessage(
                        $"[PluginManager] Failed to load MyJson {kind}: {error}");
                    return false;
                }
            }
        }

        /// <summary>Return a detached copy of the last valid document.</summary>
        public Dictionary<string, object> GetMyJsonConfig(YanshenMyJsonKind kind)
        {
            lock (_myJsonConfigLock)
                return CloneConfig(GetMyJsonConfigSnapshot(kind));
        }

        /// <summary>
        /// Persist and publish a complete independent MyJson document. Unknown keys,
        /// nested values and JSON value types supplied by the caller are retained.
        /// </summary>
        public bool ApplyMyJsonConfig(
            YanshenMyJsonKind kind,
            IReadOnlyDictionary<string, object> completeDocument,
            out string error)
        {
            if (completeDocument == null)
            {
                error = "completeDocument cannot be null";
                return false;
            }

            lock (_myJsonConfigLock)
            {
                var candidate = CloneConfig(completeDocument);
                if (!TryWriteMyJsonConfig(kind, candidate, out error))
                    return false;

                SetMyJsonConfig(kind, candidate);
            }

            M2Share.MainOutMessage(
                $"[PluginManager] Saved and applied MyJson {kind}: {GetMyJsonConfigPath(kind)}");
            return true;
        }

        private bool TryWriteMyJsonConfig(
            YanshenMyJsonKind kind,
            IReadOnlyDictionary<string, object> config,
            out string error)
        {
            var path = GetMyJsonConfigPath(kind);
            var tempPath = path + ".tmp";
            try
            {
                EnsureNativeConfigEncodable(config);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
                var json = JsonSerializer.Serialize(config, options)
                    .Replace("\n", "\r\n", StringComparison.Ordinal);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(tempPath, json, NativeConfigEncoding);
                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                error = ex.Message;
                return false;
            }
        }

        private Dictionary<string, object> GetMyJsonConfigSnapshot(
            YanshenMyJsonKind kind) => kind switch
        {
            YanshenMyJsonKind.Role => _roleConfig,
            YanshenMyJsonKind.SkillConfig => _skillConfig,
            YanshenMyJsonKind.SkillExt => _skillExtConfig,
            YanshenMyJsonKind.MonsterSkillExt => _monSkillExtConfig,
            YanshenMyJsonKind.DropRate => _dropRateConfig,
            YanshenMyJsonKind.GuaranteedDrop => _guaranteedDropConfig,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown MyJson kind"),
        };

        private void SetMyJsonConfig(
            YanshenMyJsonKind kind, Dictionary<string, object> config)
        {
            switch (kind)
            {
                case YanshenMyJsonKind.Role:
                    _roleConfig = config;
                    break;
                case YanshenMyJsonKind.SkillConfig:
                    _skillConfig = config;
                    break;
                case YanshenMyJsonKind.SkillExt:
                    _skillExtConfig = config;
                    break;
                case YanshenMyJsonKind.MonsterSkillExt:
                    _monSkillExtConfig = config;
                    break;
                case YanshenMyJsonKind.DropRate:
                    _dropRateConfig = config;
                    break;
                case YanshenMyJsonKind.GuaranteedDrop:
                    _guaranteedDropConfig = config;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown MyJson kind");
            }
        }

        public string ItemConfigPath => Path.Combine(_myJsonDir, "items", "config.json");

        public bool HasValidItemConfig
        {
            get
            {
                lock (_itemConfigLock)
                    return _itemConfig != null;
            }
        }

        /// <summary>
        /// Load GS1\MyJson\items\config.json and atomically publish the complete document.
        /// A failed reload leaves the last valid snapshot active.
        /// </summary>
        public bool ReloadItemConfig(out string error)
        {
            lock (_itemConfigLock)
            {
                try
                {
                    if (!File.Exists(ItemConfigPath))
                        throw new FileNotFoundException("items/config.json was not found", ItemConfigPath);

                    var json = File.ReadAllText(ItemConfigPath, NativeConfigEncoding);
                    var candidate = DeserializeConfig(json);
                    _itemConfig = candidate;
                    error = null;
                    M2Share.MainOutMessage(
                        $"[PluginManager] Loaded items/config.json: {candidate.Count} entries");
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    M2Share.MainOutMessage(
                        $"[PluginManager] Failed to load items/config.json: {error}");
                    return false;
                }
            }
        }

        /// <summary>Return a detached copy of the last valid items configuration.</summary>
        public Dictionary<string, object> GetItemConfig()
        {
            lock (_itemConfigLock)
                return CloneConfig(_itemConfig);
        }

        /// <summary>Return a detached value from the last valid items configuration.</summary>
        public object GetItemConfigValue(string key)
        {
            lock (_itemConfigLock)
                return _itemConfig != null && _itemConfig.TryGetValue(key, out var value)
                    ? CloneConfigValue(value)
                    : null;
        }

        /// <summary>
        /// Resolve a feature switch that native stores in a per-subsystem MyJson document
        /// instead of the top-level config.json, under "&lt;feature&gt;_是否勾选".
        /// Returns null when no subsystem document carries the switch.
        /// </summary>
        public object GetSubsystemToggleValue(string feature)
        {
            if (string.IsNullOrEmpty(feature)) return null;
            var key = feature + SubsystemToggleSuffix;

            lock (_myJsonConfigLock)
            {
                var role = GetMyJsonConfigSnapshot(YanshenMyJsonKind.Role);
                if (role != null && role.TryGetValue(key, out var roleValue))
                    return CloneConfigValue(roleValue);

                var skill = GetMyJsonConfigSnapshot(YanshenMyJsonKind.SkillConfig);
                if (skill != null && skill.TryGetValue(key, out var skillValue))
                    return CloneConfigValue(skillValue);
            }

            return GetItemConfigValue(key);
        }

        /// <summary>
        /// Merge edits into the complete items document, persist it with GBK encoding,
        /// then publish the same snapshot. Unknown keys are retained.
        /// </summary>
        public bool ApplyItemConfigChanges(
            IReadOnlyDictionary<string, object> changes, out string error)
        {
            if (changes == null)
            {
                error = "changes cannot be null";
                return false;
            }

            lock (_itemConfigLock)
            {
                var candidate = CloneConfig(_itemConfig);
                foreach (var (key, value) in changes)
                    candidate[key] = CloneConfigValue(value);

                if (!TryWriteItemConfig(candidate, out error))
                    return false;

                _itemConfig = candidate;
            }

            M2Share.MainOutMessage(
                $"[PluginManager] Saved and applied items/config.json: {ItemConfigPath}");
            return true;
        }

        private bool TryWriteItemConfig(
            IReadOnlyDictionary<string, object> config, out string error)
        {
            var tempPath = ItemConfigPath + ".tmp";
            try
            {
                EnsureNativeConfigEncodable(config);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                };
                var json = JsonSerializer.Serialize(config, options)
                    .Replace("\n", "\r\n", StringComparison.Ordinal);
                var directory = Path.GetDirectoryName(ItemConfigPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(tempPath, json, NativeConfigEncoding);
                File.Move(tempPath, ItemConfigPath, true);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                error = ex.Message;
                return false;
            }
        }

        private static void EnsureNativeConfigEncodable(object value)
        {
            switch (value)
            {
                case string text:
                    NativeConfigEncoding.GetByteCount(text);
                    return;
                case IReadOnlyDictionary<string, object> dictionary:
                    foreach (var (key, child) in dictionary)
                    {
                        NativeConfigEncoding.GetByteCount(key);
                        EnsureNativeConfigEncodable(child);
                    }
                    return;
                case IEnumerable<object> sequence:
                    foreach (var child in sequence) EnsureNativeConfigEncodable(child);
                    return;
            }
        }

        private static Dictionary<string, object> CloneConfig(
            IReadOnlyDictionary<string, object> source)
        {
            var clone = new Dictionary<string, object>(StringComparer.Ordinal);
            if (source == null) return clone;

            foreach (var (key, value) in source)
                clone[key] = CloneConfigValue(value);
            return clone;
        }

        private static object CloneConfigValue(object value)
        {
            value = NormalizeConfigValue(value);
            return value switch
            {
                IReadOnlyDictionary<string, object> dictionary => dictionary.ToDictionary(
                    entry => entry.Key,
                    entry => CloneConfigValue(entry.Value),
                    StringComparer.Ordinal),
                IEnumerable<object> sequence => sequence.Select(CloneConfigValue).ToList(),
                _ => value,
            };
        }

        public string RecycleConfigPath => Path.Combine(_myJsonDir, "recycle.json");

        public bool HasValidRecycleConfig => Volatile.Read(ref _recycleConfig) != null;

        public int RecycleConfigItemCount =>
            Volatile.Read(ref _recycleConfig)?.ItemCount ?? 0;

        public bool IsRecycleItemConfigured(string itemName) =>
            Volatile.Read(ref _recycleConfig)?.ContainsItem(itemName) == true;

        /// <summary>
        /// Parse and validate GS1\MyJson\recycle.json, then atomically publish it.
        /// A failed reload leaves the last valid snapshot active.
        /// </summary>
        public bool ReloadRecycleConfig(out string error)
        {
            try
            {
                if (!File.Exists(RecycleConfigPath))
                    throw new FileNotFoundException("recycle.json was not found", RecycleConfigPath);

                var json = File.ReadAllText(RecycleConfigPath, NativeConfigEncoding);
                var candidate = ParseRecycleConfig(json);
                Volatile.Write(ref _recycleConfig, candidate);
                error = null;
                M2Share.MainOutMessage(
                    $"[PluginManager] Loaded recycle.json: {candidate.ItemCount} items");
                if (candidate.UnresolvedItems.Count > 0)
                    M2Share.MainOutMessage(
                        "[PluginManager] recycle.json: " + candidate.UnresolvedItems.Count +
                        " item(s) name an undefined 回收类型 and will not be recycled: " +
                        string.Join(", ", candidate.UnresolvedItems));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                M2Share.MainOutMessage(
                    $"[PluginManager] Failed to load recycle.json: {error}");
                return false;
            }
        }

        internal RecycleConfigSnapshot GetRecycleConfigSnapshot() =>
            Volatile.Read(ref _recycleConfig);

        private static RecycleConfigSnapshot ParseRecycleConfig(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("recycle configuration root must be a JSON object");

            // 原生把 物品种类 与 回收类型 两个根键都列为必需，缺一整份作废（回收入口
            // 0x1006CF16 读的有效位 0x1031B8C5 只在两键齐备时才写 1：装载校验
            // 0x1009103E 找 物品种类、0x10091056 找 回收类型，任一 je 0x1009107F 就置 0
            // 并打 "物品种类和回收类型根字符不允许修改" @0x102C0A6C）。
            // 可叠材料 不参与判定 —— 只写 可叠材料 的配置，原生是一件都不动的。
            if (!root.TryGetProperty("回收类型", out var recycleTypes) ||
                !root.TryGetProperty("物品种类", out _))
                throw new JsonException(
                    "recycle configuration requires both 物品种类 and 回收类型");

            return ParseNativeRecycleConfig(root, recycleTypes);
        }

        private static RecycleConfigSnapshot ParseNativeRecycleConfig(
            JsonElement root, JsonElement recycleTypes)
        {
            if (recycleTypes.ValueKind != JsonValueKind.Object)
                throw new JsonException("回收类型 must be a JSON object");

            var rules = new Dictionary<string, RecycleRule>(StringComparer.Ordinal);
            foreach (var type in recycleTypes.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(type.Name))
                    throw new JsonException("回收类型 contains an empty type name");
                if (rules.ContainsKey(type.Name))
                    throw new JsonException($"duplicate recycle type: {type.Name}");
                rules.Add(type.Name, ParseRecycleRule(type.Name, type.Value));
            }

            // 物品名按原字节匹配。生产 recycle.json 里既有 "破魂" 这类重复键，也有
            // "  施毒术" 这种带前导空格的键（物品库里的真名是 "施毒术"，见
            // MySQL\data\mir3\stditems.MYD：带空格变体 0 命中）。是否 trim / 是否忽略
            // 大小写在原版都无从验证，取最窄的一种：不 trim、不折叠大小写，
            // 匹配不上就不回收。
            var items = new Dictionary<string, RecycleItemRule>(StringComparer.Ordinal);
            var unresolved = new List<string>();
            // 原生先判 可叠材料（0x1006B3E1）命中就走完整条堆叠分支、绝不回落到
            // 物品种类，所以同名项以 可叠材料 为准 —— 这里靠后写覆盖得到同样次序。
            foreach (var sectionName in new[] { "物品种类", "可叠材料" })
            {
                if (!root.TryGetProperty(sectionName, out var section)) continue;
                if (section.ValueKind != JsonValueKind.Object)
                    throw new JsonException($"{sectionName} must be a JSON object");

                // 作者原文（recycle(详细说明).json）：「可叠材料是自动忽略极品和元素的」。
                var stackable = sectionName == "可叠材料";
                foreach (var item in section.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        throw new JsonException($"{sectionName} contains an empty item name");
                    if (item.Value.ValueKind != JsonValueKind.String)
                        throw new JsonException($"{sectionName}.{item.Name} must name a recycle type");

                    // 指向不存在的回收类型不是语法错误，作者给 -999 的说法只覆盖
                    // 「配置文件不存在或语法错误」。生产 recycle.json 的 可叠材料 就还留着
                    // 出厂模板的 "类型2"，而它的 回收类型 段里只有 11 个中文类型名，没有 类型2；
                    // 整份判废会让那台服务器一件都回收不了。落到没有结算规则的物品身上
                    // 只能是不回收 —— 删了没处结账。
                    var typeName = item.Value.GetString();
                    if (string.IsNullOrWhiteSpace(typeName) || !rules.TryGetValue(typeName, out var rule))
                    {
                        unresolved.Add($"{sectionName}.{item.Name}->{typeName}");
                        continue;
                    }
                    items[item.Name] = new RecycleItemRule(rule, stackable);
                }
            }

            return new RecycleConfigSnapshot(items, unresolved);
        }

        /// <summary>
        /// One 回收类型 entry. Field semantics come from the author's own annotated copy,
        /// GS1\MyJson\recycle(详细说明).json。
        ///
        /// 未知字段一律忽略，这是原生行为，不是宽容：sub_1006B020 从头到尾只做「按名取」，
        /// 没有任何成员枚举。全函数 0x1006B020..0x1006CF00 内 157 处 push 常量字符串，
        /// 只涉及 16 个键名（可叠材料 0x102BF7B0 / 回收类型 0x102BF7BC / 总开关 0x102BF7C8 /
        /// v1 0x102BF7D0 / v2 0x102BF7D4 / 关闭值 0x102BF7D8 / 回收倍率 0x102BF7E0 /
        /// 元宝 0x102BF644 / 灵符 0x102BF64C / 金币 0x102BF654 / 经验 0x102B4130 /
        /// 其他 0x102BF7EC / 值 0x102BF7F4 / 物品种类 0x102BF7F8 / 极品开关 0x102BF804 /
        /// 元素开关 0x102BF810），全部喂给 jsoncpp 的按名访问族
        /// （0x100E0BE0 / 0x100E0DC0 / 0x100E0EA0 / 0x100E0ED0 / 0x100E1210 / 0x100E1240，
        /// 共 152 次调用）。装载期校验 sub_10090EF0 也只查两个根键
        /// （0x1009103E 物品种类、0x10091056 回收类型），不看规则字段。
        /// ⇒ 配置里多写一个键，原生连看都不会看。
        ///
        /// 【原生缺陷，照抄；勿"修"】REPLICATION_RULES §3.1。代价是把 "极品开关" 拼错
        /// 会静默丢掉一道保护而不报错 —— 原版就是这样。
        /// </summary>
        private static RecycleRule ParseRecycleRule(string typeName, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new JsonException($"回收类型.{typeName} must be a JSON object");

            var rule = new RecycleRule { TypeName = typeName };
            foreach (var field in value.EnumerateObject())
            {
                var path = $"回收类型.{typeName}.{field.Name}";
                switch (field.Name)
                {
                    // 「省略后失去开关效果」；GetV(v1,v2)==关闭值 时该类型停止回收。
                    //
                    // 【原生缺陷，照抄；勿"修"】子键缺一个就整段不解析，不是配置错误。
                    // 四个门 0x1006B4A1(总开关 isMember) / 0x1006B4E2(v1) / 0x1006B523(v2) /
                    // 0x1006B564(关闭值) 全部 je 0x1006B633，而 0x1006B633 紧接着就去解析
                    // 回收倍率 —— 没有任何报错路径。关闭值 于是停在每件预置的 -2
                    // （0x1006B2F4 C7 45 84 FE FF FF FF），消费端 0x1006B787 7C jl 判 < -1
                    // 直接放行 ⇒ 这道门失效。
                    case "总开关":
                        if (TryReadRuleInt(field.Value, "v1", path, out var switchGroup) &&
                            TryReadRuleInt(field.Value, "v2", path, out var switchIndex) &&
                            TryReadRuleInt(field.Value, "关闭值", path, out var switchClosed))
                        {
                            rule.MasterSwitchGroup = switchGroup;
                            rule.MasterSwitchIndex = switchIndex;
                            rule.MasterSwitchClosedValue = switchClosed;
                            rule.HasMasterSwitch = true;
                        }
                        break;
                    // 「省略后永久1倍效果」；GetV(v1,v2)=200 表示 2 倍，小于等于 0 表示无效。
                    // 同一形状：三个门 0x1006B660(回收倍率) / 0x1006B69F(v1) / 0x1006B6DE(v2)
                    // 全部 je 0x1006B783，倍率停在每件重置的 0（0x1006B29B）⇒ 1 倍。
                    case "回收倍率":
                        if (TryReadRuleInt(field.Value, "v1", path, out var rateGroup) &&
                            TryReadRuleInt(field.Value, "v2", path, out var rateIndex))
                        {
                            rule.RateGroup = rateGroup;
                            rule.RateIndex = rateIndex;
                            rule.HasRate = true;
                        }
                        break;
                    case "极品开关":
                        rule.ExtremeGroup = ReadRuleInt(field.Value, path);
                        break;
                    case "元素开关":
                        rule.ElementGroup = ReadRuleInt(field.Value, path);
                        break;
                    case "元宝": rule.Yuanbao = ReadRuleInt(field.Value, path); break;
                    case "金币": rule.Gold = ReadRuleInt(field.Value, path); break;
                    case "灵符": rule.LingFu = ReadRuleInt(field.Value, path); break;
                    // 只要写了 经验 键就置 HasExp（哪怕值是 0）—— 原生 0x1006B95C
                    // mov [ebp-0x78],esi 会把这件的经验单价压回泄漏槽，写 0 也是压 0。
                    // 缺键才让上一件的值继续泄漏（N1，见 YanshenApi.RecycleOne）。
                    case "经验":
                        rule.Exp = ReadRuleInt(field.Value, path);
                        rule.HasExp = true;
                        break;
                    // 「每件物品回收增加 This_player.SetV(v1,v2,值)」。
                    //
                    // 原生这一段的门不是齐步走的：其他 与 值 缺任一 ⇒ je 0x1006BB25 整段跳过
                    // （0x1006B997 / 0x1006B9D6）；但 v1 缺席只 je 0x1006BAA6 跳过 v1 这一读、
                    // 继续查 v2（0x1006BA60），v2 缺席才 je 0x1006BB25（0x1006BADF）。
                    // 两个坐标都停在每件重置的 0（0x1006B2DB / 0x1006B2E5），而此时 值 已经
                    // 读进 [ebp-0x68] —— 于是物品照删，SetV 被 0x6DF2B3/0x6DF2B7 两个 jle
                    // 静默丢弃。又一个「只删不给」窗口，照抄。
                    case "其他":
                        if (TryReadRuleInt(field.Value, "值", path, out var otherValue))
                        {
                            TryReadRuleInt(field.Value, "v1", path, out var otherGroup);
                            TryReadRuleInt(field.Value, "v2", path, out var otherIndex);
                            rule.OtherGroup = otherGroup;
                            rule.OtherIndex = otherIndex;
                            rule.OtherValue = otherValue;
                            rule.HasOther = true;
                        }
                        break;
                    default:
                        break;
                }
            }

            return rule;
        }

        private static int ReadRuleInt(JsonElement value, string path)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
                throw new JsonException($"{path} must be a 32-bit integer");
            return number;
        }

        /// <summary>
        /// 读复合字段的一个子键；缺席返回 false 并把 value 留在 0，由调用方决定整段是否生效。
        /// 显式 null 与缺席同义：总开关 的三个子键门用的就是 jsoncpp 的 nullValue 测试
        /// （0x1006B4DE / 0x1006B51F / 0x1006B560 <c>80 78 08 00 cmp byte [eax+8],0</c>，
        /// 非 const operator[] 对缺失键会插入一个 nullValue 再返回引用），
        /// 所以「键不在」和「键写了 null」在原生走的是同一条臂。
        /// </summary>
        private static bool TryReadRuleInt(JsonElement owner, string name, string path,
            out int value)
        {
            value = 0;
            if (owner.ValueKind != JsonValueKind.Object)
                throw new JsonException($"{path} must be a JSON object");
            if (!owner.TryGetProperty(name, out var element) ||
                element.ValueKind == JsonValueKind.Null)
                return false;
            value = ReadRuleInt(element, $"{path}.{name}");
            return true;
        }

        /// <summary>
        /// Load skills/skillext.json — per-skill damage multipliers and effects.
        /// Keyed by magic ID (as string), each value contains fields like 伤害加成(千分比),
        /// 对怪加成, 对人加成, 技能范围, 伤害类型, 是否直线, etc.
        /// </summary>
        public void LoadSkillExt()
        {
            ReloadMyJsonConfig(YanshenMyJsonKind.SkillExt, out _);
        }

        /// <summary>
        /// Load skills/monskillext.json — monster skill effects (bloodsuck, poison on attack, etc.)
        /// Contains a template object keyed by "怪物通用" with Ac, Dc, DcMax, Hit, Mac, Mc, Sc,
        /// Speed, 当前hp, 最大hp, 攻击频率, 移动频率, 攻击特效id, 切割血量(千分比), etc.
        /// </summary>
        public void LoadMonSkillExt()
        {
            ReloadMyJsonConfig(YanshenMyJsonKind.MonsterSkillExt, out _);
        }

        /// <summary>
        /// Load 角色/config.json (roles/config.json) — role-specific config including:
        /// 沙巴克坐标偏移, 沙巴克攻城范围, 自动巡逻, 伤害吸血, 英雄切割, 内存优化系数, etc.
        /// </summary>
        public void LoadRoleConfig()
        {
            ReloadMyJsonConfig(YanshenMyJsonKind.Role, out _);
        }

        /// <summary>
        /// Load 眼神爆率.json — drop rate adjustments including:
        /// 全局爆率 (base rate multiplier), 可用物品 (items with validity dates),
        /// 爆率列表 (drop tables with probabilities), 物品列表 (item pools),
        /// 物品元素 (element pools for drops), 极品物品 (extreme-grade item pools).
        /// </summary>
        public void LoadDropRateConfig()
        {
            ReloadMyJsonConfig(YanshenMyJsonKind.DropRate, out _);
        }

        /// <summary>
        /// Load 全区可爆.json — guaranteed server-wide drops.
        /// Simple key-value pairs: item name -> drop count/probability.
        /// </summary>
        public void LoadGuaranteedDropConfig()
        {
            ReloadMyJsonConfig(YanshenMyJsonKind.GuaranteedDrop, out _);
        }

        /// <summary>Load all MyJson config files at once.</summary>
        public void LoadAllMyJsonConfigs()
        {
            ReloadMyJsonConfig(YanshenMyJsonKind.SkillConfig, out _);
            LoadSkillExt();
            LoadMonSkillExt();
            LoadRoleConfig();
            LoadDropRateConfig();
            LoadGuaranteedDropConfig();
            ReloadItemConfig(out _);
            ReloadRecycleConfig(out _);
        }

        // Public getters for MyJson data
        public Dictionary<string, object> GetSkillExtConfig() =>
            GetMyJsonConfig(YanshenMyJsonKind.SkillExt);

        public Dictionary<string, object> GetMonSkillExtConfig() =>
            GetMyJsonConfig(YanshenMyJsonKind.MonsterSkillExt);

        public Dictionary<string, object> GetRoleConfig() =>
            GetMyJsonConfig(YanshenMyJsonKind.Role);

        public Dictionary<string, object> GetDropRateConfig() =>
            GetMyJsonConfig(YanshenMyJsonKind.DropRate);

        public Dictionary<string, object> GetGuaranteedDropConfig() =>
            GetMyJsonConfig(YanshenMyJsonKind.GuaranteedDrop);

        // ===== Plugin Registration =====

        public PluginInfo RegisterPlugin(string name, string description, string version, PluginType type = PluginType.Native)
        {
            var info = new PluginInfo
            {
                Name = name,
                Description = description,
                Version = version,
                Type = type,
                State = PluginState.Registered,
                ConfigPath = Path.Combine(_configDir, name + ".json"),
                LoadTime = DateTime.Now,
            };

            _plugins[name] = info;
            M2Share.MainOutMessage($"[PluginManager] Registered plugin: {name} v{version} ({type})");
            return info;
        }

        public bool LoadPlugin(string name)
        {
            if (!_plugins.TryGetValue(name, out var info))
            {
                M2Share.MainOutMessage($"[PluginManager] Plugin not found: {name}");
                return false;
            }

            lock (info.InitializationSync)
            {
                if (info.State == PluginState.Running)
                {
                    M2Share.MainOutMessage($"[PluginManager] Plugin already running: {name}");
                    return true;
                }

                try
                {
                    info.State = PluginState.Loading;
                    info.IsInitialized = false;
                    info.LoadTime = DateTime.Now;

                    // Load plugin configuration
                    if (File.Exists(info.ConfigPath))
                    {
                        info.Config = File.ReadAllText(info.ConfigPath);
                        info.Settings = DeserializeConfig(info.Config);
                    }

                    // Initialize plugin
                    InitializePlugin(info);

                    info.State = PluginState.Running;
                    M2Share.MainOutMessage($"[PluginManager] Plugin loaded: {name}");
                    return true;
                }
                catch (Exception ex)
                {
                    info.State = PluginState.Error;
                    info.IsInitialized = false;
                    info.LastError = ex.Message;
                    M2Share.MainOutMessage($"[PluginManager] Failed to load plugin '{name}': {ex.Message}");
                    return false;
                }
            }
        }

        public bool UnloadPlugin(string name)
        {
            if (!_plugins.TryGetValue(name, out var info)) return false;

            lock (info.InitializationSync)
            {
                try
                {
                    info.State = PluginState.Unloading;
                    ShutdownPlugin(info);
                    info.IsInitialized = false;
                    info.State = PluginState.Registered;
                    M2Share.MainOutMessage($"[PluginManager] Plugin unloaded: {name}");
                    return true;
                }
                catch (Exception ex)
                {
                    info.IsInitialized = false;
                    info.LastError = ex.Message;
                    return false;
                }
            }
        }

        public bool HotReloadPlugin(string name)
        {
            UnloadPlugin(name);
            return LoadPlugin(name);
        }

        // ===== Plugin Queries =====

        public PluginInfo GetPlugin(string name) =>
            _plugins.TryGetValue(name, out var info) ? info : null;

        public IReadOnlyList<PluginInfo> GetAllPlugins() => _plugins.Values.ToList().AsReadOnly();

        public IReadOnlyList<PluginInfo> GetRunningPlugins() =>
            _plugins.Values.Where(p => p.State == PluginState.Running).ToList().AsReadOnly();

        // ===== Plugin Configuration =====

        public bool SavePluginConfig(string name, Dictionary<string, object> settings)
        {
            if (!_plugins.TryGetValue(name, out var info)) return false;
            try
            {
                var normalized = settings.ToDictionary(
                    pair => pair.Key,
                    pair => NormalizeConfigValue(pair.Value),
                    StringComparer.OrdinalIgnoreCase);
                var serialized = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(_configDir);
                File.WriteAllText(info.ConfigPath, serialized);
                info.Settings = normalized;
                info.Config = serialized;
                return true;
            }
            catch (Exception ex)
            {
                info.LastError = ex.Message;
                M2Share.MainOutMessage($"[PluginManager] Failed to save plugin config '{name}': {ex.Message}");
                return false;
            }
        }

        public Dictionary<string, object> GetPluginConfig(string name)
        {
            if (_plugins.TryGetValue(name, out var info) && info.Settings != null && info.Settings.Count > 0)
                return new Dictionary<string, object>(info.Settings, StringComparer.OrdinalIgnoreCase);
            // Fall back to native config when YanshenCompat.json has no keys
            if (string.Equals(name, "YanshenCompat", StringComparison.OrdinalIgnoreCase))
                return GetNativeConfig();
            return new Dictionary<string, object>();
        }

        public Dictionary<string, object> GetPluginOwnedConfig(string name)
        {
            if (_plugins.TryGetValue(name, out var info) && info.Settings != null)
                return new Dictionary<string, object>(info.Settings, StringComparer.OrdinalIgnoreCase);
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public bool SetPluginSetting(string name, string key, object value)
        {
            var settings = GetPluginOwnedConfig(name);
            settings[key] = NormalizeConfigValue(value);
            return SavePluginConfig(name, settings);
        }

        public T GetPluginSetting<T>(string name, string key, T defaultValue = default)
        {
            // First try YanshenCompat.json
            if (_plugins.TryGetValue(name, out var info) &&
                info.Settings != null &&
                info.Settings.TryGetValue(key, out var val))
            {
                if (TryConvertSetting(val, out T converted)) return converted;
            }
            // Fall back to native config (config.json)
            var nativeVal = GetNativeConfigValue(key);
            if (nativeVal != null)
            {
                if (TryConvertSetting(nativeVal, out T converted)) return converted;
            }
            return defaultValue;
        }

        private static bool TryConvertSetting<T>(object value, out T result)
        {
            result = default;
            value = NormalizeConfigValue(value);
            if (value == null) return false;

            try
            {
                var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                object converted;
                if (targetType == typeof(bool))
                {
                    converted = value switch
                    {
                        bool boolean => boolean,
                        string text when bool.TryParse(text, out var boolean) => boolean,
                        string text when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number != 0,
                        IConvertible convertible => convertible.ToDecimal(CultureInfo.InvariantCulture) != 0,
                        _ => false,
                    };
                }
                else if (targetType == typeof(string))
                {
                    converted = Convert.ToString(value, CultureInfo.InvariantCulture);
                }
                else
                {
                    converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                }

                result = (T)converted;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ===== Plugin Health Monitoring =====

        public PluginHealth GetPluginHealth(string name)
        {
            if (!_plugins.TryGetValue(name, out var info))
                return new PluginHealth { Status = "not_found" };

            return new PluginHealth
            {
                Name = name,
                Status = info.State.ToString().ToLower(),
                Uptime = info.State == PluginState.Running
                    ? DateTime.Now - info.LoadTime
                    : TimeSpan.Zero,
                CommandCount = info.CommandCount,
                ErrorCount = info.ErrorCount,
                MemoryEstimateMB = EstimateMemoryUsage(info),
                LastError = info.LastError,
            };
        }

        // ===== Yanshen-Compatible Tunnel Protocol Support =====

        /// <summary>
        /// Check if a string is a yanshen-compatible tunnel command.
        /// Format: !!!!commandID,param1,param2,...,paramN$
        /// </summary>
        public static bool IsTunnelCommand(string input) =>
            input != null && input.StartsWith("!!!!");

        /// <summary>
        /// 入口选择器 <c>sub_1005E4D0</c> 在截尾之前比的两条，走前缀比对。
        /// </summary>
        private static readonly string[] NativeEarlyPrefixNames = { "集成函数", "爱心分割" };

        /// <summary>
        /// 入口选择器在截尾之前比的两条，走**全等**比对（`sub_10043E20`）。
        /// </summary>
        private static readonly string[] NativeExactNames = { "hq取sj戳", "zd义回收" };

        /// <summary>
        /// 入口选择器在截尾之后比的四条，走前缀比对。
        /// </summary>
        private static readonly string[] NativeLatePrefixNames =
            { "给与元素", "获取元素", "定义伤害", "英雄极品" };

        /// <summary>
        /// 原生入口选择器认得的 6 个中文命令名（不含 <c>集成函数</c> / <c>爱心分割</c>）。
        /// </summary>
        internal static readonly string[] NativeChineseCommandNames =
            NativeLatePrefixNames.Concat(NativeExactNames).ToArray();

        /// <summary>
        /// 复刻 <c>sub_1005E4D0</c> 的前缀链：命中返回 true（隧道自己接管），
        /// 不命中返回 false（原生回落到宿主真正的 <c>GetBagItemCount</c>）。
        ///
        /// 底本 `yanshen208_strparam_runtime_dump_delayed_20260719`（基 0x57C40000，
        /// 减 0x47C40000 还原成 0x10000000 基）。链上一共 8 条比对，
        /// 与全镜像里仅有的 8 条 `!!!!` 字面量一一对应（`0x102BE81C`..`0x102BE8A4`，
        /// 每条只被选择器里的一处 push 引用）：
        ///
        ///   0x1005E5A0 集成函数 / 0x1005E613 爱心分割 —— `call 0x10064BD0`
        ///   0x1005E67A hq取sj戳 / 0x1005E6EF zd义回收 —— `call 0x10043E20`
        ///   0x1005E7C1 给与元素 / 0x1005EACB 获取元素 /
        ///   0x1005EDCF 定义伤害 / 0x1005EFA7 英雄极品 —— `call 0x10064BD0`
        ///
        /// 两个比对函数都尾调 `_Traits_compare` `0x10018E20`，语义不同：
        ///   `sub_10043E20` = `compare(const basic_string&)`（`ret 4`，两侧长度都进比较）
        ///     ⇒ **全等**；
        ///   `sub_10064BD0` = `compare(0, prefixLen, prefix)`（`ret 0xC`，
        ///     `0x10064BE5 cmp [ecx+0x10],edx` / `cmovb` 把长度夹成 `min(自身长, prefixLen)`，
        ///     第一个实参 off 编译期已知为 0、函数体里没用）⇒ **前缀**。
        ///
        /// `0x1005E737` 在 `zd义回收` 之后、`给与元素` 之前把命令串砍掉最后 1 字节
        /// （`mov ecx,[ebp-0x1c]` / `dec ecx` / `mov byte [eax+ecx],0`），所以后四条
        /// 前缀比对看到的是短一个字节的串：串正好等于 12 字节前缀时反而比不中。
        /// 前四条在截尾之前，看到的是原串。
        ///
        /// 每道门前的 `cmp [cfg+disp],0x1F4` 开关不在这里建模：关掉的开关在原生那边
        /// 同样落到 -1656 回落，但本仓对「已登记命令、开关缺失」一贯 fail-closed 抛出，
        /// 本轮只处理前缀未命中，不动那条路径。
        /// </summary>
        /// <remarks>
        /// 链尾 `0x1005F1D6` → `0x1005F20F mov eax,0xFFFFF988` = **-1656**。
        /// 挂在宿主 `TPlayObject.GetBagItemCount` `0x007447C0` 上的钩子拿到 -1656 就
        /// 执行原函数：`0x58A05256 call 0x57C9F2D0`（选择器的 `ret 0xC` 包装
        /// `sub_1005F2D0`）→ `0x58A05264 cmp eax,0xFFFFF988` → `0x58BBAAF5 je 0x58DBA7B2`；
        /// `0x58DBA7B2` 是 `55 8B EC 33 C9` + `push [0x1031B9A4]` / `ret`，
        /// 即重放 `0x007447C0` 的前 5 字节再跳回保存下来的续址。
        /// 同一个 `0x58DBA7B2` 还是 `0x58E64C9E cmp dword [edx],0x21212121` /
        /// `0x58DB86BF jne` 的落点 —— 连 `!!!!` 都不打头的物品名走的也是它。
        /// 该钩子在 2.0.7 运行期转储里被直接观测到：
        /// `m2_yanshen_hooks.txt` `HOOK source=0x007447C0`。
        /// 插件的 170 条宿主挂钩目标表（`0x102B22F4` 那条就是 `0x007447C0`）里，
        /// 只有它的开头是 `55 8B EC 33 C9`，重放字节唯一对得上。
        /// </remarks>
        public static bool IsNativeSelectorHit(string input)
        {
            if (!IsTunnelCommand(input)) return false;

            foreach (var name in NativeEarlyPrefixNames)
                if (input.StartsWith("!!!!" + name, StringComparison.Ordinal))
                    return true;

            foreach (var name in NativeExactNames)
                if (string.Equals(input, "!!!!" + name, StringComparison.Ordinal))
                    return true;

            foreach (var name in NativeLatePrefixNames)
            {
                var prefix = "!!!!" + name;
                // 截尾少掉的那 1 字节必须落在前缀之外，否则 `min(自身长, prefixLen)`
                // 夹出来比 prefixLen 短，`_Traits_compare` 拿长度差判负 —— 比不中。
                if (input.Length > prefix.Length
                    && input.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Parse a tunnel command string into command ID and parameters.
        /// Formats supported:
        ///   !!!!集成函数,commandID,param1,param2,...,paramN$
        ///   !!!!commandID,param1,param2,...,paramN$ (legacy compatibility)
        ///   !!!!命令名 参数1:参数2:参数3:
        ///   !!!!分隔符^commandID^param1^param2^...$
        ///   itemName!!!!#ys,ys1,...,ys17$
        ///   itemName!!!!ys1|ys2|ys3|ys4|ys5|
        ///   #$$#command params
        /// </summary>
        public static TunnelCommand ParseTunnelCommand(string input)
        {
            if (!IsTunnelCommand(input))
            {
                // Check for item-embedded format
                var idx = input?.IndexOf("!!!!") ?? -1;
                if (idx > 0)
                {
                    return new TunnelCommand
                    {
                        Format = TunnelFormat.ItemGiveNew,
                        ItemName = input.Substring(0, idx),
                        RawPayload = input.Substring(idx + 4),
                    };
                }
                return null;
            }

            var cmd = new TunnelCommand { Format = TunnelFormat.Standard };
            var payload = input.Substring(4); // strip "!!!!"

            // Detect format
            if (payload.StartsWith("#")) // !!!!#ys...
            {
                cmd.Format = TunnelFormat.ItemGiveExt;
                cmd.RawPayload = payload;
            }
            else if (payload.Contains("^")) // !!!!爱心分割^1^...
            {
                cmd.Format = TunnelFormat.CaretSeparated;
                var parts = payload.TrimEnd('$').Split('^');
                cmd.TokenCount = parts.Length;
                if (parts.Length >= 2 && int.TryParse(parts[1], out var caretId))
                {
                    cmd.CommandId = caretId;
                    cmd.Parameters = parts.Skip(2).ToArray();
                }
            }
            else if (payload.Contains(","))
            {
                cmd.Format = TunnelFormat.NumericId;
                var clean = payload.TrimEnd('$');
                var parts = clean.Split(',');
                cmd.TokenCount = parts.Length;
                var idIndex = string.Equals(parts[0], "集成函数", StringComparison.Ordinal) ? 1 : 0;
                if (parts.Length > idIndex && int.TryParse(parts[idIndex], out var numId))
                {
                    cmd.CommandId = numId;
                    cmd.Parameters = parts.Skip(idIndex + 1).ToArray();
                }
            }
            else // !!!!命令名参数:参数:
            {
                cmd.Format = TunnelFormat.ChineseName;
                // 原生认得的中文命令名恰好 6 个。两份运行期转储全镜像扫下来，
                // NUL 结尾且以 `!!!!` 打头的 GBK 串一共只有 8 条，每条只被入口
                // 选择器里的一处 push 引用：
                //   2.0.8 0x102BE81C..0x102BE8A4 → push@0x1005E578/E58C/E65C/E6D1/
                //         E794/E7A5/EDB3/EF8B（选择器 sub_1005E4D0）
                //   2.0.7 0x102AA8E8..0x102AA95C → push@0x10051F68/1F7C/204C/20C1/
                //         2173/2184/2792/296A
                // 除去 `!!!!集成函数` 与 `!!!!爱心分割`，剩下的就是下面这 6 个。
                // 曾经列在这里的 `plus伤害` 两版都 0 命中（ascii/GBK/UTF-16LE/
                // UTF-8/Big5，含 Plus/PLUS 变体），不是原生命令名 —— AllFuc.pas 的
                // ys_MyJn_plus 发的串在原生那边会落到宿主真正的 GetBagItemCount，
                // 那条回落由 IsNativeSelectorHit 挡在隧道之外，见其注释。
                var commandName = NativeChineseCommandNames
                    .FirstOrDefault(x => payload.StartsWith(x, StringComparison.Ordinal));
                if (commandName != null)
                {
                    cmd.ChineseCommand = commandName;
                    var paramStr = payload.Substring(commandName.Length).TrimEnd('$').TrimStart();
                    cmd.Parameters = paramStr.Split(':', StringSplitOptions.RemoveEmptyEntries);
                }
                else
                {
                    var spaceIdx = payload.IndexOf(' ');
                    cmd.ChineseCommand = spaceIdx > 0 ? payload.Substring(0, spaceIdx) : payload.TrimEnd('$');
                    if (spaceIdx > 0)
                        cmd.Parameters = payload.Substring(spaceIdx + 1).TrimEnd('$')
                            .Split(':', StringSplitOptions.RemoveEmptyEntries);
                }
            }

            cmd.RawPayload = payload;
            return cmd;
        }

        // ===== Private Helpers =====

        private void InitializePlugin(PluginInfo info)
        {
            if (string.Equals(info.Name, "YanshenCompat", StringComparison.OrdinalIgnoreCase))
            {
                LoadAllMyJsonConfigs();
                return;
            }

            switch (info.Type)
            {
                case PluginType.Native:
                    // Load native plugin DLL if path specified
                    if (!string.IsNullOrEmpty(info.DllPath) && File.Exists(info.DllPath))
                    {
                        // LoadLibrary equivalent — actual DLL loading
                        // Would use NativeLibrary.Load for .NET or P/Invoke
                    }
                    break;
                case PluginType.Managed:
                    // Load managed (.NET) plugin assembly
                    if (!string.IsNullOrEmpty(info.AssemblyPath) && File.Exists(info.AssemblyPath))
                    {
                        // Assembly.LoadFrom equivalent
                    }
                    break;
                case PluginType.Script:
                    // Script-based plugin — loaded by PasScriptHost
                    break;
            }
        }

        private void ShutdownPlugin(PluginInfo info)
        {
            info.CommandCount = 0;
            info.ErrorCount = 0;
        }

        private long EstimateMemoryUsage(PluginInfo info)
        {
            // Rough estimate based on configuration size and command count
            return (info.Config?.Length ?? 0) + info.CommandCount * 1024;
        }

        /// <summary>
        /// Register the built-in yanshen-compatible command engine as a native plugin.
        /// Called during server startup.
        /// </summary>
        public void RegisterBuiltinPlugins()
        {
            RegisterPlugin(
                name: "YanshenCompat",
                description: "眼神插件兼容引擎 — 原生实现眼神插件的41+个脚本扩展命令",
                version: "2.0.7",
                type: PluginType.Native
            );

            RegisterPlugin(
                name: "PASEngine",
                description: "Pascal脚本引擎 — 编译并执行 .pas 脚本文件",
                version: "1.0.0",
                type: PluginType.Native
            );

            // All script execution uses PasEngine
        }
    }

    /// <summary>
    /// Settlement rule for one 回收类型. A field that the document omits keeps its
    /// Has* flag false, which is the author's documented "省略" behaviour, not a zero rule.
    /// </summary>
    internal sealed class RecycleRule
    {
        internal string TypeName;

        internal bool HasMasterSwitch;
        internal int MasterSwitchGroup;
        internal int MasterSwitchIndex;
        internal int MasterSwitchClosedValue;

        internal bool HasRate;
        internal int RateGroup;
        internal int RateIndex;

        /// <summary>极品开关 variable group; 0 means the document omitted the switch.</summary>
        internal int ExtremeGroup;

        /// <summary>元素开关 variable group; 0 means the document omitted the switch.</summary>
        internal int ElementGroup;

        internal int Yuanbao;
        internal int Gold;
        internal int LingFu;
        internal int Exp;

        /// <summary>
        /// 这一条类型规则里到底写没写 <c>经验</c> 键。四个标量单价里只有 经验 需要区分
        /// 「写了 0」和「没写」—— 因为原生的 经验 单价槽 <c>[ebp-0x78]</c> 跨物品泄漏
        /// （见 YanshenApi.RecycleOne）。元宝/灵符/金币 的槽每件都重置
        /// （0x1006B2A5 / 0x1006B2AC / 0x1006B2B6），缺键与写 0 同义，不需要这个标志。
        /// </summary>
        internal bool HasExp;

        internal bool HasOther;
        internal int OtherGroup;
        internal int OtherIndex;
        internal int OtherValue;
    }

    internal readonly struct RecycleItemRule
    {
        internal RecycleItemRule(RecycleRule rule, bool stackable)
        {
            Rule = rule;
            Stackable = stackable;
        }

        internal RecycleRule Rule { get; }

        /// <summary>Item came from 可叠材料, which the author documents as skipping 极品/元素.</summary>
        internal bool Stackable { get; }
    }

    internal sealed class RecycleConfigSnapshot
    {
        private readonly Dictionary<string, RecycleItemRule> _items;

        internal RecycleConfigSnapshot(
            Dictionary<string, RecycleItemRule> items, IReadOnlyList<string> unresolvedItems = null)
        {
            _items = items ?? new Dictionary<string, RecycleItemRule>(StringComparer.Ordinal);
            UnresolvedItems = unresolvedItems ?? Array.Empty<string>();
        }

        internal int ItemCount => _items.Count;

        /// <summary>Items naming a 回收类型 the document never defines. They are never recycled.</summary>
        internal IReadOnlyList<string> UnresolvedItems { get; }

        internal bool ContainsItem(string itemName) =>
            !string.IsNullOrEmpty(itemName) && _items.ContainsKey(itemName);

        /// <summary>
        /// Resolves the settlement rule for an item. A configured name without a rule
        /// reports false so that no payout-less deletion can happen.
        /// </summary>
        internal bool TryGetItemRule(string itemName, out RecycleRule rule, out bool stackable)
        {
            rule = null;
            stackable = false;
            if (string.IsNullOrEmpty(itemName) || !_items.TryGetValue(itemName, out var entry))
                return false;
            rule = entry.Rule;
            stackable = entry.Stackable;
            return rule != null;
        }
    }

    // ===== Supporting Types =====

    public enum PluginType { Native, Managed, Script }
    public enum PluginState { Registered, Loading, Running, Unloading, Error }

    public enum TunnelFormat
    {
        Standard,       // !!!!commandID,params...$
        NumericId,      // !!!!1,param1,param2$
        ChineseName,    // !!!!命令名 参数:参数:
        CaretSeparated, // !!!!分隔符^1^param^param$
        ItemGiveNew,    // itemName!!!!#ys,ys...$
        ItemGiveOld,    // itemName!!!!ys1|ys2|...|
        ItemGiveExt,    // itemName!!!!#ys... extended
    }

    public class PluginInfo
    {
        private int _initialized;

        public string Name { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public PluginType Type { get; set; }
        public PluginState State { get; set; }
        public string DllPath { get; set; }
        public string AssemblyPath { get; set; }
        public string ConfigPath { get; set; }
        public string Config { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new();
        public DateTime LoadTime { get; set; }
        public long CommandCount { get; set; }
        public long ErrorCount { get; set; }
        public string LastError { get; set; }
        public bool IsInitialized
        {
            get => Volatile.Read(ref _initialized) != 0;
            set => Volatile.Write(ref _initialized, value ? 1 : 0);
        }

        internal object InitializationSync { get; } = new();
    }

    public class PluginHealth
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public TimeSpan Uptime { get; set; }
        public long CommandCount { get; set; }
        public long ErrorCount { get; set; }
        public long MemoryEstimateMB { get; set; }
        public string LastError { get; set; }
    }

    public class FeatureToggle
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Category { get; set; }
        public bool On { get; set; }
    }

    public class TunnelLogEntry
    {
        public DateTime Time { get; set; }
        public int CommandId { get; set; }
        public string ChineseCommand { get; set; }
        public string RawPayload { get; set; }
        public int Result { get; set; }
    }

    public class TunnelCommand
    {
        public TunnelFormat Format { get; set; }
        public int CommandId { get; set; }
        public string ChineseCommand { get; set; }
        public string ItemName { get; set; }
        public string[] Parameters { get; set; } = Array.Empty<string>();
        public string RawPayload { get; set; }

        /// <summary>
        /// 原生切分出来的段数（含 <c>!!!!集成函数</c> 前缀段与操作码段），即
        /// <c>sub_100761A0</c> / <c>sub_1005DBA0</c> 里 <c>(end-begin)/24</c> 算出的
        /// <c>std::vector&lt;std::string&gt;</c> 元素数。两个派发器都拿它先做一次
        /// 「至少 2 段」检查，实现体再各自做一次自己的下限检查。
        /// </summary>
        public int TokenCount { get; set; }
    }
}

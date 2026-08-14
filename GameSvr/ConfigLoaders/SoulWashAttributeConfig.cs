using System;
using System.Collections.Generic;
using System.IO;
using SystemModule;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 灵魂洗属性配置加载器 (Soul Wash / 神佑属性 System)
    ///
    /// Native: sub_755350 @0x755350 loads Config\神佑属性.txt into [[0x7D6014]] table.
    /// Each non-comment line with '=' format: Name=ID|BaseValue|Param3
    /// Parsed into 0x2B-byte (43-byte) native records:
    ///   +0x00  int           ID (slot identifier)
    ///   +0x04  int           BaseValue (used by 0x747B38 base calculation)
    ///   +0x08  int           Param3 (additional parameter)
    ///   +0x0C  ShortString   Name (max 0x1E bytes in GBK encoding)
    ///
    /// Native slot cap [[0x7D5AEC]] set to 4 at 0x7553F9.
    /// Base formula (0x747B38): sum [entry+0x04] for each non-zero slot.
    /// </summary>
    public class SoulWashAttributeConfig
    {
        /// <summary>Default configuration file path relative to base directory.</summary>
        public const string DefaultConfigPath = @"Share\config\神佑属性.txt";

        /// <summary>Native record size (0x2B bytes = 43 bytes).</summary>
        public const int NativeRecordSize = 0x2B;

        /// <summary>Native max slots cap ([[0x7D5AEC]] = 4 at 0x7553F9).</summary>
        public const int NativeMaxSlots = 4;

        /// <summary>Max allowed entries (safety cap: NativeMaxSlots * 64).</summary>
        private const int MaxEntries = NativeMaxSlots * 64;

        /// <summary>Attribute entry dictionary keyed by ID.</summary>
        private readonly Dictionary<int, SoulWashAttributeEntry> _entries;

        /// <summary>Total loaded entry count.</summary>
        public int Count => _entries.Count;

        public SoulWashAttributeConfig()
        {
            _entries = new Dictionary<int, SoulWashAttributeEntry>();
        }

        /// <summary>
        /// Load configuration from specified file path.
        /// Returns true on success, false on failure (with error message output).
        /// </summary>
        public bool LoadConfig(string filePath)
        {
            _entries.Clear();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                M2Share.ErrorMessage("[错误] 灵魂洗属性配置文件路径为空");
                return false;
            }

            if (!File.Exists(filePath))
            {
                M2Share.ErrorMessage($"[错误] 灵魂洗属性配置文件不存在: {filePath}");
                return false;
            }

            try
            {
                var lines = File.ReadAllLines(filePath, HUtil32.GbkEncoding);
                var lineNumber = 0;

                foreach (var rawLine in lines)
                {
                    lineNumber++;
                    var line = rawLine?.Trim();

                    // Skip empty lines and comments
                    if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '/')
                    {
                        continue;
                    }

                    if (!TryParseLine(line, out var entry, out var error))
                    {
                        M2Share.ErrorMessage($"[错误] 灵魂洗属性配置解析失败 (行 {lineNumber}): {error}");
                        return false;
                    }

                    // Check for duplicate IDs
                    if (_entries.ContainsKey(entry.Id))
                    {
                        M2Share.ErrorMessage($"[错误] 灵魂洗属性配置重复ID: {entry.Id} (行 {lineNumber})");
                        return false;
                    }

                    _entries[entry.Id] = entry;

                    // Safety cap: prevent loading excessive entries
                    if (_entries.Count >= MaxEntries)
                    {
                        M2Share.MainOutMessage($"[警告] 灵魂洗属性配置达到上限 {MaxEntries}，停止加载");
                        break;
                    }
                }

                M2Share.MainOutMessage($"[配置] 成功加载灵魂洗属性配置: {_entries.Count} 条记录");
                return true;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"[错误] 加载灵魂洗属性配置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Try to get attribute entry by ID.
        /// </summary>
        public bool TryGetEntry(int id, out SoulWashAttributeEntry entry)
        {
            return _entries.TryGetValue(id, out entry);
        }

        /// <summary>
        /// Compute base value sum from slot array (native 0x747B38).
        /// Sums [entry+0x04] for each non-zero slot ID.
        /// Returns -1 if any non-zero slot ID is not found in the table.
        /// </summary>
        public int ComputeBaseValueFromSlots(ReadOnlySpan<ushort> slotIds)
        {
            var total = 0;

            for (var i = 0; i < slotIds.Length; i++)
            {
                var id = slotIds[i];
                if (id == 0)
                {
                    continue; // Skip empty slots
                }

                if (!_entries.TryGetValue(id, out var entry))
                {
                    return -1; // Slot ID not found in config -> fail
                }

                total += entry.BaseValue;
            }

            return total;
        }

        /// <summary>
        /// Parse a single config line in format: Name=ID|BaseValue|Param3
        /// Example: 攻击力=1|10|0
        /// </summary>
        private static bool TryParseLine(string line, out SoulWashAttributeEntry entry, out string error)
        {
            entry = null;
            error = string.Empty;

            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0)
            {
                error = "缺少 '=' 分隔符";
                return false;
            }

            var name = line.Substring(0, eqIndex).Trim();
            var valuesPart = line.Substring(eqIndex + 1);
            var parts = valuesPart.Split('|');

            if (parts.Length < 3)
            {
                error = "格式错误，需要: Name=ID|BaseValue|Param3";
                return false;
            }

            // Parse numeric fields
            if (!int.TryParse(parts[0].Trim(), out var id))
            {
                error = "ID 字段无效";
                return false;
            }

            if (!int.TryParse(parts[1].Trim(), out var baseValue))
            {
                error = "BaseValue 字段无效";
                return false;
            }

            if (!int.TryParse(parts[2].Trim(), out var param3))
            {
                error = "Param3 字段无效";
                return false;
            }

            // Validate name length (max 0x1E bytes in GBK encoding)
            if (string.IsNullOrEmpty(name))
            {
                name = id.ToString(); // Fallback to ID as name
            }
            else
            {
                var nameBytes = HUtil32.GbkEncoding.GetBytes(name);
                if (nameBytes.Length > 0x1E)
                {
                    error = $"名称过长 (>{0x1E} 字节): {name}";
                    return false;
                }
            }

            entry = new SoulWashAttributeEntry
            {
                Id = id,
                BaseValue = baseValue,
                Param3 = param3,
                Name = name
            };

            return true;
        }
    }

    /// <summary>
    /// Soul Wash attribute entry (maps to native 0x2B-byte record).
    /// </summary>
    public class SoulWashAttributeEntry
    {
        /// <summary>+0x00: Slot identifier.</summary>
        public int Id { get; init; }

        /// <summary>+0x04: Base value (used in 0x747B38 calculation).</summary>
        public int BaseValue { get; init; }

        /// <summary>+0x08: Additional parameter.</summary>
        public int Param3 { get; init; }

        /// <summary>+0x0C: Attribute name (max 0x1E bytes GBK encoded ShortString).</summary>
        public string Name { get; init; }
    }
}

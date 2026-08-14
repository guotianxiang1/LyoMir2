using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Loader for Config\神佑属性.txt — native sub_755350 @0x755350.
    /// Each non-comment line with '=' parses into a 0x2B-byte record:
    ///   +0x00 int, +0x04 int (base value for 0x747B38), +0x08 int,
    ///   +0x0C ShortString[0x1E] name.
    /// Records are stored in the singleton table [[0x7D6014]]; slot cap
    /// [[0x7D5AEC]] is set to 4 at 0x7553F9.
    /// </summary>
    public sealed class NativeShenYouAttributeEntry
    {
        public int Id { get; init; }
        public int BaseValue { get; init; }
        public int Param3 { get; init; }
        public string Name { get; init; }
    }

    public sealed class NativeShenYouAttributeConfig
    {
        public const string ConfigRelativePath = @"Share\config\神佑属性.txt";
        public const int NativeRecordSize = 0x2B;
        public const int NativeMaxSlots = 4;

        private static readonly NativeShenYouAttributeConfig _shared =
            new NativeShenYouAttributeConfig();

        public static NativeShenYouAttributeConfig Shared => _shared;

        private readonly Dictionary<int, NativeShenYouAttributeEntry> _byId =
            new Dictionary<int, NativeShenYouAttributeEntry>();

        public int Count => _byId.Count;

        public static string ResolveDefaultPath(string rootPath, string baseDir)
        {
            return Path.Combine(rootPath ?? string.Empty, baseDir ?? string.Empty,
                "config", "神佑属性.txt");
        }

        public bool TryGet(int id, out NativeShenYouAttributeEntry entry)
            => _byId.TryGetValue(id, out entry);

        /// <summary>0x747B38 — sum [entry+4] for each non-zero slot word.</summary>
        public int ComputeBaseFromSlots(ReadOnlySpan<ushort> slotIds)
        {
            var total = 0;
            for (var i = 0; i < slotIds.Length; i++)
            {
                var id = slotIds[i];
                if (id == 0)
                    continue;
                if (!_byId.TryGetValue(id, out var entry))
                    return -1;
                total += entry.BaseValue;
            }
            return total;
        }

        public bool Reload(string fileName, out string error)
        {
            error = string.Empty;
            _byId.Clear();

            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "[Error]:神佑属性文件不存在！！";
                M2Share.ErrorMessage(error);
                return false;
            }

            if (!File.Exists(fileName))
            {
                error = "[Error]:神佑属性文件不存在！！";
                M2Share.ErrorMessage(error);
                return false;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(fileName, HUtil32.GbkEncoding);
            }
            catch (Exception ex)
            {
                error = "[Error]:神佑属性文件加载错误: " + ex.Message;
                M2Share.ErrorMessage(error);
                return false;
            }

            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;
                if (line[0] == ';' || line[0] == '/')
                    continue;

                if (!TryParseLine(line, out var entry, out var lineError))
                {
                    error = "[Error]:神佑属性文件加载错误: " + lineError;
                    M2Share.ErrorMessage(error);
                    return false;
                }

                if (_byId.ContainsKey(entry.Id))
                {
                    error = "[Error]:神佑属性文件加载错误: duplicate id " + entry.Id;
                    M2Share.ErrorMessage(error);
                    return false;
                }

                _byId[entry.Id] = entry;
                if (_byId.Count > NativeMaxSlots * 64)
                    break;
            }

            return true;
        }

        private static bool TryParseLine(string line,
            out NativeShenYouAttributeEntry entry, out string error)
        {
            entry = null;
            error = string.Empty;

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                error = "missing '=' in: " + line;
                return false;
            }

            var name = line.Substring(0, eq).Trim();
            var rest = line.Substring(eq + 1);
            var parts = rest.Split('|');
            if (parts.Length < 3)
            {
                error = "need id|base|param: " + line;
                return false;
            }

            if (!int.TryParse(parts[0].Trim(), out var id)
                || !int.TryParse(parts[1].Trim(), out var baseValue)
                || !int.TryParse(parts[2].Trim(), out var param3))
            {
                error = "bad numeric fields: " + line;
                return false;
            }

            if (string.IsNullOrEmpty(name))
                name = id.ToString();

            var nameBytes = HUtil32.GbkEncoding.GetBytes(name);
            if (nameBytes.Length > 0x1E)
            {
                error = "name too long: " + name;
                return false;
            }

            entry = new NativeShenYouAttributeEntry
            {
                Id = id,
                BaseValue = baseValue,
                Param3 = param3,
                Name = name
            };
            return true;
        }
    }
}

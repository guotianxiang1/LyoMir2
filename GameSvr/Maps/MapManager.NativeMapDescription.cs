using System.Buffers.Binary;
using System.Globalization;
using SystemModule;

namespace GameSvr
{
    public partial class MapManager
    {
        internal const int NativeMapDescriptionRecordSize = 24;
        internal const int NativeMapDescriptionLabelCapacity = 14;

        private static readonly char[] NativeMapDescriptionSeparators =
            { ' ', '\t', ',', ';' };
        private static readonly char[] NativeMapAreaSeparators =
            { ' ', '\t' };

        private readonly Dictionary<string, List<byte[]>>
            _nativeMapDescriptionRecords = new(StringComparer.Ordinal);

        internal int NativeMapAreaRegionCount { get; private set; }
        internal int NativeMapDescriptionKeyCount =>
            _nativeMapDescriptionRecords.Count;
        internal int NativeMapDescriptionRecordCount { get; private set; }
        internal int NativeMapDescriptionSkippedRowCount { get; private set; }

        internal bool TryLoadNativeMapAreas(string fileName, out string error)
        {
            error = string.Empty;
            try
            {
                if (!File.Exists(fileName))
                {
                    ResetNativeMapAreas();
                    error = "file not found: " + fileName;
                    return false;
                }

                LoadNativeMapAreasFromLines(File.ReadLines(fileName,
                    HUtil32.GbkEncoding));
                return true;
            }
            catch (Exception exception)
            {
                ResetNativeMapAreas();
                error = exception.Message;
                return false;
            }
        }

        internal bool TryLoadNativeMapDescriptions(string fileName,
            out string error)
        {
            error = string.Empty;
            try
            {
                if (!File.Exists(fileName))
                {
                    ResetNativeMapDescriptions();
                    error = "file not found: " + fileName;
                    return false;
                }

                LoadNativeMapDescriptionsFromLines(File.ReadLines(fileName,
                    HUtil32.GbkEncoding));
                return true;
            }
            catch (Exception exception)
            {
                ResetNativeMapDescriptions();
                error = exception.Message;
                return false;
            }
        }

        internal void LoadNativeMapAreasFromLines(IEnumerable<string> lines)
        {
            ResetNativeMapAreas();
            if (lines == null)
                return;

            foreach (var sourceLine in lines)
            {
                var line = sourceLine?.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';')
                    continue;

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                var environment = FindMap(line.Substring(0, equalsIndex).Trim());
                if (environment == null)
                    continue;

                var sections = line.Substring(equalsIndex + 1).Split(';');
                for (var sectionIndex = 0;
                     sectionIndex < sections.Length; sectionIndex++)
                {
                    var fields = sections[sectionIndex].Split(
                        NativeMapAreaSeparators,
                        StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 4 ||
                        !int.TryParse(fields[1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var x) ||
                        !int.TryParse(fields[2], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var y) ||
                        !int.TryParse(fields[3], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var radius) ||
                        string.IsNullOrEmpty(fields[0]) ||
                        x <= 0 || y <= 0 || radius <= 0)
                    {
                        continue;
                    }

                    environment.PrependNativeMapAreaRegion(fields[0],
                        unchecked((ushort)x), unchecked((ushort)y),
                        unchecked((ushort)radius));
                    NativeMapAreaRegionCount++;
                }
            }
        }

        internal void LoadNativeMapDescriptionsFromLines(
            IEnumerable<string> lines)
        {
            ResetNativeMapDescriptions();
            if (lines == null)
                return;

            foreach (var sourceLine in lines)
            {
                var line = sourceLine?.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';')
                    continue;

                var fields = line.Split(NativeMapDescriptionSeparators,
                    StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 6)
                {
                    NativeMapDescriptionSkippedRowCount++;
                    continue;
                }

                var x = ParseNativeDecimal(fields[1]);
                var y = ParseNativeDecimal(fields[2]);
                var color = ParseNativeHexColor(fields[4]);
                var type = ParseNativeDecimal(fields[5]);
                var record = EncodeNativeMapDescriptionRecord(fields[3],
                    type, x, y, color);

                if (!_nativeMapDescriptionRecords.TryGetValue(fields[0],
                        out var records))
                {
                    records = new List<byte[]>();
                    _nativeMapDescriptionRecords.Add(fields[0], records);
                }
                records.Add(record);
                NativeMapDescriptionRecordCount++;
            }
        }

        internal IReadOnlyList<byte[]> GetNativeMapDescriptionRecords(
            string mapDescription)
        {
            if (!string.IsNullOrEmpty(mapDescription) &&
                _nativeMapDescriptionRecords.TryGetValue(mapDescription,
                    out var records))
            {
                return records;
            }
            return Array.Empty<byte[]>();
        }

        internal static byte[] EncodeNativeMapDescriptionRecord(string label,
            int type, int x, int y, uint color)
        {
            var wire = new byte[NativeMapDescriptionRecordSize];
            var labelBytes = HUtil32.GbkEncoding.GetBytes(label ?? string.Empty);
            var labelLength = Math.Min(labelBytes.Length,
                NativeMapDescriptionLabelCapacity);
            wire[0] = unchecked((byte)labelLength);
            labelBytes.AsSpan(0, labelLength).CopyTo(wire.AsSpan(1,
                NativeMapDescriptionLabelCapacity));
            wire[15] = unchecked((byte)type);
            BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(16, 2),
                unchecked((ushort)x));
            BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(18, 2),
                unchecked((ushort)y));
            BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(20, 4), color);
            return wire;
        }

        private void ResetNativeMapAreas()
        {
            foreach (var environment in m_MapList.Values)
                environment.ClearNativeMapAreaRegions();
            NativeMapAreaRegionCount = 0;
        }

        private void ResetNativeMapDescriptions()
        {
            _nativeMapDescriptionRecords.Clear();
            NativeMapDescriptionRecordCount = 0;
            NativeMapDescriptionSkippedRowCount = 0;
        }

        private static int ParseNativeDecimal(string value)
        {
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }

        private static uint ParseNativeHexColor(string value)
        {
            if (!string.IsNullOrEmpty(value) && value[0] == '$')
                value = value.Substring(1);
            return uint.TryParse(value, NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }
    }
}

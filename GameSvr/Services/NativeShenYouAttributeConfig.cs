using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// One row of Config\神佑属性.txt exactly as native sub_755350 @0x755350 lays it
    /// out in its GetMem(0x2B) block:
    ///   +0x00 int32 Id      (0x7554BA)
    ///   +0x04 int32 Base    (0x7554E2)
    ///   +0x08 int32 Param   (0x75550B)
    ///   +0x0C ShortString, capacity 0x1E (0x75552C, `mov cl,0x1E` then 0x4039E4)
    /// which is 12 + 1 + 30 = 0x2B bytes.
    /// </summary>
    public sealed class NativeShenYouAttributeEntry
    {
        public int Id { get; init; }
        public int BaseValue { get; init; }
        public int Param3 { get; init; }
        public string Name { get; init; }

        /// <summary>
        /// The GBK bytes 0x4039E4 actually stores, i.e. after the 0x1E cap. The cap
        /// counts bytes, so it can cut a double-byte character in half; keeping the
        /// bytes rather than the string is what makes the wire body reproducible.
        /// </summary>
        public byte[] NameBytes { get; init; }
    }

    /// <summary>
    /// Loader for Config\神佑属性.txt — native sub_755350 @0x755350, reached from the
    /// GM reload arm @0x628A72 and from 0x74C1D5.
    ///
    /// Line handling, in native order:
    ///   0x755428  empty line                      -> skip
    ///   0x755435  first char ';'                  -> skip
    ///   0x755441  first char '/'                  -> skip
    ///   0x75544D  first char '='                  -> slot-cap directive
    ///   otherwise                                 -> Id|Base|Param|Name record
    ///
    /// The rows live in the hash list at [[0x7D6014]], whose Add (0x49EC5C) appends to
    /// a tail-linked chain, so First/Next (0x49EE4C/0x49EE54) walk them in file order.
    /// That order is what CM 4125 puts on the wire, which is why this class keeps an
    /// ordered list and not just the id lookup.
    /// </summary>
    public sealed class NativeShenYouAttributeConfig
    {
        public const string ConfigRelativePath = @"Share\config\神佑属性.txt";
        public const int NativeRecordSize = 0x2B;
        public const int NativeNameCapacity = 0x1E;
        public const int NativeDefaultSlotCap = 4;

        public const string MissingFileMessage = "[Error]:神佑属性文件不存在！！ ";
        public const string LoadErrorMessage = "[Error]:神佑属性文件加载错误";

        public const string AbilSwitchFileName = "Mir2Actor.ini";
        public const string AbilSwitchSection = "setup";
        public const string AbilSwitchKey = "ShenYouAbilSwitch";

        private static readonly NativeShenYouAttributeConfig _shared =
            new NativeShenYouAttributeConfig();

        public static NativeShenYouAttributeConfig Shared => _shared;

        private readonly List<NativeShenYouAttributeEntry> _rows =
            new List<NativeShenYouAttributeEntry>();

        private readonly Dictionary<int, NativeShenYouAttributeEntry> _byId =
            new Dictionary<int, NativeShenYouAttributeEntry>();

        public int Count => _rows.Count;

        public IReadOnlyList<NativeShenYouAttributeEntry> Rows => _rows;

        /// <summary>
        /// Native word[[0x7D5AEC]], read as a word at 0x746CF7.
        ///
        /// The slot is plain BSS, so it starts at 0, and the only write that can raise
        /// it to 4 (0x7553F9) sits behind the FileExists branch at 0x7553AC. A server
        /// whose config file never loaded therefore keeps 0: the runtime capture has
        /// 0x7DCF44 = 0 while the table object at 0x7DCF40 is already a live heap
        /// pointer. Initialising this to 4 would model a state native never reaches.
        /// </summary>
        public int SlotCap { get; private set; }

        /// <summary>Native byte[[0x7D6938]] — mir2Actor.ini [setup] ShenYouAbilSwitch.</summary>
        public bool AbilSwitch { get; private set; }

        public static string ResolveDefaultPath(string rootPath, string baseDir)
        {
            return Path.Combine(rootPath ?? string.Empty, baseDir ?? string.Empty,
                "config", "神佑属性.txt");
        }

        public bool TryGet(int id, out NativeShenYouAttributeEntry entry)
            => _byId.TryGetValue(id, out entry);

        /// <summary>
        /// 0x747B38 — sum table[+4] over the caller's slot words.
        ///
        /// Native takes the used-slot count from byte[self+0x5BC] (see the cap test at
        /// 0x7478D2) and looks every one of those words up, so a miss is fatal there.
        /// The managed caller hands over the whole fixed 10-word window instead, so the
        /// zero test below stands in for native's shorter loop bound; it is not a native
        /// skip. A non-zero id that is absent from the table is still fatal, as at
        /// 0x747BC8.
        /// </summary>
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

        /// <summary>
        /// The count*0x2B body CM 4125 hands to [vmt+0x254] at 0x746D18.
        ///
        /// Native builds it with GetMem(count*0x2B) at 0x746C5F and a 0x2B-byte Move per
        /// row, so every byte comes from the record block. The one byte range that is not
        /// reproducible is the tail of each name field: the record itself came from an
        /// uninitialised GetMem(0x2B) at 0x75548D and 0x4039E4 writes only the length byte
        /// and the characters, leaving whatever the allocator left behind. Zero is used
        /// here because heap residue has no defined value to copy.
        /// </summary>
        public byte[] BuildNativeRecordBuffer()
        {
            var buffer = new byte[_rows.Count * NativeRecordSize];
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var span = buffer.AsSpan(i * NativeRecordSize, NativeRecordSize);
                BinaryPrimitives.WriteInt32LittleEndian(span, row.Id);
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), row.BaseValue);
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), row.Param3);
                var name = row.NameBytes ?? Array.Empty<byte>();
                span[0x0C] = (byte)name.Length;
                name.CopyTo(span.Slice(0x0D));
            }
            return buffer;
        }

        /// <summary>
        /// Native sub_755350 end to end. The record load and the switch read are one
        /// unit there: 0x75557C (success), 0x7555BD (exception) and the missing-file
        /// fallthrough at 0x7555BF all converge on 0x7555E6, so the switch is refreshed
        /// on every outcome.
        /// </summary>
        public bool Reload(string fileName, string shareDirectory, out string error)
        {
            var loaded = ReloadRecords(fileName, out error);
            AbilSwitch = ReadAbilSwitch(shareDirectory);
            return loaded;
        }

        private bool ReloadRecords(string fileName, out string error)
        {
            error = string.Empty;

            // 0x7553A2 tests FileExists before the TStringList is even created, and the
            // table clear at 0x7553EF sits after LoadFromFile. A missing or unreadable
            // file therefore leaves the previously loaded rows and slot cap standing.
            if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
            {
                error = MissingFileMessage + (fileName ?? string.Empty);
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
                error = LoadErrorMessage + ex.Message;
                M2Share.ErrorMessage(error);
                return false;
            }

            // 0x7553EF clears the table and 0x7553F9 writes the cap, both only on this
            // leg. Neither runs when FileExists failed at 0x7553AC.
            _rows.Clear();
            _byId.Clear();
            SlotCap = NativeDefaultSlotCap;

            for (var i = 0; i < lines.Length; i++)
                ParseLine(lines[i]);

            return true;
        }

        private void ParseLine(string line)
        {
            // Native reads the TStringList entry as-is; there is no Trim anywhere in
            // 0x75541A..0x755547, so leading blanks make a line a record line.
            if (string.IsNullOrEmpty(line))
                return;

            var first = line[0];
            if (first == ';' || first == '/')
                return;

            if (first == '=')
            {
                // 0x755452 splits on '=' and converts the part BEFORE it. On a line that
                // starts with '=' that part is always empty, so 0x755476's StrToIntDef
                // always falls back to its default of 4. `=8` does not mean 8 natively.
                SlotCap = StrToIntDef(SplitFirst(line, '=', out _), NativeDefaultSlotCap);
                return;
            }

            var rest = line;
            var id = StrToIntDef(SplitFirst(rest, '|', out rest), 0);
            var baseValue = StrToIntDef(SplitFirst(rest, '|', out rest), 0);
            var param = StrToIntDef(SplitFirst(rest, '|', out rest), 0);
            var nameBytes = TruncateGbk(rest, NativeNameCapacity);

            // 0x755531 `test eax,eax` / `je 0x755547`: a row whose first field is zero is
            // never added (native simply leaks the block).
            if (id == 0)
                return;

            var entry = new NativeShenYouAttributeEntry
            {
                Id = id,
                BaseValue = baseValue,
                Param3 = param,
                Name = HUtil32.GbkEncoding.GetString(nameBytes),
                NameBytes = nameBytes
            };

            _rows.Add(entry);

            // 0x49EC5C pushes the new node onto the front of its bucket chain, so the
            // 0x49F0EC lookup finds the LAST row added for a duplicated id, while the
            // ordered walk still sees every row.
            _byId[id] = entry;
        }

        /// <summary>
        /// 0x4C6AEC — returns the text before the first divider (or the whole string when
        /// there is none) and leaves the text after it in <paramref name="remainder"/>.
        /// </summary>
        private static string SplitFirst(string source, char divider, out string remainder)
        {
            source ??= string.Empty;
            var at = source.IndexOf(divider);
            if (at < 0)
            {
                remainder = string.Empty;
                return source;
            }
            remainder = source.Substring(at + 1);
            return source.Substring(0, at);
        }

        /// <summary>
        /// Delphi StrToIntDef (0x40CA18): leading blanks are skipped, '$' or '0x' marks
        /// hex, and anything the scan cannot consume whole yields the default.
        /// </summary>
        internal static int StrToIntDef(string text, int defaultValue)
        {
            if (string.IsNullOrEmpty(text))
                return defaultValue;

            var i = 0;
            while (i < text.Length && text[i] == ' ')
                i++;
            if (i >= text.Length)
                return defaultValue;

            var negative = false;
            if (text[i] == '-' || text[i] == '+')
            {
                negative = text[i] == '-';
                i++;
            }

            var radix = 10;
            if (i < text.Length && text[i] == '$')
            {
                radix = 16;
                i++;
            }
            else if (i + 1 < text.Length && text[i] == '0'
                     && (text[i + 1] == 'x' || text[i + 1] == 'X'))
            {
                radix = 16;
                i += 2;
            }

            if (i >= text.Length)
                return defaultValue;

            long value = 0;
            for (; i < text.Length; i++)
            {
                var digit = DigitValue(text[i]);
                if (digit < 0 || digit >= radix)
                    return defaultValue;
                value = value * radix + digit;
                if (value > uint.MaxValue)
                    return defaultValue;
            }

            var signed = negative ? -value : value;
            if (signed < int.MinValue || signed > uint.MaxValue)
                return defaultValue;
            return unchecked((int)signed);
        }

        private static int DigitValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static byte[] TruncateGbk(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<byte>();
            var bytes = HUtil32.GbkEncoding.GetBytes(text);
            if (bytes.Length <= maxBytes)
                return bytes;
            var cut = new byte[maxBytes];
            Array.Copy(bytes, cut, maxBytes);
            return cut;
        }

        /// <summary>
        /// 0x7555E6 — reopen mir2Actor.ini through 0x790210 and take
        /// [setup]/ShenYouAbilSwitch with a default of False. Delphi's TIniFile.ReadBool
        /// is ReadInteger &lt;&gt; 0, so "1" and "2" are on while "TRUE" is not a number
        /// and falls back to the default.
        /// </summary>
        public static bool ReadAbilSwitch(string shareDirectory)
        {
            if (string.IsNullOrWhiteSpace(shareDirectory))
                return false;
            try
            {
                var ini = new ActorSetupIni(Path.Combine(shareDirectory, AbilSwitchFileName));
                var raw = ini.ReadRaw(AbilSwitchSection, AbilSwitchKey);
                return StrToIntDef(raw, 0) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private sealed class ActorSetupIni : IniFile
        {
            public ActorSetupIni(string fileName) : base(fileName)
            {
                // IniFile only caches on an explicit Load; the base constructor just
                // records the path. Load also creates a missing file, matching 0x790210.
                Load();
            }

            public string ReadRaw(string section, string key)
                => ReadString(section, key, string.Empty);
        }
    }
}

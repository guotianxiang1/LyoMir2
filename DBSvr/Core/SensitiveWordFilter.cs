using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SystemModule.Common;

namespace DBSvr.Core
{
    /// <summary>
    /// 敏感词过滤器。
    /// 对应 Delphi 原版 !AbUse.txt (93116 bytes) + !AbUseName.txt (57 bytes) + DenyChrName.txt。
    /// </summary>
    public class SensitiveWordFilter
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _abuseWords = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _denyNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly StringList _denyChrNameList = new();
        private readonly List<byte[]> _nativeHeroAbusePatterns = new();
        private readonly List<byte[]> _nativeHeroNamePatterns = new();
        private int _nativeHeroAbuseTimestamp = int.MinValue;
        private int _nativeHeroNameTimestamp = int.MinValue;
        private uint _nativeReloadTick;
        private readonly string _baseDirectory;
        private static readonly Encoding Gbk;

        static SensitiveWordFilter()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        public SensitiveWordFilter(string baseDirectory = null)
        {
            _baseDirectory = string.IsNullOrEmpty(baseDirectory)
                ? AppContext.BaseDirectory
                : Path.GetFullPath(baseDirectory);
        }

        public int AbuseWordCount { get { lock (_sync) return _abuseWords.Count; } }
        public int DenyNameCount { get { lock (_sync) return _denyNames.Count; } }

        /// <summary>
        /// 加载所有敏感词文件。
        /// </summary>
        public void Load()
        {
            lock (_sync)
            {
                _nativeReloadTick = unchecked((uint)Environment.TickCount);
                _abuseWords.Clear();
                _denyNames.Clear();
                _denyChrNameList.Clear();
                var abusePath = Path.Combine(_baseDirectory, "!AbUse.txt");
                var abuseNamePath = Path.Combine(_baseDirectory, "!AbUseName.txt");
                LoadFile(abusePath, _abuseWords);
                LoadFile(abuseNamePath, _denyNames);
                ReloadNativeHeroPatterns(abusePath,
                    _nativeHeroAbusePatterns, ref _nativeHeroAbuseTimestamp);
                ReloadNativeHeroPatterns(abuseNamePath,
                    _nativeHeroNamePatterns, ref _nativeHeroNameTimestamp);
                LoadDenyChrNameList(Path.Combine(
                    _baseDirectory, "DenyChrName.txt"));

                if (_abuseWords.Count == 0)
                    AddDefaults();

                DBShare.MainOutMessage($"[SensitiveFilter] 加载 {_abuseWords.Count} 敏感词 + {_denyNames.Count} 禁止名字 + {_denyChrNameList.Count} 黑名单");
            }
        }

        /// <summary>
        /// 检查角色名是否包含敏感词。
        /// </summary>
        public bool ContainsAbuseWord(string chrName)
        {
            if (string.IsNullOrEmpty(chrName)) return false;
            lock (_sync)
            {
                foreach (var word in _abuseWords)
                    if (chrName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                return false;
            }
        }

        /// <summary>
        /// 检查角色名是否为禁止名字。
        /// </summary>
        public bool IsDenyName(string chrName)
        {
            if (string.IsNullOrEmpty(chrName)) return false;
            lock (_sync) return _denyNames.Contains(chrName);
        }

        /// <summary>
        /// 检查角色名是否在黑名单中。
        /// </summary>
        public bool IsDenyChrName(string chrName)
        {
            lock (_sync)
            {
                for (int i = 0; i < _denyChrNameList.Count; i++)
                    if (string.Compare(chrName, _denyChrNameList[i], StringComparison.OrdinalIgnoreCase) == 0)
                        return true;
                return false;
            }
        }

        /// <summary>
        /// 综合校验: 名字合法性 + GBK范围 + 敏感词 + 黑名单。
        /// </summary>
        public (bool valid, int failCode) ValidateChrName(string chrName, bool englishNamesOnly)
        {
            if (string.IsNullOrEmpty(chrName) || chrName.Length < 2)
                return (false, 0); // 名字太短

            if (!NameValidator.ValidateChrName(chrName, englishNamesOnly))
                return (false, 0); // 非法字符

            if (IsDenyChrName(chrName))
                return (false, 2); // 黑名单

            if (IsDenyName(chrName))
                return (false, 2); // 禁止名字

            if (ContainsAbuseWord(chrName))
                return (false, 2); // 包含敏感词

            return (true, 0);
        }

        public bool ValidateNativeHeroName(string heroName)
        {
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(heroName ?? string.Empty);
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
            if (bytes.Length < 4 || bytes.Length > 14) return false;
            foreach (var value in bytes)
                if (IsForbiddenNativeHeroByte(value)) return false;

            var upper = AsciiUpper(bytes);
            if (StartsWith(upper, "GM0") || StartsWith(upper, "GMO")
                || StartsWith(upper, "GD0") || StartsWith(upper, "GDO"))
                return false;
            for (var offset = 0; offset < bytes.Length;)
            {
                var lead = bytes[offset];
                if (lead < 0x80)
                {
                    if (lead < 0x21 || lead > 0x7F) return false;
                    offset++;
                    continue;
                }
                if (offset + 1 >= bytes.Length
                    || !IsAllowedNativeHeroPair(lead, bytes[offset + 1]))
                    return false;
                offset += 2;
            }

            lock (_sync)
            {
                if (MatchesNativeHeroPattern(upper, _nativeHeroAbusePatterns)
                    || MatchesNativeHeroPattern(upper, _nativeHeroNamePatterns))
                    return false;
            }
            return true;
        }

        public void Reload()
        {
            var now = unchecked((uint)Environment.TickCount);
            lock (_sync)
            {
                if (!IsNativeReloadDue(now, _nativeReloadTick)) return;
                _nativeReloadTick = now;
                ReloadNativeHeroPatterns(
                    Path.Combine(_baseDirectory, "!AbUse.txt"),
                    _nativeHeroAbusePatterns, ref _nativeHeroAbuseTimestamp);
                ReloadNativeHeroPatterns(
                    Path.Combine(_baseDirectory, "!AbUseName.txt"),
                    _nativeHeroNamePatterns, ref _nativeHeroNameTimestamp);
            }
        }

        public static bool IsNativeReloadDue(uint now, uint previous) =>
            unchecked(now - previous) >= 30000u;

        private static void LoadFile(string fileName, HashSet<string> target)
        {
            try
            {
                if (!File.Exists(fileName)) return;
                foreach (var line in File.ReadAllLines(fileName, Encoding.GetEncoding("GBK")))
                {
                    var word = line.Trim();
                    if (!string.IsNullOrEmpty(word) && !word.StartsWith(";"))
                        target.Add(word);
                }
            }
            catch { }
        }

        private static void ReloadNativeHeroPatterns(string fileName,
            List<byte[]> target, ref int loadedTimestamp)
        {
            try
            {
                if (!File.Exists(fileName)) return;
                var timestamp = GetDosFileAge(fileName);
                if (timestamp == loadedTimestamp) return;
                var source = File.ReadAllBytes(fileName);
                var loaded = new List<byte[]>();
                var sourceLength = Array.IndexOf(source, (byte)0);
                if (sourceLength < 0) sourceLength = source.Length;
                var offset = 0;
                while (offset <= sourceLength)
                {
                    var end = offset;
                    while (end < sourceLength
                           && source[end] != (byte)'\r'
                           && source[end] != (byte)'\n')
                        end++;
                    var bytes = source.AsSpan(offset, end - offset).ToArray();
                    if (bytes.Length != 0 && bytes[0] != (byte)';')
                    {
                        if (bytes.Length > 15) Array.Resize(ref bytes, 15);
                        loaded.Add(AsciiUpper(bytes));
                    }

                    if (end == sourceLength) break;
                    if (source[end] == (byte)'\r' && end + 1 < sourceLength
                        && source[end + 1] == (byte)'\n')
                        end++;
                    offset = end + 1;
                }

                if (loaded.Count != 0)
                {
                    target.Clear();
                    target.AddRange(loaded);
                }
                loadedTimestamp = timestamp;
            }
            catch { }
        }

        private static int GetDosFileAge(string fileName)
        {
            var value = File.GetLastWriteTime(fileName);
            if (value.Year is < 1980 or > 2107) return -1;
            var year = value.Year - 1980;
            return year << 25
                   | value.Month << 21
                   | value.Day << 16
                   | value.Hour << 11
                   | value.Minute << 5
                   | value.Second / 2;
        }

        private static bool IsForbiddenNativeHeroByte(byte value)
            => value <= 0x20 || value is >= 0x22 and <= 0x25
                || value is >= 0x27 and <= 0x29
                || value is 0x2B or 0x2D or 0x2F or 0x3C or 0x3E or 0x5C;

        private static bool IsAllowedNativeHeroPair(byte lead, byte trail)
        {
            return lead switch
            {
                0xA1 => trail is >= 0xA2 and <= 0xFE,
                0xA2 => trail is >= 0xA1 and <= 0xAA
                        or >= 0xB1 and <= 0xE2
                        or >= 0xE5 and <= 0xEE
                        or >= 0xF1 and <= 0xFC,
                0xA3 => trail is >= 0xA1 and <= 0xFE,
                0xA4 => trail is >= 0xA1 and <= 0xF3,
                0xA5 => trail is >= 0xA1 and <= 0xF6,
                0xA6 => trail is >= 0xA1 and <= 0xB8
                        or >= 0xC1 and <= 0xD8
                        or >= 0xE0 and <= 0xEB
                        or >= 0xEE and <= 0xF2
                        or >= 0xF4 and <= 0xF5,
                0xA7 => trail is >= 0xA1 and <= 0xC1
                        or >= 0xD1 and <= 0xF1,
                0xA8 => trail is >= 0xA1 and <= 0xC0
                        or >= 0xC5 and <= 0xE9,
                0xA9 => trail is >= 0xA4 and <= 0xEF,
                >= 0xB0 and <= 0xD6 => trail is >= 0xA1 and <= 0xFE,
                0xD7 => trail is >= 0xA1 and <= 0xF9,
                >= 0xD8 and <= 0xF7 => trail is >= 0xA1 and <= 0xFE,
                _ => false
            };
        }

        private static byte[] AsciiUpper(byte[] source)
        {
            var result = (byte[])source.Clone();
            for (var i = 0; i < result.Length; i++)
                if (result[i] is >= (byte)'a' and <= (byte)'z') result[i] -= 0x20;
            return result;
        }

        private static bool StartsWith(byte[] value, string prefix)
        {
            if (value.Length < prefix.Length) return false;
            for (var i = 0; i < prefix.Length; i++)
                if (value[i] != (byte)prefix[i]) return false;
            return true;
        }

        private static bool MatchesNativeHeroPattern(byte[] value,
            List<byte[]> patterns)
        {
            foreach (var pattern in patterns)
            {
                var index = IndexOf(value, pattern);
                if (index >= 0 && !IsDbcsTrail(value, index)) return true;
            }
            return false;
        }

        private static int IndexOf(byte[] value, byte[] pattern)
        {
            if (pattern.Length == 0) return 0;
            for (var offset = 0; offset <= value.Length - pattern.Length; offset++)
            {
                var matches = true;
                for (var i = 0; i < pattern.Length; i++)
                {
                    if (value[offset + i] == pattern[i]) continue;
                    matches = false;
                    break;
                }
                if (matches) return offset;
            }
            return -1;
        }

        private static bool IsDbcsTrail(byte[] value, int index)
        {
            if (index <= 0) return false;
            var cursor = index - 1;
            while (cursor >= 0 && value[cursor] is >= 0x81 and <= 0xFE)
                cursor--;
            return ((index - cursor) & 1) == 0;
        }

        private void LoadDenyChrNameList(string fileName)
        {
            try
            {
                if (!File.Exists(fileName)) return;
                _denyChrNameList.LoadFromFile(fileName);
                int i = 0;
                while (i < _denyChrNameList.Count)
                {
                    if (string.IsNullOrEmpty(_denyChrNameList[i].Trim()))
                    { _denyChrNameList.RemoveAt(i); continue; }
                    i++;
                }
            }
            catch { }
        }

        private void AddDefaults()
        {
            // 默认敏感词 (当 !AbUse.txt 不存在时的后备)
            var defaults = new[] { "GM", "管理员", "客服", "官方", "系统", "测试", "ADMIN", "root" };
            foreach (var w in defaults) _abuseWords.Add(w);
        }
    }
}

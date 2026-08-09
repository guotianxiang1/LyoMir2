using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DBSvr.Core
{
    /// <summary>
    /// IP/账号白名单黑名单管理。
    /// 对应 Delphi 原版:
    ///   - IpAddress.txt: [Allow]/[Deny] IP段
    ///   - WhiteList.txt / GameGateWhiteList.txt: IP白名单
    ///   - AllowPTID.txt / FastPassPTID.txt: 账号白名单
    ///   - !DenyLogon.txt: 禁止登录账号
    /// </summary>
    public class WhitelistService
    {
        private readonly object _sync = new();
        private readonly HashSet<string> _addressAllowedIps = new();
        private List<string> _whiteListIps = new();
        private List<string> _gameGateWhiteListIps = new();
        private readonly HashSet<string> _deniedIps = new();
        private readonly HashSet<string> _allowedPtids = new();
        private readonly HashSet<string> _fastPassPtids = new();
        private readonly HashSet<string> _denyLogonAccounts = new();

        /// <summary>
        /// 加载所有白名单/黑名单文件。
        /// </summary>
        public void Load(string baseDirectory = null)
        {
            lock (_sync)
            {
                _addressAllowedIps.Clear(); _deniedIps.Clear();
                _allowedPtids.Clear(); _fastPassPtids.Clear(); _denyLogonAccounts.Clear();

                LoadIpAddressFile(ResolvePath(baseDirectory, "IpAddress.txt"));
                _whiteListIps = LoadNativeListOrCurrent(
                    ResolvePath(baseDirectory, "WhiteList.txt"),
                    _whiteListIps, "LoadWhiteList error:");
                _gameGateWhiteListIps = LoadNativeListOrCurrent(
                    ResolvePath(baseDirectory, "GameGateWhiteList.txt"),
                    _gameGateWhiteListIps, "LoadGGWhiteList error:");
                LoadSimpleList(ResolvePath(baseDirectory, "AllowPTID.txt"),
                    _allowedPtids);
                LoadSimpleList(ResolvePath(baseDirectory, "FastPassPTID.txt"),
                    _fastPassPtids);
                LoadSimpleList(ResolvePath(baseDirectory, "!DenyLogon.txt"),
                    _denyLogonAccounts);

                var allowedIpCount = _addressAllowedIps.Count
                                     + _whiteListIps.Count
                                     + _gameGateWhiteListIps.Count;
                DBShare.MainOutMessage($"[Whitelist] IP: {allowedIpCount}允许/{_deniedIps.Count}禁止, PTID: {_allowedPtids.Count}允许/{_denyLogonAccounts.Count}禁止");
            }
        }

        public void ReloadNativeWhiteLists(string baseDirectory = null)
        {
            ReloadNativeList(ResolvePath(baseDirectory, "WhiteList.txt"),
                false, "LoadWhiteList error:");
            ReloadNativeList(ResolvePath(baseDirectory, "GameGateWhiteList.txt"),
                true, "LoadGGWhiteList error:");
        }

        public bool IsNativeWhiteListed(string value)
        {
            lock (_sync) return NativeListContains(_whiteListIps, value);
        }

        public bool IsNativeGameGateWhiteListed(string value)
        {
            lock (_sync) return NativeListContains(_gameGateWhiteListIps, value);
        }

        /// <summary>
        /// 检查 IP 是否被允许 (考虑到 [Allow]/[Deny] 段)。
        /// </summary>
        public bool IsIpAllowed(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;

            // 本地回环始终放行
            if (ip == "127.0.0.1" || ip == "::1" || ip == "localhost" || ip.StartsWith("192.168."))
                return true;

            lock (_sync)
            {
                // [Deny] 优先
                if (_deniedIps.Contains(ip)) return false;

                // [Allow] / 白名单
                if (_addressAllowedIps.Count == 0 && _whiteListIps.Count == 0
                    && _gameGateWhiteListIps.Count == 0) return true;
                return _addressAllowedIps.Contains(ip)
                       || NativeListContains(_whiteListIps, ip)
                       || NativeListContains(_gameGateWhiteListIps, ip);
            }
        }

        /// <summary>
        /// 检查账号是否被禁止登录。
        /// </summary>
        public bool IsAccountDenied(string ptid)
        {
            if (string.IsNullOrEmpty(ptid)) return false;
            lock (_sync) return _denyLogonAccounts.Contains(ptid);
        }

        /// <summary>
        /// 检查是否为快速通道 PTID。
        /// </summary>
        public bool IsFastPass(string ptid)
        {
            lock (_sync) return _fastPassPtids.Contains(ptid);
        }

        /// <summary>
        /// 检查是否为白名单 PTID。
        /// </summary>
        public bool IsPtidAllowed(string ptid)
        {
            lock (_sync) return _allowedPtids.Contains(ptid);
        }

        /// <summary>
        /// 解析 IpAddress.txt 的 [Allow]/[Deny] 段。
        /// </summary>
        private void LoadIpAddressFile(string fileName)
        {
            try
            {
                if (!File.Exists(fileName)) return;
                var lines = File.ReadAllLines(fileName, Encoding.GetEncoding("GBK"));
                string currentSection = "";

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(";")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line;
                        continue;
                    }

                    if (currentSection.IndexOf("Allow", StringComparison.OrdinalIgnoreCase) >= 0)
                        _addressAllowedIps.Add(line);
                    else if (currentSection.IndexOf("Deny", StringComparison.OrdinalIgnoreCase) >= 0)
                        _deniedIps.Add(line);
                }
            }
            catch { }
        }

        private static void LoadSimpleList(string fileName, HashSet<string> target)
        {
            try
            {
                if (!File.Exists(fileName)) return;
                foreach (var line in File.ReadAllLines(fileName, Encoding.GetEncoding("GBK")))
                {
                    var item = line.Trim();
                    if (!string.IsNullOrEmpty(item) && !item.StartsWith(";"))
                        target.Add(item);
                }
            }
            catch { }
        }

        private void ReloadNativeList(string fileName, bool gameGateList,
            string errorPrefix)
        {
            if (!File.Exists(fileName)) return;
            try
            {
                var replacement = ReadNativeStringList(fileName);
                lock (_sync)
                {
                    if (gameGateList) _gameGateWhiteListIps = replacement;
                    else _whiteListIps = replacement;
                }
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(errorPrefix + " " + ex.Message);
            }
        }

        private static List<string> LoadNativeListOrCurrent(string fileName,
            List<string> current, string errorPrefix)
        {
            try
            {
                return File.Exists(fileName)
                    ? ReadNativeStringList(fileName) : current;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(errorPrefix + " " + ex.Message);
                return current;
            }
        }

        private static List<string> ReadNativeStringList(string fileName)
        {
            var bytes = File.ReadAllBytes(fileName);
            var nul = Array.IndexOf(bytes, (byte)0);
            var length = nul < 0 ? bytes.Length : nul;
            var text = Encoding.GetEncoding("GBK").GetString(bytes, 0, length);
            var result = new List<string>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\r' && text[i] != '\n') continue;
                result.Add(text.Substring(start, i - start));
                if (text[i] == '\r' && i + 1 < text.Length
                                     && text[i + 1] == '\n') i++;
                start = i + 1;
            }
            if (start < text.Length) result.Add(text[start..]);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static bool NativeListContains(List<string> values, string value)
        {
            foreach (var candidate in values)
                if (string.Equals(candidate, value,
                        StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ResolvePath(string baseDirectory, string fileName) =>
            string.IsNullOrEmpty(baseDirectory)
                ? Path.Combine(AppContext.BaseDirectory, fileName)
                : Path.Combine(baseDirectory, fileName);
    }
}

using System;
using System.Text;
using SystemModule.Common;

namespace DBSvr.Core
{
    /// <summary>
    /// 角色名校验器。
    /// 完整还原 Delphi 原版 CheckChrName() 的 GBK 双字节范围校验逻辑。
    /// 从 DBServer 二进制逆向工程还原。
    /// </summary>
    public static class NameValidator
    {
        private static readonly Encoding Gbk;

        static NameValidator()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        /// <summary>
        /// GBK 字符校验。
        /// ASCII 范围: 仅允许 [0-9a-zA-Z]
        /// GBK 第一字节: [0x81-0xFE]
        /// GBK 第二字节: 第一字节 ≤ 0xF7 → [0x40-0xFE], 第一字节 > 0xF7 → [0x40-0xA0]
        /// </summary>
        public static bool CheckChrName(string sChrName)
        {
            if (string.IsNullOrEmpty(sChrName))
                return false;

            try
            {
                var bytes = Gbk.GetBytes(sChrName);
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte chr = bytes[i];
                    if (chr >= 0x81 && chr <= 0xFE)
                    {
                        if (++i >= bytes.Length) return false;
                        byte second = bytes[i];
                        if (chr <= 0xF7)
                        {
                            if (second < 0x40 || second > 0xFE) return false;
                        }
                        else if (second < 0x40 || second > 0xA0)
                        {
                            return false;
                        }
                    }
                    else if (!((chr >= (byte)'0' && chr <= (byte)'9') ||
                               (chr >= (byte)'a' && chr <= (byte)'z') ||
                               (chr >= (byte)'A' && chr <= (byte)'Z')))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 英文名专用校验: 仅允许 [0-9a-zA-Z]。
        /// </summary>
        public static bool CheckEnglishName(string sChrName)
        {
            if (string.IsNullOrEmpty(sChrName))
                return false;

            foreach (char c in sChrName)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 综合名字校验: 根据配置选择 GBK 或英文模式。
        /// </summary>
        public static bool ValidateChrName(string sChrName, bool englishNamesOnly)
        {
            if (string.IsNullOrEmpty(sChrName))
                return false;

            try
            {
                // Native CHAR(15) stores raw GBK bytes, not UTF-16 characters.
                if (Gbk.GetByteCount(sChrName) > 15) return false;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }

            if (englishNamesOnly)
                return CheckEnglishName(sChrName);

            return CheckChrName(sChrName);
        }

        /// <summary>
        /// 检查名字是否在黑名单中 (大小写不敏感)。
        /// </summary>
        public static bool IsDenied(string sChrName, StringList denyList)
        {
            if (denyList == null || denyList.Count == 0)
                return false;

            for (int i = 0; i < denyList.Count; i++)
            {
                if (string.Compare(sChrName, denyList[i], StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }
            return false;
        }
    }
}

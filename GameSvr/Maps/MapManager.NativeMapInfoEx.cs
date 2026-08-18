using System.Runtime.InteropServices;
using System.Text;
using SystemModule;

namespace GameSvr
{
    public partial class MapManager
    {
        private static readonly IComparer<string> NativeMapInfoExComparer =
            new NativeAnsiIgnoreCaseComparer();
        private readonly object _nativeMapInfoExSync = new();
        private readonly List<string> _nativeMapInfoExLines = new();

        // sub_697E50: a missing file leaves the existing TStringList untouched;
        // an existing file replaces it. The list is Sorted=True with Delphi's
        // default dupIgnore behavior.
        internal bool LoadNativeMapInfoEx(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return false;
            }

            var loaded = new List<string>();
            using (var reader = new StreamReader(fileName,
                       HUtil32.GbkEncoding, false))
            {
                while (reader.Peek() >= 0)
                {
                    var line = reader.ReadLine() ?? string.Empty;
                    var index = loaded.BinarySearch(line,
                        NativeMapInfoExComparer);
                    if (index < 0)
                    {
                        loaded.Insert(~index, line);
                    }
                }
            }

            lock (_nativeMapInfoExSync)
            {
                _nativeMapInfoExLines.Clear();
                _nativeMapInfoExLines.AddRange(loaded);
            }
            return true;
        }

        // sub_697FBC -> TStrings.GetTextStr: every stored line, including the
        // final line, is followed by CRLF. An empty list yields an empty body.
        internal string GetNativeMapInfoExText()
        {
            lock (_nativeMapInfoExSync)
            {
                if (_nativeMapInfoExLines.Count == 0)
                {
                    return string.Empty;
                }

                var text = new StringBuilder();
                for (var i = 0; i < _nativeMapInfoExLines.Count; i++)
                {
                    text.Append(_nativeMapInfoExLines[i]);
                    text.Append("\r\n");
                }
                return text.ToString();
            }
        }

        internal IReadOnlyList<string> SnapshotNativeMapInfoEx()
        {
            lock (_nativeMapInfoExSync)
            {
                return _nativeMapInfoExLines.ToArray();
            }
        }

        internal static int CompareNativeMapInfoEx(string left, string right) =>
            NativeMapInfoExComparer.Compare(left, right);

        private sealed class NativeAnsiIgnoreCaseComparer : IComparer<string>
        {
            private const uint LocaleUserDefault = 0x0400;
            private const uint NormIgnoreCase = 0x00000001;

            public int Compare(string left, string right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }
                if (left == null)
                {
                    return -1;
                }
                if (right == null)
                {
                    return 1;
                }

                var leftBytes = HUtil32.GbkEncoding.GetBytes(left);
                var rightBytes = HUtil32.GbkEncoding.GetBytes(right);
                return CompareStringA(LocaleUserDefault, NormIgnoreCase,
                    leftBytes, leftBytes.Length,
                    rightBytes, rightBytes.Length) - 2;
            }

            [DllImport("kernel32.dll", ExactSpelling = true)]
            private static extern int CompareStringA(uint locale,
                uint compareFlags, byte[] left, int leftLength,
                byte[] right, int rightLength);
        }
    }
}

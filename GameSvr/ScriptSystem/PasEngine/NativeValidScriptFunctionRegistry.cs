using System.IO;
using System.Runtime.InteropServices;
using SystemModule;

namespace GameSvr.PasEngine
{
    /// <summary>
    /// Global validScriptFunc.txt registry used by the native script-interaction
    /// gate. sub_7900FC reloads it; sub_6B8CC4 queries it through TStringList.Find.
    /// </summary>
    internal static class NativeValidScriptFunctionRegistry
    {
        private static readonly List<string> Functions = new();
        private static readonly IComparer<string> Comparer =
            new NativeAnsiTextComparer();

        internal static int Reload(string configPath)
        {
            var fileName = Path.Combine(configPath,
                "Config", "validScriptFunc.txt");
            if (!File.Exists(fileName))
                return -1;

            Functions.Clear();
            using var reader = new StreamReader(fileName,
                HUtil32.GbkEncoding, false);
            while (reader.Peek() >= 0)
                Functions.Add(reader.ReadLine());

            var count = Functions.Count;
            Functions.Sort(Comparer);
            return count;
        }

        internal static bool Find(string functionName)
        {
            return functionName != null
                   && Functions.BinarySearch(functionName, Comparer) >= 0;
        }

        internal static IReadOnlyList<string> Snapshot() =>
            Functions.ToArray();

        internal static int Compare(string left, string right) =>
            Comparer.Compare(left, right);

        private sealed class NativeAnsiTextComparer : IComparer<string>
        {
            private const uint LocaleUserDefault = 0x0400;
            private const uint NormIgnoreCase = 0x00000001;

            public int Compare(string left, string right)
            {
                if (ReferenceEquals(left, right))
                    return 0;
                if (left == null)
                    return -1;
                if (right == null)
                    return 1;

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

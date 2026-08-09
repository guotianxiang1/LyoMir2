using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SystemModule.Common;

namespace GameSvr
{
    public sealed class NativeServerSwitchStore
    {
        public const int SwitchByteCount = 5;

        private readonly object _syncRoot = new();
        private readonly string _fileName;
        private readonly string _mutexName;
        private readonly byte[] _switches;
        private readonly byte[] _dirtyMasks = new byte[SwitchByteCount];

        private NativeServerSwitchStore(string fileName, byte[] switches)
        {
            _fileName = fileName ?? string.Empty;
            _switches = Normalize(switches);
            _mutexName = string.IsNullOrEmpty(_fileName)
                ? string.Empty
                : BuildMutexName(_fileName);
        }

        public static NativeServerSwitchStore Unavailable { get; } =
            new(string.Empty, new byte[SwitchByteCount]);

        public bool Available => !string.IsNullOrEmpty(_fileName);

        public static bool TryLoad(string shareDirectory,
            out NativeServerSwitchStore store, out string error)
        {
            store = Unavailable;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(shareDirectory))
            {
                error = "Share directory is empty";
                return false;
            }

            var fileName = Path.GetFullPath(Path.Combine(shareDirectory,
                "Config", "ServerSwitch.Bin"));
            try
            {
                if (!TryRead(fileName, out var switches, out error))
                    return false;
                store = new NativeServerSwitchStore(fileName, switches);
                return true;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException ||
                                       ex is ArgumentException ||
                                       ex is NotSupportedException)
            {
                error = "ServerSwitch.Bin: " + ex.Message;
                return false;
            }
        }

        internal static NativeServerSwitchStore FromSnapshot(string fileName,
            byte[] switches) => new(fileName, switches);

        public bool IsBitSet(int byteOffset, byte mask)
        {
            ValidateBit(byteOffset, mask);
            lock (_syncRoot)
                return (_switches[byteOffset] & mask) != 0;
        }

        public uint ReadSwitchWord()
        {
            lock (_syncRoot)
                return BinaryPrimitives.ReadUInt32LittleEndian(_switches);
        }

        public byte[] GetSnapshot()
        {
            lock (_syncRoot)
                return (byte[])_switches.Clone();
        }

        public bool TrySetBit(int byteOffset, byte mask, bool enabled,
            out uint switchWord, out string error)
        {
            ValidateBit(byteOffset, mask);
            switchWord = 0;
            error = string.Empty;
            if (!Available)
                return false;

            lock (_syncRoot)
            {
                if (!TryRefreshLocked(out error))
                    return false;
                if (enabled)
                    _switches[byteOffset] |= mask;
                else
                    _switches[byteOffset] &= unchecked((byte)~mask);
                _dirtyMasks[byteOffset] |= mask;
                switchWord = BinaryPrimitives.ReadUInt32LittleEndian(_switches);
                return true;
            }
        }

        public bool TryApplySwitchWord(uint switchWord, out string error)
        {
            error = string.Empty;
            if (!Available)
                return false;

            lock (_syncRoot)
            {
                if (!TryRefreshLocked(out error))
                    return false;
                BinaryPrimitives.WriteUInt32LittleEndian(_switches, switchWord);
                for (var i = 0; i < sizeof(uint); i++)
                    _dirtyMasks[i] = byte.MaxValue;
                return true;
            }
        }

        public bool TryPersist(out string error)
        {
            error = string.Empty;
            if (!Available)
                return false;

            lock (_syncRoot)
            {
                if (!_dirtyMasks.Any(mask => mask != 0))
                    return true;

                return WithFileMutex(() =>
                {
                    if (!TryRead(_fileName, out var latest, out var readError))
                        return (false, readError);
                    MergeDirtyInto(latest);
                    try
                    {
                        AtomicFile.WriteAllBytes(_fileName, latest);
                    }
                    catch (Exception ex) when (ex is IOException ||
                                               ex is UnauthorizedAccessException ||
                                               ex is ArgumentException)
                    {
                        return (false, "ServerSwitch.Bin: " + ex.Message);
                    }

                    Buffer.BlockCopy(latest, 0, _switches, 0, SwitchByteCount);
                    Array.Clear(_dirtyMasks, 0, _dirtyMasks.Length);
                    return (true, string.Empty);
                }, out error);
            }
        }

        private bool TryRefreshLocked(out string error)
        {
            error = string.Empty;
            return WithFileMutex(() =>
            {
                if (!TryRead(_fileName, out var latest, out var readError))
                    return (false, readError);
                MergeDirtyInto(latest);
                Buffer.BlockCopy(latest, 0, _switches, 0, SwitchByteCount);
                return (true, string.Empty);
            }, out error);
        }

        private void MergeDirtyInto(byte[] target)
        {
            for (var i = 0; i < SwitchByteCount; i++)
            {
                var mask = _dirtyMasks[i];
                target[i] = (byte)((target[i] & ~mask) | (_switches[i] & mask));
            }
        }

        private bool WithFileMutex(Func<(bool Success, string Error)> action,
            out string error)
        {
            error = string.Empty;
            var acquired = false;
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(false, _mutexName);
                try
                {
                    acquired = mutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                var result = action();
                error = result.Error;
                return result.Success;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                error = "ServerSwitch.Bin: " + ex.Message;
                return false;
            }
            finally
            {
                if (acquired)
                    mutex.ReleaseMutex();
                mutex?.Dispose();
            }
        }

        private static bool TryRead(string fileName, out byte[] switches,
            out string error)
        {
            switches = new byte[SwitchByteCount];
            error = string.Empty;
            if (!File.Exists(fileName))
                return true;

            try
            {
                var stored = File.ReadAllBytes(fileName);
                if (stored.Length < SwitchByteCount)
                {
                    error = $"ServerSwitch.Bin expected 5 bytes, found {stored.Length}";
                    return false;
                }
                Buffer.BlockCopy(stored, 0, switches, 0, SwitchByteCount);
                return true;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                error = "ServerSwitch.Bin: " + ex.Message;
                return false;
            }
        }

        private static byte[] Normalize(byte[] switches)
        {
            var result = new byte[SwitchByteCount];
            if (switches != null)
                Buffer.BlockCopy(switches, 0, result, 0,
                    Math.Min(switches.Length, result.Length));
            return result;
        }

        private static void ValidateBit(int byteOffset, byte mask)
        {
            if (byteOffset is < 0 or >= SwitchByteCount)
                throw new ArgumentOutOfRangeException(nameof(byteOffset));
            if (mask == 0)
                throw new ArgumentOutOfRangeException(nameof(mask));
        }

        private static string BuildMutexName(string fileName)
        {
            var normalized = Path.GetFullPath(fileName).ToUpperInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return "Local\\LyoMir2.ServerSwitch." + Convert.ToHexString(hash);
        }
    }
}

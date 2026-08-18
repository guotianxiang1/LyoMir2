using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;

namespace GameSvr.Services
{
    internal static class NativeSecondaryGameDataLogCodec
    {
        public const int RecordSize = 0x44;
        public const int FixedFileInfoSize = 0x34;
        public const ushort BodySize = 0x3C;
        public const uint Magic = 0xFF22FF22;
        public const ushort Command = 0x0446;

        public static byte[] Encode(ReadOnlySpan<byte> fixedFileInfo,
            int serverIndex)
        {
            if (fixedFileInfo.Length != FixedFileInfoSize)
                throw new ArgumentException(
                    $"VS_FIXEDFILEINFO must be {FixedFileInfoSize} bytes.",
                    nameof(fixedFileInfo));

            var result = new byte[RecordSize];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x00, 4),
                Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x04, 2),
                Command);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x06, 2),
                BodySize);
            fixedFileInfo.CopyTo(result.AsSpan(0x08, FixedFileInfoSize));
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x3C, 4),
                serverIndex);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x40, 4), 0);
            return result;
        }
    }

    internal static class NativeExecutableFixedFileInfo
    {
        public static byte[] ReadCurrentProcess()
        {
            return Read(Environment.ProcessPath);
        }

        internal static byte[] Read(string executablePath)
        {
            var result = new byte[
                NativeSecondaryGameDataLogCodec.FixedFileInfoSize];
            if (string.IsNullOrEmpty(executablePath)) return result;

            uint ignored;
            var size = GetFileVersionInfoSizeA(executablePath, out ignored);
            if (size == 0 || size > int.MaxValue) return result;

            var block = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetFileVersionInfoA(executablePath, 0, size, block))
                    throw new InvalidOperationException(
                        "GetFileVersionInfoA failed after returning a nonzero size.");
                if (!VerQueryValueA(block, "\\", out var fixedInfo,
                        out var fixedInfoLength)
                    || fixedInfo == IntPtr.Zero
                    || fixedInfoLength
                        < NativeSecondaryGameDataLogCodec.FixedFileInfoSize)
                    throw new InvalidOperationException(
                        "VerQueryValueA did not return VS_FIXEDFILEINFO.");

                Marshal.Copy(fixedInfo, result, 0, result.Length);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(block);
            }
        }

        [DllImport("version.dll", EntryPoint = "GetFileVersionInfoSizeA",
            ExactSpelling = true, CharSet = CharSet.Ansi,
            CallingConvention = CallingConvention.Winapi)]
        private static extern uint GetFileVersionInfoSizeA(
            [MarshalAs(UnmanagedType.LPStr)] string filename,
            out uint handle);

        [DllImport("version.dll", EntryPoint = "GetFileVersionInfoA",
            ExactSpelling = true, CharSet = CharSet.Ansi,
            CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileVersionInfoA(
            [MarshalAs(UnmanagedType.LPStr)] string filename,
            uint handle, uint length, IntPtr data);

        [DllImport("version.dll", EntryPoint = "VerQueryValueA",
            ExactSpelling = true, CharSet = CharSet.Ansi,
            CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VerQueryValueA(IntPtr block,
            [MarshalAs(UnmanagedType.LPStr)] string subBlock,
            out IntPtr value, out uint length);
    }

    internal sealed class NativeSecondaryGameDataLogService : IDisposable
    {
        public const int NativePort = 20_000;
        private const int ExecuteIntervalMilliseconds = 1_000;
        private const int ReportIntervalMilliseconds = 10_000;

        public static NativeSecondaryGameDataLogService Instance { get; } =
            new(NativeExecutableFixedFileInfo.ReadCurrentProcess(), NativePort);

        private readonly object _stateLock = new();
        private readonly NativeGameDataLogService _transport = new();
        private readonly byte[] _fixedFileInfo;
        private readonly int _port;
        private int _started;
        private uint _lastExecuteTick;
        // The steady-state gate at 0x650E51 is verified. The virtualized
        // TAppEngine constructor's original +0x50 value is still unknown, so
        // the first-report timing represented by this zero default is provisional.
        private uint _lastReportTick;

        internal NativeSecondaryGameDataLogService(byte[] fixedFileInfo,
            int port)
        {
            if (fixedFileInfo == null
                || fixedFileInfo.Length
                != NativeSecondaryGameDataLogCodec.FixedFileInfoSize)
            {
                throw new ArgumentException(
                    "A 52-byte VS_FIXEDFILEINFO block is required.",
                    nameof(fixedFileInfo));
            }
            if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException(nameof(port));

            _fixedFileInfo = (byte[])fixedFileInfo.Clone();
            _port = port;
        }

        public bool Connected => _transport.Connected;
        public int QueuedRecordCount => _transport.QueuedRecordCount;
        public int PendingByteCount => _transport.PendingByteCount;
        internal bool WorkerRunning => _transport.WorkerRunning;
        internal uint LastExecuteTick => _lastExecuteTick;
        internal uint LastReportTick => _lastReportTick;

        public void Start(string host)
        {
            lock (_stateLock)
            {
                if (_started != 0) return;
                _transport.Start(host, _port);
                _started = 1;
            }
        }

        public bool Run(uint currentTick, int serverIndex)
        {
            byte[] payload;
            lock (_stateLock)
            {
                if (_started == 0
                    || !IsExecuteDue(currentTick, _lastExecuteTick))
                {
                    return false;
                }

                _lastExecuteTick = currentTick;
                if (!IsReportDue(currentTick, _lastReportTick)) return false;

                _lastReportTick = currentTick;
                payload = NativeSecondaryGameDataLogCodec.Encode(
                    _fixedFileInfo, serverIndex);
            }

            return _transport.TryEnqueueRaw(payload);
        }

        internal static bool IsExecuteDue(uint now, uint lastExecute) =>
            unchecked(now - lastExecute) >= ExecuteIntervalMilliseconds;

        internal static bool IsReportDue(uint now, uint lastReport) =>
            unchecked(now - lastReport) >= ReportIntervalMilliseconds;

        internal void RequestStop()
        {
            lock (_stateLock)
            {
                if (_started == 0) return;
                _transport.RequestStop();
            }
        }

        internal void WaitForStop()
        {
            lock (_stateLock)
            {
                if (_started == 0) return;
            }

            _transport.WaitForStop();
            lock (_stateLock) _started = 0;
        }

        public void Stop()
        {
            RequestStop();
            WaitForStop();
        }

        public void Dispose()
        {
            Stop();
            _transport.Dispose();
        }
    }
}

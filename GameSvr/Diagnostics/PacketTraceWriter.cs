using System.Text;

namespace GameSvr
{
    /// <summary>
    /// One process-wide append stream for opt-in packet diagnostics.  The normal
    /// build never calls this (all callers are Conditional), while trace builds
    /// avoid opening and closing a file for every packet.
    /// </summary>
    internal static class PacketTraceWriter
    {
        private static readonly object SyncRoot = new();
        private static FileStream Stream;

        internal static void Write(string line)
        {
#if GAMESVR_PACKET_TRACE
            if (string.IsNullOrEmpty(line)) return;
            try
            {
                lock (SyncRoot)
                {
                    Stream ??= new FileStream(
                        Path.Combine(AppContext.BaseDirectory,
                            "gamesvr-packet-trace.log"),
                        FileMode.Append, FileAccess.Write,
                        FileShare.ReadWrite, 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var bytes = Encoding.UTF8.GetBytes(
                        line + Environment.NewLine);
                    Stream.Write(bytes, 0, bytes.Length);
                    Stream.Flush();
                }
            }
            catch
            {
                // Diagnostics must never affect the game loop.
            }
#endif
        }
    }
}

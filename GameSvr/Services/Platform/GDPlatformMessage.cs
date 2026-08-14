using System;
using SystemModule;

namespace GameSvr.Services.Platform
{
    /// <summary>
    /// Represents a platform message from the GD (Grand Dao) platform subsystem.
    /// This is a minimal viable implementation scaffolding for platform integration.
    /// </summary>
    public sealed class GDPlatformMessage
    {
        private readonly byte[] _gbkBytes;

        /// <summary>
        /// Creates a new platform message from GBK-encoded bytes.
        /// </summary>
        /// <param name="gbkBytes">The GBK-encoded message payload.</param>
        public GDPlatformMessage(byte[] gbkBytes)
        {
            _gbkBytes = gbkBytes ?? Array.Empty<byte>();
            Text = HUtil32.GbkEncoding.GetString(_gbkBytes);
        }

        /// <summary>
        /// The decoded text content of the message.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the raw GBK-encoded bytes of the message.
        /// </summary>
        public byte[] GetGbkBytes()
        {
            return _gbkBytes;
        }

        /// <summary>
        /// Gets the length of the raw message in bytes.
        /// </summary>
        public int Length => _gbkBytes.Length;

        public override string ToString()
        {
            return Text;
        }
    }
}

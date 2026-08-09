using System;

namespace SystemModule.Packet
{
    /// <summary>
    /// Codec for the native YB forging-mode 108/1108 exchange.
    /// Transport and the process-runtime mode owner live in GameSvr.
    /// </summary>
    public static class YbDbForgeModeProtocol
    {
        public const ushort RequestIdent = 108;
        public const ushort ResponseIdent = 1108;
        public const int RequestQueryId = 0;
        public const int SingleMode = 1;
        public const int DoubleMode = 2;
        public const int RequestPayloadSize = 0;
        public const string DoubleModeMessage = "==> 开启元宝双倍锻造";
        public const string SingleModeMessage = "==> 元宝单倍锻造";

        public static YbDbLegacy77Frame CreateRequest(bool doubleForging)
        {
            return new YbDbLegacy77Frame(RequestQueryId,
                doubleForging ? DoubleMode : SingleMode,
                RequestIdent, Array.Empty<byte>());
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out Response response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "forge-mode response frame is null";
                return false;
            }
            if (frame.Ident != ResponseIdent)
            {
                error = $"forge-mode response Ident must be {ResponseIdent}";
                return false;
            }

            var mode = frame.QueryId == DoubleMode ? DoubleMode : SingleMode;
            response = new Response(frame.QueryId, frame.Param,
                frame.Payload?.Length ?? 0, mode);
            return true;
        }

        public sealed class Response
        {
            internal Response(int wireQueryId, int ignoredParam,
                int ignoredPayloadLength, int mode)
            {
                WireQueryId = wireQueryId;
                IgnoredParam = ignoredParam;
                IgnoredPayloadLength = ignoredPayloadLength;
                Mode = mode;
            }

            public int WireQueryId { get; }
            public int IgnoredParam { get; }
            public int IgnoredPayloadLength { get; }
            public int Mode { get; }
            public bool DoubleForging => Mode == DoubleMode;
            public string ConsoleMessage => DoubleForging
                ? DoubleModeMessage
                : SingleModeMessage;
        }
    }
}

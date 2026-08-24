using System;

namespace SystemModule.Packet
{
    /// <summary>
    /// Direction-specific command values used by the native 2.08 GameGate link.
    /// The historical Grobal2.GM_* values are a separate C# dialect and must not
    /// be used to infer the command on the wire in the opposite direction.
    /// </summary>
    public static class NativeGameGateCommands
    {
        public const int MinGateIndex = 1;
        public const int MaxGateIndex = 32;

        // Gate -> M2Server (the M2 receive jump table accepts 0..7).
        public const ushort GateKeepAliveRequest = 3;
        public const ushort GateClientData = 4;
        public const ushort GateRegistrationRequest = 5;

        // M2Server -> GameGate (RunGate's 0x0B+ dispatch table).
        public const ushort M2KeepAliveReply = 13;
        public const ushort M2ClientData = 14;
        public const ushort M2RegistrationReply = 15;

        // Gate -> M2 receive contract.  This is intentionally separate from
        // the larger M2 -> Gate outer-frame limit: the native M2 validator at
        // 0x5F6679 accepts BodyLen <= 0x3000 and abandons the receive buffer
        // for a larger declaration.
        public const int NativeM2ReceiveBufferLength = 0x8000;
        public const int NativeM2MaximumBodyLength = 0x3000;
        public const int NativeM2MaximumFrameLength =
            InternalPacket77.HEADER_SIZE + NativeM2MaximumBodyLength;

        /// <summary>
        /// Native M2/GameGate route key: (gate index &lt;&lt; 17) | WORD session.
        /// The low word is deliberately truncated like the original
        /// <c>movzx word</c> route builder.
        /// </summary>
        public static uint ComposeRouteId(int gateIndex, ushort sessionWord)
        {
            if (gateIndex is < MinGateIndex or > MaxGateIndex)
                throw new ArgumentOutOfRangeException(nameof(gateIndex),
                    "native gate index must be in the range 1..32");
            return ((uint)gateIndex << 17) | sessionWord;
        }
    }
}

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant codec and save-route model for the native YBDB 104 request.
    /// The authoritative 6108 lifecycle is not available, so this type must
    /// not be wired into the live client transport.
    /// </summary>
    public static class YbDbLogoutProtocol
    {
        public const ushort RequestIdent = 104;
        public const int QueryId = 0;
        public const int Param = 0;
        public const int PayloadSize = YbDbLegacy77Codec.IdentitySize;
        public const int FrameSize = YbDbLegacy77Codec.HeaderSize + PayloadSize;

        public static bool TryCreateRequest(YbDbLegacy77Identity identity,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
                return false;

            frame = new YbDbLegacy77Frame(QueryId, Param,
                RequestIdent, payload);
            return true;
        }

        /// <summary>
        /// Models sub_6B6510 without performing transport or persistence.
        /// A failed 104 send never blocks the following character save.
        /// </summary>
        public static SaveRouteDecision EvaluateSaveInvocation(
            bool alreadySaved, uint transferTimeLow, uint transferTimeHigh,
            int saveType)
        {
            var entersSaveBody = !alreadySaved || transferTimeLow != 0 ||
                                 transferTimeHigh != 0;
            return new SaveRouteDecision(entersSaveBody,
                entersSaveBody && saveType != 0);
        }

        public sealed class SaveRouteDecision
        {
            internal SaveRouteDecision(bool entersSaveBody,
                bool sendsLogoutRequest)
            {
                EntersSaveBody = entersSaveBody;
                SendsLogoutRequest = sendsLogoutRequest;
            }

            public bool EntersSaveBody { get; }
            public bool ReportsLingFuAccounting => EntersSaveBody;
            public bool SendsLogoutRequest { get; }
            public bool QueuesHumanSave => EntersSaveBody;
            public bool BlocksSaveOnLogoutFailure => false;
            public bool RegistersPendingRequest => false;
            public bool WaitsForAcknowledgement => false;
            public bool ProducesUiMessage => false;
            public bool MutatesAccountOrGameLog => false;
        }
    }
}

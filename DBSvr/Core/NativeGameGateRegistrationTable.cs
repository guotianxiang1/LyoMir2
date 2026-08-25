using System;

namespace DBSvr.Core
{
    public sealed class NativeGameGateRegistrationTable
    {
        public const int MaximumGateCount = 32;

        private readonly object _sync = new();
        private readonly NativeGameGateEndpoint[] _entries =
            new NativeGameGateEndpoint[MaximumGateCount];

        public void Clear()
        {
            lock (_sync)
                Array.Clear(_entries, 0, _entries.Length);
        }

        public bool TrySet(int gateId, string address, int port)
        {
            if (gateId is < 1 or > MaximumGateCount
                || string.IsNullOrWhiteSpace(address)
                || port <= 0)
                return false;

            lock (_sync)
                _entries[gateId - 1] = new NativeGameGateEndpoint(
                    address.Trim(), port);
            return true;
        }

        public bool TrySetFromSpecification(int gateId,
            string specification)
        {
            ParseSpecification(specification, out var address, out var port);
            return TrySet(gateId, address, port);
        }

        public static void ParseSpecification(string specification,
            out string address, out int port)
        {
            specification = string.IsNullOrWhiteSpace(specification)
                ? "127.0.0.1"
                : specification.Trim();
            address = specification;
            port = 7100;
            var colonIdx = specification.LastIndexOf(':');
            if (colonIdx > 0)
            {
                address = specification.Substring(0, colonIdx).Trim();
                if (int.TryParse(specification.Substring(colonIdx + 1),
                        out var parsedPort))
                    port = parsedPort;
            }
        }

        public byte Resolve(string remoteAddress, int registeredPort)
        {
            if (string.Equals(remoteAddress, "127.0.0.9",
                    StringComparison.Ordinal))
                return 9;

            lock (_sync)
            {
                for (var i = 0; i < _entries.Length; i++)
                {
                    var entry = _entries[i];
                    if (entry == null || entry.Port != registeredPort
                        || !string.Equals(entry.Address, remoteAddress,
                            StringComparison.Ordinal))
                        continue;
                    return checked((byte)(i + 1));
                }
            }
            return 0;
        }

        public byte ResolveForRegistration(byte currentGateId,
            string remoteAddress, int registeredPort) =>
            currentGateId != 0
                ? currentGateId
                : Resolve(remoteAddress, registeredPort);

        private sealed class NativeGameGateEndpoint
        {
            public NativeGameGateEndpoint(string address, int port)
            {
                Address = address;
                Port = port;
            }

            public string Address { get; }
            public int Port { get; }
        }
    }
}

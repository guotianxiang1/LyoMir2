using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SystemModule.Packet;

namespace LoginGate.Core
{
    /// <summary>
    /// Stateless codecs for the verified LoginGate client and native 77 wires.
    /// Opaque fields stay as bytes until their meaning is supported by evidence.
    /// </summary>
    public static class LoginGateWireProtocol
    {
        public const uint ClientMagic = 0xFF44FF44;
        public const int ClientHeaderSize = 12;
        public const int ClientInboundMaximumPayloadSize = 0xF3;
        public const int ClientMaximumPayloadSize = ClientInboundMaximumPayloadSize;
        public const int ClientWireMaximumPayloadSize = ushort.MaxValue;
        public const byte ClientDataFlag = 0x00;
        public const byte ClientDataCommand = 0x17;
        public const byte ClientConnectFlag = 0x02;
        public const byte ClientConnectCommand = 0x18;

        public const int InnerHeaderSize = 12;
        public const ushort ServerListIdent = 4001;
        public const ushort SelectServerIdent = 4002;
        public const int ServerGroupInfoSize = 40;
        public const int ServerListPayloadSize = 52;
        public const int SelectServerJumpPayloadSize = 32;

        public const ushort NativeRegistrationIdent = 2000;
        public const ushort NativeRegistrationAckIdent = 1000;
        public const ushort NativeProbeRequestIdent = 1001;
        public const ushort NativeProbeResponseIdent = 2001;
        public const ushort NativeAuthRequestIdent = 2018;
        public const ushort NativeAuthResponseIdent = 1003;
        public const ushort NativeAuthFailureIdent = 1004;
        public const ushort NativeType2EnabledIdent = 0x07D2;
        public const ushort NativeType2DisabledIdent = 0x07D3;

        public const int NativeRegistrationPayloadSize = 40;
        public const int NativeProbePayloadSize = 28;
        public const int NativeAuthRequestPayloadSize = 136;
        public const int NativeAuthResponseShortPayloadSize = 12;
        public const int NativeAuthResponseMediumPayloadSize = 20;
        public const int NativeAuthResponseFullPayloadSize = 124;
        public const int NativeAuthFailureMaximumPayloadSize = 112;
        public const int NativeAuthFailureMaximumTextBytes = 99;

        private static readonly Encoding Ascii;
        private static readonly Encoding Gbk;

        static LoginGateWireProtocol()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Ascii = Encoding.GetEncoding(Encoding.ASCII.CodePage,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        public static LoginGateClientFrame CreateConnectRequest(uint dataIndex) =>
            new(ClientConnectFlag, ClientConnectCommand, dataIndex, Array.Empty<byte>());

        public static bool TryEncodeClientFrame(LoginGateClientFrame? frame,
            out byte[] data, out string error)
        {
            data = Array.Empty<byte>();
            error = string.Empty;
            if (frame == null)
            {
                error = "LoginGate client frame is null";
                return false;
            }

            if (frame.Payload.Length > ClientWireMaximumPayloadSize)
            {
                error = $"LoginGate client payload exceeds {ClientWireMaximumPayloadSize} bytes";
                return false;
            }

            data = new byte[ClientHeaderSize + frame.Payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), ClientMagic);
            data[4] = frame.Flag;
            data[5] = frame.Command;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2),
                (ushort)frame.Payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), frame.DataIndex);
            frame.Payload.CopyTo(data, ClientHeaderSize);
            return true;
        }

        public static bool TryDecodeClientFrame(ReadOnlySpan<byte> data,
            out LoginGateClientFrame frame, out string error)
        {
            frame = null!;
            error = string.Empty;
            if (data.Length < ClientHeaderSize)
            {
                error = "LoginGate client frame is truncated";
                return false;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4)) != ClientMagic)
            {
                error = "LoginGate client frame magic mismatch";
                return false;
            }

            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
            if (data.Length != ClientHeaderSize + payloadLength)
            {
                error = "LoginGate client frame payload length mismatch";
                return false;
            }

            frame = new LoginGateClientFrame(data[4], data[5],
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4)),
                payloadLength == 0
                    ? Array.Empty<byte>()
                    : data.Slice(ClientHeaderSize, payloadLength).ToArray());
            return true;
        }

        public static bool TryParseConnectRequest(LoginGateClientFrame? frame,
            out uint dataIndex, out string error)
        {
            dataIndex = 0;
            error = string.Empty;
            if (frame == null
                || frame.Flag != ClientConnectFlag
                || frame.Command != ClientConnectCommand
                || frame.Payload.Length != 0)
            {
                error = "LoginGate CONNECT must be flag 2, command 0x18, and carry no payload";
                return false;
            }

            dataIndex = frame.DataIndex;
            return true;
        }

        public static bool TryCreateServerListFrame(uint dataIndex,
            string groupName, string groupDescription,
            out LoginGateClientFrame frame, out string error)
            => TryCreateServerListFrame(dataIndex,
                [(groupName, groupDescription)], out frame, out error);

        public static bool TryCreateServerListFrame(uint dataIndex,
            IReadOnlyList<(string Name, string Description)> groups,
            out LoginGateClientFrame frame, out string error)
        {
            frame = null!;
            error = string.Empty;
            if (groups == null || groups.Count is < 1 or > 32)
            {
                error = "LoginGate server list requires 1 to 32 groups";
                return false;
            }

            var payload = new byte[InnerHeaderSize + ServerGroupInfoSize * groups.Count];
            WriteInnerHeader(payload, new LoginGateInnerHeader(
                0, ServerListIdent, checked((ushort)groups.Count), 0, 0));
            for (var index = 0; index < groups.Count; index++)
            {
                var offset = InnerHeaderSize + ServerGroupInfoSize * index;
                if (!TryWriteGbkCString(payload.AsSpan(offset, 16),
                        groups[index].Name, out error)
                    || !TryWriteGbkCString(payload.AsSpan(offset + 16, 16),
                        groups[index].Description, out error))
                {
                    return false;
                }
            }

            frame = new LoginGateClientFrame(ClientDataFlag, ClientDataCommand,
                dataIndex, payload);
            return true;
        }

        public static bool TryParseSelectServerRequest(LoginGateClientFrame? frame,
            out LoginGateServerSelection selection, out string error)
        {
            selection = null!;
            error = string.Empty;
            if (frame == null
                || frame.Flag != ClientDataFlag
                || frame.Command != ClientDataCommand)
            {
                error = "LoginGate server selection must be a DATA frame";
                return false;
            }
            if (frame.Payload.Length <= InnerHeaderSize)
            {
                error = "LoginGate server selection name is missing";
                return false;
            }
            if (!TryParseInnerHeader(frame.Payload, out var inner, out error))
                return false;
            if (inner.Ident != SelectServerIdent)
            {
                error = $"LoginGate server selection ident must be {SelectServerIdent}";
                return false;
            }
            if (!TryReadExactGbkCString(frame.Payload.AsSpan(InnerHeaderSize),
                    out var selectedName, out error))
            {
                return false;
            }
            if (selectedName.Length == 0)
            {
                error = "LoginGate server selection name is empty";
                return false;
            }

            selection = new LoginGateServerSelection(frame.DataIndex, inner, selectedName);
            return true;
        }

        public static bool TryCreateSelectServerJumpFrame(uint dataIndex,
            int sessionId, string gameGateAddress, ushort gameGatePort,
            int areaId, int groupId, string suffix,
            out LoginGateClientFrame frame, out string error)
        {
            frame = null!;
            error = string.Empty;
            if (sessionId <= 0)
            {
                error = "LoginGate jump session id must be positive";
                return false;
            }
            if (gameGatePort == 0)
            {
                error = "LoginGate jump port must be non-zero";
                return false;
            }
            if (!IPAddress.TryParse(gameGateAddress, out var address)
                || address.AddressFamily != AddressFamily.InterNetwork)
            {
                error = "LoginGate jump address must be IPv4";
                return false;
            }

            var addressValue = BinaryPrimitives.ReadUInt32LittleEndian(
                address.GetAddressBytes());
            var payload = new byte[SelectServerJumpPayloadSize];
            WriteInnerHeader(payload, new LoginGateInnerHeader(
                sessionId, SelectServerIdent, gameGatePort,
                (ushort)addressValue, (ushort)(addressValue >> 16)));
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), areaId);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), groupId);
            payload[20] = 0;
            if (!TryWriteGbkCString(payload.AsSpan(24, 8), suffix, out error))
                return false;

            frame = new LoginGateClientFrame(ClientDataFlag, ClientDataCommand,
                dataIndex, payload);
            return true;
        }

        public static bool TryParseInnerHeader(ReadOnlySpan<byte> payload,
            out LoginGateInnerHeader header, out string error)
        {
            header = default;
            error = string.Empty;
            if (payload.Length < InnerHeaderSize)
            {
                error = "LoginGate inner header is truncated";
                return false;
            }

            header = new LoginGateInnerHeader(
                BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2)));
            return true;
        }

        public static bool TryParseNativeRegistration(YbDbLegacy77Frame? frame,
            out NativeLoginGateRegistration registration, out string error)
        {
            registration = null!;
            if (!TryRequireNativeFrame(frame, NativeRegistrationIdent,
                    NativeRegistrationPayloadSize, out error))
            {
                return false;
            }

            var payload = frame!.Payload;
            if (!TryReadFixedGbkCString(payload.AsSpan(0, 16),
                    out var serverName, out error))
            {
                return false;
            }

            var humanCounts = new int[6];
            for (var i = 0; i < humanCounts.Length; i++)
                humanCounts[i] = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(16 + i * 4, 4));

            registration = new NativeLoginGateRegistration(
                serverName, humanCounts, payload);
            return true;
        }

        public static YbDbLegacy77Frame CreateNativeRegistrationAck() =>
            new(0, 0, NativeRegistrationAckIdent, Array.Empty<byte>());

        public static bool TryCreateNativeProbeRequest(byte[]? opaquePayload,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null!;
            error = string.Empty;
            if (opaquePayload == null || opaquePayload.Length != NativeProbePayloadSize)
            {
                error = $"native LoginGate probe payload must be {NativeProbePayloadSize} bytes";
                return false;
            }

            frame = new YbDbLegacy77Frame(0, 0, NativeProbeRequestIdent,
                (byte[])opaquePayload.Clone());
            return true;
        }

        public static bool TryParseNativeProbeResponse(YbDbLegacy77Frame? frame,
            out NativeLoginGateProbeRoute route, out string error)
        {
            route = null!;
            if (!TryRequireNativeFrame(frame, NativeProbeResponseIdent,
                    NativeProbePayloadSize, out error))
            {
                return false;
            }

            var payload = frame!.Payload;
            route = new NativeLoginGateProbeRoute(
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(8, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(10, 2)),
                payload.AsSpan(12, 4).ToArray(),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(16, 2)),
                payload[18], payload[19],
                payload.AsSpan(20, 8).ToArray(), payload);
            return true;
        }

        public static bool TryParseNativeAuthRequest(YbDbLegacy77Frame? frame,
            out NativeLoginGateAuthRequest request, out string error)
        {
            request = null!;
            if (!TryRequireNativeFrame(frame, NativeAuthRequestIdent,
                    NativeAuthRequestPayloadSize, out error))
            {
                return false;
            }

            var payload = frame!.Payload;
            // TLoginCenterAuthInfo body: szAuthID(52)=login ticket, szPwd(32),
            // szClientIP(16), szMacAddr(20). Only the ticket authenticates; the
            // password/IP/MAC vendor-SDK fields are parsed for fidelity but ignored.
            if (!TryReadFixedAsciiCString(payload.AsSpan(12, 52),
                    out var ticket, out error)
                || !TryReadFixedAsciiCString(payload.AsSpan(96, 16),
                    out var clientIp, out error)
                || !TryReadFixedAsciiCString(payload.AsSpan(112, 20),
                    out var macAddress, out error))
            {
                return false;
            }

            request = new NativeLoginGateAuthRequest(
                payload[0], payload[1],
                BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(2, 4)),
                payload.AsSpan(6, 6).ToArray(), ticket,
                payload.AsSpan(64, 32).ToArray(), clientIp, macAddress,
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(132, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(134, 2)),
                payload);
            return true;
        }

        public static bool TryCreateNativeAuthResponse12(byte status, byte version,
            int queryId, out YbDbLegacy77Frame frame, out string error) =>
            TryCreateNativeAuthResponse(status, version, queryId,
                Array.Empty<byte>(), NativeAuthResponseShortPayloadSize,
                out frame, out error);

        public static bool TryCreateNativeAuthResponse20(byte status, byte version,
            int queryId, byte[]? opaque12To19,
            out YbDbLegacy77Frame frame, out string error) =>
            TryCreateNativeAuthResponse(status, version, queryId,
                opaque12To19, NativeAuthResponseMediumPayloadSize,
                out frame, out error);

        public static bool TryCreateNativeAuthResponse124(byte status, byte version,
            int queryId, byte[]? opaque12To123,
            out YbDbLegacy77Frame frame, out string error) =>
            TryCreateNativeAuthResponse(status, version, queryId,
                opaque12To123, NativeAuthResponseFullPayloadSize,
                out frame, out error);

        public static bool TryCreateNativeAuthFailure(byte status, byte version,
            int queryId, string? message,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null!;
            error = string.Empty;
            byte[] messageBytes = Array.Empty<byte>();
            if (message != null)
            {
                try
                {
                    messageBytes = Ascii.GetBytes(message);
                }
                catch (EncoderFallbackException ex)
                {
                    error = "native LoginGate failure text is not ASCII: " + ex.Message;
                    return false;
                }
                if (messageBytes.Length > NativeAuthFailureMaximumTextBytes)
                {
                    error = $"native LoginGate failure text exceeds {NativeAuthFailureMaximumTextBytes} bytes";
                    return false;
                }
            }

            var payloadLength = NativeAuthResponseShortPayloadSize
                                + (message == null ? 0 : messageBytes.Length + 1);
            if (payloadLength > NativeAuthFailureMaximumPayloadSize)
            {
                error = $"native LoginGate failure payload exceeds {NativeAuthFailureMaximumPayloadSize} bytes";
                return false;
            }

            var payload = new byte[payloadLength];
            WriteNativeAuthCommon(payload, status, version, queryId);
            if (messageBytes.Length > 0)
                messageBytes.CopyTo(payload, NativeAuthResponseShortPayloadSize);
            frame = new YbDbLegacy77Frame(0, 0, NativeAuthFailureIdent, payload);
            return true;
        }

        public static bool TryParseNativeType2Control(YbDbLegacy77Frame? frame,
            out bool enabled, out string error)
        {
            enabled = false;
            error = string.Empty;
            if (frame == null || frame.Payload.Length != 0
                || (frame.Ident != NativeType2EnabledIdent
                    && frame.Ident != NativeType2DisabledIdent))
            {
                error = "native LoginGate type-2 control must be an empty 0x07D2 or 0x07D3 frame";
                return false;
            }

            enabled = frame.Ident == NativeType2EnabledIdent;
            return true;
        }

        private static bool TryCreateNativeAuthResponse(byte status, byte version,
            int queryId, byte[]? opaqueTail, int payloadSize,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null!;
            error = string.Empty;
            var requiredTailLength = payloadSize - NativeAuthResponseShortPayloadSize;
            if (opaqueTail == null || opaqueTail.Length != requiredTailLength)
            {
                error = $"native LoginGate {payloadSize}-byte response requires {requiredTailLength} opaque tail bytes";
                return false;
            }

            var payload = new byte[payloadSize];
            WriteNativeAuthCommon(payload, status, version, queryId);
            opaqueTail.CopyTo(payload, NativeAuthResponseShortPayloadSize);
            frame = new YbDbLegacy77Frame(0, 0, NativeAuthResponseIdent, payload);
            return true;
        }

        private static void WriteNativeAuthCommon(Span<byte> payload,
            byte status, byte version, int queryId)
        {
            payload[0] = status;
            payload[1] = version;
            BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(2, 4), queryId);
            payload.Slice(6, 6).Clear();
        }

        private static bool TryRequireNativeFrame(YbDbLegacy77Frame? frame,
            ushort expectedIdent, int expectedPayloadSize, out string error)
        {
            error = string.Empty;
            if (frame == null || frame.Ident != expectedIdent
                || frame.Payload.Length != expectedPayloadSize)
            {
                error = $"native LoginGate frame must be ident {expectedIdent} with {expectedPayloadSize} bytes";
                return false;
            }
            return true;
        }

        private static void WriteInnerHeader(Span<byte> destination,
            LoginGateInnerHeader header)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(0, 4), header.Recog);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), header.Ident);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), header.Param);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8, 2), header.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(10, 2), header.Series);
        }

        private static bool TryWriteGbkCString(Span<byte> destination,
            string? value, out string error)
        {
            error = string.Empty;
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "LoginGate text is not GBK: " + ex.Message;
                return false;
            }
            // Native StrPLCopy(dst, src, MaxLen) truncates to at most MaxLen BYTES
            // and null-terminates; it never rejects an over-long field. Every caller
            // uses MaxLen == destination.Length - 1: 16-byte GroupName/GroupDesc slots
            // and the 8-byte suffix (uServerInfo.pas: StrPLCopy(GroupName,sName,15),
            // StrPLCopy(GroupDesc,sDesc,15), StrPLCopy(szPostfix,sSuffix,7)).
            // Reproduce the byte-level truncation exactly — this can split a 2-byte
            // GBK char at the boundary, exactly as native does.
            destination.Clear();
            var copyLength = Math.Min(bytes.Length, destination.Length - 1);
            bytes.AsSpan(0, copyLength).CopyTo(destination);
            return true;
        }

        private static bool TryReadExactGbkCString(ReadOnlySpan<byte> source,
            out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            var terminator = source.IndexOf((byte)0);
            if (terminator < 0 || terminator != source.Length - 1)
            {
                error = "LoginGate server selection must contain one terminal GBK C string";
                return false;
            }
            try
            {
                value = Gbk.GetString(source.Slice(0, terminator));
                return true;
            }
            catch (DecoderFallbackException ex)
            {
                error = "LoginGate server selection is not GBK: " + ex.Message;
                return false;
            }
        }

        private static bool TryReadFixedGbkCString(ReadOnlySpan<byte> source,
            out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            var terminator = source.IndexOf((byte)0);
            if (terminator < 0) terminator = source.Length;
            try
            {
                value = Gbk.GetString(source.Slice(0, terminator));
                return true;
            }
            catch (DecoderFallbackException ex)
            {
                error = "native LoginGate text is not GBK: " + ex.Message;
                return false;
            }
        }

        private static bool TryReadFixedAsciiCString(ReadOnlySpan<byte> source,
            out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            var terminator = source.IndexOf((byte)0);
            if (terminator < 0) terminator = source.Length;
            try
            {
                value = Ascii.GetString(source.Slice(0, terminator));
                return true;
            }
            catch (DecoderFallbackException ex)
            {
                error = "native LoginGate text is not ASCII: " + ex.Message;
                return false;
            }
        }
    }

    public sealed class LoginGateClientFrame
    {
        public LoginGateClientFrame(byte flag, byte command,
            uint dataIndex, byte[]? payload)
        {
            Flag = flag;
            Command = command;
            DataIndex = dataIndex;
            Payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
        }

        public byte Flag { get; }
        public byte Command { get; }
        public uint DataIndex { get; }
        public byte[] Payload { get; }
    }

    public readonly struct LoginGateInnerHeader
    {
        public LoginGateInnerHeader(int recog, ushort ident,
            ushort param, ushort tag, ushort series)
        {
            Recog = recog;
            Ident = ident;
            Param = param;
            Tag = tag;
            Series = series;
        }

        public int Recog { get; }
        public ushort Ident { get; }
        public ushort Param { get; }
        public ushort Tag { get; }
        public ushort Series { get; }
    }

    public sealed class LoginGateServerSelection
    {
        public LoginGateServerSelection(uint dataIndex,
            LoginGateInnerHeader inner, string selectedName)
        {
            DataIndex = dataIndex;
            Inner = inner;
            SelectedName = selectedName ?? string.Empty;
        }

        public uint DataIndex { get; }
        public LoginGateInnerHeader Inner { get; }
        public string SelectedName { get; }
    }

    /// <summary>
    /// DGM_PING (2000) registration/heartbeat. Maps onto the native TPingMsg record
    /// (uTypes.pas): a 16-byte GroupName followed by TGS_Human_Count — six Int32 slots
    /// where index 0 is the forge count (锻造数) and 1..5 are per-GameServer player
    /// counts. A group's online total is the sum of slots 1..5 that are reporting
    /// (>= 0), matching uFormMain.pas ReceiveMsgUpdateGroup.
    /// </summary>
    public sealed class NativeLoginGateRegistration
    {
        public NativeLoginGateRegistration(string serverName,
            int[] humanCounts, byte[] rawPayload)
        {
            ServerName = serverName ?? string.Empty;
            var counts = new int[6];
            if (humanCounts != null)
                Array.Copy(humanCounts, counts, Math.Min(humanCounts.Length, 6));
            HumanCounts = counts;
            RawPayload = Clone(rawPayload);
        }

        public string ServerName { get; }

        /// <summary>Native TGS_Human_Count: six Int32 slots, index 0 is the forge count.</summary>
        public IReadOnlyList<int> HumanCounts { get; }

        /// <summary>Native HumCounts[0]: forge (锻造) count.</summary>
        public int ForgeCount => HumanCounts[0];

        /// <summary>Group online total: sum of per-GameServer slots 1..5 that report (>= 0).</summary>
        public int OnlineCount
        {
            get
            {
                var total = 0;
                for (var i = 1; i < HumanCounts.Count; i++)
                    if (HumanCounts[i] >= 0)
                        total += HumanCounts[i];
                return total;
            }
        }

        public byte[] RawPayload { get; }

        private static byte[] Clone(byte[]? value) =>
            value == null ? Array.Empty<byte>() : (byte[])value.Clone();
    }

    /// <summary>
    /// DGM_SELECT_SERVER (2001) response. Maps one-for-one onto the native
    /// TSelectGroupInfo record (uTypes.pas): the DBServer tells LoginGate which
    /// resource GameGate to route the client to, plus session and encrypt indices.
    /// </summary>
    public sealed class NativeLoginGateProbeRoute
    {
        public NativeLoginGateProbeRoute(uint sessionId, int enCodeIndex,
            ushort socketHandle, ushort port, byte[] ipv4AddressBytes,
            ushort areaIndex, byte groupIndex, byte errorType,
            byte[] suffix, byte[] rawPayload)
        {
            SessionId = sessionId;
            EnCodeIndex = enCodeIndex;
            SocketHandle = socketHandle;
            Port = port;
            Ipv4AddressBytes = Clone(ipv4AddressBytes);
            AreaIndex = areaIndex;
            GroupIndex = groupIndex;
            ErrorType = errorType;
            Suffix = Clone(suffix);
            RawPayload = Clone(rawPayload);
        }

        /// <summary>Native ciSessionID: session correlation id.</summary>
        public uint SessionId { get; }

        /// <summary>Native iEnCodeIdx: dynamic encrypt-table index (-999 marks a mobile client).</summary>
        public int EnCodeIndex { get; }

        /// <summary>Native wSocketHandle.</summary>
        public ushort SocketHandle { get; }

        /// <summary>Native wGatePort: resource GameGate port.</summary>
        public ushort Port { get; }

        /// <summary>Native ciGateIP: resource GameGate IPv4 address.</summary>
        public byte[] Ipv4AddressBytes { get; }

        /// <summary>Native wAreaID.</summary>
        public ushort AreaIndex { get; }

        /// <summary>Native bGroupNo.</summary>
        public byte GroupIndex { get; }

        /// <summary>Native bErrorType: DB-supplied selection error/status byte.</summary>
        public byte ErrorType { get; }

        /// <summary>Native szPostfix[8]: account suffix.</summary>
        public byte[] Suffix { get; }

        public byte[] RawPayload { get; }

        private static byte[] Clone(byte[]? value) =>
            value == null ? Array.Empty<byte>() : (byte[])value.Clone();
    }

    /// <summary>
    /// DGM_DirectLoginCenterAuth (2018) request. Maps onto the native
    /// TLoginCenterAuthInfo record: a TSDKAuthHead followed by szAuthID (the login
    /// ticket), szPwd, szClientIP and szMacAddr. Authentication uses ONLY the
    /// operator's account <see cref="Ticket"/> (szAuthID); the password/IP/MAC
    /// vendor-SDK fields are captured for protocol fidelity but are intentionally
    /// not enforced (vendor authentication disabled).
    /// </summary>
    public sealed class NativeLoginGateAuthRequest
    {
        public NativeLoginGateAuthRequest(byte reserved0, byte protocolVersion,
            int queryId, byte[] reserved6To11, string ticket,
            byte[] passwordSlot, string clientIp, string macAddress,
            ushort areaId, ushort groupId, byte[] rawPayload)
        {
            Reserved0 = reserved0;
            ProtocolVersion = protocolVersion;
            QueryId = queryId;
            Reserved6To11 = Clone(reserved6To11);
            Ticket = ticket ?? string.Empty;
            PasswordSlot = Clone(passwordSlot);
            ClientIp = clientIp ?? string.Empty;
            MacAddress = macAddress ?? string.Empty;
            AreaId = areaId;
            GroupId = groupId;
            RawPayload = Clone(rawPayload);
        }

        public byte Reserved0 { get; }
        public byte ProtocolVersion { get; }
        public int QueryId { get; }
        public byte[] Reserved6To11 { get; }

        /// <summary>Native szAuthID: the operator account login ticket — the only field used to authenticate.</summary>
        public string Ticket { get; }

        /// <summary>Native szPwd. Vendor-SDK field, not enforced.</summary>
        public byte[] PasswordSlot { get; }

        /// <summary>Native szClientIP. Vendor-SDK field, not enforced.</summary>
        public string ClientIp { get; }

        /// <summary>Native szMacAddr (machine binding). Vendor-SDK field, not enforced.</summary>
        public string MacAddress { get; }

        public ushort AreaId { get; }
        public ushort GroupId { get; }
        public byte[] RawPayload { get; }

        private static byte[] Clone(byte[]? value) =>
            value == null ? Array.Empty<byte>() : (byte[])value.Clone();
    }
}

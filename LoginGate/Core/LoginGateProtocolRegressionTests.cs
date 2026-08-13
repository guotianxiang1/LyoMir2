using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SystemModule.Packet;

namespace LoginGate.Core
{
    /// <summary>Offline byte fixtures callable by a host or audit project.</summary>
    public static class LoginGateProtocolRegressionTests
    {
        public static IReadOnlyList<string> RunAll()
        {
            var passed = new List<string>();
            Run("client CONNECT fixture", TestConnect, passed);
            Run("client 4001 fixture", TestServerList, passed);
            Run("client 4002 selection and jump fixtures", TestSelectionAndJump, passed);
            Run("client endpoint bounds", TestClientBounds, passed);
            Run("native registration and ACK", TestNativeRegistration, passed);
            Run("native probe request and response", TestNativeProbe, passed);
            Run("native select-group-info and error frame", TestSelectGroupInfoAndError, passed);
            Run("native auth request", TestNativeAuthRequest, passed);
            Run("native 1003 response variants", TestNativeAuthResponses, passed);
            Run("native 1004 failure bounds", TestNativeAuthFailureBounds, passed);
            return passed;
        }

        private static void TestConnect()
        {
            var fixture = Convert.FromHexString(
                "44FF44FF02180000B4000000");
            Check(LoginGateWireProtocol.TryDecodeClientFrame(fixture,
                out var decoded, out var error), error);
            Check(LoginGateWireProtocol.TryParseConnectRequest(decoded,
                out var dataIndex, out error), error);
            Equal((uint)180, dataIndex, "CONNECT DataIndex");

            var created = LoginGateWireProtocol.CreateConnectRequest(180);
            Check(LoginGateWireProtocol.TryEncodeClientFrame(created,
                out var encoded, out error), error);
            Bytes(fixture, encoded, "CONNECT bytes");
        }

        private static void TestServerList()
        {
            var fixture = Convert.FromHexString(
                "44FF44FF00173400530C0000" +
                "00000000A10F010000000000" +
                "C2EAB7A8CCE5D1E9B7FE000000000000" +
                "C2EAB7A8CCE5D1E9B7FE0000000000000000000000000000");
            var groupName = DecodeGbk("C2EAB7A8CCE5D1E9B7FE");
            Check(LoginGateWireProtocol.TryCreateServerListFrame(
                3155, groupName, groupName, out var frame, out var error), error);
            Equal(LoginGateWireProtocol.ServerListPayloadSize,
                frame.Payload.Length, "4001 payload length");
            Check(LoginGateWireProtocol.TryEncodeClientFrame(frame,
                out var encoded, out error), error);
            Bytes(fixture, encoded, "4001 fixture bytes");

            Check(LoginGateWireProtocol.TryCreateServerListFrame(3156,
                [(groupName, groupName), ("SECOND", "SECOND-DESC")],
                out var multiple, out error), error);
            Equal(92, multiple.Payload.Length, "multi-group 4001 payload length");
            Check(LoginGateWireProtocol.TryParseInnerHeader(multiple.Payload,
                out var multipleHeader, out error), error);
            Equal((ushort)2, multipleHeader.Param, "multi-group 4001 count");
            Equal("SECOND", ReadGbkSlot(multiple.Payload.AsSpan(52, 16)),
                "multi-group second name");
            Equal("SECOND-DESC", ReadGbkSlot(multiple.Payload.AsSpan(68, 16)),
                "multi-group second description");

            var maximumGroups = Enumerable.Range(1, 32)
                .Select(index => ($"G{index}", $"D{index}"))
                .ToArray();
            Check(LoginGateWireProtocol.TryCreateServerListFrame(3157,
                maximumGroups, out var maximumList, out error), error);
            Equal(1292, maximumList.Payload.Length,
                "32-group 4001 payload length");
            Check(LoginGateWireProtocol.TryEncodeClientFrame(maximumList,
                out var maximumListWire, out error), error);
            Check(LoginGateWireProtocol.TryDecodeClientFrame(maximumListWire,
                out var decodedMaximumList, out error), error);
            Equal(1292, decodedMaximumList.Payload.Length,
                "32-group 4001 wire roundtrip");

            // Native StrPLCopy(...,15) truncates an over-long name/desc to 15 bytes
            // + null and continues; the frame builder must not throw or drop it.
            Check(LoginGateWireProtocol.TryCreateServerListFrame(3158,
                "ABCDEFGHIJKLMNOPQRSTUV", "1234567890ABCDEFGHIJ",
                out var truncated, out error), error);
            Equal("ABCDEFGHIJKLMNO",
                ReadGbkSlot(truncated.Payload.AsSpan(12, 16)),
                "oversized group name truncated to 15 bytes");
            Equal("1234567890ABCDE",
                ReadGbkSlot(truncated.Payload.AsSpan(28, 16)),
                "oversized group desc truncated to 15 bytes");
            Equal((byte)0, truncated.Payload[12 + 15],
                "truncated name null terminator");
            Equal((byte)0, truncated.Payload[28 + 15],
                "truncated desc null terminator");
        }

        private static void TestSelectionAndJump()
        {
            var selectionFixture = Convert.FromHexString(
                "44FF44FF001717009A060000" +
                "FFFFFFFFA20F010001000000C2EAB7A8CCE5D1E9B7FE00");
            Check(LoginGateWireProtocol.TryDecodeClientFrame(selectionFixture,
                out var frame, out var error), error);
            Check(LoginGateWireProtocol.TryParseSelectServerRequest(frame,
                out var selection, out error), error);
            Equal((uint)1690, selection.DataIndex, "4002 selection DataIndex");
            Equal(-1, selection.Inner.Recog, "4002 selection Recog");
            Equal(LoginGateWireProtocol.SelectServerIdent,
                selection.Inner.Ident, "4002 selection Ident");
            Equal((ushort)1, selection.Inner.Param, "4002 selection Param");
            Equal((ushort)1, selection.Inner.Tag, "4002 selection Tag");
            Equal(DecodeGbk("C2EAB7A8CCE5D1E9B7FE"),
                selection.SelectedName, "4002 selection name");

            var trailing = new LoginGateClientFrame(0, 0x17, 1,
                frame.Payload.Concat(new byte[] { 0 }).ToArray());
            Check(!LoginGateWireProtocol.TryParseSelectServerRequest(trailing,
                out _, out _), "4002 selection accepted bytes after its terminator");

            var jumpFixture = Convert.FromHexString(
                "44FF44FF00172000530C0000" +
                "DC070000A20FBC1B7CDD600F" +
                "B400000001000000000000000000000000000000");
            Check(LoginGateWireProtocol.TryCreateSelectServerJumpFrame(
                3155, 2012, "124.221.96.15", 7100, 180, 1, string.Empty,
                out var jump, out error), error);
            Equal((byte)0, jump.Payload[20], "4002 jump sdoa");
            Check(LoginGateWireProtocol.TryEncodeClientFrame(jump,
                out var jumpBytes, out error), error);
            Bytes(jumpFixture, jumpBytes, "4002 jump fixture bytes");
        }

        private static void TestClientBounds()
        {
            var maximum = new LoginGateClientFrame(0, 0x17, 1,
                new byte[LoginGateWireProtocol.ClientMaximumPayloadSize]);
            Check(LoginGateWireProtocol.TryEncodeClientFrame(maximum,
                out var encoded, out var error), error);
            Check(LoginGateWireProtocol.TryDecodeClientFrame(encoded,
                out _, out error), error);

            var oversized = new LoginGateClientFrame(0, 0x17, 1,
                new byte[LoginGateWireProtocol.ClientMaximumPayloadSize + 1]);
            Check(LoginGateWireProtocol.TryEncodeClientFrame(oversized,
                out var oversizedWire, out error), error);
            var parser = new LoginGateClientStreamParser();
            var rejected = false;
            try { parser.Append(oversizedWire, _ => { }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "client endpoint accepted a 244-byte inbound payload");

            var wireOversized = new LoginGateClientFrame(0, 0x17, 1,
                new byte[LoginGateWireProtocol.ClientWireMaximumPayloadSize + 1]);
            Check(!LoginGateWireProtocol.TryEncodeClientFrame(wireOversized,
                out _, out _), "client wire accepted a 65536-byte payload");

            var extraByte = encoded.Concat(new byte[] { 0 }).ToArray();
            Check(!LoginGateWireProtocol.TryDecodeClientFrame(extraByte,
                out _, out _), "client decoder accepted trailing frame bytes");
        }

        private static void TestNativeRegistration()
        {
            var fixture = Convert.FromHexString(
                "77BBAA330000000000000000D0072800" +
                "C2EAB7A8CCE5D1E9B7FE00000000000000000000" +
                "09000000FFFFFFFFFFFFFFFFFFFFFFFF00000000");
            Check(YbDbLegacy77Codec.TryDecode(fixture,
                out var frame, out var error), error);
            Check(LoginGateWireProtocol.TryParseNativeRegistration(frame,
                out var registration, out error), error);
            Equal(DecodeGbk("C2EAB7A8CCE5D1E9B7FE"),
                registration.ServerName, "native registration name");
            Equal(0, registration.ForgeCount, "native registration forge count");
            Equal(9, registration.OnlineCount, "native registration online total");
            Equal(9, registration.HumanCounts[1], "native registration GS1 count");
            Equal(-1, registration.HumanCounts[2], "native registration GS2 slot");
            Equal(-1, registration.HumanCounts[3], "native registration GS3 slot");
            Equal(-1, registration.HumanCounts[4], "native registration GS4 slot");
            Equal(0, registration.HumanCounts[5], "native registration GS5 slot");

            Check(YbDbLegacy77Codec.TryEncode(
                LoginGateWireProtocol.CreateNativeRegistrationAck(),
                out var ack, out error), error);
            Bytes(Convert.FromHexString(
                "77BBAA330000000000000000E8030000"), ack,
                "native registration ACK bytes");
        }

        private static void TestNativeProbe()
        {
            var requestFixture = Convert.FromHexString(
                "77BBAA330000000000000000E9031C00" +
                "4407000019FCFFFF4F0B000000000000B40001000000000000000000");
            Check(YbDbLegacy77Codec.TryDecode(requestFixture,
                out var decodedRequest, out var error), error);
            Check(LoginGateWireProtocol.TryCreateNativeProbeRequest(
                decodedRequest.Payload, out var createdRequest, out error), error);
            Check(YbDbLegacy77Codec.TryEncode(createdRequest,
                out var requestBytes, out error), error);
            Bytes(requestFixture, requestBytes, "native probe request bytes");

            var responseFixture = Convert.FromHexString(
                "77BBAA330000000000000000D1071C00" +
                "4407000019FCFFFF4F0BBC1B7CDD600FB40001000000000000000000");
            Check(YbDbLegacy77Codec.TryDecode(responseFixture,
                out var response, out error), error);
            Check(LoginGateWireProtocol.TryParseNativeProbeResponse(response,
                out var route, out error), error);
            Equal(1860u, route.SessionId, "native probe session id");
            Equal(-999, route.EnCodeIndex, "native probe encrypt index (mobile sentinel)");
            Equal((ushort)2895, route.SocketHandle, "native probe socket handle");
            Equal((ushort)7100, route.Port, "native probe port");
            Bytes(Convert.FromHexString("7CDD600F"),
                route.Ipv4AddressBytes, "native probe IPv4 bytes");
            Equal((ushort)180, route.AreaIndex, "native probe area");
            Equal((byte)1, route.GroupIndex, "native probe group");
            Equal((byte)0, route.ErrorType, "native probe error type");
        }

        private static void TestSelectGroupInfoAndError()
        {
            Check(LoginGateWireProtocol.TryCreateSelectGroupInfo(
                1860, LoginGateWireProtocol.NativeMobileEncodeIndex, 2895,
                180, 1, "", out var payload, out var error), error);
            Equal(28, payload.Length, "TSelectGroupInfo size");
            Equal(1860u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4)),
                "ciSessionID");
            Equal(LoginGateWireProtocol.NativeMobileEncodeIndex,
                BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)),
                "iEnCodeIdx mobile sentinel");
            Equal((ushort)2895,
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(8, 2)),
                "wSocketHandle");
            Equal((ushort)0,
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(10, 2)),
                "wGatePort left for DB");
            Equal((ushort)180,
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(16, 2)),
                "wAreaID");
            Equal((byte)1, payload[18], "bGroupNo");
            Equal((byte)0, payload[19], "bErrorType left for DB");

            Check(LoginGateWireProtocol.TryCreateSelectServerErrorFrame(
                3155, 3, out var fail, out error), error);
            Equal(12, fail.Payload.Length, "error frame is TDefaultMessage only");
            Check(LoginGateWireProtocol.TryParseInnerHeader(fail.Payload,
                out var inner, out error), error);
            Equal(LoginGateWireProtocol.SelectServerIdent, inner.Ident,
                "error ident");
            Equal(0, inner.Recog, "error Recog stays 0");
            Equal((ushort)3, inner.Series, "error Series = wRes");

            var secondZoneSession = unchecked((int)(1000u
                ^ LoginGateWireProtocol.NativeSecondZoneSessionXor));
            Check(secondZoneSession < 0, "SecondZone session is negative as int");
            Check(LoginGateWireProtocol.TryCreateSelectServerJumpFrame(
                3155, secondZoneSession, "127.0.0.1", 7100, 180, 1, "",
                out var jump, out error), error);
            Check(LoginGateWireProtocol.TryParseInnerHeader(jump.Payload,
                out var jumpInner, out error), error);
            Equal(secondZoneSession, jumpInner.Recog, "jump Recog keeps xor bits");
        }

        private static void TestNativeAuthRequest()
        {
            var fixture = Convert.FromHexString(
                "77BBAA330000000000000000E2078800000137090000000000000000" +
                "6334313361626566306431623637316335386461353933616239336437633936" +
                "0000000000000000000000000000000000000000" +
                "4CF2D1CFFFFFFFFF000000000000000000000000000000000000000000000000" +
                "3232332E3136302E3230332E31333500" +
                "6D6F62696C652D6D61632D616464726573730000B4000100");
            Check(YbDbLegacy77Codec.TryDecode(fixture,
                out var frame, out var error), error);
            Check(LoginGateWireProtocol.TryParseNativeAuthRequest(frame,
                out var request, out error), error);
            Equal((byte)0, request.Reserved0, "native auth reserve +0");
            Equal((byte)1, request.ProtocolVersion, "native auth version");
            Equal(2359, request.QueryId, "native auth query");
            Equal("c413abef0d1b671c58da593ab93d7c96",
                request.Ticket, "native auth ticket");
            Bytes(Convert.FromHexString("4CF2D1CFFFFFFFFF"),
                request.PasswordSlot.AsSpan(0, 8).ToArray(), "native auth password slot prefix");
            Equal("223.160.203.135", request.ClientIp, "native auth client IP");
            Equal("mobile-mac-address", request.MacAddress, "native auth MAC address");
            Equal((ushort)180, request.AreaId, "native auth area");
            Equal((ushort)1, request.GroupId, "native auth group");
        }

        private static void TestNativeAuthResponses()
        {
            Check(LoginGateWireProtocol.TryCreateNativeAuthResponse12(
                6, 1, 2359, out var shortFrame, out var error), error);
            Equal(12, shortFrame.Payload.Length, "native 1003 short length");
            Equal(LoginGateWireProtocol.NativeAuthResponseIdent,
                shortFrame.Ident, "native 1003 short ident");
            // On the LoginCenterAuth path success is nResult = LC_AUTH_SUCCESS = 0
            // (uSDKAuth.pas:549 + :565), so +8 must be an explicit zero.
            Equal(LoginGateWireProtocol.NativeLcAuthSuccess,
                BinaryPrimitives.ReadInt32LittleEndian(shortFrame.Payload.AsSpan(8, 4)),
                "native 1003 nResult");

            Check(LoginGateWireProtocol.TryCreateNativeAuthResponse20(
                6, 1, 2359, Enumerable.Range(1, 8).Select(i => (byte)i).ToArray(),
                out var mediumFrame, out error), error);
            Equal(20, mediumFrame.Payload.Length, "native 1003 medium length");
            Bytes(Enumerable.Range(1, 8).Select(i => (byte)i).ToArray(),
                mediumFrame.Payload.AsSpan(12, 8).ToArray(),
                "native 1003 medium opaque tail");

            var fullTail = new byte[112];
            fullTail[0] = 0x5A;
            fullTail[^1] = 0xA5;
            Check(LoginGateWireProtocol.TryCreateNativeAuthResponse124(
                6, 1, 2359, fullTail, out var fullFrame, out error), error);
            Equal(124, fullFrame.Payload.Length, "native 1003 full length");
            Bytes(fullTail, fullFrame.Payload.AsSpan(12).ToArray(),
                "native 1003 full opaque tail");
            Check(!LoginGateWireProtocol.TryCreateNativeAuthResponse124(
                6, 1, 2359, new byte[111], out _, out _),
                "native 1003 accepted a 123-byte payload shape");
        }

        private static void TestNativeAuthFailureBounds()
        {
            const byte authType = LoginGateWireProtocol.NativeAuthTypeLoginCenter;
            const int failed = LoginGateWireProtocol.NativeLcAuthFailed;

            Check(LoginGateWireProtocol.TryCreateNativeAuthFailure(
                authType, 1, 2359, failed, null, out var noText, out var error), error);
            Equal(12, noText.Payload.Length, "native 1004 no-text length");
            Equal(LoginGateWireProtocol.NativeAuthFailureIdent,
                noText.Ident, "native 1004 ident");
            // wAuthType stays atLoginCenterAuth on the failure reply
            // (uSDKAuth.pas:1476 sets it, :1624 ships the stored head).
            Equal(authType, noText.Payload[0], "native 1004 wAuthType");
            Equal((byte)1, noText.Payload[1], "native 1004 GateIdx echo");
            Equal(2359, BinaryPrimitives.ReadInt32LittleEndian(
                noText.Payload.AsSpan(2, 4)), "native 1004 handle/dyn-ident echo");
            Equal(failed, BinaryPrimitives.ReadInt32LittleEndian(
                noText.Payload.AsSpan(8, 4)), "native 1004 nResult");

            // uSDKAuth.pas:1128 gates the tail on StrLen+1 in [2..100], so an empty
            // message is dropped entirely rather than becoming a 13th NUL byte.
            Check(LoginGateWireProtocol.TryCreateNativeAuthFailure(
                authType, 1, 2359, failed, string.Empty,
                out var emptyText, out error), error);
            Equal(12, emptyText.Payload.Length, "native 1004 empty-text length");

            Check(LoginGateWireProtocol.TryCreateNativeAuthFailure(
                authType, 1, 2359, failed, new string('x', 99),
                out var maximum, out error), error);
            Equal(112, maximum.Payload.Length, "native 1004 maximum length");
            Equal((byte)0, maximum.Payload[^1], "native 1004 terminator");
            Check(!LoginGateWireProtocol.TryCreateNativeAuthFailure(
                authType, 1, 2359, failed, new string('x', 100), out _, out _),
                "native 1004 accepted 100 text bytes");
        }

        private static string DecodeGbk(string hex)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936).GetString(Convert.FromHexString(hex));
        }

        private static string ReadGbkSlot(ReadOnlySpan<byte> slot)
        {
            var end = slot.IndexOf((byte)0);
            if (end < 0) end = slot.Length;
            return Encoding.GetEncoding(936).GetString(slot.Slice(0, end));
        }

        private static void Run(string name, Action test, ICollection<string> passed)
        {
            test();
            passed.Add(name);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    $"{name}: expected {expected}, got {actual}");
        }

        private static void Bytes(byte[] expected, byte[] actual, string name)
        {
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException(
                    $"{name}: expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}");
        }
    }
}

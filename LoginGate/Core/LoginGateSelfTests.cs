using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SystemModule.Packet;

namespace LoginGate.Core;

internal static class LoginGateSelfTests
{
    public static async Task<int> RunAsync()
    {
        var passed = new List<string>();
        try
        {
            foreach (var name in LoginGateProtocolRegressionTests.RunAll())
                passed.Add(name);
            Run("preserving GBK config", TestConfigRoundTrip, passed);
            Run("client split and sticky parser", TestClientStreamParser, passed);
            await RunAsync("loopback service lifecycle and wires",
                TestLoopbackServicesAsync, passed);
            Console.WriteLine($"LoginGate self-test: PASS ({passed.Count}/{passed.Count})");
            foreach (var name in passed) Console.WriteLine("PASS " + name);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("LoginGate self-test: FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestConfigRoundTrip()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding(936);
        var directory = Path.Combine(Path.GetTempPath(),
            "logingate-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "LoginGate.ini");
        try
        {
            var input = "[Setup]\r\n" +
                        "LoginGateListen = 7000 ;; keep\r\n" +
                        "DBServerListen=5600\r\nPIGServerListen=5100\r\n" +
                        "PIGServerIP=127.0.0.1\r\n" +
                        "Project=1\r\nSecondZone=0\r\nDenySpreader=0\r\n" +
                        "PK_Warning=1\r\nDebugMode=0\r\nCompressMode=0\r\n" +
                        "UnknownKey=preserve\r\n\r\n" +
                        "/*\r\n[Area1]\r\ngroup9name=ignored\r\n*/\r\n" +
                        "[DBServerIP]\r\nIPAddress1=127.0.0.1\r\n\r\n" +
                        "[Area1]\r\nAreaIdx=180\r\nSuffix=\r\n" +
                        "group1DBS=LOCAL-OFFLINE\r\n" +
                        "group1name=LOCAL-OFFLINE\r\n" +
                        "group1Desc=LOCAL-OFFLINE\r\n" +
                        "group1idx=1\r\n\r\n" +
                        "[Area2]\r\nAreaIdx=181\r\nSuffix=B\r\n" +
                        "group1DBS=AREA-TWO\r\n" +
                        "group1name=AREA-TWO\r\n" +
                        "group1Desc=AREA-TWO\r\n" +
                        "group1idx=2\r\n";
            File.WriteAllBytes(path, gbk.GetBytes(input));
            var config = LoginGateConfig.Load(path);
            Equal(1, config.Groups.Count, "config group count");
            Equal("127.0.0.1", config.PIGServerIP, "config PIG peer IP");
            Equal("LOCAL-OFFLINE", config.Groups[0].DbServerName,
                "config group DBS mapping");
            Equal(2, config.GetConfiguredAreas().Count, "config area count");
            Equal(181, config.GetConfiguredAreas()[1].AreaIdx,
                "config second area index");
            Equal("AREA-TWO", config.GetConfiguredAreas()[1].Groups[0].Name,
                "config second area group");
            config.LoginGateListen = 17000;
            config.Save();
            var output = gbk.GetString(File.ReadAllBytes(path));
            Check(output.Contains("LoginGateListen = 17000 ;; keep",
                StringComparison.Ordinal), "known key formatting/comment was not preserved");
            Check(output.Contains("UnknownKey=preserve", StringComparison.Ordinal),
                "unknown key was dropped");
            Check(output.Contains("group9name=ignored", StringComparison.Ordinal),
                "block comment content was dropped");
            Check(!output.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n'),
                "config output contains non-CRLF line endings");
            var reloaded = LoginGateConfig.Load(path);
            Equal(2, reloaded.GetConfiguredAreas().Count,
                "reloaded config area count");
            Equal("B", reloaded.GetConfiguredAreas()[1].Suffix,
                "reloaded second area suffix");
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static void TestClientStreamParser()
    {
        Check(LoginGateWireProtocol.TryEncodeClientFrame(
            LoginGateWireProtocol.CreateConnectRequest(180),
            out var first, out var error), error);
        Check(LoginGateWireProtocol.TryEncodeClientFrame(
            LoginGateWireProtocol.CreateConnectRequest(181),
            out var second, out error), error);
        var wire = new byte[first.Length + second.Length + 2];
        wire[0] = 0xAA;
        wire[1] = 0xBB;
        first.CopyTo(wire, 2);
        second.CopyTo(wire, 2 + first.Length);

        var parser = new LoginGateClientStreamParser();
        var frames = new List<LoginGateClientFrame>();
        parser.Append(wire.AsSpan(0, 5), frames.Add);
        parser.Append(wire.AsSpan(5, 8), frames.Add);
        parser.Append(wire.AsSpan(13), frames.Add);
        Equal(2, frames.Count, "split/sticky client frame count");
        Equal((uint)180, frames[0].DataIndex, "first split frame");
        Equal((uint)181, frames[1].DataIndex, "second sticky frame");
        Equal(0, parser.BufferedLength, "client parser buffered bytes");
    }

    private static async Task TestLoopbackServicesAsync()
    {
        var config = new LoginGateConfig
        {
            LoginGateListen = 0,
            DBServerListen = 0,
            PIGServerListen = 0,
            Project = 1,
            AreaIdx = 180
        };
        config.Groups.Add(new LoginGateGroup(
            1, 1, "LOCAL-OFFLINE", "LOCAL-OFFLINE", "LOCAL-OFFLINE"));
        config.Groups.Add(new LoginGateGroup(
            2, 2, "SECOND", "SECOND-DESC", "SECOND"));
        var secondArea = new LoginGateArea(2) { AreaIdx = 181, Suffix = "B" };
        secondArea.Groups.Add(new LoginGateGroup(
            1, 1, "AREA-TWO", "AREA-TWO", "AREA-TWO"));
        config.Areas.Add(secondArea);
        var authenticator = new ConcurrentAuthenticator("testaccount");
        await using var server = new LoginGateServer(config, authenticator);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var startCancellation = new CancellationTokenSource();
        await server.StartAsync(startCancellation.Token);
        startCancellation.Cancel();
        await Task.Delay(20, timeout.Token);
        var started = server.GetStats();
        Check(started.Running, "caller cancellation stopped the running server");
        Check(started.ClientListenPort > 0, "client listener did not bind");
        Check(started.DbServerListenPort > 0, "DB listener did not bind");
        Check(started.PigServerListenPort > 0, "PIG listener did not bind");

        using var native = new TcpClient();
        await native.ConnectAsync(IPAddress.Loopback, started.DbServerListenPort,
            timeout.Token);
        var nativeStream = native.GetStream();
        var registration = CreateRegistration("LOCAL-OFFLINE", 9);
        var type2 = new YbDbLegacy77Frame(0, 0,
            LoginGateWireProtocol.NativeType2EnabledIdent, Array.Empty<byte>());
        var stickyNative = EncodeNative(registration).Concat(EncodeNative(type2)).ToArray();
        await nativeStream.WriteAsync(stickyNative.AsMemory(0, 7), timeout.Token);
        await nativeStream.WriteAsync(stickyNative.AsMemory(7), timeout.Token);

        var ack = DecodeNative(await ReadNativeWireAsync(nativeStream, timeout.Token));
        Equal(LoginGateWireProtocol.NativeRegistrationAckIdent, ack.Ident,
            "registration ACK ident");
        await WaitUntilAsync(() => server.GetBackends().Any(backend =>
            backend.Type2Enabled), timeout.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, started.ClientListenPort,
            timeout.Token);
        var clientStream = client.GetStream();
        var connect = EncodeClient(LoginGateWireProtocol.CreateConnectRequest(180));
        var select = EncodeClient(CreateSelection("LOCAL-OFFLINE"));
        var stickyClient = connect.Concat(select).ToArray();
        await clientStream.WriteAsync(stickyClient, timeout.Token);
        var serverList = DecodeClient(await ReadClientWireAsync(clientStream, timeout.Token));
        Check(LoginGateWireProtocol.TryParseInnerHeader(serverList.Payload,
            out var listInner, out var listError), listError);
        Equal(LoginGateWireProtocol.ServerListIdent, listInner.Ident,
            "server-list inner ident");
        Equal((ushort)2, listInner.Param, "server-list group count");
        Equal(92, serverList.Payload.Length, "server-list multi-group payload length");

        // uDBListen.pas:231: 1001 is issued by the select, not by registration.
        var selectRequest = DecodeNative(await ReadNativeWireAsync(nativeStream, timeout.Token));
        Equal(LoginGateWireProtocol.NativeSelectServerRequestIdent, selectRequest.Ident,
            "select-server request ident");
        Equal(28, selectRequest.Payload.Length, "TSelectGroupInfo size");
        Equal(LoginGateWireProtocol.NativeMobileEncodeIndex,
            BinaryPrimitives.ReadInt32LittleEndian(selectRequest.Payload.AsSpan(4, 4)),
            "iEnCodeIdx mobile sentinel");
        var selectPayload = (byte[])selectRequest.Payload.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(selectPayload.AsSpan(10, 2), 7100);
        IPAddress.Loopback.GetAddressBytes().CopyTo(selectPayload, 12);
        selectPayload[19] = 0;
        await nativeStream.WriteAsync(EncodeNative(new YbDbLegacy77Frame(0, 0,
            LoginGateWireProtocol.NativeSelectServerResponseIdent, selectPayload)),
            timeout.Token);

        var jump = DecodeClient(await ReadClientWireAsync(clientStream, timeout.Token));
        Check(LoginGateWireProtocol.TryParseInnerHeader(jump.Payload,
            out var jumpInner, out var jumpError), jumpError);
        Equal(LoginGateWireProtocol.SelectServerIdent, jumpInner.Ident,
            "jump inner ident");
        Equal((ushort)7100, jumpInner.Param, "jump GameGate port");
        Check(jumpInner.Recog != 0, "jump session id is non-zero");

        using var areaTwoClient = new TcpClient();
        await areaTwoClient.ConnectAsync(IPAddress.Loopback, started.ClientListenPort,
            timeout.Token);
        var areaTwoStream = areaTwoClient.GetStream();
        await areaTwoStream.WriteAsync(
            EncodeClient(LoginGateWireProtocol.CreateConnectRequest(181)), timeout.Token);
        var areaTwoList = DecodeClient(
            await ReadClientWireAsync(areaTwoStream, timeout.Token));
        Check(LoginGateWireProtocol.TryParseInnerHeader(areaTwoList.Payload,
            out var areaTwoHeader, out var areaTwoError), areaTwoError);
        Equal((ushort)1, areaTwoHeader.Param, "second-area group count");
        Equal("AREA-TWO", ReadGbkSlot(areaTwoList.Payload.AsSpan(12, 16)),
            "second-area group name");

        var authWire = EncodeNative(CreateAuthRequest(42, "ticket-a"))
            .Concat(EncodeNative(CreateAuthRequest(43, "ticket-b"))).ToArray();
        await nativeStream.WriteAsync(authWire.AsMemory(0, 11), timeout.Token);
        await nativeStream.WriteAsync(authWire.AsMemory(11), timeout.Token);
        await authenticator.ReleaseAfterConcurrentEntryAsync(timeout.Token);
        var authResponses = new[]
        {
            DecodeNative(await ReadNativeWireAsync(nativeStream, timeout.Token)),
            DecodeNative(await ReadNativeWireAsync(nativeStream, timeout.Token))
        };
        var responseQueries = new HashSet<int>();
        foreach (var authResponse in authResponses)
        {
            Equal(LoginGateWireProtocol.NativeAuthResponseIdent, authResponse.Ident,
                "auth success ident");
            Equal(LoginGateWireProtocol.NativeAuthResponseFullPayloadSize,
                authResponse.Payload.Length, "auth success payload length");
            Equal((byte)6, authResponse.Payload[0], "auth success status");
            responseQueries.Add(BinaryPrimitives.ReadInt32LittleEndian(
                authResponse.Payload.AsSpan(2, 4)));
            Equal("testaccount", ReadAsciiSlot(authResponse.Payload.AsSpan(12, 21)),
                "auth response account");
        }
        Check(responseQueries.SetEquals([42, 43]), "concurrent auth query ids were not preserved");

        await server.StopAsync();
        var stopped = server.GetStats();
        Equal(0, stopped.ClientListenPort, "client listener after stop");
        Equal(0, stopped.DbServerListenPort, "DB listener after stop");
        Equal(0, stopped.PigServerListenPort, "PIG listener after stop");
        await server.StartAsync(timeout.Token);
        Check(server.GetStats().Running, "server did not restart");
        await server.StopAsync();
    }

    private static YbDbLegacy77Frame CreateRegistration(string name, int online)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var payload = new byte[LoginGateWireProtocol.NativeRegistrationPayloadSize];
        Encoding.GetEncoding(936).GetBytes(name).CopyTo(payload, 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), online);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4), -1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4), -1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(32, 4), -1);
        return new YbDbLegacy77Frame(0, 0,
            LoginGateWireProtocol.NativeRegistrationIdent, payload);
    }

    private static YbDbLegacy77Frame CreateAuthRequest(int queryId, string ticket)
    {
        var payload = new byte[LoginGateWireProtocol.NativeAuthRequestPayloadSize];
        payload[1] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(2, 4), queryId);
        Encoding.ASCII.GetBytes(ticket).CopyTo(payload, 12);
        Enumerable.Range(1, 8).Select(value => (byte)value).ToArray().CopyTo(payload, 64);
        Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(payload, 96);
        Encoding.ASCII.GetBytes("device").CopyTo(payload, 112);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(132, 2), 180);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(134, 2), 1);
        return new YbDbLegacy77Frame(0, 0,
            LoginGateWireProtocol.NativeAuthRequestIdent, payload);
    }

    private static LoginGateClientFrame CreateSelection(string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var nameBytes = Encoding.GetEncoding(936).GetBytes(name);
        var payload = new byte[LoginGateWireProtocol.InnerHeaderSize + nameBytes.Length + 1];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), -1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2),
            LoginGateWireProtocol.SelectServerIdent);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 1);
        nameBytes.CopyTo(payload, LoginGateWireProtocol.InnerHeaderSize);
        return new LoginGateClientFrame(0,
            LoginGateWireProtocol.ClientDataCommand, 1690, payload);
    }

    private static byte[] EncodeNative(YbDbLegacy77Frame frame)
    {
        Check(YbDbLegacy77Codec.TryEncode(frame, out var wire, out var error), error);
        return wire;
    }

    private static YbDbLegacy77Frame DecodeNative(byte[] wire)
    {
        Check(YbDbLegacy77Codec.TryDecode(wire, out var frame, out var error), error);
        return frame;
    }

    private static byte[] EncodeClient(LoginGateClientFrame frame)
    {
        Check(LoginGateWireProtocol.TryEncodeClientFrame(frame,
            out var wire, out var error), error);
        return wire;
    }

    private static LoginGateClientFrame DecodeClient(byte[] wire)
    {
        Check(LoginGateWireProtocol.TryDecodeClientFrame(wire,
            out var frame, out var error), error);
        return frame;
    }

    private static async Task<byte[]> ReadNativeWireAsync(NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[YbDbLegacy77Codec.HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14, 2));
        var wire = new byte[header.Length + payloadLength];
        header.CopyTo(wire, 0);
        if (payloadLength > 0)
            await stream.ReadExactlyAsync(wire.AsMemory(header.Length), cancellationToken);
        return wire;
    }

    private static async Task<byte[]> ReadClientWireAsync(NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[LoginGateWireProtocol.ClientHeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
        var wire = new byte[header.Length + payloadLength];
        header.CopyTo(wire, 0);
        if (payloadLength > 0)
            await stream.ReadExactlyAsync(wire.AsMemory(header.Length), cancellationToken);
        return wire;
    }

    private static async Task WaitUntilAsync(Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
            await Task.Delay(10, cancellationToken);
    }

    private static string ReadAsciiSlot(ReadOnlySpan<byte> slot)
    {
        var end = slot.IndexOf((byte)0);
        if (end < 0) end = slot.Length;
        return Encoding.ASCII.GetString(slot.Slice(0, end));
    }

    private static string ReadGbkSlot(ReadOnlySpan<byte> slot)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var end = slot.IndexOf((byte)0);
        if (end < 0) end = slot.Length;
        return Encoding.GetEncoding(936).GetString(slot.Slice(0, end));
    }

    private static void Run(string name, Action test, ICollection<string> passed)
    {
        test();
        passed.Add(name);
    }

    private static async Task RunAsync(string name, Func<Task> test,
        ICollection<string> passed)
    {
        await test();
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

    private sealed class ConcurrentAuthenticator : ILoginTicketAuthenticator
    {
        private readonly string _account;
        private readonly TaskCompletionSource<bool> _bothEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;
        private int _active;
        private int _maximumActive;

        public ConcurrentAuthenticator(string account) => _account = account;

        public async ValueTask<LoginTicketAuthenticationResult> AuthenticateAsync(
            NativeLoginGateAuthRequest request, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            if (Interlocked.Increment(ref _entered) >= 2)
                _bothEntered.TrySetResult(true);
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return LoginTicketAuthenticationResult.Accepted(_account);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public async Task ReleaseAfterConcurrentEntryAsync(CancellationToken cancellationToken)
        {
            await _bothEntered.Task.WaitAsync(cancellationToken);
            Check(Volatile.Read(ref _maximumActive) >= 2,
                "native authentication requests were serialized");
            _release.TrySetResult(true);
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumActive);
                if (current >= value || Interlocked.CompareExchange(
                        ref _maximumActive, value, current) == current)
                    return;
            }
        }
    }
}

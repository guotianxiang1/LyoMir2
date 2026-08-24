using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LoginGate.Core;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var options = Options.Parse(args);
if (string.IsNullOrWhiteSpace(options.Ticket))
{
    Console.Error.WriteLine("Usage: LiveLoginChainCheck --ticket <ticket> [--host 127.0.0.1] [--login-port 7000] [--area 180] [--server <name>] [--character <name>] [--tiger]");
    return 2;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
try
{
    var selection = await SelectServerAsync(options, timeout.Token);
    var gameHost = options.GameHost ?? selection.Host;
    Console.WriteLine($"PASS select-server session={selection.SessionId} route={selection.Host}:{selection.Port}");

    using var gameClient = new TcpClient { NoDelay = true };
    await gameClient.ConnectAsync(gameHost, selection.Port, timeout.Token);
    var gameStream = gameClient.GetStream();
    var selectedSession = unchecked((uint)selection.SessionId);
    var reader = new MobileFrameReader(gameStream, options.Tiger, selectedSession);

    await WriteGameWireAsync(gameStream, MobileCodec.WriteConnect(selectedSession),
        options.Tiger, 0, timeout.Token);
    var serverInfo = await ReadUntilAsync(reader,
        frame => frame.Inner.Ident == 4003, "server info", timeout.Token);
    Console.WriteLine($"PASS game-connect ident={serverInfo.Inner.Ident}");

    var authBody = CreateAuthBody(options.Ticket);
    await WriteMobileAsync(gameStream, new MobileCodec.InnerHeader
    {
        Recog = 131532307,
        Ident = 4004,
        Param = 2
    }, authBody, 2, options.Tiger, selectedSession, timeout.Token);

    var authAccepted = false;
    MobileCodec.MobileFrame? characterList = null;
    while (characterList == null)
    {
        var frame = await reader.ReadAsync(timeout.Token);
        Trace(frame);
        if (frame.Inner.Ident == 4004)
        {
            if (frame.Inner.Param != 1)
                throw new InvalidOperationException($"authentication rejected: recog={frame.Inner.Recog} param={frame.Inner.Param}");
            authAccepted = true;
        }
        else if (frame.Inner.Ident == 4010)
        {
            characterList = frame;
        }
    }
    if (!authAccepted)
        throw new InvalidOperationException("character list arrived before an authentication success frame");

    var characters = ParseCharacters(characterList.Value);
    if (characters.Count == 0)
        throw new InvalidOperationException("authenticated account returned an empty character list");
    Console.WriteLine("PASS characters " + string.Join(", ", characters));

    var character = string.IsNullOrWhiteSpace(options.Character)
        ? characters[0]
        : characters.FirstOrDefault(value => value.Equals(options.Character,
            StringComparison.Ordinal))
          ?? throw new InvalidOperationException($"character '{options.Character}' is not in the server list");

    var characterBody = MobileCodec.EncodeGbk(character + '\0');
    await WriteMobileAsync(gameStream, new MobileCodec.InnerHeader
    {
        Recog = selection.SessionId,
        Ident = 4017
    }, characterBody, 3, options.Tiger, selectedSession, timeout.Token);

    var startPlay = await ReadUntilAsync(reader,
        frame => frame.Inner.Ident == 4017, "start play", timeout.Token);
    if (startPlay.Inner.Param != 1)
        throw new InvalidOperationException($"character selection failed: recog={startPlay.Inner.Recog} param={startPlay.Inner.Param}");
    Console.WriteLine($"PASS select-character name={character}");

    await WriteMobileAsync(gameStream, new MobileCodec.InnerHeader
    {
        Recog = 0,
        Ident = 1018
    }, CreateNativeClientVersionBody(), 4, options.Tiger, selectedSession,
        timeout.Token);

    var sawLogon = false;
    var sawNewMap = false;
    while (!sawLogon || !sawNewMap)
    {
        var frame = await reader.ReadAsync(timeout.Token);
        Trace(frame);
        sawLogon |= frame.Inner.Ident == Grobal2.SM_LOGON;
        sawNewMap |= frame.Inner.Ident == Grobal2.SM_NEWMAP;
    }

    await WriteGameWireAsync(gameStream, MobileCodec.WriteDisconnect(5),
        options.Tiger, selectedSession, timeout.Token);
    Console.WriteLine($"LiveLoginChainCheck PASS mode={(options.Tiger ? "tiger" : "binary")} character={character} logon={sawLogon} newmap={sawNewMap}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("LiveLoginChainCheck FAIL: " + ex.Message);
    return 1;
}

static async Task<ServerSelection> SelectServerAsync(Options options,
    CancellationToken cancellationToken)
{
    using var client = new TcpClient { NoDelay = true };
    await client.ConnectAsync(options.Host, options.LoginPort, cancellationToken);
    var stream = client.GetStream();

    await WriteLoginAsync(stream,
        LoginGateWireProtocol.CreateConnectRequest((uint)options.Area), cancellationToken);
    var list = await ReadLoginAsync(stream, cancellationToken);
    Require(LoginGateWireProtocol.TryParseInnerHeader(list.Payload,
        out var listHeader, out var listError), listError);
    Require(listHeader.Ident == LoginGateWireProtocol.ServerListIdent,
        $"expected server list, got ident {listHeader.Ident}");

    var names = new List<string>();
    for (var index = 0; index < listHeader.Param; index++)
    {
        var offset = LoginGateWireProtocol.InnerHeaderSize
                     + index * LoginGateWireProtocol.ServerGroupInfoSize;
        names.Add(ReadGbkSlot(list.Payload.AsSpan(offset, 16)));
    }
    var selectedName = string.IsNullOrWhiteSpace(options.ServerName)
        ? names.FirstOrDefault()
        : names.FirstOrDefault(value => value.Equals(options.ServerName,
            StringComparison.Ordinal));
    Require(!string.IsNullOrEmpty(selectedName),
        "requested server is absent; available=" + string.Join(",", names));

    var nameBytes = MobileCodec.EncodeGbk(selectedName);
    var payload = new byte[LoginGateWireProtocol.InnerHeaderSize + nameBytes.Length + 1];
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), -1);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2),
        LoginGateWireProtocol.SelectServerIdent);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 1);
    nameBytes.CopyTo(payload, LoginGateWireProtocol.InnerHeaderSize);
    await WriteLoginAsync(stream, new LoginGateClientFrame(0,
        LoginGateWireProtocol.ClientDataCommand, list.DataIndex, payload), cancellationToken);

    var jump = await ReadLoginAsync(stream, cancellationToken);
    Require(LoginGateWireProtocol.TryParseInnerHeader(jump.Payload,
        out var jumpHeader, out var jumpError), jumpError);
    Require(jumpHeader.Ident == LoginGateWireProtocol.SelectServerIdent,
        $"expected server jump, got ident {jumpHeader.Ident}");
    Require(jumpHeader.Recog != 0 && jumpHeader.Param != 0,
        $"server selection failed: series={jumpHeader.Series}");
    var address = new IPAddress([
        (byte)jumpHeader.Tag,
        (byte)(jumpHeader.Tag >> 8),
        (byte)jumpHeader.Series,
        (byte)(jumpHeader.Series >> 8)]).ToString();
    return new ServerSelection(jumpHeader.Recog, address, jumpHeader.Param);
}

static byte[] CreateAuthBody(string ticket)
{
    var ticketBytes = Encoding.ASCII.GetBytes(ticket);
    var gameType = Encoding.ASCII.GetBytes("gametea");
    var deviceName = Encoding.ASCII.GetBytes("mobile-mac-address");
    var body = new byte[ticketBytes.Length + 1 + 8 + 1
                        + gameType.Length + 1 + deviceName.Length + 1];
    var offset = 0;
    ticketBytes.CopyTo(body, offset);
    offset += ticketBytes.Length + 1;
    Convert.FromHexString("4CF2D1CFFFFFFFFF").CopyTo(body, offset);
    offset += 9;
    gameType.CopyTo(body, offset);
    offset += gameType.Length + 1;
    deviceName.CopyTo(body, offset);
    return body;
}

static List<string> ParseCharacters(MobileCodec.MobileFrame frame)
{
    var result = new List<string>();
    const int recordSize = 20;
    var count = Math.Min(frame.Inner.Param, frame.Body.Length / recordSize);
    for (var index = 0; index < count; index++)
    {
        var offset = index * recordSize;
        var length = Math.Min(frame.Body[offset], (byte)14);
        if (length == 0) continue;
        result.Add(MobileCodec.Gbk.GetString(frame.Body, offset + 1, length));
    }
    return result;
}

static async Task<MobileCodec.MobileFrame> ReadUntilAsync(MobileFrameReader reader,
    Func<MobileCodec.MobileFrame, bool> predicate, string expected,
    CancellationToken cancellationToken)
{
    while (true)
    {
        var frame = await reader.ReadAsync(cancellationToken);
        Trace(frame);
        if (predicate(frame)) return frame;
        if (frame.Inner.Ident == 4018)
            throw new InvalidOperationException($"server closed the login state while waiting for {expected}");
    }
}

static void Trace(MobileCodec.MobileFrame frame) => Console.WriteLine(
    $"RECV ident={frame.Inner.Ident} recog={frame.Inner.Recog} param={frame.Inner.Param} body={frame.Body.Length}");

static async Task WriteMobileAsync(NetworkStream stream,
    MobileCodec.InnerHeader inner, byte[] body, uint sequence,
    bool tiger, uint tigerKeyOffset, CancellationToken cancellationToken)
{
    var wire = MobileCodec.WriteFrame(inner, body, sequence, MobileCodec.MARKER_DATA);
    await WriteGameWireAsync(stream, wire, tiger, tigerKeyOffset, cancellationToken);
}

// The 2.08 client sends the fixed TOsVersion3 payload on CM_LOGINNOTICEOK.
// The captured login frame is exactly 81 bytes (all zero for this build).
static byte[] CreateNativeClientVersionBody() => new byte[81];

static async Task WriteGameWireAsync(NetworkStream stream, byte[] frame,
    bool tiger, uint tigerKeyOffset, CancellationToken cancellationToken)
{
    var wire = tiger
        ? Encoding.ASCII.GetBytes(TigerReferenceCodec.Encode(frame, tigerKeyOffset))
        : frame;
    await stream.WriteAsync(wire, cancellationToken);
}

static async Task WriteLoginAsync(NetworkStream stream, LoginGateClientFrame frame,
    CancellationToken cancellationToken)
{
    Require(LoginGateWireProtocol.TryEncodeClientFrame(frame,
        out var wire, out var error), error);
    await stream.WriteAsync(wire, cancellationToken);
}

static async Task<LoginGateClientFrame> ReadLoginAsync(NetworkStream stream,
    CancellationToken cancellationToken)
{
    var header = new byte[LoginGateWireProtocol.ClientHeaderSize];
    await stream.ReadExactlyAsync(header, cancellationToken);
    var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
    var wire = new byte[header.Length + payloadLength];
    header.CopyTo(wire, 0);
    if (payloadLength > 0)
        await stream.ReadExactlyAsync(wire.AsMemory(header.Length), cancellationToken);
    Require(LoginGateWireProtocol.TryDecodeClientFrame(wire,
        out var frame, out var error), error);
    return frame;
}

static string ReadGbkSlot(ReadOnlySpan<byte> value)
{
    var end = value.IndexOf((byte)0);
    if (end < 0) end = value.Length;
    return end == 0 ? string.Empty : MobileCodec.Gbk.GetString(value[..end]);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record ServerSelection(int SessionId, string Host, int Port);

sealed class MobileFrameReader(NetworkStream stream, bool tiger,
    uint tigerKeyOffset)
{
    private byte[] _buffer = new byte[4096];
    private int _length;

    public async Task<MobileCodec.MobileFrame> ReadAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (tiger)
            {
                var suffix = TigerReferenceCodec.FindSuffix(_buffer, _length);
                if (suffix >= 0)
                {
                    var encoded = Encoding.ASCII.GetString(_buffer, 0, suffix);
                    var wire = TigerReferenceCodec.Decode(encoded, tigerKeyOffset);
                    Consume(suffix + 3);
                    if (!MobileCodec.TryReadFrame(wire, 0, wire.Length,
                            out var tigerFrame, out var tigerConsumed)
                        || tigerConsumed != wire.Length)
                        throw new InvalidDataException(
                            "Tiger response did not contain exactly one mobile frame");
                    return tigerFrame;
                }
            }

            var consumed = 0;
            if (!tiger && _length > 0 && MobileCodec.TryReadFrame(_buffer, 0, _length,
                    out var frame, out consumed))
            {
                Consume(consumed);
                return frame;
            }
            if (_length > 0 && consumed > 0)
            {
                Consume(consumed);
                continue;
            }
            if (_length == _buffer.Length)
                Array.Resize(ref _buffer, checked(_buffer.Length * 2));
            var read = await stream.ReadAsync(
                _buffer.AsMemory(_length, _buffer.Length - _length), cancellationToken);
            if (read == 0) throw new EndOfStreamException("game gate closed the connection");
            _length += read;
        }
    }

    private void Consume(int count)
    {
        _length -= count;
        if (_length > 0) Buffer.BlockCopy(_buffer, count, _buffer, 0, _length);
    }
}

static class TigerReferenceCodec
{
    private const string Alphabet =
        "1Y0lSUQMH+mbKXRTBtFiWvLx32/gNAzGr674oeyn5dCEp8jDqasI9VcwJPhufkOZ";

    public static string Encode(ReadOnlySpan<byte> data, uint keyOffset)
    {
        var key = RotatedKey(keyOffset);
        var result = new StringBuilder();
        var offset = 0;
        while (offset < data.Length)
        {
            long chunk = 0;
            var count = 0;
            for (var index = 0; index < 3; index++)
            {
                chunk *= 256;
                if (offset < data.Length)
                {
                    chunk += data[offset++];
                    count++;
                }
            }
            for (var index = 0; index < count + 1; index++)
            {
                result.Append(key[(int)(chunk / 262144 % 64)]);
                chunk *= 64;
            }
            for (var index = 0; index < 3 - count; index++)
                result.Append('=');
        }
        return result.Append("|LH").ToString();
    }

    public static byte[] Decode(string encoded, uint keyOffset)
    {
        var key = RotatedKey(keyOffset);
        var result = new List<byte>();
        var offset = 0;
        while (offset < encoded.Length)
        {
            long value = 0;
            var count = 0;
            for (var index = 0; index < 4 && offset < encoded.Length; index++)
            {
                var character = encoded[offset++];
                if (character == '=') break;
                var keyIndex = key.IndexOf(character);
                if (keyIndex < 0)
                    throw new FormatException("invalid Tiger character");
                value = value * 64 + keyIndex;
                count++;
            }
            for (var index = count - 2; index >= 0; index--)
                result.Add((byte)(value >> (index * 8)));
        }
        return result.ToArray();
    }

    public static int FindSuffix(byte[] buffer, int length)
    {
        for (var index = 0; index <= length - 3; index++)
        {
            if (buffer[index] == (byte)'|' && buffer[index + 1] == (byte)'L'
                && buffer[index + 2] == (byte)'H')
                return index;
        }
        return -1;
    }

    private static string RotatedKey(uint offset)
    {
        var rotation = (int)(offset % 63);
        return rotation == 0
            ? Alphabet
            : Alphabet[rotation..] + Alphabet[..rotation];
    }
}

sealed class Options
{
    public string Host { get; private set; } = "127.0.0.1";
    public int LoginPort { get; private set; } = 7000;
    public int Area { get; private set; } = 180;
    public string? ServerName { get; private set; }
    public string? GameHost { get; private set; }
    public string? Character { get; private set; }
    public string Ticket { get; private set; } = string.Empty;
    public int TimeoutSeconds { get; private set; } = 45;
    public bool Tiger { get; private set; }

    public static Options Parse(string[] arguments)
    {
        var result = new Options();
        for (var index = 0; index < arguments.Length; index++)
        {
            var value = index + 1 < arguments.Length ? arguments[index + 1] : null;
            switch (arguments[index])
            {
                case "--host": result.Host = NeedValue(value, arguments[index]); index++; break;
                case "--login-port": result.LoginPort = int.Parse(NeedValue(value, arguments[index])); index++; break;
                case "--area": result.Area = int.Parse(NeedValue(value, arguments[index])); index++; break;
                case "--server": result.ServerName = NeedValue(value, arguments[index]); index++; break;
                case "--game-host": result.GameHost = NeedValue(value, arguments[index]); index++; break;
                case "--character": result.Character = NeedValue(value, arguments[index]); index++; break;
                case "--ticket": result.Ticket = NeedValue(value, arguments[index]); index++; break;
                case "--timeout": result.TimeoutSeconds = int.Parse(NeedValue(value, arguments[index])); index++; break;
                case "--tiger": result.Tiger = true; break;
                default: throw new ArgumentException("unknown argument: " + arguments[index]);
            }
        }
        return result;
    }

    private static string NeedValue(string? value, string option) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("missing value for " + option)
            : value;
}

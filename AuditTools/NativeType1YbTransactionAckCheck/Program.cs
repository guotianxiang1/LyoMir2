using System.Buffers.Binary;
using GameSvr.Services;
using SystemModule.Packet;

var failures = new List<string>();
Run("0060/0061 exact ACK mapping", ExactMapping);
Run("result equality and 32-bit correlation", ResultAndCorrelation);
Run("malformed callbacks stay silent", Malformed);
Run("runtime enqueue seam preserves one frame", EnqueueSeam);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeType1YbTransactionAckCheck PASS tests=4 " +
                  "type1=0060/0061 ybdb=0468 ident=105/106");
return 0;

void ExactMapping()
{
    foreach (var command in new ushort[] { 0x0060, 0x0061 })
    {
        var source = Response(command, 1, unchecked((int)0x89ABCDEF), 19);
        Assert(NativeType1YbTransactionAck.TryCreateForwardFrame(source,
            out var forward), $"{command:X4} rejected");
        Equal(0x0468, forward.QueryId, $"{command:X4} QueryId");
        Equal(unchecked((int)0x89ABCDEF), forward.Param,
            $"{command:X4} correlation");
        Equal((ushort)105, forward.Ident, $"{command:X4} success ident");
        Equal(0, forward.Payload.Length, $"{command:X4} payload length");

        Assert(YbDbLegacy77Codec.TryEncode(forward, out var wire,
            out var error), error);
        Equal(16, wire.Length, $"{command:X4} wire length");
        Equal(0x0468, BinaryPrimitives.ReadInt32LittleEndian(
            wire.AsSpan(4, 4)), $"{command:X4} wire QueryId");
        Equal(unchecked((int)0x89ABCDEF),
            BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4)),
            $"{command:X4} wire correlation");
        Equal((ushort)105, BinaryPrimitives.ReadUInt16LittleEndian(
            wire.AsSpan(12, 2)), $"{command:X4} wire ident");
        Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(
            wire.AsSpan(14, 2)), $"{command:X4} wire body length");
    }
}

void ResultAndCorrelation()
{
    foreach (var result in new ushort[] { 0, 2, ushort.MaxValue })
    {
        Assert(NativeType1YbTransactionAck.TryCreateForwardFrame(
            Response(0x0060, result, int.MinValue), out var forward),
            $"result {result} rejected");
        Equal((ushort)106, forward.Ident, $"result {result} ident");
        Equal(int.MinValue, forward.Param, $"result {result} correlation");
    }

    Assert(NativeType1YbTransactionAck.TryCreateForwardFrame(
        Response(0x0061, 1, 0), out var zero), "zero correlation rejected");
    Equal(0, zero.Param, "zero correlation");
}

void Malformed()
{
    Equal(false, NativeType1YbTransactionAck.TryCreateForwardFrame(null,
        out _), "null frame");
    Equal(false, NativeType1YbTransactionAck.TryCreateForwardFrame(
        new LegacyDbServerFrame(2, 0, Response(0x0060, 1, 7).Payload),
        out _), "wrong outer type");
    Equal(false, NativeType1YbTransactionAck.TryCreateForwardFrame(
        Response(0x005F, 1, 7), out _), "wrong command");
    Equal(false, NativeType1YbTransactionAck.TryCreateForwardFrame(
        new LegacyDbServerFrame(1, 0, new byte[0x47]), out _),
        "truncated header");
}

void EnqueueSeam()
{
    var calls = 0;
    YbDbLegacy77Frame captured = null;
    Assert(NativeType1YbTransactionAck.TryProcessResponse(
        Response(0x0061, 1, -37), frame =>
        {
            calls++;
            captured = frame;
            return true;
        }), "accepted enqueue failed");
    Equal(1, calls, "enqueue call count");
    Equal(0x0468, captured.QueryId, "enqueue QueryId");
    Equal(-37, captured.Param, "enqueue correlation");
    Equal((ushort)105, captured.Ident, "enqueue ident");

    Equal(false, NativeType1YbTransactionAck.TryProcessResponse(
        Response(0x0060, 1, 99), _ => false), "rejected enqueue result");
    Equal(false, NativeType1YbTransactionAck.TryProcessResponse(
        Response(0x005F, 1, 99), _ => throw new InvalidOperationException()),
        "malformed callback reached enqueue");
}

LegacyDbServerFrame Response(ushort command, ushort result, int correlation,
    int tailLength = 0)
{
    var payload = new byte[0x48 + tailLength];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), result);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), correlation);
    return new LegacyDbServerFrame(1, 0xBEEF, payload);
}

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add("FAIL " + name + ": " + ex.Message);
    }
}

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected={expected} actual={actual}");
}

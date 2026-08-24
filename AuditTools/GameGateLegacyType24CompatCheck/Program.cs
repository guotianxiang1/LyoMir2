using System.Buffers.Binary;
using SystemModule.Packet;

var tests = 0;

var boundaries = new LegacyGateType24Cache();
Reject(boundaries, MakePayload(1, 12, 0x11), "12-byte payload");
Accept(boundaries, MakePayload(1, 13, 0x12), "13-byte payload");
Accept(boundaries, MakePayload(2, 512, 0x13), "512-byte payload");
Reject(boundaries, MakePayload(3, 513, 0x14), "513-byte payload");

var exact = new LegacyGateType24Cache();
var original = MakePayload(unchecked((int)0x89ABCDEF), 73, 0x55);
Accept(exact, original, "exact payload");
original[12] ^= 0xFF;
Require(exact.TryGet(unchecked((int)0x89ABCDEF), out var cached),
    "stored payload lookup failed");
Equal((byte)0x55, cached[12], "store must preserve an exact private copy");
cached[12] ^= 0xFF;
Require(exact.TryGet(unchecked((int)0x89ABCDEF), out var cachedAgain),
    "second payload lookup failed");
Equal((byte)0x55, cachedAgain[12], "lookup must return a private copy");

var replacement = MakePayload(unchecked((int)0x89ABCDEF), 21, 0x66);
Accept(exact, replacement, "duplicate replacement");
Require(exact.TryGet(unchecked((int)0x89ABCDEF), out var replaced),
    "replacement lookup failed");
Equal(21, replaced.Length, "replacement length");
Equal((byte)0x66, replaced[12], "replacement bytes");
Equal(1, exact.Count, "duplicate key count");

var eviction = new LegacyGateType24Cache();
for (var key = 1; key <= LegacyGateType24Cache.Capacity; key++)
    Accept(eviction, MakePayload(key, 13, (byte)key), $"capacity entry {key}");
Require(eviction.TryGet(1, out _), "oldest lookup failed");
Accept(eviction, MakePayload(LegacyGateType24Cache.Capacity + 1, 13, 0x7A),
    "capacity overflow entry");
Require(eviction.TryGet(1, out _), "lookup must retain and promote the hit");
Require(!eviction.TryGet(2, out _),
    "native one-step promotion must make the former second entry oldest");
Equal(LegacyGateType24Cache.Capacity, eviction.Count, "bounded capacity");

Require(!LegacyGateType24Cache.IsLookupDue(400, 0),
    "400ms boundary must remain throttled");
Require(LegacyGateType24Cache.IsLookupDue(401, 0),
    "401ms boundary must pass");
Require(!LegacyGateType24Cache.IsLookupDue(1400, 1000),
    "repeat at exactly 400ms must remain throttled");
Require(LegacyGateType24Cache.IsLookupDue(1401, 1000),
    "repeat after 400ms must pass");

Console.WriteLine($"PASS GameGate native type-24 cache compatibility ({tests} checks)");

void Accept(LegacyGateType24Cache cache, byte[] payload, string name)
{
    Require(cache.TryStore(payload), $"{name} should be accepted");
}

void Reject(LegacyGateType24Cache cache, byte[] payload, string name)
{
    Require(!cache.TryStore(payload), $"{name} should be rejected");
}

byte[] MakePayload(int recog, int length, byte fill)
{
    var payload = Enumerable.Repeat(fill, length).ToArray();
    if (length >= sizeof(int))
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, sizeof(int)), recog);
    return payload;
}

void Equal<T>(T expected, T actual, string name) where T : notnull
{
    tests++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
}

void Require(bool condition, string message)
{
    tests++;
    if (!condition) throw new InvalidOperationException(message);
}

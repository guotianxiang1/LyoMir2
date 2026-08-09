using System.Buffers.Binary;
using GameSvr.Services;
using SystemModule.Packet;

var checkCount = 0;
var handler = new NativeType1PersistenceCompletionHandler();

var heroWire = Frame(NativeType1PersistenceAckCodec.HeroSaveCommand,
    stateWord: 0xBEEF, param1: unchecked((int)0x88776655),
    param2: 0x12345678);
Check(NativeType1PersistenceAckCodec.TryDecode(heroWire, out var decoded),
    "013C decode");
Equal(NativeType1PersistenceAckKind.HeroSave, decoded.Kind,
    "013C kind");
Equal((ushort)0xBEEF, decoded.StateWord, "013C state word");
Equal(unchecked((int)0x88776655), decoded.Param1, "013C Param1");
Equal(0x12345678, decoded.Param2, "013C Param2");

Check(NativeType1PersistenceAckCodec.TryDecode(
        Frame(NativeType1PersistenceAckCodec.PlayStateCommand,
            stateWord: 1, param1: 7, param2: 8, extraBytes: 5),
        out decoded),
    "013D decode with ignored tail");
Equal(NativeType1PersistenceAckKind.PlayState, decoded.Kind,
    "013D kind");
Check(!NativeType1PersistenceAckCodec.TryDecode(null, out _),
    "null frame rejected");
Check(!NativeType1PersistenceAckCodec.TryDecode(
        Frame(0x013B, 0, 1, 2), out _),
    "unowned command rejected");
Check(!NativeType1PersistenceAckCodec.TryDecode(
        Frame(NativeType1PersistenceAckCodec.HeroSaveCommand,
            0, 1, 2, payloadLength: 0x47), out _),
    "short payload rejected");
Check(!NativeType1PersistenceAckCodec.TryDecode(
        Frame(NativeType1PersistenceAckCodec.HeroSaveCommand,
            0, 1, 2, type: 2), out _),
    "non-Type1 frame rejected");

var sessionA = new NativeType1CorrelationKey(0x10, 0x20);
var sessionB = new NativeType1CorrelationKey(0x11, 0x21);
var saveShared = new NativeType1CorrelationKey(0x30, 0x40);
var saveOther = new NativeType1CorrelationKey(0x31, 0x41);
Check(handler.TryRegister(sessionA, saveShared,
        heroSaveRequired: true, out var pendingA),
    "register first pending");
Check(handler.TryRegister(sessionB, saveShared,
        heroSaveRequired: true, out var pendingB),
    "register duplicate save correlation");
Check(!handler.TryRegister(sessionA, saveOther,
        heroSaveRequired: true, out _),
    "duplicate session correlation rejected");
Equal(2, handler.Count, "pending count after duplicate rejection");

var result = handler.Consume(Frame(
    NativeType1PersistenceAckCodec.HeroSaveCommand,
    stateWord: 0, sessionA.Param1, sessionA.Param2));
Equal(0, result.MatchedCount,
    "013C must not match the session correlation key");
Check(!pendingA.HeroSaveCompleted && !pendingB.HeroSaveCompleted,
    "013C wrong key leaves both pending entries unchanged");

result = handler.Consume(Frame(
    NativeType1PersistenceAckCodec.HeroSaveCommand,
    stateWord: 0x2222, saveShared.Param1, saveShared.Param2));
Equal(NativeType1PersistenceAckDisposition.Processed, result.Disposition,
    "013C disposition");
Equal(2, result.MatchedCount, "013C matches every save key");
Equal(2, result.ChangedCount, "013C first change count");
Check(pendingA.HeroSaveCompleted && pendingB.HeroSaveCompleted,
    "013C sets bit2 on every matching pending");
Equal((byte)0x04, pendingA.CompletionFlags, "013C exact bit");

result = handler.Consume(Frame(
    NativeType1PersistenceAckCodec.HeroSaveCommand,
    stateWord: 0, saveShared.Param1, saveShared.Param2));
Equal(2, result.MatchedCount, "repeated 013C still matches");
Equal(0, result.ChangedCount, "repeated 013C is idempotent");

result = handler.Consume(Frame(
    NativeType1PersistenceAckCodec.PlayStateCommand,
    stateWord: 0, sessionA.Param1, sessionA.Param2));
Equal(NativeType1PersistenceAckDisposition.IgnoredStateWord,
    result.Disposition, "013D state word gate");
Check(!pendingA.PlayStateCompleted,
    "013D wrong state word leaves bit3 clear");

result = handler.Consume(Frame(
    NativeType1PersistenceAckCodec.PlayStateCommand,
    stateWord: 1, saveShared.Param1, saveShared.Param2));
Equal(0, result.MatchedCount,
    "013D must not match the save correlation key");
Check(!pendingA.PlayStateCompleted && !pendingB.PlayStateCompleted,
    "013D wrong key leaves both pending entries unchanged");

result = handler.Consume(Frame(
    NativeType1PersistenceAckCodec.PlayStateCommand,
    stateWord: 1, sessionA.Param1, sessionA.Param2));
Equal(1, result.MatchedCount, "013D session key match");
Equal(1, result.ChangedCount, "013D first change count");
Check(pendingA.PlayStateCompleted && !pendingB.PlayStateCompleted,
    "013D sets bit3 only on its unique session key");
Equal((byte)0x0C, pendingA.CompletionFlags,
    "013C and 013D exact combined bits");
result = handler.Consume(Frame(
    NativeType1PersistenceAckCodec.PlayStateCommand,
    stateWord: 1, sessionA.Param1, sessionA.Param2));
Equal(1, result.MatchedCount, "repeated 013D still matches");
Equal(0, result.ChangedCount, "repeated 013D is idempotent");

pendingA.MarkCompletion(0x01);
pendingA.MarkCompletion(0x02);
Check(pendingA.IsReady, "all four native completion bits are required");
pendingA.ClearCompletionFlags();
Check(!pendingA.IsReady && pendingA.CompletionFlags == 0,
    "post-continuation flag reset");

var noHeroSession = new NativeType1CorrelationKey(0x50, 0x60);
Check(handler.TryRegister(noHeroSession, saveOther,
        heroSaveRequired: false, out var noHero),
    "register no-hero pending");
Check(noHero.HeroSaveCompleted && noHero.CompletionFlags == 0x04,
    "no-hero registration premarks bit2");

var invalidResult = handler.Consume(Frame(
    NativeType1PersistenceAckCodec.HeroSaveCommand,
    0, 1, 2, payloadLength: 15));
Equal(NativeType1PersistenceAckDisposition.InvalidFrame,
    invalidResult.Disposition, "invalid frame disposition");
Equal(0, invalidResult.MatchedCount, "invalid frame match count");

Check(handler.Remove(pendingB), "remove exact pending object");
Check(!handler.Remove(pendingB), "repeat remove rejected");
Equal(2, handler.Count, "pending count after remove");
handler.Clear();
Equal(0, handler.Count, "clear pending state");

Console.WriteLine(
    $"PASS NativeType1PersistenceCompletionCheck checks={checkCount} " +
    "013C=save-key/all/bit2 013D=state1/session-key/bit3 required=0F");

static LegacyDbServerFrame Frame(ushort command, ushort stateWord,
    int param1, int param2, int payloadLength = 0x48,
    ushort type = 1, int extraBytes = 0)
{
    var payload = new byte[payloadLength + extraBytes];
    if (payload.Length >= 2)
        BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    if (payload.Length >= 4)
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), stateWord);
    if (payload.Length >= 12)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), param1);
    if (payload.Length >= 16)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), param2);
    return new LegacyDbServerFrame(type, 0, payload);
}

void Check(bool condition, string description)
{
    checkCount++;
    if (!condition)
        throw new InvalidOperationException(description);
}

void Equal<T>(T expected, T actual, string description)
{
    checkCount++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{description}: expected={expected}, actual={actual}");
}

using System.Buffers.Binary;
using GameSvr.Services;

var state = new NativeType2FieldHeroSnapshotState();
var completions = 0;
state.SetCompletionCallback(completed =>
{
    Check(completed.Completed, "callback observes completed state");
    completions++;
});

Check(state.Consume(Packet(new byte[0], completed: false))
      == NativeType2FieldHeroSnapshotResult.Ignored,
    "header-only non-terminal field hero packet");
Check(!state.Completed && state.Records.Count == 0,
    "header-only non-terminal field hero state");

var firstBody = CreateBody("FieldHeroA", 0x11);
Check(state.Consume(Packet(firstBody, completed: false))
      == NativeType2FieldHeroSnapshotResult.RecordAppended,
    "first field hero append");
Check(state.Records.Count == 1, "first field hero count");
firstBody[0] = 0;
var firstRecord = state.Records[0].CopyWireBody();
Check(firstRecord[0] == (byte)"FieldHeroA".Length,
    "field hero record is a deep copy");
Check(firstRecord[0x138] == 0x11 && firstRecord[0x13B] == 0x44,
    "field hero runtime-slot wire bytes preserved in raw state");

var duplicateBody = CreateBody("FieldHeroA", 0x22);
Check(state.Consume(Packet(duplicateBody, completed: false))
      == NativeType2FieldHeroSnapshotResult.RecordAppended,
    "duplicate field hero appends");
Check(state.Records.Count == 2, "duplicate field hero count");

Check(state.Consume(Packet(new byte[NativeType2FieldHeroSnapshotState.BodySize - 1],
        completed: false)) == NativeType2FieldHeroSnapshotResult.Ignored,
    "short field hero body does not append");
Check(state.Consume(Packet(new byte[NativeType2FieldHeroSnapshotState.BodySize + 1],
        completed: false)) == NativeType2FieldHeroSnapshotResult.Ignored,
    "long field hero body does not append");
Check(state.Records.Count == 2, "malformed field hero count");

Check(state.Consume(Packet(CreateBody("FieldHeroB", 0x33), completed: true))
      == NativeType2FieldHeroSnapshotResult.RecordAppendedAndCompleted,
    "terminal field hero append");
Check(state.Completed && completions == 1,
    "terminal field hero completion callback");
Check(state.Records.Count == 3, "terminal field hero count");
Check(state.Consume(Packet(CreateBody("FieldHeroC", 0x44), completed: false))
      == NativeType2FieldHeroSnapshotResult.Ignored,
    "post-terminal field hero ignored");
Check(state.Records.Count == 3 && completions == 1,
    "post-terminal field hero state");

state.Reset();
Check(!state.Completed && state.Records.Count == 0,
    "field hero reset");
state.SetCompletionCallback(_ => completions++);
Check(state.Consume(Packet(new byte[0], completed: true))
      == NativeType2FieldHeroSnapshotResult.StreamCompleted,
    "malformed terminal field hero packet completes");
Check(state.Completed && state.Records.Count == 0 && completions == 2,
    "malformed terminal field hero completion callback");

Console.WriteLine("PASS NativeType2FieldHeroSnapshotCheck command=006C " +
                  "length=0148-exact duplicates=append " +
                  "terminal=param2-equals-1 callback=one-shot");

static byte[] CreateBody(string name, byte slotSeed)
{
    var body = new byte[NativeType2FieldHeroSnapshotState.BodySize];
    var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
    body[0] = (byte)nameBytes.Length;
    nameBytes.CopyTo(body, 1);
    for (var index = 0; index < 4; index++)
        body[0x138 + index] = unchecked((byte)(slotSeed + index * 0x11));
    return body;
}

static byte[] Packet(byte[] body, bool completed)
{
    var payload = new byte[NativeType2FieldHeroSnapshotState.HeaderSize
                           + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2FieldHeroSnapshotState.Command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    body.CopyTo(payload, NativeType2FieldHeroSnapshotState.HeaderSize);
    return payload;
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

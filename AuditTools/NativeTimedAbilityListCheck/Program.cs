// Pins SM 3554 (the "send my whole timed-ability list" login packet) to the
// original 战神 M2Server frame.
//
// NATIVE EVIDENCE (flat_image.bin, ImageBase 0x400000)
// ----------------------------------------------------
// sub_6E99B8 is dispatched by the login-burst virtual sub_6E9A98 (dword refs at
// VMT slots 0x62F190 / 0x6ACACC) exactly once per player login -- production
// counter srv_AppearTimes 3554 = 50,911 = the SM_LOGON count. It walks the
// timed-ability list head at [self+0xDC] (m_TimedAbilityHead; 3555's node-getter
// sub_773B98 reads the same field at 0x773BBA) and emits ONE 10-byte record per
// node, then sends via [obj+0x254]:
//   0x6E9A14 mov dl,[node+1]  / 0x6E9A17 mov [buf+i*10],dl      ; +0 InternalType
//   0x6E9A1D mov byte [buf+i*10+1],0                            ; +1 zero pad
//   0x6E9A28 mov edx,[node+2] / 0x6E9A2B mov [buf+i*10+2],edx   ; +2 RemainingMs
//   0x6E9A35 mov edx,[node+0xA]/0x6E9A38 mov [buf+i*10+6],edx   ; +6 Value
//   0x6E9A4C push ebx                                           ; Param = count
//   0x6E9A4D push 0 / push 0                                    ; Tag = Series = 0
//   0x6E9A54 push [ebp-0xC]                                     ; Buf
//   0x6E9A55 mov eax,ebx / add eax,eax / lea eax,[eax+eax*4]    ; Len = count*10
//   0x6E9A5D xor ecx,ecx                                        ; Recog = 0
//   0x6E9A5F mov dx,0xDE2                                       ; ident 3554
//   0x6E9A68 call [ebx+0x254]
// The 10-byte record is byte-identical to the non-removed body of SM 3555's
// BuildTimedAbilityClientState. An empty list still sends (je 0x6E9A4C skips the
// packing loop but not the count=0 / Len=0 send).
//
// These are source contracts (no runtime harness): the frame facts are anchored
// to the VAs above so a future faithful rewrite updates them deliberately.
using System.Text.RegularExpressions;

var root = FindRepositoryRoot();
var timedSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
    "TBaseObject.TimedAbility.cs"));
var playerTimedSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.TimedAbility.cs"));
var loginSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.Base.cs"));

CheckConstant(timedSource);
CheckListBuilder(timedSource);
CheckPlayerSend(playerTimedSource);
CheckLoginWiring(loginSource);

Console.WriteLine(
    "NativeTimedAbilityListCheck PASS ident=3554 recog=0 param=count " +
    "tag=0 series=0 record=InternalType+0+RemainingMs+Value len=count*10 " +
    "empty-still-sends trigger=UserLogon-once");
return 0;

static void CheckConstant(string timedSource)
{
    // 0x6E9A5F mov dx,0xDE2 -> ident 3554, kept as a private const alongside
    // TimedAbilityMessage=3555 (both are direct sends, not RM queue tags).
    Contains(timedSource, "TimedAbilityListMessage = 3554",
        "SM 3554 constant value");
}

static void CheckListBuilder(string timedSource)
{
    var block = MethodBlock(timedSource,
        "internal (ClientPacket Header, byte[] Body) BuildTimedAbilityListState()");
    var compact = Compact(block);

    // Header: Recog=0, Param=count, Tag=0, Series=0 (MakeDefaultMsg order is
    // msg, Recog, param, tag, series). Native 0x6E9A4C..0x6E9A5D.
    Contains(compact,
        "Grobal2.MakeDefaultMsg(TimedAbilityListMessage,0,count,0,0)",
        "SM 3554 header five-tuple");

    // Count is the node count of the whole list (native ebx increments per node).
    Contains(compact, "for(varnode=m_TimedAbilityHead;node!=null;node=node.Next)",
        "SM 3554 iterates the whole timed-ability list");

    // Empty list still emits the packet with an empty body (native je 0x6E9A4C).
    Contains(compact, "if(count==0)", "SM 3554 empty-list guard");
    Contains(compact, "return(header,Array.Empty<byte>());",
        "SM 3554 empty-list still sends header");

    // 10-byte record layout, in native write order:
    //   +0 InternalType, +1 zero, +2 RemainingMilliseconds, +6 Value.
    Ordered(compact, "if(count==0)", "writer.Write(node.InternalType);",
        "SM 3554 body written only when count>0");
    Ordered(compact, "writer.Write(node.InternalType);", "writer.Write((byte)0);",
        "SM 3554 record: InternalType before zero pad");
    Ordered(compact, "writer.Write((byte)0);",
        "writer.Write(node.RemainingMilliseconds);",
        "SM 3554 record: zero pad before RemainingMilliseconds");
    Ordered(compact, "writer.Write(node.RemainingMilliseconds);",
        "writer.Write(node.Value);",
        "SM 3554 record: RemainingMilliseconds before Value");
    Equal(1, Count(compact, "writer.Write(node.Value);"),
        "SM 3554 exactly one Value write per record");
}

static void CheckPlayerSend(string playerTimedSource)
{
    var block = MethodBlock(playerTimedSource,
        "private void SendNativeTimedAbilityListOnLogon()");
    Contains(Compact(block), "BuildTimedAbilityListState();",
        "player builds the list packet");
    Contains(Compact(block), "SendSocket(state.Header,state.Body);",
        "player sends header+body via [obj+0x254] equivalent");
}

static void CheckLoginWiring(string loginSource)
{
    // The burst is a VMT call whose exact intra-login position is not byte-pinned;
    // the contract is once-per-login. Assert exactly one call site in UserLogon.
    Equal(1, Count(loginSource, "SendNativeTimedAbilityListOnLogon();"),
        "SM 3554 sent exactly once per login");
}

static string MethodBlock(string source, string anchor)
{
    var start = source.IndexOf(anchor, StringComparison.Ordinal);
    Require(start >= 0, "method anchor: " + anchor);
    return BraceBlock(source, start, anchor);
}

static string BraceBlock(string source, int start, string label)
{
    var open = source.IndexOf('{', start);
    Require(open >= 0, "opening brace: " + label);
    var depth = 0;
    for (var index = open; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        else if (source[index] == '}' && --depth == 0)
            return source[start..(index + 1)];
    }
    throw new InvalidOperationException("closing brace: " + label);
}

static string Compact(string value) =>
    Regex.Replace(value, @"\s+", string.Empty);

static int Count(string source, string value)
{
    var count = 0;
    for (var index = 0;;)
    {
        index = source.IndexOf(value, index, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        index += value.Length;
    }
}

static void Contains(string source, string value, string label) =>
    Require(source.Contains(value, StringComparison.Ordinal), label);

static void Ordered(string source, string first, string second, string label)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Require(firstIndex >= 0 && secondIndex > firstIndex, label);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

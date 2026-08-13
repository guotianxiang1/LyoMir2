// Pins CM 1089 (0x441) 组长广播 —— dispatch @0x6D970A + gate sub_6B7BAC/sub_726C14 +
// broadcast callee sub_727628. Zero production traffic, but a fully-reversed handler that
// C# can replicate byte-for-byte because the group model already exists.
//
// SHAPE
// -----
//   handler 0x6D970A: IsGroupLeader?(self) gate, then sub_727628([self+0xA80], Recog=[msg+0]).
//   gate 0x6B7BAC   : group=[self+0xA80]!=0 AND sub_726C14 == (self == [group+0x3C]).
//   callee 0x727628 : stores Recog into [group+0x40], then for 11 member slots
//                     [group+0x48+i*4] -> [rec+0x10]=playobj, if non-nil and [obj+0x73]==0
//                     (not ghost) sends SM 965 (0x3C5) with nRecog=Recog, Param/Tag/Series=0.
//
// SM 965 is independently confirmed as a wire ident by WireIdentPinCheck's Wire table
// (image scan of every send slot), so the SM_965 constant is not a new unconfirmed SM_.
//
// C# lives in TPlayObject.NativeGroupProtocol.cs::HandleNativeGroupLeaderBroadcast, wired
// under TryHandleNativeGroupProtocol case Grobal2.CM_1089.

using SystemModule;

var asserts = 0;
var image = LoadNativeImage();

CheckDispatchHandler();
CheckLeaderGate();
CheckBroadcastCallee();
CheckConstants();

Console.WriteLine($"NativeGroupLeaderBroadcastCheck PASS idents=1 asserts={asserts}");

// ---------------------------------------------------------------------------
// native side
// ---------------------------------------------------------------------------

void CheckDispatchHandler()
{
    // The whole handler, including the je/jmp to the 0x6DBC2C drop stub: proof that the
    // only gate is IsGroupLeader and the only action is the sub_727628 broadcast.
    Pin(0x006D970A,
        "8B45FC E89AE4FDFF 84C0 0F8412250000 8B45CC 8B10 8B45FC 8B80800A0000 E8FBDE0400 E9FA240000",
        "CM 1089 handler: gate 0x6B7BAC, Recog=[msg+0], broadcast 0x727628([self+0xA80])");

    // Sub-facts, each so a byte drift is named.
    Pin(0x006D970D, "E89AE4FDFF", "call gate -> 0x6B7BAC (IsGroupLeader?)");
    Pin(0x006D9714, "0F8412250000", "not leader -> 0x6DBC2C DEFAULT drop");
    Pin(0x006D971D, "8B10", "edx = [msg+0] = Recog");
    Pin(0x006D9722, "8B80800A0000", "eax = [self+0xA80] = group object");
    Pin(0x006D9728, "E8FBDE0400", "call broadcast -> 0x727628");
}

void CheckLeaderGate()
{
    // group=[self+0xA80]; if nil return 0; else return sub_726C14(group, self).
    Pin(0x006B7BAC,
        "55 8BEC 33D2 8B88800A0000 85C9 740B 8BD0 8BC1 E850F00600 8BD0 8BC2 5D C3",
        "sub_6B7BAC: group=[self+0xA80], nil->0, else sub_726C14(group,self)");
    Pin(0x006B7BBF, "E850F00600", "gate tail call -> 0x726C14");

    // sub_726C14: al = (edx==self) == [eax+0x3C]==leader.
    Pin(0x00726C14, "3B503C 0F94C0 C3",
        "sub_726C14: sete al = (self == [group+0x3C]) i.e. self is the leader");
}

void CheckBroadcastCallee()
{
    // Prologue then the [group+0x40] = Recog store.
    Pin(0x00727636, "8B45FC 8B55F8 895040",
        "sub_727628: [group+0x40] = Recog (the cached broadcast recog)");

    // The 11-slot loop: fetch member record, deref playobj, ghost gate, send, advance.
    Pin(0x00727644, "8B449848", "eax = [group + ebx*4 + 0x48] = member record slot");
    Pin(0x00727648, "8B4010", "eax = [rec+0x10] = playobj");
    Pin(0x0072764B, "85C0 741D", "nil member slot -> skip");
    Pin(0x0072764F, "80787300 7517", "ghost gate: [obj+0x73]!=0 -> skip");

    // The send itself: four push 0, ecx=Recog, dx=0x3C5 (SM 965), call [obj+0x250].
    Pin(0x00727655,
        "6A00 6A00 6A00 6A00 8B4DF8 66BAC503 8B30 FF9650020000",
        "send SM 965 (0x3C5): nRecog=Recog, Param/Tag/Series=0, via [obj+0x250]");

    // Exactly 11 slots (ident-style array cap), then ret.
    Pin(0x0072766D, "83FB0B 75CF", "loop 11 slots then fall out");
}

// ---------------------------------------------------------------------------
// C# side
// ---------------------------------------------------------------------------

void CheckConstants()
{
    Assert(Grobal2.CM_1089 == 1089, "Grobal2.CM_1089 == 1089");
    // dx = 0x3C5 = 965. WireIdentPinCheck.Wire independently lists 965 as a send-slot ident.
    Assert(Grobal2.SM_965 == 965, "Grobal2.SM_965 == 965 (0x3C5)");
    Assert(0x3C5 == 965, "0x3C5 == 965 sanity");
}

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------

static byte[] LoadNativeImage()
{
    const string known = @"D:\loym2\staging\_reunpack_work\flat_image.bin";
    if (!File.Exists(known))
        throw new InvalidOperationException("flat_image.bin not found at " + known);
    return File.ReadAllBytes(known);
}

void Pin(int va, string expectedHex, string label)
{
    const int imageBase = 0x400000;
    var offset = va - imageBase;
    var expected = Convert.FromHexString(expectedHex.Replace(" ", string.Empty));
    Assert(offset >= 0 && offset + expected.Length <= image.Length, label + " range");
    for (var i = 0; i < expected.Length; i++)
    {
        if (image[offset + i] != expected[i])
            throw new InvalidOperationException(
                $"{label}: byte[{i}] at 0x{va + i:X6} expected={expected[i]:X2} " +
                $"actual={image[offset + i]:X2}");
    }
    asserts++;
}

void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    asserts++;
}

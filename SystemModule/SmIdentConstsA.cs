namespace SystemModule
{
    /// <summary>
    /// SM (server -> client) ident constants recovered from native M2Server
    /// (flat_image.bin, ImageBase 0x400000) send sites in the 3000-3600 band
    /// that were not yet named in <see cref="Grobal2"/>.
    /// <para>
    /// Each value is the literal <c>mov dx, imm16</c> loaded immediately before a
    /// <c>call dword ptr [self+0x250]</c> (SendDefMessage @0x6D7CB0, string body)
    /// or <c>call dword ptr [self+0x254]</c> (SendSocket @0x6D7BF8, binary body)
    /// virtual. The per-ident packet layout and its VA + bytes + disassembly
    /// evidence live beside each builder in
    /// <c>GameSvr/Actors/TBaseObject.SmA.cs</c>.
    /// </para>
    /// <para>
    /// Header field mapping is identical for both slots (verified from the two
    /// callees): <c>ecx = Recog</c>, <c>dx = Ident</c>; the stack pushes, in
    /// execution order, are <c>Param, Tag, Series</c> then either
    /// <c>sMsg</c> (0x250) or <c>Buf, Len</c> (0x254). C# assembles this with
    /// <c>Grobal2.MakeDefaultMsg(Ident, Recog, Param, Tag, Series)</c>.
    /// </para>
    /// </summary>
    public static class SmIdentConstsA
    {
        // 0xBBB @0x6329A5 slot 0x250 - YB-deal buyer result code (Recog = <=0 result).
        public const int SM_3003 = 3003;

        // 0xBBC @0x632BC0 slot 0x250 - YB-deal result to a resolved target (Recog = result).
        public const int SM_3004 = 3004;

        // 0xBBF @0x633E84 slot 0x250 - YB-deal count/value result (Recog = incoming value).
        public const int SM_3007 = 3007;
    }
}

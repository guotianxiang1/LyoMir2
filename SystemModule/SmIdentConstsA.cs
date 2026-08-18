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

        // 0xBC7 @0x6E85FA slot 0x250 - YB trade-setting result (Recog = result; -1 on error).
        // NOTE: numerically equal to Grobal2.CM_HEAVYHIT, but this is the opposite
        // direction (server -> client SM), a distinct message.
        public const int SM_3015 = 3015;

        // 0xCEE @0x6EB11C slot 0x250 - three substrings concatenated (via
        // _LStrCatN @0x405890) with each substring's byte length carried in
        // Param/Tag/Series so the client can re-split. Recog = incoming value.
        public const int SM_3310 = 3310;

        // 0xCF0 @0x64F13B slot 0x250 - small helper: Recog + Series(=flag, default 4).
        public const int SM_3312 = 3312;

        // 0xCF0 default Series substituted at 0x64F125 when the flag argument is 0.
        public const int SM_3312_DefaultSeries = 4;

        // 0xD0C @0x6B46DD slot 0x254 - full-dword variant of the visible-entity
        // refresh record (sibling of the word-split idents 0xF/0x10/0x11 sent from
        // the same record). Header from record offsets, 8-byte body from +0xC/+0x8.
        public const int SM_3340 = 3340;

        // 0xD0C body length pushed at 0x6B46CF.
        public const int SM_3340_BodyLength = 8;

        // 0xD0D @0x73FC47 slot 0x250 - name notice: Param = flag?2:1, Tag =
        // word[player+0x278], Series = 0, sMsg = player name (player+0x106).
        public const int SM_3341 = 3341;

        // 0xD27 @0x6E987E slot 0x250 - validated-action result (Recog = result;
        // only sent for non-zero result: -1 no player / -5 level / -6 call failed).
        public const int SM_3367 = 3367;

        // 0xCFC @0x7468DB/0x7468FE slot 0x250 - login state sync.
        public const int SM_3324 = 3324;

        // 0xCFD @0x746A37 slot 0x250 - state-0x36 spirit/shape sync notice:
        // Recog = [self+0x60C], Param = word[self+0x610], Tag = 0, Series = 1,
        // sMsg = notice text. Sibling of the login-sync 3324 family.
        public const int SM_3325 = 3325;

        // 0xCFD constant Series pushed at 0x746A23.
        public const int SM_3325_Series = 1;
    }
}

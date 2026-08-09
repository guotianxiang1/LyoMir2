using System.Buffers.Binary;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// ================================================================================================
// NativeStallWireIntegrationCheck — END-TO-END proof that the stall (摆摊) CM wire layer parses real
// client requests byte-exactly, reversed in staging/stall_cm_wire_formats_20260802.md.
//
// It builds ENCODED CM bodies + header scalars exactly as the wire delivers them, feeds them through
// the REAL production decoders (NativeStallWireCodec — the SAME code the live TPlayObject handlers call),
// and asserts every field lands where the spec says. It also drives the REAL 4418 browse-response builder
// and the REAL manager by-CharID lookup, and proves the BUY-echo loop (the item id BUY quotes back in
// Recog is exactly the id the browse response advertised).
//
// Verified wire -> TProcessMessage mapping (ProcessUserMessage DEFAULT case
// `SendMsg(obj, Ident, Series, Recog, Param, Tag, sMsg, payload)`; header = Recog i32@0 / Ident u16@4 /
// Param u16@6 / Tag u16@8 / Series u16@0xA):
//   Recog -> nParam1 · Param -> nParam2 · Tag -> nParam3 · Series -> wParam · encoded body -> Payload.
// The Wire() helper below reproduces that mapping exactly, so the assertions run from raw wire bytes.
// ================================================================================================

try
{
    VerifyBodyRoundTrip();
    VerifyBuyDecode();
    VerifyAddDecode();
    VerifyQueryDecode();
    VerifyScalarDecoders();
    VerifySetNameDecode();
    VerifyMessageDecode();
    VerifyLengthGuards();
    VerifyManagerByCharId();
    VerifyBrowseResponse();
    VerifyBuyEchoLoop();
    VerifyGateStaysDormant();

    Console.WriteLine(
        "PASS NativeStallWireIntegrationCheck: wire->msg (Recog=nParam1/Param=nParam2/Tag=nParam3/" +
        "Series=wParam/body=Payload) + BUY(itemId=Recog,owner=body[0]/[4] int64,count=Series) + " +
        "ADD(itemId=Recog,uprice=body[0],moneytype=Tag,count=Param) + Query(owner=body[0]/[4]) + " +
        "SetName(whole-body GBK) + len-guards(>=8 4418/4426,>=4 4421) + mgr by-CharID + " +
        "4418 response(88 hdr + 16xN array + blob; ClientItemID/price/type/count round-trip; BUY-echo) " +
        "gate=OFF");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeStallWireIntegrationCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

// ---- wire helpers ------------------------------------------------------------------------------

// 6-bit ENCODE a raw body exactly as the client wire carries it (the inverse of Misc.Decode6BitBufDirect
// the handlers use). Returns just the encoded bytes.
static byte[] Enc(byte[] body)
{
    var dst = new byte[body.Length * 2 + 16];
    var n = Misc.Encode6BitBufDirect(body, body.Length, dst);
    var outp = new byte[n];
    Array.Copy(dst, outp, n);
    return outp;
}

// Reproduce the ProcessUserMessage DEFAULT-case mapping from raw header scalars + a raw (to-be-encoded)
// body — the exact TProcessMessage the stall handlers receive.
static TProcessMessage Wire(int recog, int param, int tag, int series, byte[] body) =>
    new TProcessMessage
    {
        nParam1 = recog,           // DefMsg.Recog  (dword@0)
        nParam2 = param,           // DefMsg.Param  (word@6)
        nParam3 = tag,             // DefMsg.Tag    (word@8)
        wParam = series,           // DefMsg.Series (word@0xA)
        Payload = Enc(body),       // 6-bit ENCODED body
    };

static byte[] I64(long v) { var b = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, v); return b; }
static byte[] I32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); return b; }

// ---- (0) the 6-bit body round-trips exactly (encode -> decode == identity) --------------------
static void VerifyBodyRoundTrip()
{
    // 8-byte CharID body
    var owner = I64(0x1122334455667788L);
    var d1 = NativeStallWireCodec.DecodeBody(Enc(owner));
    Assert(d1.Length >= 8 && BytesEq(d1, owner, 8), "roundtrip: 8-byte CharID body");

    // 4-byte uprice body
    var uprice = I32(123456);
    var d2 = NativeStallWireCodec.DecodeBody(Enc(uprice));
    Assert(d2.Length >= 4 && BytesEq(d2, uprice, 4), "roundtrip: 4-byte uprice body");

    // trailing-0 payload strip robustness: the wire may append a literal 0 terminator AFTER the encoded
    // bytes; DecodeBody strips exactly one and still recovers the body (encoded bytes are >= 0x3C, never 0).
    var enc = Enc(owner);
    var encWith0 = new byte[enc.Length + 1];
    Array.Copy(enc, encWith0, enc.Length);   // last byte stays 0
    var d3 = NativeStallWireCodec.DecodeBody(encWith0);
    Assert(d3.Length >= 8 && BytesEq(d3, owner, 8), "roundtrip: trailing-0 payload strip");

    // no payload => empty
    Assert(NativeStallWireCodec.DecodeBody(null).Length == 0, "roundtrip: null payload -> empty");
    Assert(NativeStallWireCodec.DecodeBody(Array.Empty<byte>()).Length == 0, "roundtrip: empty payload -> empty");
}

// ---- (1) 4426 BUY: itemId=Recog, owner=body[0]/body[4] (int64 LE), count=Series -----------------
static void VerifyBuyDecode()
{
    const int clientItemId = 0x0BADF00D;       // rides Recog -> nParam1
    const int count = 7;                        // rides Series -> wParam
    // owner CharID lo=0xAABBCCDD, hi=0x11223344 => int64 0x11223344AABBCCDD (proves lo|hi<<32 combine).
    const long ownerId = 0x11223344AABBCCDDL;

    var msg = Wire(recog: clientItemId, param: 999 /*unused*/, tag: 888 /*unused*/, series: count,
        body: I64(ownerId));

    Assert(NativeStallWireCodec.TryDecodeBuyRequest(msg, out var gotOwner, out var gotItem, out var gotCount),
        "BUY: decodes an 8-byte body");
    Assert(gotItem == clientItemId, $"BUY: ClientItemID=Recog ({gotItem:X} != {clientItemId:X})");
    Assert(gotCount == count, $"BUY: count=Series ({gotCount} != {count})");
    Assert(gotOwner == ownerId, $"BUY: owner CharID=body[0]/body[4] ({gotOwner:X} != {ownerId:X})");

    // count as a word (Series is u16 on the wire) — full 0..65535 range survives into wParam.
    var wide = Wire(1, 0, 0, 0xFFFF, I64(5));
    Assert(NativeStallWireCodec.TryDecodeBuyRequest(wide, out _, out _, out var wc) && wc == 0xFFFF,
        "BUY: count carries the full word (0xFFFF)");
}

// ---- (2) 4421 ADD: itemId=Recog, uprice=body[0] dword, moneytype=Tag, count=Param ---------------
static void VerifyAddDecode()
{
    const int clientItemId = 0x00C0FFEE;   // Recog -> nParam1
    const int count = 3;                    // Param -> nParam2
    const int moneyType = 1;                // Tag   -> nParam3
    const int uprice = 654321;              // body[0] dword

    // Series is UNUSED by ADD; set a decoy to prove it is not read as any ADD field.
    var msg = Wire(recog: clientItemId, param: count, tag: moneyType, series: 0x7777, body: I32(uprice));

    Assert(NativeStallWireCodec.TryDecodeAddItemRequest(msg, out var gotItem, out var gotUprice,
        out var gotMoney, out var gotCount), "ADD: decodes a 4-byte body");
    Assert(gotItem == clientItemId, $"ADD: ClientItemID=Recog ({gotItem:X} != {clientItemId:X})");
    Assert(gotUprice == uprice, $"ADD: uprice=body[0] dword ({gotUprice} != {uprice})");
    Assert(gotMoney == moneyType, $"ADD: moneytype=Tag ({gotMoney} != {moneyType})");
    Assert(gotCount == count, $"ADD: count=Param ({gotCount} != {count})");
}

// ---- (3) 4418 Query owner CharID = body[0]/body[4] ---------------------------------------------
static void VerifyQueryDecode()
{
    const long ownerId = 0x0102030405060708L;
    var msg = Wire(0, 0, 0, 0, I64(ownerId));
    Assert(NativeStallWireCodec.TryDecodeQueryOwner(msg, out var got) && got == ownerId,
        "Query: owner CharID = body[0]/body[4]");

    // 0 owner (self path marker) decodes cleanly to 0.
    var self = Wire(0, 0, 0, 0, I64(0));
    Assert(NativeStallWireCodec.TryDecodeQueryOwner(self, out var zero) && zero == 0,
        "Query: owner 0 (self) decodes to 0");
}

// ---- (4) scalar-only decoders: DEL=Recog, 4419 duration=Param ----------------------------------
static void VerifyScalarDecoders()
{
    var del = Wire(recog: 0xDEAD, param: 11, tag: 22, series: 33, body: Array.Empty<byte>());
    Assert(NativeStallWireCodec.DecodeStallItemClientId(del) == 0xDEAD, "DEL: ClientItemID=Recog");

    var tl = Wire(recog: 1, param: 6 /*duration*/, tag: 99 /*timestamp*/, series: 2, body: Array.Empty<byte>());
    Assert(NativeStallWireCodec.DecodeSetTimeLevelDuration(tl) == 6, "4419: duration=Param");
}

// ---- (5) 4420 SetName = whole decoded body as a GBK C-string -----------------------------------
static void VerifySetNameDecode()
{
    // ASCII
    var ascii = Wire(0, 0, 0, 0, HUtil32.GetBytes("MyBooth"));
    Assert(NativeStallWireCodec.DecodeSetStallName(ascii) == "MyBooth", "SetName: ASCII whole-body");

    // GBK Chinese name, with the wire's trailing C-string NUL inside the body (stops at the NUL).
    var nameBytes = HUtil32.GetBytes("小店铺");
    var withNul = new byte[nameBytes.Length + 1];
    Array.Copy(nameBytes, withNul, nameBytes.Length);
    var gbk = Wire(0, 0, 0, 0, withNul);
    Assert(NativeStallWireCodec.DecodeSetStallName(gbk) == "小店铺", "SetName: GBK whole-body C-string (NUL-terminated)");
}

// ---- (5b) 4467 Message: owner CharID = body[0]/body[4], text @ body+8 --------------------------
static void VerifyMessageDecode()
{
    const long ownerId = 0x00A1B2C3D4E5F607L;
    var text = "hello 你好";
    var textBytes = HUtil32.GetBytes(text);
    var body = new byte[NativeStallWireCodec.OwnerCharIdSize + textBytes.Length + 1];   // CharID + text + NUL
    Array.Copy(I64(ownerId), 0, body, 0, 8);
    Array.Copy(textBytes, 0, body, 8, textBytes.Length);
    var msg = Wire(0, 0, 0, 0, body);
    Assert(NativeStallWireCodec.TryDecodeMessageStall(msg, out var gotOwner, out var gotText), "Message: decodes");
    Assert(gotOwner == ownerId, "Message: owner CharID = body[0]/body[4]");
    Assert(gotText == text, "Message: text @ body+8 (GBK C-string)");
}

// ---- (6) length guards: reject short bodies (spec §5.2) ----------------------------------------
static void VerifyLengthGuards()
{
    // 4426/4418 need >= 8 decoded body bytes for the CharID.
    var short7 = Wire(1, 0, 0, 1, new byte[7]);
    Assert(!NativeStallWireCodec.TryDecodeBuyRequest(short7, out _, out _, out _), "guard: BUY rejects a 7-byte body");
    Assert(!NativeStallWireCodec.TryDecodeQueryOwner(short7, out _), "guard: Query rejects a 7-byte body");
    var empty = Wire(1, 0, 0, 1, Array.Empty<byte>());
    Assert(!NativeStallWireCodec.TryDecodeBuyRequest(empty, out _, out _, out _), "guard: BUY rejects an empty body");

    // exactly 8 is accepted.
    var exact8 = Wire(1, 0, 0, 1, new byte[8]);
    Assert(NativeStallWireCodec.TryDecodeBuyRequest(exact8, out _, out _, out _), "guard: BUY accepts exactly 8 bytes");

    // 4421 needs >= 4 for the uprice dword.
    var short3 = Wire(1, 2, 3, 4, new byte[3]);
    Assert(!NativeStallWireCodec.TryDecodeAddItemRequest(short3, out _, out _, out _, out _),
        "guard: ADD rejects a 3-byte body");
    var exact4 = Wire(1, 2, 3, 4, I32(4242));
    Assert(NativeStallWireCodec.TryDecodeAddItemRequest(exact4, out _, out var up, out _, out _) && up == 4242,
        "guard: ADD accepts exactly 4 bytes");
}

// ---- (7) manager by-CharID lookup (BUY/Query/Message target resolver) --------------------------
static void VerifyManagerByCharId()
{
    const long id = 0x1122334455667788L;
    var mgr = new NativeStallManager();
    var rec = mgr.GetOrCreate("seller", id);

    Assert(ReferenceEquals(mgr.TryGetRecordById(id), rec), "mgr: by-CharID finds the record");
    Assert(ReferenceEquals(mgr.TryGetRecord("seller"), rec), "mgr: by-name still finds the same record");
    Assert(mgr.TryGetRecordById(0x999L) == null, "mgr: unknown CharID -> null");
    Assert(mgr.TryGetRecordById(0) == null, "mgr: CharID 0 -> null");

    Assert(mgr.Remove("seller"), "mgr: remove by name");
    Assert(mgr.TryGetRecordById(id) == null, "mgr: by-CharID index cleared on remove");

    // Register path also indexes by CharID.
    var reg = new NativeStallRecord { OwnerName = "reg", OwnerId = 0x42L };
    mgr.Register(reg);
    Assert(ReferenceEquals(mgr.TryGetRecordById(0x42L), reg), "mgr: Register indexes by CharID");
}

// ---- (8) 4418 browse RESPONSE: 88-byte header + 16xN item array + per-item blob, byte-exact -----
static void VerifyBrowseResponse()
{
    var createDate = new DateTime(2026, 8, 2, 9, 30, 0);
    var rec = new NativeStallRecord
    {
        OwnerId = 0x1122334455667788L,
        OwnerName = "seller",
        StallName = "MyStall",
        DbIdx = 4242,
        Status = StallRecordStatus.Running,
        DuraTime = 12,
        CreateDate = createDate,
    };
    rec.Items.Add(new NativeStallItem
    { Item = new TUserItem { ClientItemID = 111, wIndex = 7, Dura = 5, DuraMax = 100 }, UnitPrice = 500, MoneyType = 0, ItemCount = 5 });
    rec.Items.Add(new NativeStallItem
    { Item = new TUserItem { ClientItemID = 222, wIndex = 21 }, UnitPrice = 9999, MoneyType = 1, ItemCount = 1 });

    const int remaining = 3600 * 12 - 60;
    // per-item detail blob = the item's ClientItemID as 4 LE bytes (a controllable stand-in for the live
    // item client-record encoder), so the blob region's placement + per-item order are provable.
    byte[] Blob(TUserItem it) => I32(it.ClientItemID);

    var p = NativeStallWireCodec.BuildQueryResponse(rec, remaining, ownerOnline: true, Blob);

    const int Hdr = NativeStallWireCodec.QueryHeaderSize;       // 88
    const int Recsz = NativeStallWireCodec.QueryItemRecordSize; // 16
    Assert(Hdr == 88 && Recsz == 16, "response: header/record constants");
    Assert(p.Length == Hdr + Recsz * 2 + 4 * 2, $"response: total length {p.Length} != 88+16*2+4*2");

    // --- segment ① header ---
    Assert(BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(0x00, 8)) == rec.OwnerId, "hdr: owner CharID lo/hi @0/@4");
    // name#1 (ShortString[15]) @0x08 = OwnerName; name#2 (ShortString[33]) @0x17 = StallName.
    Assert(ReadShortStr(p, 0x08) == "seller", "hdr: name#1 ShortString @0x08 == OwnerName");
    Assert(ReadShortStr(p, 0x17) == "MyStall", "hdr: name#2 ShortString @0x17 == StallName");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x38, 4)) == 4242, "hdr: DbIdx @0x38");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x3C, 4)) == 1, "hdr: status @0x3C == Running(1)");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x40, 4)) == 12, "hdr: time-level/DuraTime @0x40");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x44, 4)) == remaining, "hdr: remaining seconds @0x44");
    Assert(BinaryPrimitives.ReadDoubleLittleEndian(p.AsSpan(0x48, 8)) == createDate.ToOADate(), "hdr: createdate (OADate) @0x48");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x50, 4)) == 1, "hdr: owner-online @0x50");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x54, 4)) == 2, "hdr: item count @0x54 == 2");

    // --- segment ② item-scalar array (the records BUY echoes back) ---
    CheckItemRecord(p, Hdr + 0 * Recsz, 111, 500, 0, 5);
    CheckItemRecord(p, Hdr + 1 * Recsz, 222, 9999, 1, 1);

    // --- segment ③ per-item blob (appended verbatim, in item order) ---
    var blobStart = Hdr + Recsz * 2;
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(blobStart + 0, 4)) == 111, "blob: item0 detail @array-end");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(blobStart + 4, 4)) == 222, "blob: item1 detail follows");

    // --- empty stall => header only (length 88, itemcount 0) ---
    var empty = new NativeStallRecord { OwnerId = 7, OwnerName = "e", CreateDate = createDate };
    var pe = NativeStallWireCodec.BuildQueryResponse(empty, 0, ownerOnline: false, Blob);
    Assert(pe.Length == Hdr, "response: empty stall => header only (88)");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(0x54, 4)) == 0, "response: empty stall itemcount 0");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(0x50, 4)) == 0, "response: empty stall owner-online 0");
}

// ---- (9) BUY-echo loop: the id BUY quotes in Recog == the id the browse response advertised -----
static void VerifyBuyEchoLoop()
{
    var rec = new NativeStallRecord { OwnerId = 0x55L, OwnerName = "s", CreateDate = DateTime.Now };
    rec.Items.Add(new NativeStallItem
    { Item = new TUserItem { ClientItemID = 0x1234, wIndex = 7 }, UnitPrice = 42, MoneyType = 0, ItemCount = 1 });

    var p = NativeStallWireCodec.BuildQueryResponse(rec, 0, false, _ => Array.Empty<byte>());
    var advertisedId = BinaryPrimitives.ReadInt32LittleEndian(
        p.AsSpan(NativeStallWireCodec.QueryHeaderSize + 0, 4));   // segment ② record[0].ClientItemID
    Assert(advertisedId == 0x1234, "echo: browse advertises the item ClientItemID");

    // The client buys it back: Recog = the advertised id, owner = the stall CharID.
    var buy = Wire(recog: advertisedId, param: 0, tag: 0, series: 1, body: I64(rec.OwnerId));
    Assert(NativeStallWireCodec.TryDecodeBuyRequest(buy, out var owner, out var itemId, out _),
        "echo: BUY decodes");
    Assert(itemId == advertisedId, "echo: BUY ClientItemID (Recog) == the advertised browse id");
    Assert(owner == rec.OwnerId, "echo: BUY owner CharID == the browsed stall owner");
}

// ---- gate stays OFF (dormant) — this audit must not flip it ------------------------------------
static void VerifyGateStaysDormant()
{
    Assert(!NativeStallWriteGate.SupportsStallWrites, "gate: SupportsStallWrites must stay OFF");
    Assert(!NativeStallWriteGate.Enabled, "gate: Enabled must stay false (store not injected)");
}

// ---- byte helpers -------------------------------------------------------------------------------
static bool BytesEq(byte[] a, byte[] b, int n)
{
    for (var i = 0; i < n; i++) if (a[i] != b[i]) return false;
    return true;
}

static void CheckItemRecord(byte[] p, int off, int clientItemId, int uprice, int moneyType, int count)
{
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(off + 0x00, 4)) == clientItemId, $"item@{off}: ClientItemID");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(off + 0x04, 4)) == uprice, $"item@{off}: uprice");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(off + 0x08, 4)) == moneyType, $"item@{off}: moneytype");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(off + 0x0C, 4)) == count, $"item@{off}: count");
}

static string ReadShortStr(byte[] p, int off)
{
    int len = p[off];
    return len == 0 ? string.Empty : HUtil32.GetString(p, off + 1, len);
}

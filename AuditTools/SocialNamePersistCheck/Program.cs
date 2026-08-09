// SocialNamePersistCheck — proves the social-block NAME slots now round-trip.
//
// 战神 keeps the relationship FLAG and the relationship NAME in sync because every
// state change writes the name straight into the 128-byte social block, and only
// then block-copies the region into the record on save:
//   marry    sub_6C55F5 `mov byte [ebx+0xb94],1`
//            sub_6C55FC `lea eax,[ebx+0xc48]` + 0x6C5608 `mov cl,0xF` -> sub_4039E4
//   divorce  0x6C52BC `mov byte [esi+0xb94],0` + 0x6C52C3 `mov byte [esi+0xc48],0`
//   master   0x6C58A0 `lea eax,[esi+0xc58]` + 0x6C58AC `mov cl,0xF`
//   student  0x6C58D5 `lea eax,[edi+eax*8+0xc78]` (eax=i*2 => stride 16)
//            + 0x6C58E2 `mov cl,0xF`
//   save     sub_6B0FF0 @0x6B1699 `rep movsd` 0x20 dwords from obj+0xC48
//   load     sub_6AFD7C @0x6B096C `lea esi,[eax+0x658]` (raw arg => inflated 0x650)
// obj+0xC48 == inflated 0x650, so slots are 0x650 spouse / 0x660 master /
// 0x670 companion / 0x680+i*16 students[0..4].
//
// Before the 2026-08-07 fix TryEncode restored the block VERBATIM and never wrote
// the names, so boMarried=1 persisted with an empty name slot and a divorce left a
// stale name behind. This check pins both directions.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DBSvr.Core;
using SystemModule;
using SystemModule.Packet;

internal static class Program
{
    private const int DataRecordSize = 0xEEF8;
    private const int SpouseSlot = 0x650;
    private const int MasterSlot = 0x660;
    private const int CompanionSlot = 0x670;
    private const int StudentSlot0 = 0x680;
    private const int SlotStride = 0x10;

    private static readonly List<string> Failures = new List<string>();

    private static void Check(bool ok, string what)
    {
        if (!ok) Failures.Add(what);
    }

    private static Encoding Gbk =>
        Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

    private static byte[] Inflate(byte[] blob)
    {
        using var input = new MemoryStream(blob, 8, blob.Length - 8);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    private static string ReadSlot(byte[] raw, int offset)
    {
        var len = raw[offset];
        if (len > 15) return string.Empty;
        return Gbk.GetString(raw, offset + 1, len);
    }

    private static int Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        RoundTripFormedRelations();
        RoundTripDivorceClears();
        ForeignCompanionByteSurvives();
        TruncatesAtFifteenLikeNative();
        NegativeHpMpStillLoads();

        if (Failures.Count > 0)
        {
            Console.WriteLine("SocialNamePersistCheck FAIL:");
            foreach (var f in Failures) Console.WriteLine("  " + f);
            return 1;
        }
        Console.WriteLine(
            "SocialNamePersistCheck PASS spouse@0x650 master@0x660 students@0x680+i*16 " +
            "marry/divorce=sync foreign-0x670-overflow=preserved cap=15-truncate");
        return 0;
    }

    private static THumDataInfo NewInfo()
    {
        var info = new THumDataInfo
        {
            Data = new THumInfoData
            {
                sCharName = "TestChar",
                sCurMap = "0",
                sAccount = "acct",
                sHomeMap = "0",
                Abil = new TAbility { Level = 40, HP = 100, MP = 100 },
                sStudentNames = new string[5]
                    { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty },
                sDearName = string.Empty,
                sMasterName = string.Empty,
                NativeSocialBlob = new byte[128]
            }
        };
        return info;
    }

    private static byte[] EncodeInflated(THumDataInfo info, string label)
    {
        if (!NativeHumanDataCodec.TryEncode(info, out var blob, out _, out var err))
        {
            Failures.Add(label + ": TryEncode failed: " + err);
            return null;
        }
        var raw = Inflate(blob);
        if (raw.Length != DataRecordSize)
        {
            Failures.Add(label + $": inflated {raw.Length}, expected {DataRecordSize}");
            return null;
        }
        return raw;
    }

    // A marriage/master/student formed on THIS server must land in the block.
    private static void RoundTripFormedRelations()
    {
        var info = NewInfo();
        info.Data.boMarried = true;
        info.Data.sDearName = "Spouse01";
        info.Data.boStudent = true;
        info.Data.sMasterName = "Master01";
        info.Data.btStudentCount = 2;
        info.Data.sStudentNames[0] = "Stu0";
        info.Data.sStudentNames[1] = "Stu1";

        var raw = EncodeInflated(info, "formed");
        if (raw == null) return;

        Check(ReadSlot(raw, SpouseSlot) == "Spouse01",
            $"spouse name must reach block slot 0x650 (got '{ReadSlot(raw, SpouseSlot)}')");
        Check(ReadSlot(raw, MasterSlot) == "Master01",
            $"master name must reach block slot 0x660 (got '{ReadSlot(raw, MasterSlot)}')");
        Check(ReadSlot(raw, StudentSlot0) == "Stu0",
            $"students[0] must reach 0x680 (got '{ReadSlot(raw, StudentSlot0)}')");
        Check(ReadSlot(raw, StudentSlot0 + SlotStride) == "Stu1",
            $"students[1] must reach 0x690 (got '{ReadSlot(raw, StudentSlot0 + SlotStride)}')");
        Check(raw[NativeHumanDataCodec.MarriedFlagOffset] == 1,
            "married flag must persist at 0xDB");

        // and decoding must give the names back (flag+name in sync)
        if (!NativeHumanDataCodec.TryEncode(info, out var blob2, out var script2, out _))
        {
            Failures.Add("formed: second TryEncode failed");
            return;
        }
        if (!NativeHumanDataCodec.TryDecode(blob2, script2, out var back, out var derr))
        {
            Failures.Add("formed: TryDecode failed: " + derr);
            return;
        }
        Check(back.Data.sDearName == "Spouse01",
            $"decode must recover sDearName (got '{back.Data.sDearName}')");
        Check(back.Data.sMasterName == "Master01",
            $"decode must recover sMasterName (got '{back.Data.sMasterName}')");
        Check(back.Data.sStudentNames[0] == "Stu0",
            $"decode must recover students[0] (got '{back.Data.sStudentNames[0]}')");
        Check(back.Data.boMarried, "decode must recover boMarried");
    }

    // A divorce must clear the slot, matching native 0x6C52C3.
    private static void RoundTripDivorceClears()
    {
        var info = NewInfo();
        // simulate a record that already carried a spouse name in the block
        var blob = new byte[128];
        var name = Gbk.GetBytes("ExSpouse");
        blob[0] = (byte)name.Length;
        name.CopyTo(blob, 1);
        info.Data.NativeSocialBlob = blob;
        // ...and a divorce that happened this session
        info.Data.boMarried = false;
        info.Data.sDearName = string.Empty;

        var raw = EncodeInflated(info, "divorce");
        if (raw == null) return;

        Check(raw[SpouseSlot] == 0,
            $"divorce must clear the 0x650 length byte like native 0x6C52C3 (got 0x{raw[SpouseSlot]:X2})");
        Check(raw[NativeHumanDataCodec.MarriedFlagOffset] == 0,
            "divorce must clear married flag at 0xDB");
        Check(ReadSlot(raw, SpouseSlot).Length == 0,
            "divorced record must decode to an empty spouse name");
    }

    // The externally-written ':'/'$' string at 0x670 overruns into 0x680 in all 30
    // golden records; the encode must not touch that foreign byte.
    private static void ForeignCompanionByteSurvives()
    {
        var info = NewInfo();
        var blob = new byte[128];
        // companion slot: length 0x1A with ':' filler running past 0x680
        blob[CompanionSlot - SpouseSlot] = 0x1A;
        for (var i = 1; i <= 0x1A; i++)
            blob[CompanionSlot - SpouseSlot + i] = 0x3A;
        info.Data.NativeSocialBlob = blob;
        info.Data.btStudentCount = 0;
        // no students -> encode would otherwise write len 0 over the foreign byte

        var raw = EncodeInflated(info, "foreign");
        if (raw == null) return;

        Check(raw[CompanionSlot] == 0x1A,
            $"companion length byte must survive (got 0x{raw[CompanionSlot]:X2})");
        Check(raw[StudentSlot0] == 0x3A,
            $"foreign overflow byte at 0x680 must survive, not be zeroed " +
            $"(got 0x{raw[StudentSlot0]:X2})");
        // slots past the overflow are legitimately writable
        Check(raw[StudentSlot0 + SlotStride] == 0,
            "students[1] slot stays empty when there is no student");
    }

    // 战神 loads HP/MP as plain dwords, no validation:
    //   0x6AFFF3 `mov eax,[eax+0x48]` -> 0x6AFFF9 `mov [edx+0x2ac],eax`
    //   0x6B0002 `mov eax,[eax+0x4c]` -> 0x6B0008 `mov [edx+0x2b4],eax`
    // A negative stored value must therefore LOAD, not reject the record. The old
    // ReadNonNegativeAbilityValue guard threw and locked the character out.
    private static void NegativeHpMpStillLoads()
    {
        var raw = new byte[DataRecordSize];
        raw[0x3E] = 1;                                   // hair, required by the load gate
        raw[0x00] = 4;                                   // char name "Test"
        Gbk.GetBytes("Test").CopyTo(raw, 1);
        raw[0x10] = 1;
        Gbk.GetBytes("0").CopyTo(raw, 0x11);
        raw[0x20] = 4;
        Gbk.GetBytes("acct").CopyTo(raw, 0x21);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x48, 4), -5);   // HP
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x4C, 4), -9);   // MP

        if (!NativeHumanLogicalCache.TryCreatePersistence("acct", "Test", raw,
                Array.Empty<byte>(), out var persistence, out var cerr))
        {
            Failures.Add("negativeHpMp: persistence creation failed: " + cerr);
            return;
        }
        // null script blob == "no sidecar", the same shape the golden bare
        // user_data rows have (ScriptData column is NULL in all 30).
        if (!NativeHumanDataCodec.TryDecode(persistence.DataBlob,
                null, out var info, out var derr))
        {
            Failures.Add("negativeHpMp: a record with negative HP/MP must still " +
                         "decode (native 0x6AFFF3 has no guard), got: " + derr);
            return;
        }
        Check(info.Data.Abil.HP == -5,
            $"negative HP must survive decode verbatim (got {info.Data.Abil.HP})");
        Check(info.Data.Abil.MP == -9,
            $"negative MP must survive decode verbatim (got {info.Data.Abil.MP})");
    }

    // native sub_4039E4 truncates at cl=0x0F rather than throwing.
    private static void TruncatesAtFifteenLikeNative()
    {
        var info = NewInfo();
        info.Data.boMarried = true;
        info.Data.sDearName = new string('A', 40);

        var raw = EncodeInflated(info, "truncate");
        if (raw == null) return;

        Check(raw[SpouseSlot] == 15,
            $"over-long name must truncate to 15 like sub_4039E4, not throw " +
            $"(got len {raw[SpouseSlot]})");
        Check(ReadSlot(raw, SpouseSlot) == new string('A', 15),
            "truncated name content must be the first 15 bytes");
    }
}

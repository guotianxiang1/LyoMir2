using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    /// <summary>
    /// The ten frames the original Delphi DBServer PUSHES to GameServers and
    /// that C# DBSvr had no builder for at all.
    ///
    /// They are asynchronous notifications, not replies: none of them is reached
    /// from the type-1 or type-2 request dispatcher.  Every producer is driven
    /// from the internal event queue drained by <c>sub_5D25EC</c> (pops
    /// <c>[self+0x44]</c> under the critical section at <c>[self+0x4C]</c>, then
    /// switches on <c>word[node+8]</c> @0x5D269C) or, for 0x0078/0x0079/0x007A,
    /// from the LoginGate session handlers.  Because C# DBSvr has no equivalent
    /// queue yet, this class only pins the WIRE BYTES; the triggers are still
    /// unwired and the codes are still MISSING at runtime.  See
    /// docs/m_dbsvr_fidelity_outbound_20260813.md.
    ///
    /// All VAs are DBServer (staging/_dbsvr_reunpack_work/dbserver_CODE_live.bin,
    /// VA = 0x401000 + offset).
    ///
    /// Shared type-1 body, 0x48 bytes, identical to
    /// <see cref="NativeAuxiliaryType1Protocol"/>:
    ///   +0x00 word   command
    ///   +0x02 word   secondary selector
    ///   +0x04 dword  scalar
    ///   +0x08 dword  } only 0x013B uses these two
    ///   +0x0C dword  }
    ///   +0x10 ShortString[20]  account       (assign helper 0x4035D8 with cl=0x14)
    ///   +0x25 ShortString[15]  character     (assign helper 0x4035D8 with cl=0x0F)
    ///   +0x35 ShortString[15]  third slot
    ///
    /// The type-1 producers allocate with <c>0x40ADCC</c>, which is AllocMem and
    /// not GetMem — it calls 0x402F48 then 0x4036E8 with ecx=0 — so every byte
    /// the constructor does not write is a hard zero on the wire.  The one
    /// exception is 0x0079/0x007A, whose frame is the stack local
    /// <c>[ebp-0x64]</c> in sub_5CEBC0 with no FillChar: there the untouched
    /// bytes are stack residue.  That residue is not reproducible and no reader
    /// consumes it, so these builders zero-fill throughout.
    /// </summary>
    public static class NativeOutboundNotificationProtocol
    {
        // ---- type 1, fixed 0x48 body -------------------------------------
        /// <summary>0x598676 <c>66 c7 00 46 00</c> — sub_598618, arm taken when the caller's Boolean is true.</summary>
        public const ushort CharacterQueryHitCommand = 0x0046;

        /// <summary>0x598680 <c>66 c7 00 47 00</c> — sub_598618, the false arm of the same branch (0x59866D <c>cmp byte [ebp+8],0</c> / 0x598671 <c>je</c>).</summary>
        public const ushort CharacterQueryMissCommand = 0x0047;

        /// <summary>0x598500 <c>66 c7 00 58 00</c> — sub_5984A8.</summary>
        public const ushort AccountNotificationCommand = 0x0058;

        /// <summary>0x5CF798 <c>66 c7 45 98 78 00</c> — sub_5CF514, LoginGate session arm at 0x5CF763.</summary>
        public const ushort SessionStateCommand = 0x0078;

        /// <summary>0x5CEC06 <c>66 c7 45 a8 79 00</c> — sub_5CEBC0, selector <c>word[msg+4] == 1</c>.</summary>
        public const ushort SessionDetailCommandA = 0x0079;

        /// <summary>0x5CEC9C <c>66 c7 45 a8 7a 00</c> — sub_5CEBC0, selector <c>word[msg+4] == 2</c>.</summary>
        public const ushort SessionDetailCommandB = 0x007A;

        /// <summary>0x59E22F <c>66 c7 00 2d 01</c> — sub_59E1CC.</summary>
        public const ushort AccountCharacterBroadcastCommand = 0x012D;

        /// <summary>0x59E38D <c>66 c7 00 3b 01</c> — sub_59E338. Neighbour of the already-implemented 0x013C.</summary>
        public const ushort PairedScalarBroadcastCommand = 0x013B;

        // ---- type 2, variable body ---------------------------------------
        /// <summary>
        /// 0x59D03E <c>66 c7 45 e8 72 00</c> — sub_59D020, emitted through the
        /// broadcast helper sub_59CE94.  This one was recorded as "frame type
        /// not traced" last round; it IS on the wire and it is TYPE 2, not 1:
        /// 0x59CEF1 <c>c7 00 77 bb aa 33</c> / 0x59CEFA <c>66 c7 40 04 02 00</c> /
        /// 0x59CF0A <c>89 42 08</c> with eax = payloadLen + 0x0C.
        /// </summary>
        public const ushort RelayBroadcastCommand = 0x0072;

        /// <summary>0x59E30B <c>66 c7 00 30 01</c> — sub_59E298, type 2 (0x59E2D2 <c>66 c7 40 04 02 00</c>).</summary>
        public const ushort BulkBroadcastCommand = 0x0130;

        public const int Type1BodySize = 0x48;
        /// <summary>The 12-byte type-2 body header: word command, word 0, dword 0, dword 0.</summary>
        public const int Type2HeaderSize = 0x0C;

        private const int SecondaryOffset = 0x02;
        private const int ScalarOffset = 0x04;
        private const int PairedFirstOffset = 0x08;
        private const int PairedSecondOffset = 0x0C;
        private const int AccountOffset = 0x10;
        private const int AccountCapacity = 20;
        private const int CharacterOffset = 0x25;
        private const int CharacterCapacity = 15;

        /// <summary>
        /// 0x0046 / 0x0047 — sub_598618(eax=connection, edx=characterName, cl=status,
        /// [esp+4]=found).  Delivered to the requesting connection alone
        /// (0x5986C1 <c>call 0x59C3C4</c> with ecx = 0x54).
        /// </summary>
        public static LegacyDbServerFrame CreateCharacterQuery(bool found,
            byte[] characterName, byte status)
        {
            var body = NewType1Body(found ? CharacterQueryHitCommand : CharacterQueryMissCommand);
            // 0x5986AD movzx eax,byte [ebp-9] -> 0x5986B3 mov [body+4],eax:
            // the byte argument is stored ZERO-EXTENDED as a full dword.
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(ScalarOffset, 4), status);
            WriteShortString(body, CharacterOffset, CharacterCapacity, characterName);
            return new LegacyDbServerFrame(1, 0, body);
        }

        /// <summary>
        /// 0x0058 — sub_5984A8(eax=connection, edx=account, ecx=scalar).  Unicast
        /// to the requesting connection (0x59853C <c>call 0x59C3C4</c>).
        /// </summary>
        public static LegacyDbServerFrame CreateAccountNotification(
            byte[] account, int scalar)
        {
            var body = NewType1Body(AccountNotificationCommand);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(ScalarOffset, 4), scalar);
            WriteShortString(body, AccountOffset, AccountCapacity, account);
            return new LegacyDbServerFrame(1, 0, body);
        }

        /// <summary>
        /// 0x0078 — sub_5CF514 arm 0x5CF763.  The arm is gated on
        /// <c>ax = word[msg+4]; dec ax; sub ax,2; jae skip</c> (0x5CF76A..0x5CF76F),
        /// i.e. it only fires for selector 1 or 2.  Sent to the single GameServer
        /// whose <c>[conn+0x40A0]</c> equals the session's <c>[self+0x19]</c>
        /// (0x5CF810 <c>call 0x59E450</c> with dl = that byte).
        /// </summary>
        public static LegacyDbServerFrame CreateSessionState(ushort selector,
            int scalar, byte[] account, byte[] characterName)
        {
            var body = NewType1Body(SessionStateCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(SecondaryOffset, 2), selector);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(ScalarOffset, 4), scalar);
            WriteShortString(body, AccountOffset, AccountCapacity, account);
            WriteShortString(body, CharacterOffset, CharacterCapacity, characterName);
            return new LegacyDbServerFrame(1, 0, body);
        }

        /// <summary>
        /// 0x0079 — sub_5CEBC0 with <c>word[msg+4] == 1</c> (0x5CEBDF <c>dec ax</c> /
        /// 0x5CEBE2 <c>je 0x5CEBF2</c>).  Carries BOTH scalars; the 0x007A arm
        /// carries only the dword.
        /// </summary>
        public static LegacyDbServerFrame CreateSessionDetailA(int scalar,
            ushort secondary, byte[] account, byte[] characterName)
        {
            var body = NewType1Body(SessionDetailCommandA);
            // 0x5CEC5B mov eax,[src+0xC] -> 0x5CEC5E mov [body+4],eax
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(ScalarOffset, 4), scalar);
            // 0x5CEC64 mov ax,word [src+0x10] -> 0x5CEC68 mov [body+2],ax
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(SecondaryOffset, 2), secondary);
            WriteShortString(body, AccountOffset, AccountCapacity, account);
            WriteShortString(body, CharacterOffset, CharacterCapacity, characterName);
            return new LegacyDbServerFrame(1, 0, body);
        }

        /// <summary>
        /// 0x007A — sub_5CEBC0 with <c>word[msg+4] == 2</c> (0x5CEBE4 <c>dec ax</c> /
        /// 0x5CEBE7 <c>je 0x5CEC88</c>).  Same shape as 0x0079 minus body+0x02:
        /// its tail 0x5CECEE..0x5CECF4 writes only <c>[body+4]</c>.
        /// </summary>
        public static LegacyDbServerFrame CreateSessionDetailB(int scalar,
            byte[] account, byte[] characterName)
        {
            var body = NewType1Body(SessionDetailCommandB);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(ScalarOffset, 4), scalar);
            WriteShortString(body, AccountOffset, AccountCapacity, account);
            WriteShortString(body, CharacterOffset, CharacterCapacity, characterName);
            return new LegacyDbServerFrame(1, 0, body);
        }

        /// <summary>
        /// 0x012D — sub_59E1CC(eax=manager, dx=selector, ecx=account,
        /// [esp+4]=characterName).  Broadcast: 0x59E28C <c>call 0x59E450</c> with
        /// dl = 0, and sub_59E450 with a zero filter sends to every connection
        /// whose role byte <c>[conn+0x40A0]</c> is not 9 / DBTool (0x59E4A8
        /// <c>cmp byte [eax+0x40a0],9</c> / 0x59E4AF <c>je</c> skip).
        ///
        /// Note the ORDER: the selector is stored at 0x59E228, BEFORE the command
        /// word at 0x59E22F. Both land in the same dword, so a builder that wrote
        /// a dword command would erase the selector.
        /// </summary>
        public static LegacyDbServerFrame CreateAccountCharacterBroadcast(
            ushort selector, byte[] account, byte[] characterName)
        {
            var body = NewType1Body(AccountCharacterBroadcastCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(SecondaryOffset, 2), selector);
            WriteShortString(body, AccountOffset, AccountCapacity, account);
            WriteShortString(body, CharacterOffset, CharacterCapacity, characterName);
            return new LegacyDbServerFrame(1, 0, body);
        }

        /// <summary>
        /// 0x013B — sub_59E338(eax=manager, edx=first, ecx=second).  The only one
        /// of the ten that uses body+0x08 and body+0x0C (0x59E3A4 <c>mov [body+8],edx</c>
        /// / 0x59E3AA <c>mov [body+0xC],edx</c>) and the only one with no string
        /// slot at all.  Broadcast, dl = 0.
        /// </summary>
        public static LegacyDbServerFrame CreatePairedScalarBroadcast(
            int first, int second)
        {
            var body = NewType1Body(PairedScalarBroadcastCommand);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(PairedFirstOffset, 4), first);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(PairedSecondOffset, 4), second);
            return new LegacyDbServerFrame(1, 0, body);
        }

        /// <summary>
        /// 0x0072 — sub_59D020 -> sub_59CE94.  Type 2 with a variable tail:
        /// DataLength is <c>payload.Length + 0x0C</c> and the body is the 12-byte
        /// header followed by the payload at frame+0x18 (0x59CF32
        /// <c>lea edx,[eax+0x18]</c> / 0x59CF38 <c>call 0x4031D0</c> = Move).
        /// The length argument is a WORD (0x59D04A <c>mov ax,word [ebp-0xc]</c>),
        /// so a payload above 65535 cannot be expressed.
        /// </summary>
        public static LegacyDbServerFrame CreateRelayBroadcast(byte[] payload)
            => CreateType2(RelayBroadcastCommand, payload);

        /// <summary>
        /// 0x0130 — sub_59E298(eax=manager, edx=record).  Type 2.  The copied
        /// length is <c>dword[record]</c> and the copy STARTS AT the record
        /// pointer itself (0x59E316 <c>mov eax,[ebp-8]</c> as the Move source), so
        /// that leading length dword is part of the transmitted payload rather
        /// than a header the sender strips.
        /// </summary>
        public static LegacyDbServerFrame CreateBulkBroadcast(byte[] lengthPrefixedRecord)
            => CreateType2(BulkBroadcastCommand, lengthPrefixedRecord);

        private static LegacyDbServerFrame CreateType2(ushort command, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            if (payload.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(payload),
                    "native type-2 payload length is carried in a word");
            var body = new byte[Type2HeaderSize + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(body, command);
            payload.CopyTo(body, Type2HeaderSize);
            return new LegacyDbServerFrame(2, 0, body);
        }

        private static byte[] NewType1Body(ushort command)
        {
            var body = new byte[Type1BodySize];
            BinaryPrimitives.WriteUInt16LittleEndian(body, command);
            return body;
        }

        /// <summary>
        /// The Delphi fixed ShortString assign sub_4035D8: <c>mov bl,[edx]</c> /
        /// <c>cmp cl,bl</c> / <c>jbe +2</c> / <c>mov ecx,ebx</c> -> the stored
        /// length is min(sourceLength, capacity), and it TRUNCATES instead of
        /// failing.  It does not clear the rest of the slot, but every type-1
        /// body here starts zeroed, so the tail is zero either way.
        /// </summary>
        private static void WriteShortString(Span<byte> destination, int offset,
            int capacity, byte[] value)
        {
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            destination[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
        }
    }
}

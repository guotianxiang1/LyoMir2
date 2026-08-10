using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    /// <summary>
    /// Sub-command of the native 宗派/师门 protocol, taken from the original
    /// dispatcher's 14-entry jump table at 0x594122 (index = header[+4]).
    /// </summary>
    public enum NativeZongpaiSubCommand
    {
        None = 0,
        Enumerate = 1,
        CreateMaster = 2,
        AddMember = 3,
        RemoveMember = 4,
        UpdateMemberRole = 5,
        UpdateStudentExp = 6,
        UpdateStudentAndMasterExp = 7,
        UpdateMasterExp = 8,
        UpdateMasterLevel = 9,
        QueryMembers = 10,
        ReadNotice = 11,
        ModifyNotice = 12,
        DeleteMaster = 13,
    }

    /// <summary>
    /// How the original replies once a sub-command produced a result:
    /// 1 = 只答复请求方 (0x59C51C:0059C558 → 0x49CB34),
    /// 2 = 广播 to every non-DB-tool GameServer (0x59C51C:0059C570 → 0x59E450, edx=0).
    /// </summary>
    public enum NativeZongpaiReplyMode
    {
        None = 0,
        Sender = 1,
        Broadcast = 2,
    }

    public sealed class NativeZongpaiRequest
    {
        public NativeZongpaiSubCommand SubCommand { get; init; }

        /// <summary>The original 0x48-byte type1 header (dispatcher edx / [ebp-8]).</summary>
        public byte[] Header { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// The variable-length tail the caller derives as record+0x48
        /// (0x59DDB2); its length is the value the 0x54 gate tests.
        /// </summary>
        public byte[] Tail { get; init; } = Array.Empty<byte>();

        /// <summary>ShortString at tail+0x00.</summary>
        public byte[] TailSlot00 { get; init; } = Array.Empty<byte>();
        /// <summary>ShortString at tail+0x10.</summary>
        public byte[] TailSlot10 { get; init; } = Array.Empty<byte>();
        /// <summary>ShortString at tail+0x25.</summary>
        public byte[] TailSlot25 { get; init; } = Array.Empty<byte>();
        /// <summary>ShortString at tail+0x35.</summary>
        public byte[] TailSlot35 { get; init; } = Array.Empty<byte>();
        /// <summary>dword at tail+0x4C.</summary>
        public int TailValue4C { get; init; }
        /// <summary>dword at tail+0x50.</summary>
        public int TailValue50 { get; init; }

        // ===== 槽 → 语义 的选择器 =====
        // 这些不是便利属性，而是**唯一允许的取值途径**：GameSocService 的每个 case
        // 都必须经由它们取参数。这样审计才能在不实例化 GameSocService（20+ 依赖，
        // 审计里构造不出来）的前提下，真正覆盖「哪个槽喂给哪个参数」这一层。
        //
        // 背景：sub 2 / sub 3 各有一处已确证的写坏数据的槽映射错误，
        // 而当时**没有任何断言覆盖调用点** —— 两个审计在修复前后都绿。
        // 把映射收敛到这里，配合 NativeZongpaiProtocolCheck 的断言，才算真护栏。

        /// <summary>
        /// sub 3 的 MemberName。模板 0x593140 的 TVarRec slot1 = [ebp+8]（0x5930DD）。
        /// 槽容量 15（tail+0x25 到 tail+0x35 = 长度字节+15），对应 DDL 0x5BF0C4 的
        /// <c>MemberName varchar(15)</c>，且该列有 <c>unique key MemberName_Index</c>，
        /// 所以传错会撞唯一键而不只是存错字符串。
        /// </summary>
        public byte[] AddMemberMemberName => TailSlot25;

        /// <summary>
        /// sub 3 的 RoleName。TVarRec slot2 = [ebp-0xC]（0x5930E7）。
        /// 槽容量 20（tail+0x10 到 tail+0x25），对应 <c>RoleName varchar(20)</c>。
        /// </summary>
        public byte[] AddMemberRoleName => TailSlot10;

        /// <summary>
        /// sub 2 的 MasterLevel。调用点 0x59420C <c>mov cx, word ptr [eax+0x4c]</c>
        /// —— 取 tail+0x4C 的**低 16 位**（原版 movzx，DDL 为 smallint unsigned）。
        /// </summary>
        public ushort CreateMasterLevel => unchecked((ushort)TailValue4C);

        /// <summary>
        /// sub 2 的 StudentExp。调用点 0x5941EE <c>mov eax,[eax+0x50]</c> / push
        /// —— tail+0x50 的完整 dword，按 u32 落库（DDL <c>int unsigned</c>）。
        /// 此前这个值被在 INSERT 里硬写 0，客户端送的值被丢弃。
        /// </summary>
        public uint CreateMasterStudentExp => unchecked((uint)TailValue50);

        /// <summary>
        /// sub 6 的 StudentExp 增量。worker 0x5943BA 按 **dword** 读 tail+0x4C
        /// （sub 2 读同一偏移的 word —— 同一字段两种宽度，不是两个字段）。
        /// </summary>
        public uint StudentExpDelta => unchecked((uint)TailValue4C);

        /// <summary>
        /// sub 7 的转换额度。worker 0x59433A 读 tail+0x50。
        /// 它先从 StudentExp 扣这个数，再把 <c>额度 / 10</c> 加到 MasterExp。
        /// </summary>
        public uint ConvertExpAmount => unchecked((uint)TailValue50);

        /// <summary>
        /// sub 8 的 MasterExp 扣减额。worker 0x594368 按 dword 读 tail+0x4C。
        /// </summary>
        public uint MasterExpDelta => unchecked((uint)TailValue4C);

        /// <summary>
        /// sub 2 / sub 9 / sub 13 的 MasterName —— 取 tail+0x35，**不是** tail+0x00。
        /// sub 2 调用点 0x5941FB <c>add edx,0x35</c>。
        /// </summary>
        public byte[] MasterNameSlot35 => TailSlot35;

        /// <summary>ShortString at header+0x25 (used by sub-commands 11/12).</summary>
        public byte[] HeaderSlot25 { get; init; } = Array.Empty<byte>();
        /// <summary>ShortString at header+0x35 (used by sub-commands 11/12/9/10).</summary>
        public byte[] HeaderSlot35 { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// dword the reply echoes into body+4; the original reads header[+4]
        /// (0x59443E `[[ebp-0xC]+4]`), which is the sub-command dword itself.
        /// </summary>
        public int EchoDword { get; init; }
    }

    /// <summary>
    /// Native 宗派/师门 sub-protocol (type1 command 0x0170), reversed byte-for-byte
    /// from the 战神 DBServer: dispatcher 0x599206 → 0x59C51C → 0x594070.
    ///
    /// Argument roles were resolved from the dispatcher's own caller at
    /// 0x59DDAC-0x59DDD3: the record splits into a 0x48-byte header (edx) and a
    /// tail at record+0x48 (ecx) whose length is what the `cmp …,0x54` gate tests.
    /// The field slots below are therefore TAIL offsets, except HeaderSlot25/35.
    /// See staging/dbsvr_type1_dispatch_census_20260803.md §3之二.
    /// </summary>
    public static class NativeZongpaiProtocol
    {
        public const ushort RequestCommand = 0x0170;
        public const ushort ResponseCommand = 0x0071;

        /// <summary>The 0x48-byte type1 header every native frame carries.</summary>
        public const int HeaderSize = 0x48;
        /// <summary>Tail length the original requires: 0x594102 `cmp [len],0x54 / jl`.</summary>
        public const int MinimumTailLength = 0x54;
        /// <summary>Standard reply: 0x5943CF writes 0xA8 total, 0x9C payload.</summary>
        public const int StandardReplyTotalLength = 0xA8;
        /// <summary>Sub-command 9 reply: 0x5944B8 writes 0x84 total, 0x78 payload.</summary>
        public const int LevelReplyTotalLength = 0x84;
        /// <summary>Sub-command 9 copies 48 bytes to reply+0x54 (rep movsd ecx=0xC).</summary>
        public const int LevelReplyRecordSize = 0x30;
        /// <summary>Sub-command 10 stride: 0x59457F `imul eax,[n],0x29`.</summary>
        public const int MemberRecordSize = 0x29;
        /// <summary>
        /// Where trailing data starts, PAYLOAD-relative. The original writes it to
        /// buf+0x54, and payload == buf+0x0C (magic/type/len occupy the first 12
        /// bytes), so 0x54 - 0x0C = 0x48. Cross-checks: standard reply
        /// 0x48 + 0x54 == 0x9C payload; sub-command 9 0x48 + 0x30 == 0x78.
        /// </summary>
        public const int ReplyDataOffset = 0x48;

        private const int WireHeaderSize = LegacyDbServerFrameCodec.HeaderSize;
        private const int SubCommandOffset = 0x04;
        private const int Slot00Offset = 0x00;
        private const int Slot10Offset = 0x10;
        private const int Slot25Offset = 0x25;
        private const int Slot35Offset = 0x35;
        private const int Value4COffset = 0x4C;
        private const int Value50Offset = 0x50;
        private const int ShortStringCapacity = 0x0F;
        private const int WideShortStringCapacity = 0x14;

        /// <summary>
        /// Reply mode each sub-command sets via `mov [ebp-0x10], n` at its case
        /// entry. Sub-command 6 (0x5943A3) never writes it, so the original gives
        /// no guaranteed reply for it — modelled as None rather than inventing a
        /// frame the original does not emit.
        /// </summary>
        public static NativeZongpaiReplyMode GetReplyMode(
            NativeZongpaiSubCommand subCommand) => subCommand switch
            {
                NativeZongpaiSubCommand.CreateMaster => NativeZongpaiReplyMode.Sender,
                NativeZongpaiSubCommand.UpdateStudentAndMasterExp => NativeZongpaiReplyMode.Sender,
                NativeZongpaiSubCommand.UpdateMasterExp => NativeZongpaiReplyMode.Sender,
                NativeZongpaiSubCommand.UpdateMasterLevel => NativeZongpaiReplyMode.Sender,
                NativeZongpaiSubCommand.QueryMembers => NativeZongpaiReplyMode.Sender,
                NativeZongpaiSubCommand.ReadNotice => NativeZongpaiReplyMode.Sender,
                NativeZongpaiSubCommand.ModifyNotice => NativeZongpaiReplyMode.Sender,
                NativeZongpaiSubCommand.DeleteMaster => NativeZongpaiReplyMode.Sender,
                NativeZongpaiSubCommand.AddMember => NativeZongpaiReplyMode.Broadcast,
                NativeZongpaiSubCommand.RemoveMember => NativeZongpaiReplyMode.Broadcast,
                NativeZongpaiSubCommand.UpdateMemberRole => NativeZongpaiReplyMode.Broadcast,
                _ => NativeZongpaiReplyMode.None,
            };

        /// <summary>
        /// Sub-commands routed through a group that re-tests the tail-length gate:
        /// 1..8 and 13 via 0x594102, 9 via 0x594465, 10 via 0x59454C. Sub-command 0
        /// falls straight to 0x59476B, and 11/12 (0x59463E) never test it.
        /// </summary>
        public static bool RequiresLengthGate(
            NativeZongpaiSubCommand subCommand) => subCommand switch
            {
                NativeZongpaiSubCommand.Enumerate => true,
                NativeZongpaiSubCommand.CreateMaster => true,
                NativeZongpaiSubCommand.AddMember => true,
                NativeZongpaiSubCommand.RemoveMember => true,
                NativeZongpaiSubCommand.UpdateMemberRole => true,
                NativeZongpaiSubCommand.UpdateStudentExp => true,
                NativeZongpaiSubCommand.UpdateStudentAndMasterExp => true,
                NativeZongpaiSubCommand.UpdateMasterExp => true,
                NativeZongpaiSubCommand.UpdateMasterLevel => true,
                NativeZongpaiSubCommand.QueryMembers => true,
                NativeZongpaiSubCommand.DeleteMaster => true,
                _ => false,
            };

        public static bool TryDecodeRequest(LegacyDbServerFrame frame,
            out NativeZongpaiRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native zongpai frame is null";
                return false;
            }
            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length < HeaderSize)
            {
                error = "native zongpai payload is truncated";
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != RequestCommand)
            {
                error = "native zongpai command mismatch";
                return false;
            }

            // 0x594070: `eax = [hdr+4]` then `cmp eax,0xD / ja` — anything above
            // 13 takes the error exit and sends no reply.
            var rawSubCommand = BinaryPrimitives.ReadInt32LittleEndian(
                payload.AsSpan(SubCommandOffset, 4));
            if (rawSubCommand < 0 || rawSubCommand > 0x0D)
            {
                error = $"native zongpai sub-command out of range: {rawSubCommand}";
                return false;
            }
            var subCommand = (NativeZongpaiSubCommand)rawSubCommand;

            // 0x59DDAC: the caller only forms a tail when total length > 0x48.
            var header = payload.AsSpan(0, HeaderSize).ToArray();
            var tail = payload.Length > HeaderSize
                ? payload.AsSpan(HeaderSize).ToArray()
                : Array.Empty<byte>();

            if (RequiresLengthGate(subCommand) && tail.Length < MinimumTailLength)
            {
                error = "native zongpai tail shorter than 0x54";
                return false;
            }

            if (!TryReadShortString(tail, Slot00Offset, WideShortStringCapacity,
                    "tail", out var tailSlot00, out error)
                || !TryReadShortString(tail, Slot10Offset, WideShortStringCapacity,
                    "tail", out var tailSlot10, out error)
                || !TryReadShortString(tail, Slot25Offset, ShortStringCapacity,
                    "tail", out var tailSlot25, out error)
                || !TryReadShortString(tail, Slot35Offset, ShortStringCapacity,
                    "tail", out var tailSlot35, out error)
                || !TryReadShortString(header, Slot25Offset, ShortStringCapacity,
                    "header", out var headerSlot25, out error)
                || !TryReadShortString(header, Slot35Offset, ShortStringCapacity,
                    "header", out var headerSlot35, out error))
                return false;

            var value4C = 0;
            var value50 = 0;
            if (tail.Length >= Value4COffset + 4)
                value4C = BinaryPrimitives.ReadInt32LittleEndian(
                    tail.AsSpan(Value4COffset, 4));
            if (tail.Length >= Value50Offset + 4)
                value50 = BinaryPrimitives.ReadInt32LittleEndian(
                    tail.AsSpan(Value50Offset, 4));

            request = new NativeZongpaiRequest
            {
                SubCommand = subCommand,
                Header = header,
                Tail = tail,
                TailSlot00 = tailSlot00,
                TailSlot10 = tailSlot10,
                TailSlot25 = tailSlot25,
                TailSlot35 = tailSlot35,
                TailValue4C = value4C,
                TailValue50 = value50,
                HeaderSlot25 = headerSlot25,
                HeaderSlot35 = headerSlot35,
                EchoDword = rawSubCommand,
            };
            return true;
        }

        /// <summary>
        /// Standard reply built by the shared tail 0x5943C5: 0xA8 total / 0x9C
        /// payload / command 0x71 / result at body+2 / echo at body+4 / the first
        /// 0x54 bytes of the request TAIL copied to payload+0x54
        /// (0x59444A: `eax=[ebp-4]` = tail pointer, rep movsd ecx=0x15).
        /// </summary>
        public static LegacyDbServerFrame CreateStandardResponse(
            NativeZongpaiRequest request, int result)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[StandardReplyTotalLength - WireHeaderSize];
            WriteReplyHeader(payload, result, request.EchoDword);
            var echo = Math.Min(MinimumTailLength, request.Tail.Length);
            if (echo > 0)
                request.Tail.AsSpan(0, echo)
                    .CopyTo(payload.AsSpan(ReplyDataOffset));
            return new LegacyDbServerFrame(1, 0, payload);
        }

        /// <summary>
        /// Sub-command 9 reply (0x594465): 0x84 total / 0x78 payload, trailing
        /// 48-byte record at payload+0x54 (rep movsd ecx=0xC from [ebp-0x8C],
        /// an out-parameter the worker 0x593944 fills).
        /// </summary>
        public static LegacyDbServerFrame CreateMasterLevelResponse(
            NativeZongpaiRequest request, int result, ReadOnlySpan<byte> record)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[LevelReplyTotalLength - WireHeaderSize];
            WriteReplyHeader(payload, result, request.EchoDword);
            var copy = Math.Min(LevelReplyRecordSize, record.Length);
            if (copy > 0)
                record.Slice(0, copy).CopyTo(payload.AsSpan(ReplyDataOffset));
            return new LegacyDbServerFrame(1, 0, payload);
        }

        /// <summary>
        /// Sub-command 10 reply (0x59454C): total = count*0x29 + 0x54, payload =
        /// count*0x29 + 0x48; body+2 carries the COUNT (not a result code); the
        /// request's TAIL +0x35 ShortString goes to body+0x25 (0x594600-0x59460E);
        /// member records land at payload+0x54. With count &lt;= 0 the original
        /// exits at 0x594613 without copying record data.
        /// </summary>
        public static LegacyDbServerFrame CreateMemberListResponse(
            NativeZongpaiRequest request, int count, ReadOnlySpan<byte> records)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (count < 0) count = 0;
            // total = count*0x29 + 0x54 ⇒ payload = total - 0x0C = count*0x29 + 0x48
            var payload = new byte[count * MemberRecordSize + HeaderSize];
            WriteReplyHeader(payload, count, request.EchoDword);
            WriteShortString(payload, Slot25Offset, ShortStringCapacity,
                request.TailSlot35);
            if (count > 0)
            {
                var copy = Math.Min(count * MemberRecordSize, records.Length);
                if (copy > 0)
                    records.Slice(0, copy)
                        .CopyTo(payload.AsSpan(ReplyDataOffset));
            }
            return new LegacyDbServerFrame(1, 0, payload);
        }

        /// <summary>
        /// Sub-command 11/12 reply (0x59463E): total = len + 0x54 + 1, payload =
        /// len + 0x48 + 1; body+2 carries the notice LENGTH; the HEADER's +0x25
        /// and +0x35 ShortStrings are copied to body+0x25 and body+0x35
        /// (0x594738/0x59474E read `[ebp-0xC]` = the header base); the notice text
        /// goes to payload+0x54. The original skips the reply entirely when the
        /// notice pointer is null (0x594695).
        /// </summary>
        public static LegacyDbServerFrame CreateNoticeResponse(
            NativeZongpaiRequest request, ReadOnlySpan<byte> notice)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var length = notice.Length;
            var payload = new byte[length + HeaderSize + 1];
            WriteReplyHeader(payload, length, request.EchoDword);
            WriteShortString(payload, Slot25Offset, ShortStringCapacity,
                request.HeaderSlot25);
            WriteShortString(payload, Slot35Offset, ShortStringCapacity,
                request.HeaderSlot35);
            if (length > 0)
                notice.CopyTo(payload.AsSpan(ReplyDataOffset));
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static void WriteReplyHeader(byte[] payload, int result,
            int echo)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                unchecked((ushort)result));
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), echo);
        }

        private static bool TryReadShortString(byte[] source, int offset,
            int capacity, string region, out byte[] value, out string error)
        {
            value = Array.Empty<byte>();
            error = string.Empty;
            if (offset >= source.Length) return true;
            var length = source[offset];
            if (length > capacity)
            {
                error = $"native zongpai {region} ShortString at 0x{offset:X2} "
                        + $"exceeds {capacity} bytes";
                return false;
            }
            if (offset + 1 + length > source.Length)
            {
                error = $"native zongpai {region} ShortString at 0x{offset:X2} "
                        + "runs past the record";
                return false;
            }
            value = source.AsSpan(offset + 1, length).ToArray();
            return true;
        }

        private static void WriteShortString(byte[] payload, int offset,
            int capacity, byte[] value)
        {
            var source = value ?? Array.Empty<byte>();
            var length = Math.Min(source.Length, capacity);
            if (offset >= payload.Length) return;
            payload[offset] = (byte)length;
            if (length > 0 && offset + 1 + length <= payload.Length)
                source.AsSpan(0, length).CopyTo(payload.AsSpan(offset + 1));
        }
    }
}

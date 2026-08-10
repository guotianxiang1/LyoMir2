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
        /// sub 1 的**输入**：要查找的成员名，取 tail+0x35。
        /// 0x594173 `lea eax,[ebp-0x90]` / 0x594179 `mov edx,[ebp-4]` /
        /// 0x59417C `add edx,0x35` / 0x59417F `call 0x404E5C`（ShortString→长串），
        /// 该长串即 worker 0x5933CC 的 edx 参数（0x594184/0x59418A/0x59418E）。
        /// ⚠️ 与 sub 10 不同：sub 10 的名字取 tail+0x00，sub 1 取 tail+0x35。
        /// </summary>
        public byte[] EnumerateMemberName => TailSlot35;

        /// <summary>
        /// sub 10 的**查询键**：师父名，取 tail+0x00。
        /// 0x59455D `lea eax,[ebp-0x1D0]` / 0x594563 `mov edx,[ebp-4]`（**无 add**）/
        /// 0x594566 `call 0x404E5C` ⇒ 直接用 tail 基址，即 tail+0x00。
        /// </summary>
        public byte[] QueryMembersMasterName => TailSlot00;

        /// <summary>
        /// sub 10 回包里回显到 body+0x25 的名字，取 tail+0x35（0x594606
        /// `mov edx,[ebp-4]` / 0x594609 `add edx,0x35` / 0x59460C `mov cl,0x0F`）。
        /// ⚠️ 与查询键**不是同一个槽** —— 原版用 tail+0x00 查、用 tail+0x35 回显。
        /// </summary>
        public byte[] QueryMembersEchoName => TailSlot35;

        /// <summary>
        /// sub 11 / sub 12 的师父名，取 **HEADER**+0x35，不是 tail。
        /// sub 11: 0x59464D `mov edx,[ebp-0xC]` / 0x594650 `add edx,0x35`；
        /// sub 12: 0x594679 `mov edx,[ebp-0xC]` / 0x59467C `add edx,0x35`。
        /// `[ebp-0xC]` 是 0x59408A 存入的 edx = 0x48 字节头部基址。
        /// </summary>
        public byte[] NoticeMasterName => HeaderSlot35;

        /// <summary>
        /// sub 12 要写入的公告正文 = **整条 tail 原始字节**，长度 = tail 长度。
        /// 0x59466B `mov eax,[ebp+0x10]` / push（=派发器第三参数 tail 长度 → worker 的
        /// [ebp+0xC]）、0x59468A `mov ecx,[ebp-4]`（=tail 指针 → worker 的 [ebp-0xC]），
        /// worker 0x593E43 `mov edx,[ebp-0xC]` / 0x593E46 `mov ecx,[ebp+0xC]` /
        /// 0x593E4E `call dword [ebx+0x10]` ⇒ 按 (指针, 长度) 原样写进 blob 流。
        /// ⚠️ 不是 ShortString、不做任何槽解析：tail 里的 0x00 填充也会被写进 Notice 列。
        /// </summary>
        public byte[] ModifyNoticeText => Tail;

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
        /// sub 12 自己的**上界**门（与 0x54 下界门无关，group 4 没有 0x54 门）：
        /// worker 0x593DA0 `cmp dword [ebp+0xC],0x80` / 0x593DA7 `jg 0x593ECC`
        /// ⇒ 公告长度 &gt; 0x80 直接跳到出口，既不写库也不产生回包字符串。
        /// 判据是 `jg`（**带符号**），且比较值是派发器透传的 tail 长度。
        /// </summary>
        public const int MaximumNoticeLength = 0x80;

        /// <summary>
        /// sub 10 的成员记录内部布局，逐字取自 worker 0x593B74 的填充循环：
        ///   +0x00 (cl=0x14) RoleName    0x593C8D `mov eax,[ebp-0x30]` / 0x593C90 `mov cl,0x14`
        ///   +0x15 (cl=0x0F) MemberName  0x593C60 `add eax,0x15` / 0x593C63 `mov cl,0x0F`
        ///   +0x25 word      Level       0x593C9A 先清 0；0x593CD6 `mov ax,[live+0x3E]` /
        ///                               0x593CDD `mov word [rec+0x25],ax`
        ///   +0x27 byte      OnlineFlag  0x593CA3 先清 0；0x593CE4 `mov al,[live+0x25]` /
        ///                               0x593CEA `mov byte [rec+0x27],al`
        ///   +0x28           **从不写入**（0x29 - 0x28 = 1 字节尾部空洞，恒 0；
        ///                   整块在 0x593BFC 已被 0x4036E8 清零）
        /// ⚠️ 顺序违反直觉：容量 20 的 RoleName 在**前**、容量 15 的 MemberName 在后。
        /// 两者由 `mov cl` 的容量与写入基址共同定死，不能按名字顺序猜。
        /// </summary>
        public const int MemberRecordRoleNameOffset = 0x00;
        /// <summary>See <see cref="MemberRecordRoleNameOffset"/> (0x593C60，cl=0x0F)。</summary>
        public const int MemberRecordMemberNameOffset = 0x15;
        /// <summary>See <see cref="MemberRecordRoleNameOffset"/> (0x593CDD word)。</summary>
        public const int MemberRecordLevelOffset = 0x25;
        /// <summary>See <see cref="MemberRecordRoleNameOffset"/> (0x593CEA byte)。</summary>
        public const int MemberRecordOnlineOffset = 0x27;
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
        /// Reply mode each sub-command sets via `mov dword [ebp-0x10], n` at its
        /// case entry. `[ebp-0x10]` IS the dispatcher's return value — the
        /// epilogue is `0059481A mov eax,[ebp-0x10]` (bytes `8b 45 f0`) followed
        /// by `ret 0xc` — and the caller 0x59C51C branches on it:
        ///   0059C544 call 0x594070      ; -> [ebp-0x18]
        ///   0059C54C cmp [ebp-0x10],0 / je   ; empty buffer -> send nothing
        ///   0059C552 cmp [ebp-0x18],1 / jne  ; 1 -> 0x49CB34 (sender only)
        ///   0059C56A cmp [ebp-0x18],2 / jne  ; 2 -> 0x59E450 (broadcast)
        /// It is initialised to 0 at 0x5940A0 (`xor eax,eax / mov [ebp-0x10],eax`),
        /// so "never written" == no reply.
        ///
        /// Full census, taken from the two-level dispatch (byte-verified):
        /// first-level group table 0x5940EE = {0x59476B, 0x594102, 0x594465,
        /// 0x59454C, 0x59463E} selected by the byte map at 0x5940E0 =
        /// `00 01 01 01 01 01 01 01 01 02 03 04 04 01` (index = sub-command),
        /// i.e. sub0 -> group0, sub1..8+13 -> group1, sub9 -> group2,
        /// sub10 -> group3, sub11/12 -> group4.
        ///   sub0  0x59476B  no write            -> None (native no-op)
        ///   sub1  0x59415A  c7 45 f0 01 000000  -> Sender   ★ was wrongly None
        ///   sub2  0x5941E4  c7 45 f0 01 000000  -> Sender
        ///   sub3  0x594220  c7 45 f0 02 000000  -> Broadcast
        ///   sub4  0x59427C  c7 45 f0 02 000000  -> Broadcast
        ///   sub5  0x5942C0  c7 45 f0 02 000000  -> Broadcast
        ///   sub6  0x5943A3  no write            -> None
        ///   sub7  0x59431C  c7 45 f0 01 000000  -> Sender
        ///   sub8  0x59434A  c7 45 f0 01 000000  -> Sender
        ///   sub9  0x59446F  c7 45 f0 01 000000  -> Sender
        ///   sub10 0x594556  c7 45 f0 01 000000  -> Sender
        ///   sub11 0x59469F  c7 45 f0 01 000000  -> Sender (see note)
        ///   sub12 0x59469F  c7 45 f0 01 000000  -> Sender (see note)
        ///   sub13 0x594378  c7 45 f0 01 000000  -> Sender
        /// The group-1 second-level table at 0x594122 has 14 slots but is only
        /// reached for the nine sub-commands mapped to group 1; its slots for
        /// 0/9/10/11/12 are filler pointing at the shared reply allocator
        /// 0x5943C5 and are unreachable.
        ///
        /// Note on 11/12: they share one route write at 0x59469F which is guarded
        /// by `00594695 cmp dword [ebp-0x24],0 / je 0x59476B` — the reply exists
        /// only when the produced string is non-empty. Sender is therefore the
        /// mode *when a reply happens*; the emptiness test belongs to the body.
        /// Sub-command 6 (0x5943A3) never writes the route at all, so the
        /// original gives no reply for it — modelled as None rather than
        /// inventing a frame the original does not emit.
        /// </summary>
        public static NativeZongpaiReplyMode GetReplyMode(
            NativeZongpaiSubCommand subCommand) => subCommand switch
            {
                // 0x59415A: the FIRST instruction of the sub-1 case body is the
                // route write. Modelling it as None suppressed a reply the
                // original always sends.
                NativeZongpaiSubCommand.Enumerate => NativeZongpaiReplyMode.Sender,
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
        /// Sub-commands routed through a group whose entry re-tests the tail-length
        /// gate `cmp dword [ebp+0x10],0x54 / jl 0x59476B` (bytes `83 7d 10 54`).
        /// Byte-verified per group entry:
        ///   group1 0x594102 `83 7d 10 54` + `0f 8c` -> GATED (sub 1..8, 13)
        ///   group2 0x594465 `83 7d 10 54` + `0f 8c` -> GATED (sub 9)
        ///   group3 0x59454C `83 7d 10 54` + `0f 8c` -> GATED (sub 10)
        ///   group4 0x59463E starts `8b 45 f4` (mov eax,[ebp-0xc]) -> NOT gated
        ///                                                            (sub 11, 12)
        ///   group0 0x59476B is the shared exit itself -> N/A (sub 0)
        /// `[ebp+0x10]` is the dispatcher's third stack argument = the tail length.
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

        /// <summary>
        /// sub 12 独有的**上界**门，逐字取自 worker 0x593D70：
        ///   0x593DA0  `cmp dword [ebp+0xC],0x80`
        ///   0x593DA7  `jg 0x593ECC`        ; **带符号** jg，落到 worker 出口
        /// 出口 0x593ECC 之后不写 out 参数，故 out 保持 0x593D9B（`call 0x404BF8`
        /// 清空 out）留下的空串；派发器随即在 0x594695 `cmp [ebp-0x24],0 / je`
        /// 判空 ⇒ **超长请求既不落库也不回包**（连 [ebp-0x10] 都不写）。
        /// ⚠️ 这与 group 1/2/3 的 0x54 **下界**门是两件独立的事：group 4 入口
        /// 0x59463E 是 `8b 45 f4`，根本没有 0x54 门（<see cref="RequiresLengthGate"/>）。
        /// </summary>
        public static bool IsNoticeLengthAccepted(int noticeLength)
            => noticeLength <= MaximumNoticeLength;

        /// <summary>
        /// 该子命令是否把 tail 当作**带 ShortString 槽的结构**来解析。
        /// group 1/2/3 都会（0x5941AF/0x594563/0x5941FB 之类的 `add …,0x35` 取槽），
        /// 但 **group 4（sub 11/12）不会**：入口 0x59463E 全程只碰 tail 指针一次 ——
        ///   0x59468A `mov ecx,[ebp-4]`（指针）配 0x59466B `mov eax,[ebp+0x10]`（长度）
        ///   → 0x594690 `call 0x593D70`，worker 再 0x593E4E `call [ebx+0x10]` 原样写流。
        /// 组内没有任何 `add eax,0x25` / `add eax,0x35` 之类的 tail 取槽指令
        /// （sub 11/12 的两处 `add …,0x35` 都作用在 `[ebp-0xC]` = **header**）。
        ///
        /// ⚠️ 这条不是装饰：公告正文是**二进制 blob**（Notice 列类型 blob，
        /// DDL 0x5BEE34），任意字节都合法。若对 sub 11/12 照样按 ShortString
        /// 校验 tail，正文里 offset 0x25 的字节只要 &gt; 0x0F 整条请求就会被拒 ——
        /// 而原版照单全收并写进库。那是 C# 侧凭空多出来的拒绝，属功能缺失。
        /// </summary>
        public static bool ParsesTailShortStrings(
            NativeZongpaiSubCommand subCommand) => subCommand switch
            {
                // group 4：只透传 (指针, 长度)，不解析槽。
                NativeZongpaiSubCommand.ReadNotice => false,
                NativeZongpaiSubCommand.ModifyNotice => false,
                // sub 0 是共享出口 0x59476B，根本不看 tail。
                NativeZongpaiSubCommand.None => false,
                _ => true,
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

            // 头部槽对所有子命令都解析（sub 11/12 靠它取师父名，0x594738/0x59474E）。
            if (!TryReadShortString(header, Slot25Offset, ShortStringCapacity,
                    "header", out var headerSlot25, out error)
                || !TryReadShortString(header, Slot35Offset, ShortStringCapacity,
                    "header", out var headerSlot35, out error))
                return false;

            // tail 槽只对真正取槽的组解析。sub 11/12（group 4）把 tail 整块当
            // 二进制公告正文透传（见 ParsesTailShortStrings），对它做 ShortString
            // 校验会凭空拒掉原版接受的请求。
            var tailSlot00 = Array.Empty<byte>();
            var tailSlot10 = Array.Empty<byte>();
            var tailSlot25 = Array.Empty<byte>();
            var tailSlot35 = Array.Empty<byte>();
            if (ParsesTailShortStrings(subCommand)
                && (!TryReadShortString(tail, Slot00Offset, WideShortStringCapacity,
                        "tail", out tailSlot00, out error)
                    || !TryReadShortString(tail, Slot10Offset, WideShortStringCapacity,
                        "tail", out tailSlot10, out error)
                    || !TryReadShortString(tail, Slot25Offset, ShortStringCapacity,
                        "tail", out tailSlot25, out error)
                    || !TryReadShortString(tail, Slot35Offset, ShortStringCapacity,
                        "tail", out tailSlot35, out error)))
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
        /// sub 1 回包（0x59415A → 共享分配器 0x5943C5）。帧型与标准回包完全相同
        /// （0xA8/0x9C、body+2 = 返回码、body+4 = echo、tail 前 0x54 字节回显），
        /// **但结果不是通过独立字段返回的** —— 原版把答案就地写回请求 tail，
        /// 再让共享分配器把这段 tail 拷进回包：
        ///   0x594196 `lea eax,[ebp-0x190]` / 0x59419C `mov edx,[ebp-0x1C]`（out1=师父名）
        ///            / 0x59419F `mov ecx,0xFF` / 0x5941A4 `call 0x404E94`  长串→ShortString
        ///   0x5941AF `mov eax,[ebp-4]`（**tail 基址，无 add**）/ 0x5941B2 `mov cl,0x0F`
        ///            / 0x5941B4 `call 0x4035D8`  ⇒ 师父名写到 **tail+0x00，容量 15**
        ///   0x5941BF `mov edx,[ebp-0x20]`（out2=角色名）/ 0x5941C7 `call 0x404E94`
        ///   0x5941D2 `mov eax,[ebp-4]` / 0x5941D5 `add eax,0x10` / 0x5941D8 `mov cl,0x14`
        ///            ⇒ 角色名写到 **tail+0x10，容量 20**
        ///
        /// 两处必须逐字复刻的细节：
        ///  (1) 0x4035D8 **不清尾部字节**（`min(cl,srclen)` 只改长度字节+前 n 字节），
        ///      所以槽内剩余字节保留**请求原样**，不是 0。
        ///  (2) 未找到时 out1/out2 是空长串，0x404E94 走 `edx==0` 分支
        ///      （0x404EB1 `mov byte [eax],0`）得到长度 0 的 ShortString，随后照样
        ///      赋值 ⇒ **tail+0x00 与 tail+0x10 的长度字节被清 0**，其后字节不动。
        ///      即「未找到」也会改写 tail，不是原封不动回显。
        /// 容量不对称：写回用 15，而解码侧对 tail+0x00 放到 20 —— 原版写回处
        /// `mov cl,0x0F` 是唯一判据，不能按解码侧的容量写。
        /// </summary>
        /// <param name="found">
        /// 是否找到。true ⇒ body+2 = <see cref="EnumerateFoundResult"/>（0），
        /// 见 worker 0x59340A/0x59347F 的**反极性**说明。
        /// </param>
        public static LegacyDbServerFrame CreateEnumerateResponse(
            NativeZongpaiRequest request, bool found, byte[] masterName,
            byte[] roleName)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // 原版就地改写请求 tail；这里改写副本，语义相同且不破坏调用方的请求对象。
            var patched = request.Tail.Length == 0
                ? Array.Empty<byte>()
                : (byte[])request.Tail.Clone();

            // 未找到 ⇒ 两个槽都按空串赋值（长度字节归 0），而不是跳过赋值。
            WriteShortStringNoFill(patched, Slot00Offset, ShortStringCapacity,
                found ? masterName : Array.Empty<byte>());
            WriteShortStringNoFill(patched, Slot10Offset, WideShortStringCapacity,
                found ? roleName : Array.Empty<byte>());

            var payload = new byte[StandardReplyTotalLength - WireHeaderSize];
            WriteReplyHeader(payload,
                found ? EnumerateFoundResult : EnumerateNotFoundResult,
                request.EchoDword);
            var echo = Math.Min(MinimumTailLength, patched.Length);
            if (echo > 0)
                patched.AsSpan(0, echo).CopyTo(payload.AsSpan(ReplyDataOffset));
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
        ///
        /// 长度与 `+1` 的来源，以及正文拷贝的**截断语义**：
        ///   0x5946A9 `call 0x404EB8`  ⇒ len = Length(str) = dword [str-4]
        ///                              （0x404EBC `mov eax,[eax-4]`；空指针返 0）
        ///   0x5946B4 `add eax,0x54` / 0x5946B7 `inc eax`  ⇒ 总长 = len + 0x54 + 1
        ///   0x594700 `add eax,0x48` / 0x594703 `inc eax`  ⇒ 载荷 = len + 0x48 + 1
        ///   0x5946D8 `call 0x4036E8`（ecx=0）⇒ 整个缓冲先清零，故那 `+1` 字节恒 0
        ///   0x594766 `call 0x40C818`（eax=buf+0x54, edx=str, ecx=len）
        ///
        /// 0x40C818 → 0x40C7B0 是 StrPLCopy 语义，**在第一个 0x00 处截断**：
        ///   0x40C7BF `repne scasb`（al=0，最多 ecx 字节）扫首个 NUL
        ///   0x40C7C4 `sub ebx,ecx` ⇒ 实拷字节数 = min(len, strlen(str))
        ///   0x40C7DA `stosb`       ⇒ 末尾补一个 NUL（即上面那个 `+1` 的用途）
        /// ⚠️ 于是内嵌 0x00 的公告会出现 **body+2 声明的长度 &gt; 实际拷进去的字节数**，
        /// 其后字节保持 0x5946D8 的清零值。Notice 列是 <c>blob</c>（DDL 0x5BEE34），
        /// 二进制内容合法，所以这条不是理论情况，必须按截断复刻，
        /// 不能整块 CopyTo —— 那会把 NUL 之后的字节也发出去。
        /// </summary>
        public static LegacyDbServerFrame CreateNoticeResponse(
            NativeZongpaiRequest request, ReadOnlySpan<byte> notice)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            // body+2 用的是 Length(str)（0x5946A9），**不是**截断后的长度。
            var length = notice.Length;
            var payload = new byte[length + HeaderSize + 1];
            WriteReplyHeader(payload, length, request.EchoDword);
            WriteShortString(payload, Slot25Offset, ShortStringCapacity,
                request.HeaderSlot25);
            WriteShortString(payload, Slot35Offset, ShortStringCapacity,
                request.HeaderSlot35);
            // 0x40C7BF 的 repne scasb：实拷字节数在首个 NUL 处截断。
            var copied = notice.IndexOf((byte)0);
            if (copied < 0) copied = length;
            if (copied > 0)
                notice.Slice(0, copied).CopyTo(payload.AsSpan(ReplyDataOffset));
            return new LegacyDbServerFrame(1, 0, payload);
        }

        /// <summary>
        /// sub 1 的返回码。⚠️ **极性是反的**：worker 0x5933CC 把 `[ebp-0x10]`
        /// 初始化为 **1**（0x59340A `mov dword [ebp-0x10],1`），只在**找到**成员的
        /// 那条路径上才改写为 0（0x59347D `xor eax,eax` / 0x59347F
        /// `mov [ebp-0x10],eax`），出口 0x593512 `mov eax,[ebp-0x10]` 返回它，
        /// 调用点 0x594193 `mov [ebp-0x14],eax` 把它存进 `[ebp-0x14]`，
        /// 再由共享回包器 0x594436 `mov dx,word [ebp-0x14]` /
        /// 0x59443A `mov word [eax+2],dx` 写进 body+2。
        /// 所以 body+2 == 0 表示**找到**，非 0 表示没找到 —— 与其余子命令
        /// 「0 = 成功」凑巧同向，但成因不同（这里是 found 标志，不是错误码）。
        /// 「找到」的判据不是遍历命中，而是第二个出参非空：
        ///   0x593467 `mov eax,[ebp+8]`（=out2）/ 0x59346A `cmp dword [eax],0` / je
        /// ⇒ out2（角色名）为空长串（Delphi 空串 = nil 指针）时仍走未找到出口
        /// 0x59348C 并继续遍历下一个师父。只有 out2 非空才在 0x59346F 把
        /// 师父名（record+0x0C）写进 out1，并在 0x59347F 置 0 收工。
        /// </summary>
        public const int EnumerateFoundResult = 0;
        /// <summary>See <see cref="EnumerateFoundResult"/> (0x59340A 的初值)。</summary>
        public const int EnumerateNotFoundResult = 1;

        /// <summary>
        /// 组装一条 sub 10 成员记录（0x29 字节），布局见
        /// <see cref="MemberRecordRoleNameOffset"/>。等级/在线位在原版取自**活体**
        /// 角色对象（0x593CC5 `call 0x5ABC18` 按名字查，0x593CD1 `je` 查不到就
        /// **保留 0x593C9A/0x593CA3 写下的 0**），所以查不到时必须传
        /// level=0 / online=false，而不是回落到库里的等级。
        /// </summary>
        public static byte[] BuildMemberRecord(byte[] roleName,
            byte[] memberName, ushort level, bool online)
        {
            var record = new byte[MemberRecordSize];
            // 0x593C8D/0x593C90: cl=0x14 → 容量 20，写在记录起始处。
            WriteShortString(record, MemberRecordRoleNameOffset,
                WideShortStringCapacity, roleName);
            // 0x593C60/0x593C63: add eax,0x15 / cl=0x0F → 容量 15。
            WriteShortString(record, MemberRecordMemberNameOffset,
                ShortStringCapacity, memberName);
            // 0x593CDD: mov word [rec+0x25],ax —— word 宽度，取自 live+0x3E。
            BinaryPrimitives.WriteUInt16LittleEndian(
                record.AsSpan(MemberRecordLevelOffset, 2), level);
            // 0x593CEA: mov byte [rec+0x27],al —— 直接搬 live+0x25 这一个字节。
            record[MemberRecordOnlineOffset] = online ? (byte)1 : (byte)0;
            // +0x28 原版从不写入，保持 0（0x593BFC 的整块清零）。
            return record;
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

        /// <summary>
        /// Delphi ShortString 赋值 0x4035D8 的逐字语义，用于**就地改写已有缓冲**
        /// （sub 1 写回请求 tail）：
        ///   0x4035D9 `mov bl,[edx]` / 0x4035DB `cmp cl,bl` / 0x4035DD `jbe`
        ///            ⇒ 长度 = min(capacity, srclen)
        ///   0x4035E1 `mov [eax],cl`  ⇒ 只写长度字节
        ///   0x4035EC `call 0x4031D0` ⇒ 只搬 n 字节
        /// **不清尾部**：槽内 n 之后的字节保持原值。<see cref="WriteShortString"/>
        /// 写的是全新的零缓冲，那里区分不出来；这里写的是请求副本，区分得出来，
        /// 所以必须分成两个方法而不是复用。
        /// </summary>
        private static void WriteShortStringNoFill(byte[] buffer, int offset,
            int capacity, byte[] value)
        {
            if (offset >= buffer.Length) return;
            var source = value ?? Array.Empty<byte>();
            var length = Math.Min(source.Length, capacity);
            // 尾部越界时按可写空间截断（原版靠 0x54 长度门保证槽在界内）。
            if (offset + 1 + length > buffer.Length)
                length = Math.Max(0, buffer.Length - offset - 1);
            buffer[offset] = (byte)length;
            if (length > 0)
                source.AsSpan(0, length).CopyTo(buffer.AsSpan(offset + 1));
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

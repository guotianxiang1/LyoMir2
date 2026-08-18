using System;
using System.Buffers.Binary;
using SystemModule;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public enum DbServerWireMode
    {
        Unknown,
        PrivateRequestServer,
        NativeType12
    }

    /// <summary>Connection-local first-frame detector for port 6000.</summary>
    public sealed class DbServerWireModeDetector
    {
        public DbServerWireMode Mode { get; private set; }

        public bool TryAppend(byte[] data, int offset, int count,
            out byte[] replay, out string error)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            replay = Array.Empty<byte>();
            error = string.Empty;
            if (count == 0) return true;
            if (Mode != DbServerWireMode.Unknown)
            {
                replay = data.AsSpan(offset, count).ToArray();
                return true;
            }

            if (data[offset] == (byte)'#')
            {
                Mode = DbServerWireMode.PrivateRequestServer;
                replay = data.AsSpan(offset, count).ToArray();
                return true;
            }

            // Any non-private first byte belongs to the native stream. Its parser is
            // responsible for the original bytewise magic resynchronization.
            Mode = DbServerWireMode.NativeType12;
            replay = data.AsSpan(offset, count).ToArray();
            return true;
        }

        public void Reset()
        {
            Mode = DbServerWireMode.Unknown;
        }
    }

    public sealed class NativeDbServerHeartbeat
    {
        // The original sender does not initialize payload bytes 2..3. Preserve them only
        // for diagnostics; they are not a request id or token.
        public ushort UninitializedWord { get; init; }
        public int State { get; init; }
        public int UserCount { get; init; }
    }

    /// <summary>
    /// Session fields copied by the original DBServer into THumDataInfo+0xEF00.
    /// Offset-based names are intentional where the original business name is not proven.
    /// </summary>
    public sealed class NativeHumanSessionContext
    {
        public string UserIp { get; init; } = string.Empty;
        public string AuthText54 { get; init; } = string.Empty;
        public ushort AuthFlags75 { get; set; }

        /// <summary>
        /// suffix+0x56 的位域。与 0x55 语义独立，原版分别测试 bit0 / bit2 / bit4：
        /// bit0(0x01) -> obj+0x4C6、bit2(0x04) -> obj+0xB77、bit4(0x10) -> obj+0xB74
        /// （IsNetCafeUser）。
        ///
        /// ⚠️ 来源 **UNPROVEN**：DBServer 与 M2Server 两个镜像里都没有写入者，
        /// 也不存在能算出该位的 IP 段 / 网吧配置 / SQL / 认证 RPC。怀疑来自更上游。
        /// 在拿到 Tier-1 证据前恒为 0；**不得发明算法产生该位**。
        /// 独立成员的意义是让"已知未建模"在类型上可见，而不是被 AuthFlags75 的
        /// 高字节静默污染（此前 Slice(0x55, 2) 的越界 ushort 写入即是该情形）。
        /// </summary>
        public byte AuthByte56 { get; set; }
        public byte AuthByte77 { get; init; }
        public byte AuthByte78 { get; init; }
        public byte SelectionState { get; init; } = 1;
        public byte GroupIndex { get; init; }
        public ushort ZoneIndex { get; init; }
        public ushort ConnectionId { get; init; }
        public uint LoginElapsedMilliseconds { get; init; }
        public string AuthText81 { get; init; } = string.Empty;
        public string AuthText102 { get; init; } = string.Empty;
        public byte SessionMode { get; init; } = 1;
        public int CachedValue38 { get; init; }
        public int CachedValue3C { get; init; }
        public byte[] LoginExtension { get; init; }

        /// <summary>
        /// suffix+0x40..0x47 的 DB 时钟基准（Delphi TDateTime，天为单位的 double）。
        /// 原版 DBServer 在**记录发送时刻**求值：0x59A9E6 `fstp qword ptr [eax+0x40]`
        /// 写入未经截断的 Now() 原值，随后 sub_5986CC 把该结构 blit 进记录 +0xEF00
        /// （0x28*4=0xA0 blockA + 0x42*4=0x108 blockB = 0x1A8 == HumanInfoSuffixSize）。
        ///
        /// 三条必须遵守的契约（逆向自 DBServer_repaired_20260803.exe）：
        /// 1. 取值时机 = 发送时刻，不是登录时刻。sub_59DC1C 的 fan-out 循环里
        ///    逐个 GameServer 重新求值，故本成员可写（非 init），由发送方每次赋值。
        /// 2. 不得截断。0x59A9F0 处的 Trunc 结果落到 struct+0x58，**不回写 +0x40**，
        ///    故 +0x40 保留亚秒精度。GameSvr 侧 `fsub qword [eax+0xef40]` 减的是
        ///    全精度值；写入截断值会让到期判定整体偏最多一天。
        /// 3. 值为 0.0 是合法状态：姊妹发送器 sub_59CA94（ident 0x12E）从不写 suffix，
        ///    该路径下基准恒 0.0，属原版行为，不得为此添加回退守卫。
        /// </summary>
        public double DbClockBase { get; set; }
    }

    public sealed class NativeSaveHumanRequest
    {
        public ushort HeaderWord2 { get; init; }
        public int HeaderValue8 { get; init; }
        public int HeaderValueC { get; init; }
        public string Account { get; init; } = string.Empty;
        public string CharacterName { get; init; } = string.Empty;
        public byte[] HumanInfoPrefix { get; init; }
        public byte[] NativeData { get; init; }
        public byte[] HumanInfoSuffix { get; init; }
        public byte[] NativeScriptData { get; init; }
    }

    /// <summary>Original 0x0150 save data after the DBServer's in-memory normalization.</summary>
    public sealed class NativeSavePersistenceData
    {
        public string Account { get; init; } = string.Empty;
        public string CharacterName { get; init; } = string.Empty;
        public byte[] DataBlob { get; init; } = Array.Empty<byte>();
        public byte[] ScriptDataBlob { get; init; } = Array.Empty<byte>();
        public ushort Level { get; init; }
        public uint Experience { get; init; }
        public byte Job { get; init; }
        public byte Sex { get; init; }
        public int ApprenticeNum { get; init; }
        public byte HeroCardLevel { get; init; }
        public byte PlatinaCharacterLevel { get; init; }
        public ushort SfLevel { get; init; }
    }

    /// <summary>Verified native port 6000 heartbeat and selected-human push layouts.</summary>
    public static class NativeDbServerProtocol
    {
        public const ushort SilentNoOpCommand = 0x0155;
        public const ushort HeroSaveNotificationCommand = 0x013C;
        public const ushort HeartbeatCommand = 0x003C;
        public const ushort LoadHumanCommand = 0x0050;
        public const ushort SaveHumanCommand = 0x0150;
        // Original receiver accepts only frames whose total length is below 0x20000.
        public const int MaximumFrameLength = 0x1FFFF;

        public static bool IsSilentNormalType1Command(ushort command,
            byte serverType) => serverType != 9 && command is
                SilentNoOpCommand
                // These cases share the original Type1 dispatcher default
                // target at 0x599502 and return without a response.
                or 0x0158 or 0x015C or 0x015D or 0x015E or 0x015F
                or 0x0169 or 0x016D or 0x016E or 0x016F
                or 0x0171 or 0x0175
                or 0x0177 or 0x0178 or 0x0179 or 0x017A or 0x017B
                or 0x017C or 0x017D or 0x017E or 0x017F or 0x0180
                or 0x0184 or 0x0185 or 0x0186 or 0x0187 or 0x0188
                or 0x0189 or 0x018A or 0x018B or 0x018C or 0x018D
                or 0x018E or 0x018F or 0x0190 or 0x0191
                or 0x0195 or 0x0196 or 0x0197 or 0x0198 or 0x0199
                or 0x019F;

        public static bool UsesNormalType1Dispatcher(byte serverType) =>
            serverType != 9;

        public static bool IsDbToolType1Command(ushort command) =>
            command is >= 0x0100 and <= 0x0104;

        public static LegacyDbServerFrame CreateHeroSaveNotification(
            int param1, int param2)
        {
            var payload = new byte[0x48];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                HeroSaveNotificationCommand);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), param1);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), param2);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        public static bool ShouldReceiveHeroSaveNotification(byte serverType) =>
            serverType != 9;
        public const int HeartbeatPayloadSize = 12;
        public const int LoadHumanPayloadSize = 0xFFFC;
        public const int LoadHumanHeaderSize = 0x48;
        public const int HumanInfoSize = 0xF0A8;
        public const int HumanInfoPrefixSize = 0x08;
        public const int HumanInfoSuffixSize = 0x01A8;
        public const int NativeDataOffset = LoadHumanHeaderSize + HumanInfoPrefixSize;
        public const int HumanInfoSuffixOffset = NativeDataOffset
                                                + NativeHumanDataCodec.DataRecordSize;
        public const int ScriptDataOffset = LoadHumanHeaderSize + HumanInfoSize;
        public const int ScriptDataSlotSize = 0x0F0C;
        public const int AccountOffset = 0x10;
        public const int CharacterOffset = 0x25;
        public const int SessionPrefixSize = 0x00A0;
        public const int LoginExtensionSize = 0x0108;
        public const ushort SwitchSaveMode = 2;
        public const ushort AwardPlayerFlag = 0x0800;
        private const int AccountCapacity = 20;
        private const int CharacterCapacity = 15;
        private const int NativeCharacterOffset = 0x0000;
        private const int NativeAccountOffset = 0x0020;
        private const int NativeLevelOffset = 0x003C;
        private const int NativeSexOffset = 0x003F;
        private const int NativeJobOffset = 0x0040;
        private const int NativeExperienceOffset = 0x0050;
        private const int NativePlatinaCharacterLevelOffset = 0x016E;
        private const int NativeHeroCardLevelOffset = 0x016F;
        private const int NativeApprenticeNumOffset = 0x0174;
        private const int NativeSfLevelOffset = 0x053E;

        public static bool TryDecodeHeartbeat(LegacyDbServerFrame frame,
            out NativeDbServerHeartbeat heartbeat, out string error)
        {
            heartbeat = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native DBServer heartbeat frame is null";
                return false;
            }
            if (frame.Type != 2)
            {
                error = "native DBServer heartbeat envelope is invalid";
                return false;
            }
            if (frame.Payload.Length < HeartbeatPayloadSize)
            {
                error = $"native DBServer heartbeat payload must be at least {HeartbeatPayloadSize} bytes";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2)) != HeartbeatCommand)
            {
                error = "native DBServer heartbeat command mismatch";
                return false;
            }

            heartbeat = new NativeDbServerHeartbeat
            {
                UninitializedWord = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2)),
                State = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4)),
                UserCount = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4))
            };
            return true;
        }

        public static bool TryDecodeSaveHuman(LegacyDbServerFrame frame,
            out NativeSaveHumanRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native DBServer save frame is null";
                return false;
            }
            if (frame.Type != 1)
            {
                error = "native DBServer save envelope is invalid";
                return false;
            }
            if (frame.Payload.Length <= ScriptDataOffset)
            {
                error = $"native DBServer save payload must exceed {ScriptDataOffset} bytes";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0, 2))
                != SaveHumanCommand)
            {
                error = "native DBServer save command mismatch";
                return false;
            }
            if (!TryReadShortString(payload, AccountOffset, AccountCapacity,
                    allowEmpty: false, out var account, out error)
                || !TryReadShortString(payload, CharacterOffset, CharacterCapacity,
                    allowEmpty: false, out var characterName, out error))
                return false;

            request = new NativeSaveHumanRequest
            {
                HeaderWord2 = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2)),
                HeaderValue8 = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)),
                HeaderValueC = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12, 4)),
                Account = account,
                CharacterName = characterName,
                HumanInfoPrefix = payload.Slice(LoadHumanHeaderSize,
                    HumanInfoPrefixSize).ToArray(),
                NativeData = payload.Slice(NativeDataOffset,
                    NativeHumanDataCodec.DataRecordSize).ToArray(),
                HumanInfoSuffix = payload.Slice(HumanInfoSuffixOffset,
                    HumanInfoSuffixSize).ToArray(),
                NativeScriptData = payload.Slice(ScriptDataOffset).ToArray()
            };
            return true;
        }

        public static bool TryExtractSwitchLoginExtension(
            NativeSaveHumanRequest request, out byte[] extension)
        {
            extension = null;
            if (request?.HeaderWord2 != SwitchSaveMode
                || request.HumanInfoSuffix == null
                || request.HumanInfoSuffix.Length != HumanInfoSuffixSize)
                return false;
            extension = request.HumanInfoSuffix.AsSpan(
                SessionPrefixSize, LoginExtensionSize).ToArray();
            return true;
        }

        public static bool TryCreateSavePersistenceData(NativeSaveHumanRequest request,
            out NativeSavePersistenceData persistence, out string error)
        {
            persistence = null;
            error = string.Empty;
            if (request == null)
            {
                error = "native DBServer save request is null";
                return false;
            }
            if (request.HumanInfoPrefix == null
                || request.HumanInfoPrefix.Length != HumanInfoPrefixSize
                || request.NativeData == null
                || request.NativeData.Length != NativeHumanDataCodec.DataRecordSize
                || request.NativeScriptData == null
                || request.NativeScriptData.Length == 0)
            {
                error = "native DBServer save persistence fields are truncated";
                return false;
            }

            var nativeData = (byte[])request.NativeData.Clone();
            if (!TryReadShortString(nativeData, NativeCharacterOffset,
                    CharacterCapacity, allowEmpty: false,
                    out var characterName, out error)
                || !TryReadShortString(nativeData, NativeAccountOffset,
                    AccountCapacity, allowEmpty: false,
                    out var account, out error))
                return false;
            if (!string.Equals(request.CharacterName, characterName,
                    StringComparison.Ordinal)
                || !string.Equals(request.Account, account, StringComparison.Ordinal))
            {
                error = "native DBServer save header and human record identity differ";
                return false;
            }

            var level = BinaryPrimitives.ReadUInt16LittleEndian(
                nativeData.AsSpan(NativeLevelOffset, 2));
            if (level == 0)
            {
                level = 1;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    nativeData.AsSpan(NativeLevelOffset, 2), level);
            }

            var dataBlob = new byte[NativeHumanDataCodec.DataSizeMarker];
            request.HumanInfoPrefix.CopyTo(dataBlob, 0);
            nativeData.CopyTo(dataBlob, HumanInfoPrefixSize);
            // The fixed record uses total envelope size here, unlike ScriptData's raw size.
            BinaryPrimitives.WriteUInt16LittleEndian(dataBlob.AsSpan(4, 2),
                NativeHumanDataCodec.DataSizeMarker);

            var scriptBlobLength = (HumanInfoPrefixSize
                                    + request.NativeScriptData.Length + 0xFF) & ~0xFF;
            var scriptBlob = new byte[scriptBlobLength];
            BinaryPrimitives.WriteUInt16LittleEndian(scriptBlob.AsSpan(4, 2),
                unchecked((ushort)request.NativeScriptData.Length));
            request.NativeScriptData.CopyTo(scriptBlob, HumanInfoPrefixSize);

            persistence = new NativeSavePersistenceData
            {
                Account = account,
                CharacterName = characterName,
                DataBlob = dataBlob,
                ScriptDataBlob = scriptBlob,
                Level = level,
                Experience = BinaryPrimitives.ReadUInt32LittleEndian(
                    nativeData.AsSpan(NativeExperienceOffset, 4)),
                Job = nativeData[NativeJobOffset],
                Sex = nativeData[NativeSexOffset],
                ApprenticeNum = BinaryPrimitives.ReadInt32LittleEndian(
                    nativeData.AsSpan(NativeApprenticeNumOffset, 4)),
                HeroCardLevel = nativeData[NativeHeroCardLevelOffset],
                PlatinaCharacterLevel = nativeData[NativePlatinaCharacterLevelOffset],
                SfLevel = BinaryPrimitives.ReadUInt16LittleEndian(
                    nativeData.AsSpan(NativeSfLevelOffset, 2))
            };
            return true;
        }

        public static bool TryCreateLoadHumanFrame(string account, string characterName,
            byte[] nativeData, byte[] nativeScriptData,
            NativeHumanSessionContext sessionContext,
            out LegacyDbServerFrame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (nativeData == null || nativeData.Length != NativeHumanDataCodec.DataRecordSize)
            {
                error = $"native human data must be {NativeHumanDataCodec.DataRecordSize} bytes";
                return false;
            }
            if (nativeScriptData != null && nativeScriptData.Length > ScriptDataSlotSize)
            {
                error = $"native ScriptData exceeds {ScriptDataSlotSize} bytes";
                return false;
            }
            if (sessionContext == null)
            {
                error = "native human session context is required";
                return false;
            }

            var payload = new byte[LoadHumanPayloadSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), LoadHumanCommand);
            if (!TryWriteShortString(payload, AccountOffset, account,
                    AccountCapacity, allowEmpty: false, out error)
                || !TryWriteShortString(payload, CharacterOffset, characterName,
                    CharacterCapacity, allowEmpty: false, out error)
                || !TryWriteSessionSuffix(payload.AsSpan(
                        HumanInfoSuffixOffset, HumanInfoSuffixSize),
                    account, sessionContext, out error))
                return false;

            nativeData.CopyTo(payload, NativeDataOffset);
            if (nativeScriptData != null && nativeScriptData.Length > 0)
                nativeScriptData.CopyTo(payload, ScriptDataOffset);
            frame = new LegacyDbServerFrame(1, 0, payload);
            return true;
        }

        public static bool TryWriteSessionSuffix(Span<byte> destination,
            string account, NativeHumanSessionContext context, out string error)
        {
            error = string.Empty;
            if (destination.Length != HumanInfoSuffixSize)
            {
                error = $"native human suffix must be {HumanInfoSuffixSize} bytes";
                return false;
            }
            if (context == null)
            {
                error = "native human session context is required";
                return false;
            }
            if (context.LoginExtension != null
                && context.LoginExtension.Length != LoginExtensionSize)
            {
                error = $"native login extension must be {LoginExtensionSize} bytes";
                return false;
            }

            destination.Clear();
            if (!TryWriteShortString(destination, 0x00, context.UserIp,
                    15, allowEmpty: true, out error)
                || !TryWriteShortString(destination, 0x10, account,
                    20, allowEmpty: false, out error)
                || !TryWriteShortString(destination, 0x25, context.AuthText54,
                    20, allowEmpty: true, out error)
                || !TryWriteShortString(destination, 0x68, context.AuthText81,
                    20, allowEmpty: true, out error)
                || !TryWriteShortString(destination, 0x7D, context.AuthText102,
                    20, allowEmpty: true, out error))
                return false;

            if (context.AuthByte77 != 0)
                destination[0x48] = context.AuthByte78;
            destination[0x49] = context.AuthByte77;
            destination[0x4A] = context.SelectionState;
            destination[0x4B] = context.GroupIndex;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0x4C, 2),
                context.ZoneIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0x4E, 2),
                context.ConnectionId);
            // suffix+0x40..0x47 = DB 时钟基准（Delphi TDateTime double，未截断）。
            // 原版：0x59A9E6 `fstp qword ptr [eax+0x40]`。详见 DbClockBase 的三条契约。
            // 注意这是 disp8 编码；用 4 字节位移模式检索该写入点必定假阴性。
            // 未赋值时在此按发送时刻取值 —— 契约(1) 要求的正是「发送时刻」，而本函数
            // 就在发送路径上，故此处求值与原版时序一致。
            // 之所以保底而非要求调用方必赋：漏赋值会静默退化成恒 0.0，而恒 0.0 正是
            // 本次修复前那个 P0 缺陷的形态（整个定时 buff 族永不恢复）。让协议层兜住，
            // 就消除了「新增发送路径忘记赋值」这一整类同型缺陷。
            // 注意这不违反契约(3)（0.0 合法）：那条路径是姊妹发送器 sub_59CA94
            // (ident 0x12E)，它**根本不写 suffix**，不经过本函数。故在此 0.0 只可能是漏赋值。
            var clockBase = context.DbClockBase != 0.0
                ? context.DbClockBase
                : HUtil32.DateTimeToDouble(DateTime.Now);
            BinaryPrimitives.WriteDoubleLittleEndian(destination.Slice(0x40, 8),
                clockBase);

            // 0x55 与 0x56 是两个**独立**位域，原版分别逐位测试后送往不同的玩家对象字段：
            //   0x6B09AB test [ebx+0x55],2 / 0x6B09D7 test [ebx+0x55],0x10 / 0x6B09E7 test [ebx+0x55],0x20
            //   0x6B09BB test [ebx+0x56],4    -> obj+0xB77
            //   0x6B0A1C test [ebx+0x56],1    -> obj+0x4C6
            //   0x6B0A2C test [ebx+0x56],0x10 -> obj+0xB74 (IsNetCafeUser)
            // 此前用 Slice(0x55, 2) 写 ushort，使 0x56 携带 AuthFlags75 的高字节 ——
            // 两个不同语义被压在一个写入里，属语义污染。故拆为单字节写入。
            //
            // ⚠️ 上面那段推理是**错的**，我按字节自我更正（保留原文以留下教训痕迹）：
            //
            // 原版 0x55..0x56 是**一个 u16**，不是两个独立字节：
            //   0x5CDDDF  mov ax, word ptr [eax+0x75]     ; 从 rec+0x75 整字读
            //   0x5CDDE3  mov word ptr [ebp-0x53], ax     ; 整字写进 blockA 的 0x55
            //   0x598752  or  word ptr [eax+0x55], 0x800  ; 有条件置 bit11
            // bit11 落在**高字节**，即 0x56 |= 0x08。
            //
            // 而 C# 的 AwardPlayerFlag == 0x0800 **就是这个 bit11**（见本文件常量定义），
            // 由 GameSocService 在 TryConsumeAwardPlayer 命中时 `AuthFlags75 |= AwardPlayerFlag`。
            // 我此前写成 `(byte)(AuthFlags75 & 0xFF)`，把 0x0800 整位丢掉 ——
            // 领奖标志再也到不了 GameSvr。**修复前那个\"越界 ushort\"在这一点上反而是对的。**
            //
            // 正确形态：按 u16 写（恢复原版整字语义），同时保留 0x56 的独立来源通道。
            //
            // ⚠️ 二次更正（上面写的「rec+0x75」和「两个镜像都无写入者」两处都错，按字节推翻）：
            //
            // (1) 载体不是人物记录，是 DBServer 的**账号/登录会话对象**（下称 ACCT）。
            //     宿主函数的字符串坐实：0x5CC410「你的账号存在风险…修改密码」、
            //     0x5CE6CC「[错误]：帐号被锁定 / 帐号被CD卡登录锁定 / 服务器维护，禁止登录」。
            //     链路是三段而非两段：
            //       入站报文体 body+0x4B --(0x5CE8B1 批发整字)--> ACCT+0x75
            //       ACCT+0x75 --(0x5CDDDF 读 / 0x5CDDE3 写 [ebp-0x53])--> blockA+0x55
            //       blockA(0xA0) --(0x598833 rep movsd)--> rec+0xEF00
            //       另一块 0x108 --> rec+0xEFA0        (0xA0 + 0x108 = 0x1A8 ✔)
            //     ⚠️ ACCT→blockA **不是常数差**（同段还有 0x10→0x4B、0x0E→0x4C、
            //     0x77→0x49、0x78→0x48），故「0x75 与 0x55 差 0x20」是巧合，
            //     **不可当换算规则外推**。
            //
            // (2) 网吧位 bit12（= 字节 0x56 的 bit4）**DBServer 自己会置**。
            //     全镜像 `or word [reg+0x75], imm16` 仅两个站点，imm 都是 0x1000：
            //       0x5CE96C / 0x5CEF51   or word ptr [eax+0x75], 0x1000
            //     门 = 0x5C9A24 `(self+0x78 <> nil) and (TStrings.IndexOf(...) <> -1)`：
            //       0x5C9A33 cmp dword [eax+0x78],0 / je   → nil 则返 false
            //       0x5C9A44 call dword [ecx+0x54]         → TStrings.IndexOf
            //       0x5C9A47 inc eax / 0x5C9A48 setne      → 即 IndexOf <> -1
            //     即「登录 IP 命中名单」。名单单例来自 [0x5D9B04]+0x78，
            //     其 ini 装填源尚未定案（UNPROVEN）。
            //
            // 现状：C# 侧尚无 IP 名单子系统，AuthByte56 全仓零生产者 → bit12 恒 0。
            // 这是**已知缺口（缺前置子系统）**，不再是「来源不明」。补齐需先实现
            // IP 名单装载 + ACCT 会话对象；在那之前不得凭空造值。
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0x55, 2),
                (ushort)(context.AuthFlags75 | ((ushort)context.AuthByte56 << 8)));

            // 0x50..0x54 / 0x58..0x61：DBServer **确实写**（0x58BF05 写 0x50、0x58BF10 写 0x51，
            // 均在 0x598734 call 0x59A978 内，即 rep movsd 之前，故确实上线）。
            // 0x52 是遍历链表 `or` 累积的位掩码，0x50/0x51 是 mov 故\"最后命中者胜\"。
            // 0x58..0x61 = 5 个 u16，14 路排行榜的 1-based 名次，0 = 未上榜。
            // ⛔ 这两段是 **C# 单侧缺失**（不是双侧真空），但接线前必须先建两个内存管理器
            // 子系统（[0x5DA80C] / [0x5D9CFC] / 排行榜容器 [0x5D9B04]+0x80），否则无处取值。
            // 类名/RTTI 未确认 → 语义 UNPROVEN，此处暂留 0，不猜。
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0x64, 4),
                context.LoginElapsedMilliseconds);
            destination[0x92] = context.SessionMode;
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(0x98, 4),
                context.CachedValue38);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(0x9C, 4),
                context.CachedValue3C);
            if (context.LoginExtension != null)
                context.LoginExtension.CopyTo(destination.Slice(SessionPrefixSize));
            return true;
        }

        private static bool TryWriteShortString(Span<byte> destination, int offset,
            string value, int capacity, bool allowEmpty, out string error)
        {
            error = string.Empty;
            byte[] encoded;
            try
            {
                encoded = LegacyGbkText.Encode(value);
            }
            catch (Exception ex) when (ex is ArgumentException)
            {
                error = "native DBServer text is not valid GBK: " + ex.Message;
                return false;
            }
            if ((!allowEmpty && encoded.Length == 0) || encoded.Length > capacity)
            {
                var minimum = allowEmpty ? 0 : 1;
                error = $"native DBServer text length must be {minimum}..{capacity} GBK bytes";
                return false;
            }
            destination[offset] = (byte)encoded.Length;
            encoded.CopyTo(destination.Slice(offset + 1));
            return true;
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source, int offset,
            int capacity, bool allowEmpty, out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            if (offset < 0 || offset >= source.Length)
            {
                error = "native DBServer ShortString is truncated";
                return false;
            }

            var length = source[offset];
            if ((!allowEmpty && length == 0) || length > capacity
                || offset + 1 + length > source.Length)
            {
                var minimum = allowEmpty ? 0 : 1;
                error = $"native DBServer ShortString length must be {minimum}..{capacity} GBK bytes";
                return false;
            }
            try
            {
                value = LegacyGbkText.Decode(source.Slice(offset + 1, length).ToArray());
                return true;
            }
            catch (ArgumentException ex)
            {
                error = "native DBServer ShortString is not valid GBK: " + ex.Message;
                return false;
            }
        }
    }
}

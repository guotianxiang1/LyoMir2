using System.Buffers.Binary;
using System.Text;
using SystemModule.Packet;

namespace GameSvr
{
    /// <summary>
    /// Dormant codec and reward calculator for the native ClientQuestGetDiam
    /// completion. It deliberately performs no player or transport mutation.
    /// </summary>
    public static class NativeQuestDiamondProtocol
    {
        public const ushort RequestIdent = 122;
        public const ushort CompletionIdent = 1122;
        public const ushort SuccessAckIdent = 105;
        public const ushort FailureAckIdent = 106;
        public const int CompletionPayloadSize = 32;
        public const int RoleNameMaximumGbkBytes = 15;
        public const int BountyMinimumDiamondCount = 12;
        public const int GameLogType = 33;

        public const string RequestUnavailableDialog =
            "元宝系统暂时关闭中...\\ \\ \\ <返回/@main>";
        public const string GameLogItemName = "金刚宝石";
        public const string GameLogReason = "元宝系统获得";
        public const string NpcContinueCommands =
            "\\ \\ \\<继续领取/@askybdiam>      <关闭/@exit>";

        private static readonly Encoding Gbk;

        static NativeQuestDiamondProtocol()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // Delphi promotes the wire ShortString to AnsiString without a
            // validation branch.  The default CP936 replacement fallback
            // preserves that permissive boundary for malformed external bytes.
            Gbk = Encoding.GetEncoding(936);
        }

        public static bool TryDecodeCompletion(YbDbLegacy77Frame frame,
            out Completion completion, out string error)
        {
            completion = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "quest-diamond completion frame is null";
                return false;
            }
            if (frame.Ident != CompletionIdent)
            {
                error = $"quest-diamond completion ident must be {CompletionIdent}";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length != CompletionPayloadSize)
            {
                error = $"quest-diamond completion payload must be " +
                        $"{CompletionPayloadSize} bytes";
                return false;
            }

            var roleNameLength = payload[0];
            if (roleNameLength > RoleNameMaximumGbkBytes)
            {
                error = $"quest-diamond role name length {roleNameLength} " +
                        $"exceeds {RoleNameMaximumGbkBytes}";
                return false;
            }

            var roleNameGbkBytes = payload.AsSpan(1, roleNameLength).ToArray();
            var roleName = Gbk.GetString(roleNameGbkBytes);

            completion = new Completion(
                frame.QueryId,
                roleName,
                roleNameGbkBytes,
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(20, 4)));
            return true;
        }

        public static YbDbLegacy77Frame CreateAck(int transactionCode,
            bool succeeded)
        {
            return new YbDbLegacy77Frame(
                CompletionIdent,
                transactionCode,
                succeeded ? SuccessAckIdent : FailureAckIdent,
                Array.Empty<byte>());
        }

        public static string BuildFailureDialog(int result)
        {
            return result switch
            {
                -1 => "锻造未完成 或 没有锻造\\ \\<返回/@Main>",
                -3 => "对不起，你没有完成锻造那么多颗金刚石。\\ \\<返回/@askybdiam>",
                -4 => "没有领取金刚石丢失的记录\\ \\<返回/@Main>",
                _ => "领取金刚石失败(" + result + ")\\ \\<返回/@Main>"
            };
        }

        public static int GetLevelExperienceBase(ushort level)
        {
            return level switch
            {
                0 => 0,
                <= 7 => 57_000,
                <= 14 => 75_000,
                <= 18 => 90_000,
                <= 21 => 105_000,
                <= 24 => 120_000,
                <= 27 => 135_000,
                <= 30 => 150_000,
                <= 34 => 180_000,
                <= 37 => 210_000,
                <= 40 => 240_000,
                <= 42 => 270_000,
                <= 44 => 285_000,
                45 => 300_000,
                46 => 330_000,
                <= 48 => 345_000,
                <= 50 => 360_000,
                <= 52 => 375_000,
                <= 54 => 390_000,
                <= 56 => 405_000,
                <= 58 => 420_000,
                _ => 450_000
            };
        }

        public static bool TryCalculateGrant(ushort level, int firstCount,
            int secondCount, Func<uint, uint> nativeRandom,
            out Grant grant)
        {
            if (nativeRandom == null)
                throw new ArgumentNullException(nameof(nativeRandom));

            grant = null;
            var levelBase = GetLevelExperienceBase(level);
            if (levelBase <= 0)
                return false;

            var total = unchecked(firstCount + secondCount);
            var weightedCount = unchecked(firstCount +
                                          unchecked(secondCount * 2));
            var weightedExperience = unchecked(levelBase * weightedCount);
            var signedRandomBound = weightedExperience / 5;
            var randomValue = unchecked((int)nativeRandom(
                unchecked((uint)signedRandomBound)));
            var experience = unchecked(weightedExperience - randomValue +
                                       weightedExperience / 10);

            grant = new Grant(firstCount, secondCount, total, levelBase,
                weightedExperience, signedRandomBound, randomValue,
                experience, total >= BountyMinimumDiamondCount);
            return true;
        }

        public static uint NextDelphiRandom(ref uint state, uint range)
        {
            state = unchecked(134_775_813u * state + 1u);
            return unchecked((uint)(((ulong)state * range) >> 32));
        }

        public static string BuildSuccessDialog(int total, int experience)
        {
            return "你成功领取金刚石 " + total + " 颗！获得经验：" + experience;
        }

        public static string BuildNpcSuccessDialog(int total, int experience)
        {
            return BuildSuccessDialog(total, experience) + NpcContinueCommands;
        }

        public sealed class Completion
        {
            internal Completion(int result, string roleName,
                byte[] roleNameGbkBytes, int firstCount, int secondCount)
            {
                Result = result;
                RoleName = roleName;
                RoleNameGbkBytes = roleNameGbkBytes ?? Array.Empty<byte>();
                FirstCount = firstCount;
                SecondCount = secondCount;
            }

            public int Result { get; }
            public string RoleName { get; }
            public ReadOnlyMemory<byte> RoleNameGbkBytes { get; }
            public int FirstCount { get; }
            public int SecondCount { get; }
        }

        public sealed class Grant
        {
            internal Grant(int firstCount, int secondCount, int total,
                int levelExperienceBase, int weightedExperience,
                int signedRandomBound, int randomValue, int experience,
                bool receivesBounty)
            {
                FirstCount = firstCount;
                SecondCount = secondCount;
                Total = total;
                LevelExperienceBase = levelExperienceBase;
                WeightedExperience = weightedExperience;
                SignedRandomBound = signedRandomBound;
                RandomValue = randomValue;
                Experience = experience;
                ReceivesBounty = receivesBounty;
            }

            public int FirstCount { get; }
            public int SecondCount { get; }
            public int Total { get; }
            public int DiamondCacheDelta => Total;
            public int LevelExperienceBase { get; }
            public int WeightedExperience { get; }
            public int SignedRandomBound { get; }
            public uint NativeRandomRange => unchecked((uint)SignedRandomBound);
            public int RandomValue { get; }
            public int Experience { get; }
            public bool ReceivesBounty { get; }
        }
    }
}

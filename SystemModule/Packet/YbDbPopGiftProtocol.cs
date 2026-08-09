using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant codec for the native ReqPopGift 123/1123 transaction.
    /// It performs no transport, player, inventory, account, or ACK mutation.
    /// </summary>
    public static class YbDbPopGiftProtocol
    {
        public const ushort RequestIdent = 123;
        public const ushort ResponseIdent = 1123;
        public const ushort SuccessAckIdent = 105;
        public const ushort FailureAckIdent = 106;
        public const int NativeBagCapacity = 48;
        public const int ResponsePayloadSize = 84;
        public const int RoleNameOffset = 32;
        public const int RoleNameMaximumGbkBytes = 15;
        public const int ItemNameOffset = 64;
        public const int ItemNameMaximumGbkBytes = 14;
        public const int NativeItemFlagOffset = 79;
        public const int ItemCountOffset = 80;
        public const int GameLogType = 53;
        public const int InvalidAwardMakeIndex = 444444;
        public const int LingFuMakeIndex = 222222;

        public const string RequestUnavailableDialog =
            "元宝系统暂时关闭中...\\ \\ \\ <返回/@main>";
        public const string ClientRequestUnavailableMessage =
            "网络故障，请稍候...";
        public const string NoAwardDialog =
            "对不起，没有可领取的活动道具 \\ \\ <离开/@exit>";
        public const string GenericFailureDialog =
            " 领取失败 \\ \\ <返回/@main>";
        public const string DeliveryLogReason = "MQ送道具领取";
        public const string InvalidAwardLogReason = "奖品名非法";
        public const string MissingItemLogReason = "奖品出错";
        public const string LingFuItemName = "灵符";

        public static bool TryCreateRequest(YbDbLegacy77Identity identity,
            int currentBagItemCount, out YbDbLegacy77Frame frame,
            out string error)
        {
            frame = null;
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
                return false;

            // Native does not clamp a full or over-capacity bag to zero.
            var remainingBagSlots = unchecked(NativeBagCapacity -
                                              currentBagItemCount);
            frame = new YbDbLegacy77Frame(0, remainingBagSlots,
                RequestIdent, payload);
            return true;
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out Response response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "pop-gift response frame is null";
                return false;
            }
            if (frame.Ident != ResponseIdent)
            {
                error = $"pop-gift response Ident must be {ResponseIdent}";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length != ResponsePayloadSize)
            {
                error = $"pop-gift response payload must be " +
                        $"{ResponsePayloadSize} bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(payload,
                    RoleNameOffset, RoleNameMaximumGbkBytes,
                    out var roleName, out error))
                return false;
            if (!YbDbLegacy77Codec.TryDecodeShortString(payload,
                    ItemNameOffset, ItemNameMaximumGbkBytes,
                    out var itemName, out error))
                return false;

            response = new Response(frame.QueryId, frame.Param,
                roleName, itemName, payload[NativeItemFlagOffset],
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(ItemCountOffset, sizeof(int))));
            return true;
        }

        public static bool TryGetFailureDialog(Response response,
            out string dialog)
        {
            dialog = string.Empty;
            if (response == null || response.Result > 0)
                return false;

            dialog = response.Result switch
            {
                -2 => "您背包不足，无法领取道具：" + response.ItemName +
                      " 数量:" + response.ItemCount +
                      " \\ \\ <离开/@exit>",
                -1 => NoAwardDialog,
                _ => GenericFailureDialog
            };
            return true;
        }

        public static bool TryCreateAck(int transactionToken, bool succeeded,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (transactionToken <= 0)
            {
                error = "pop-gift ACK requires a positive transaction token";
                return false;
            }

            frame = new YbDbLegacy77Frame(ResponseIdent, transactionToken,
                succeeded ? SuccessAckIdent : FailureAckIdent,
                Array.Empty<byte>());
            return true;
        }

        public static string BuildLingFuSuccessDialog(int count) =>
            "成功领取了" + count + "张灵符";

        public static string BuildItemSuccessDialog(string itemName,
            int count) => "成功领取了 " + (itemName ?? string.Empty) +
                          " " + count;

        public sealed class Response
        {
            internal Response(int result, int ignoredHeaderParam,
                string roleName, string itemName, byte nativeItemFlag,
                int itemCount)
            {
                Result = result;
                IgnoredHeaderParam = ignoredHeaderParam;
                RoleName = roleName;
                ItemName = itemName;
                NativeItemFlag = nativeItemFlag;
                ItemCount = itemCount;
            }

            public int Result { get; }
            public int IgnoredHeaderParam { get; }
            public string RoleName { get; }
            public string ItemName { get; }
            public byte NativeItemFlag { get; }
            public bool NativeItemFlagIsOne => NativeItemFlag == 1;
            public int ItemCount { get; }
            public bool Succeeded => Result > 0;
        }
    }
}

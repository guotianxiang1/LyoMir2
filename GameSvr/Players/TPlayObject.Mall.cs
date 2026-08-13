using System;
using System.Collections.Generic;
using System.IO;
using SystemModule;
using GameSvr.Mall;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int WhitePigMallRecordSize = 180;
        private const int WhitePigMallHotRecordCount = 5;
        private int _whitePigMallSentMask;
        private int _lastWhitePigMallBuyTick;

        public void InvalidateWhitePigMallCache()
        {
            _whitePigMallSentMask = 0;
        }

        public void ClientQueryWhitePigMall(int requestedType)
        {
            if (requestedType < 0 || requestedType >= 8)
            {
                return;
            }

            var sentBit = 2 << requestedType;
            if ((_whitePigMallSentMask & sentBit) != 0)
            {
                return;
            }

            var items = MallManager.Instance.GetItemsForClientType(requestedType);
            var body = BuildWhitePigMallBody(items, requestedType, 0, out var recordCount);
            if (recordCount > 0)
            {
                var response = Grobal2.MakeDefaultMsg(Grobal2.SM_SHOPITEMS, ObjectId, requestedType, 0, 0);
                SendSocket(response, body);
                SendNativeGpForbidItems();
            }

            var hotItems = MallManager.Instance.GetHotItems(WhitePigMallHotRecordCount);
            var hotBody = BuildWhitePigMallBody(hotItems, 10, WhitePigMallHotRecordCount, out var hotRecordCount);
            if (hotRecordCount > 0)
            {
                var response = Grobal2.MakeDefaultMsg(Grobal2.SM_FIRSTSHOP, ObjectId, 0, 0, 0);
                SendSocket(response, hotBody);
            }

            _whitePigMallSentMask |= sentBit;
        }

        public void ClientRefreshWhitePigMall(int requestedType)
        {
            if (requestedType < 0 || requestedType >= 8)
            {
                return;
            }

            var items = MallManager.Instance.GetItemsForClientType(requestedType);
            var body = BuildWhitePigMallBody(items, requestedType, 0, out var recordCount);
            var ident = recordCount > 0 ? Grobal2.SM_RESHOPITEMS_OK : Grobal2.SM_RESHOPITEMS_FAIL;
            var response = Grobal2.MakeDefaultMsg(ident, ObjectId, requestedType, 0, 0);
            SendSocket(response, recordCount > 0 ? body : Array.Empty<byte>());
            if (recordCount > 0)
            {
                SendNativeGpForbidItems();
            }
        }

        private byte[] BuildWhitePigMallBody(IReadOnlyList<MallItem> items, int page, int recordSlots, out int validCount)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            validCount = 0;

            foreach (var item in items)
            {
                if (recordSlots > 0 && validCount >= recordSlots)
                {
                    break;
                }

                var stdItem = MallManager.Instance.ResolveStdItem(item, out _);
                if (stdItem == null || stdItem.Looks == 0)
                {
                    continue;
                }

                var recordStart = stream.Position;
                WriteClientFixedGbkString(writer, item.ItemName, 15);
                WriteClientFixedGbkString(writer, item.CategoryName, 15);
                writer.Write(ClampToShort(stdItem.Looks));
                writer.Write(ClampToShort(page));
                writer.Write(ClampToShort(item.Price));
                writer.Write(ClampToShort(item.CurPrice));
                writer.Write(ClampToShort(item.LimitType));
                writer.Write(ClampToShort(item.LimitCount));
                writer.Write(ClampToShort(MallManager.Instance.GetCurrentLimitValue(this, item)));
                AlignWriter(writer, 4);
                writer.Write((uint)item.CurrencyType);
                WriteClientFixedGbkString(writer, item.Description, 127);

                if (stream.Position - recordStart != WhitePigMallRecordSize)
                {
                    throw new InvalidDataException($"商城商品结构长度错误: {stream.Position - recordStart}");
                }
                validCount++;
            }

            if (recordSlots > 0)
            {
                while (stream.Length < recordSlots * WhitePigMallRecordSize)
                {
                    writer.Write(new byte[WhitePigMallRecordSize]);
                }
            }

            var body = stream.ToArray();
            var expectedLength = recordSlots > 0
                ? recordSlots * WhitePigMallRecordSize
                : validCount * WhitePigMallRecordSize;
            if (body.Length != expectedLength)
            {
                throw new InvalidDataException($"商城列表长度错误: {body.Length}，预期:{expectedLength}");
            }
            return body;
        }

        public void ClientBuyWhitePigMall(string itemName, int quantity)
        {
            itemName = itemName?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(itemName))
            {
                SendWhitePigMallFailure(0);
                return;
            }

            var now = HUtil32.GetTickCount();
            if (_lastWhitePigMallBuyTick != 0 && now - _lastWhitePigMallBuyTick < 300)
            {
                SendWhitePigMallFailure(-6);
                return;
            }
            _lastWhitePigMallBuyTick = now;

            if (!MallManager.Instance.PurchaseItemByName(this, itemName, quantity,
                out var failureCode, out var errorMsg))
            {
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    SysMsg(errorMsg, MsgColor.Red, MsgType.Hint);
                }
                SendWhitePigMallFailure(failureCode);
            }
        }

        private void SendWhitePigMallFailure(int failureCode)
        {
            var response = Grobal2.MakeDefaultMsg(Grobal2.SM_DOSHOP_FAIL, failureCode, 0, 0, 0);
            SendSocket(response, Array.Empty<byte>());
        }

        private void SendNativeGpForbidItems()
        {
            if (!MallManager.Instance.TryGetGpForbidBody(out var count, out var body))
            {
                return;
            }
            var response = Grobal2.MakeDefaultMsg(Grobal2.SM_GPFORBIDITEMS, ObjectId, count, 0, 0);
            SendSocket(response, body);
        }

    }
}

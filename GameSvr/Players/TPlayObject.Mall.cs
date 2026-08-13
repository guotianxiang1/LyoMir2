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
                // 1101 Looks fill sub_639D24:
                //   0x639DB5 e8 1a 25 11 00  call 0x74C2D4     ; std-item by name
                //   0x639DC1 74 10           je  0x639DD3      ; miss -> fallback
                //   0x639DC6 66 8b 40 18     mov ax,[std+0x18] ; Looks
                //   0x639DCD 66 89 42 20     mov [rec+0x20],ax
                //   0x639DD6 66 8b 40 30     mov ax,[rec+0x30] ; vEffectImg low word
                //   0x639DDD 66 89 42 20     mov [rec+0x20],ax
                // Native still emits the 180-byte record when the std-item is missing
                // or Looks is 0. Skipping either case dropped production rows whose
                // vEffectImg (520/410/380) is the documented fallback.
                var looks = stdItem != null ? stdItem.Looks : item.EffectImg;

                // 180-byte TClientShop, filled by native sub_636D68 at esi = record+0x58:
                //   +0   0x637157 cl=0x0F   商品名 ShortString[15]
                //   +16  0x63717A cl=0x0F   分类名 ShortString[15]
                //   +32  0x637187 66 c7 46 20 00 00   Looks, zero here; the 1101 handler
                //                                     patches in the canonical std-item Looks
                //   +34  0x637181 66 c7 46 22 00 00   page/category, zero here; set on send
                //   +36  0x637191 66 89 46 24         vSrcPrice
                //   +38  0x637199 66 89 46 26         vCurPrice
                //   +40  0x6371A1 66 89 46 28         vLimitType
                //   +42  0x6371A9 66 89 46 2a         vLimitCount
                //   +44  0x6371AD 66 c7 46 2c 00 00   zero here; sub_63CD0C backfills the
                //                                     per-player limit at 0x63CDC3 before
                //                                     every 812/813/815
                //   +46  0x6371BD 66 89 46 2e         vEffectCount   (WORD)
                //   +48  0x6371B6 89 46 30            vEffectImg     (DWORD)
                //   +52  0x6371DF cl=0x7F             描述 ShortString[127]
                var recordStart = stream.Position;
                WriteClientFixedGbkString(writer, item.ItemName, 15);
                WriteClientFixedGbkString(writer, item.CategoryName, 15);
                writer.Write(ClampToShort(looks));
                writer.Write(ClampToShort(page));
                writer.Write(ClampToShort(item.Price));
                writer.Write(ClampToShort(item.CurPrice));
                writer.Write(ClampToShort(item.LimitType));
                writer.Write(ClampToShort(item.LimitCount));
                writer.Write(ClampToShort(MallManager.Instance.GetCurrentLimitValue(this, item)));
                writer.Write(ClampToShort(item.EffectCount));
                writer.Write(item.EffectImg);
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

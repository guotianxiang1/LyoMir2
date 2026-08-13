using SystemModule;

namespace GameSvr.PasEngine
{
    public partial class PasApiBridge
    {
        private const string GiveExperience = "经验";
        private const string GiveHeroExperience = "英雄经验";
        private const string GiveLingFu = "灵符";
        private const string GiveLimitedLingFu = "限时灵符";
        private const string GiveVitality = "牛气值";
        private const string GiveReputation = "声望";
        private const string GiveGold = "金币";
        private const string GiveGloryPoint = "荣耀点";

        private bool TryNativeGive(string sourceName, int requestedCount, bool bind,
            bool writeGiveAudit)
        {
            if (CurrentPlayer == null || string.IsNullOrEmpty(sourceName)) return false;

            if (TryExecuteTunnelGive(sourceName, requestedCount, bind)) return true;

            if (!TryExecuteNativeGiveDescriptor(sourceName, requestedCount, bind,
                false, out var showSuccess, out _, out _))
                return false;

            if (showSuccess)
                CurrentPlayer.SysMsg("恭喜：你获得了：" + sourceName,
                    MsgColor.Green, MsgType.Hint);
            if (writeGiveAudit)
                WriteNativeGiveAudit(sourceName, requestedCount);
            return true;
        }

        private bool TryNativeGiveGbk(byte[] sourceNameGbkBytes,
            int requestedCount, bool bind, bool writeGiveAudit)
        {
            if (CurrentPlayer == null || sourceNameGbkBytes == null ||
                sourceNameGbkBytes.Length == 0)
                return false;

            var sourceName = HUtil32.GbkEncoding.GetString(sourceNameGbkBytes);
            if (TryExecuteTunnelGive(sourceName, requestedCount, bind)) return true;

            if (!TryExecuteNativeGiveDescriptorGbk(sourceNameGbkBytes,
                    requestedCount, bind, false, out var showSuccess, out _,
                    out _))
                return false;

            if (showSuccess)
                CurrentPlayer.SysMsg("恭喜：你获得了：" + sourceName,
                    MsgColor.Green, MsgType.Hint);
            if (writeGiveAudit)
                WriteNativeGiveAudit(sourceName, requestedCount);
            return true;
        }

        internal bool TryNativeExchangeBookGive(string sourceName)
        {
            return string.IsNullOrEmpty(sourceName)
                ? false
                : TryNativeExchangeBookGiveGbk(
                    HUtil32.GbkEncoding.GetBytes(sourceName));
        }

        internal bool TryNativeExchangeBookGiveGbk(byte[] sourceNameGbkBytes) =>
            TryNativeGiveGbk(sourceNameGbkBytes, 1, false, true);

        private bool TryNativeGiveConfigPrize(int prizeIndex, string infoStr,
            bool tempTransferFlag)
        {
            _ = tempTransferFlag;
            if (CurrentPlayer == null || M2Share.ConfigPrizeManager == null ||
                !M2Share.ConfigPrizeManager.TrySelectGbk(prizeIndex,
                    out var descriptorGbkBytes))
                return false;

            var descriptor = HUtil32.GbkEncoding.GetString(descriptorGbkBytes);
            if (!TryExecuteNativeGiveDescriptorGbk(descriptorGbkBytes, 1,
                false, true,
                out var showSuccess, out _, out _))
                return false;

            ResolveNativeConfigPrizeLogFieldsGbk(descriptorGbkBytes,
                out var rewardName, out var rewardCount);
            if (showSuccess)
                SendNativeConfigPrizeMessage("恭喜：你获得了：" + descriptor, 0xFC);

            WriteNativeConfigPrizeSpecialLog(prizeIndex, rewardName, rewardCount);
            if (!string.IsNullOrEmpty(infoStr))
            {
                var expanded = ExpandNativeConfigPrizeInfo(infoStr, rewardName);
                BroadcastNativeConfigPrizeInfo(expanded);
            }
            return true;
        }

        private bool TryExecuteNativeGiveDescriptor(string sourceName,
            int requestedCount, bool bind, bool configPrize,
            out bool showSuccess, out string itemName, out int count)
        {
            ResolveNativeGiveNameAndCount(sourceName, requestedCount, out itemName,
                out count);
            return TryExecuteNativeGiveDescriptorCore(sourceName, null, itemName,
                count, bind, configPrize, out showSuccess);
        }

        private bool TryExecuteNativeGiveDescriptorGbk(byte[] sourceNameGbkBytes,
            int requestedCount, bool bind, bool configPrize,
            out bool showSuccess, out string itemName, out int count)
        {
            if (sourceNameGbkBytes == null)
            {
                showSuccess = false;
                itemName = null;
                count = 0;
                return false;
            }

            ResolveNativeGiveNameAndCountGbk(sourceNameGbkBytes,
                requestedCount, out var itemNameGbkBytes, out itemName,
                out count);
            var sourceName = HUtil32.GbkEncoding.GetString(sourceNameGbkBytes);
            return TryExecuteNativeGiveDescriptorCore(sourceName,
                itemNameGbkBytes, itemName, count, bind, configPrize,
                out showSuccess);
        }

        private bool TryExecuteNativeGiveDescriptorCore(string sourceName,
            ReadOnlyMemory<byte>? itemNameGbkBytes, string itemName, int count,
            bool bind, bool configPrize, out bool showSuccess)
        {
            bool NameEquals(string expected) => itemNameGbkBytes.HasValue
                ? EqualsNativeGiveName(itemNameGbkBytes.Value.Span, expected)
                : EqualsNativeGiveName(itemName, expected);

            showSuccess = true;
            bool success;
            if (NameEquals(GiveExperience))
            {
                CurrentPlayer.GrantNativeScriptExperience(count);
                showSuccess = false;
                success = true;
            }
            else if (NameEquals(GiveHeroExperience))
            {
                success = TryGiveNativeHeroExperience(count, out showSuccess);
            }
            else if (NameEquals(GiveLingFu) || NameEquals(GiveLimitedLingFu))
            {
                // The original executor selects the account from one global switch;
                // the descriptor spelling (LingFu versus LimitedLingFu) is ignored.
                var creditService = M2Share.CreditCardService ??
                    NativeCreditCardService.Disabled;
                success = creditService.Enabled
                    ? TryGiveNativeLimitedLingFu(count)
                    : CurrentPlayer.AddNativeLingFu(23001, count);
            }
            else if (NameEquals(GiveVitality))
            {
                success = CurrentPlayer.AddNativeCattle(count);
            }
            else if (NameEquals(GiveReputation))
            {
                CurrentPlayer.m_nShengWan = unchecked(CurrentPlayer.m_nShengWan + count);
                success = true;
            }
            else if (NameEquals(GiveGold))
            {
                CurrentPlayer.m_nGold = unchecked(CurrentPlayer.m_nGold + count);
                success = true;
            }
            else if (NameEquals(GiveGloryPoint))
            {
                // GloryPoint is an account-side value, not m_nHonorValue or
                // m_nActivePoint.  The native executor does not emit the generic
                // config-prize success text for this reward.
                success = TryGiveNativeGloryPoint(count);
                showSuccess = false;
            }
            else
            {
                success = TryGiveNativeItems(itemName, count, bind, configPrize,
                    sourceName, itemNameGbkBytes);
            }

            return success;
        }

        private bool TryNativeLoopGive(IReadOnlyList<PasValue> args)
        {
            if (CurrentPlayer == null || args.Count < 3) return false;

            var sourceName = args[0].AsString();
            var requestedCount = args[1].AsInt();
            var loops = args[2].AsInt();
            if (string.IsNullOrEmpty(sourceName) || requestedCount <= 0 || loops <= 0)
                return false;

            for (var i = 0; i < loops; i++)
                _ = TryNativeGive(sourceName, requestedCount, false, true);
            return true;
        }

        private bool TryGiveNativeItems(string itemName, int count, bool bind,
            bool configPrize, string sourceName,
            ReadOnlyMemory<byte>? itemNameGbkBytes = null)
        {
            GoodItem configuredItem = null;
            if (configPrize)
            {
                configuredItem = itemNameGbkBytes.HasValue
                    ? FindNativeStdItem(itemNameGbkBytes.Value.Span)
                    : M2Share.UserEngine.GetStdItem(itemName);
            }
            if (configPrize && configuredItem == null)
            {
                if (CurrentPlayer.m_btPermission > 3)
                    SendNativeConfigPrizeMessage("[错误]：不存在的奖品：" + sourceName,
                        0x38);
                return false;
            }
            if (configuredItem != null) itemName = configuredItem.Name;

            var remaining = count;
            var gaveAny = false;
            while (remaining > 0)
            {
                TUserItem item = null;
                if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref item) || item == null)
                    break;

                var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                if (stdItem == null)
                {
                    CurrentPlayer.Dispose(item);
                    break;
                }

                var quantity = 1;
                // 原生 sub_6C87B4 @0x6C89C6 `80 78 14 07 cmp byte [eax+0x14],7` 读的是
                // 【物品实例】的运行时 KIND 字节（基类构造器 sub_783788 @0x7837AE 写 0、
                // 堆叠基类 sub_7880F0 @0x788118 写 7），不是模板 +0x14 的 StdMode。
                // 随后 0x6C89CF movzx 用的也是实例 +0x28(DuraMax) / +0x26(Dura)。
                // 按 StdMode==7 判会把护身符族（派发表 mode 7 -> TCharm 族）当成堆叠，
                // 又漏掉真正的堆叠物，导致「给 100 个金条」占 100 个背包格。
                if (NativeItemFactory.IsPileItem(stdItem))
                {
                    if (item.DuraMax == 0)
                    {
                        CurrentPlayer.Dispose(item);
                        break;
                    }
                    quantity = Math.Min(remaining, item.DuraMax);
                    item.Dura = (ushort)quantity;
                }

                if (bind) item.Bind = 1;
                if (!CurrentPlayer.AddItemToBag(item))
                {
                    CurrentPlayer.Dispose(item);
                    break;
                }

                CurrentPlayer.SendAddItem(item);
                WriteNativeItemGiveLog(stdItem.Name, item, quantity);
                gaveAny = true;
                remaining -= quantity;
            }
            return gaveAny;
        }

        private bool TryGiveNativeHeroExperience(int count, out bool showSuccess)
        {
            showSuccess = false;
            var hero = CurrentPlayer.m_HeroObject;
            if (hero == null)
            {
                CurrentPlayer.SysMsg("请先将您的英雄召唤出来！", MsgColor.Red, MsgType.Hint);
                return true;
            }

            showSuccess = true;
            if (hero.m_Abil.Level == 200)
            {
                CurrentPlayer.SysMsg("你的英雄级数已满", MsgColor.Red, MsgType.Hint);
                return true;
            }

            CurrentPlayer.GrantNativeHeroExperience(hero, count, false, true);
            return true;
        }

        private bool TryGiveNativeLimitedLingFu(int count)
        {
            if (count <= 0) return false;

            // sub_7140B8 has no Loaded check: it updates Value, clamps a
            // negative signed result to zero, and marks the account dirty.
            lock (CurrentPlayer.m_CreditCard.SyncRoot)
            {
                var value = unchecked(CurrentPlayer.m_CreditCard.Value + count);
                CurrentPlayer.m_CreditCard.Value = value < 0 ? 0 : value;
                CurrentPlayer.m_CreditCard.Dirty = true;
                CurrentPlayer.m_CreditCard.DirtyVersion++;
            }

            CurrentPlayer.RefreshNativeLingFu();
            WriteNativeGiveCurrencyLog(GiveLimitedLingFu, 23002, count,
                "npc给予");
            return true;
        }

        private bool TryAddNativeGloryPoint(int count)
        {
            if (count <= 0) return false;

            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_SYSMESSAGE, 0,
                0xDB, 0xFF, 0, count + "点荣耀点增加");

            // sub_714108 updates the account object before the executor queues
            // the native 10054 capital refresh.  It is intentionally independent
            // of the CreditCard feature switch and the Loaded flag.
            lock (CurrentPlayer.m_CreditCard.SyncRoot)
            {
                CurrentPlayer.m_CreditCard.GloryPointValue = unchecked(
                    CurrentPlayer.m_CreditCard.GloryPointValue + count);
                CurrentPlayer.m_CreditCard.GloryPointDirty = true;
                CurrentPlayer.m_CreditCard.GloryPointDirtyVersion++;
            }

            CurrentPlayer.RefreshNativeLingFu();
            return true;
        }

        private bool TryGiveNativeGloryPoint(int count)
        {
            if (!TryAddNativeGloryPoint(count)) return false;

            M2Share.AddGameDataLog(string.Join('\t', 9,
                CurrentPlayer.m_sMapName, CurrentPlayer.m_nCurrX,
                CurrentPlayer.m_nCurrY, CurrentPlayer.m_sCharName,
                GiveGloryPoint, 888888, count, "系统给予"));
            return true;
        }

        private void WriteNativeGiveCurrencyLog(string itemName, int reason,
            int amount, string npcPrefix)
        {
            var description = CurrentPlayer.m_NPC == null
                ? string.Empty
                : npcPrefix + CurrentPlayer.m_NPC.m_sCharName + '-' +
                  CurrentPlayer.m_NPC.m_sMapName;
            M2Share.AddGameDataLog(string.Join('\t', 9,
                CurrentPlayer.m_sMapName, CurrentPlayer.m_nCurrX,
                CurrentPlayer.m_nCurrY, CurrentPlayer.m_sCharName, itemName,
                reason, amount, description));
        }

        private void WriteNativeItemGiveLog(string itemName, TUserItem item, int quantity)
        {
            M2Share.AddGameDataLog(string.Join('\t', 9, CurrentPlayer.m_sMapName,
                CurrentPlayer.m_nCurrX, CurrentPlayer.m_nCurrY, CurrentPlayer.m_sCharName,
                itemName, item.MakeIndex, quantity, "系统给予"));
        }

        private void WriteNativeGiveAudit(string sourceName, int requestedCount)
        {
            // Native Give inner 0x6DF2E8 only emits the 经验/内功经验/荣耀点
            // audit when player+0xCD8 is non-nil (0x6DF341 cmp [edi+0xCD8],0 /
            // je 0x6DF454). CurrentNpc is the script context and is already
            // bound during @main; m_NPC is not (click handler writes it after
            // the vcall at 0x6B8BA7/0x6B8C48). Using CurrentNpc here made the
            // first-click Give log a NPC native would omit.
            var npc = CurrentPlayer?.m_NPC as NormNpc;
            if (npc == null) return;

            string category;
            string reason;
            int makeIndex;
            if (sourceName == GiveExperience)
            {
                category = GiveExperience;
                reason = "NPC给予: ";
                makeIndex = 555555;
            }
            else if (sourceName == GiveHeroExperience)
            {
                category = GiveHeroExperience;
                reason = "NPC给予";
                makeIndex = 555555;
            }
            else if (sourceName == GiveGloryPoint)
            {
                category = GiveGloryPoint;
                reason = "NPC给予：";
                makeIndex = 888888;
            }
            else
            {
                return;
            }

            reason += npc.m_sCharName + "-" + npc.m_sMapName;
            M2Share.AddGameDataLog(string.Join('\t', 9, CurrentPlayer.m_sMapName,
                CurrentPlayer.m_nCurrX, CurrentPlayer.m_nCurrY, CurrentPlayer.m_sCharName,
                category, makeIndex, requestedCount, reason));
        }

        private void WriteNativeConfigPrizeSpecialLog(int prizeIndex,
            string rewardName, int rewardCount)
        {
            int makeIndex;
            if (string.Equals(rewardName, GiveExperience, StringComparison.Ordinal))
                makeIndex = 555555;
            else if (string.Equals(rewardName, GiveLingFu, StringComparison.Ordinal))
                makeIndex = 222222;
            else
                return;

            M2Share.AddGameDataLog(string.Join('\t', 9, CurrentPlayer.m_sMapName,
                CurrentPlayer.m_nCurrX, CurrentPlayer.m_nCurrY,
                CurrentPlayer.m_sCharName, rewardName, makeIndex, rewardCount,
                "奖励配置" + prizeIndex));
        }

        private void SendNativeConfigPrizeMessage(string text, int background)
        {
            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_SYSMESSAGE, 0,
                0xFF, background, 0, text);
        }

        private static void ResolveNativeConfigPrizeLogFields(string descriptor,
            out string rewardName, out int rewardCount)
        {
            rewardName = descriptor;
            rewardCount = 0;
            var separator = descriptor.IndexOf(':');
            if (separator < 0) return;

            rewardName = descriptor.Substring(0, separator);
            if (TryParseNativeDelphiInteger(descriptor.Substring(separator + 1),
                out var parsedCount))
                rewardCount = parsedCount;
        }

        private static void ResolveNativeConfigPrizeLogFieldsGbk(
            ReadOnlySpan<byte> descriptor, out string rewardName,
            out int rewardCount)
        {
            rewardCount = 0;
            var separator = descriptor.IndexOf((byte)':');
            var rewardNameBytes = separator < 0
                ? descriptor
                : descriptor.Slice(0, separator);
            rewardName = HUtil32.GbkEncoding.GetString(rewardNameBytes);
            if (separator >= 0 && TryParseNativeDelphiIntegerGbk(
                    descriptor.Slice(separator + 1), out var parsedCount))
                rewardCount = parsedCount;
        }

        private static string ExpandNativeConfigPrizeInfo(string infoStr,
            string rewardName)
        {
            var result = infoStr;
            // The Delphi wrapper keeps a scan copy separate from the mutable
            // broadcast string.  A case-mixed tag can therefore consume one
            // scan slot even when the exact replacement token is absent.
            var scanTail = infoStr;
            const string replacementToken = "<$GIFTITEM>";
            for (var i = 0; i < 11; i++)
            {
                var open = scanTail.IndexOf('<');
                if (open < 0) break;

                var close = scanTail.IndexOf('>', open + 1);
                if (close < 0) break;

                var tag = scanTail.Substring(open + 1, close - open - 1);
                scanTail = scanTail.Substring(close + 1);
                if (string.Equals(tag, "$GIFTITEM",
                    StringComparison.OrdinalIgnoreCase))
                {
                    // Delphi compares the tag case-insensitively, then the
                    // replacement helper searches for the exact token.
                    var tokenStart = result.IndexOf(replacementToken,
                        StringComparison.Ordinal);
                    if (tokenStart >= 0)
                    {
                        result = result.Substring(0, tokenStart) + rewardName
                            + result.Substring(tokenStart + replacementToken.Length);
                    }
                }
            }
            return result;
        }

        private static void BroadcastNativeConfigPrizeInfo(string text)
        {
            var players = M2Share.UserEngine?.GetPlayerList();
            if (players == null) return;

            foreach (var player in players)
            {
                if (player == null || player.m_boGhost) continue;
                player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                    0xFF, 0x38, 0, text);
            }
        }

        private static bool EqualsNativeGiveName(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static bool EqualsNativeGiveName(ReadOnlySpan<byte> left,
            string right)
        {
            var rightBytes = HUtil32.GbkEncoding.GetBytes(right ?? string.Empty);
            if (left.Length != rightBytes.Length) return false;

            for (var index = 0; index < left.Length; index++)
            {
                var leftByte = left[index];
                var rightByte = rightBytes[index];
                if (IsGbkLeadByte(leftByte) && index + 1 < left.Length &&
                    IsGbkTrailByte(left[index + 1]))
                {
                    if (leftByte != rightByte ||
                        left[index + 1] != rightBytes[index + 1])
                        return false;
                    index++;
                    continue;
                }

                if (FoldAscii(leftByte) != FoldAscii(rightByte)) return false;
            }
            return true;
        }

        private static GoodItem FindNativeStdItem(ReadOnlySpan<byte> itemName)
        {
            var userEngine = M2Share.UserEngine;
            if (userEngine?.StdItemList == null || itemName.IsEmpty) return null;
            foreach (var stdItem in userEngine.StdItemList)
            {
                if (stdItem != null &&
                    EqualsNativeGiveName(itemName, stdItem.Name))
                    return stdItem;
            }
            return null;
        }

        private static void ResolveNativeGiveNameAndCount(string sourceName,
            int requestedCount, out string itemName, out int count)
        {
            itemName = sourceName;
            count = requestedCount;
            var separator = sourceName.IndexOf(':');
            if (separator >= 0)
            {
                itemName = sourceName.Substring(0, separator);
                var textCount = sourceName.Substring(separator + 1);
                if (TryParseNativeDelphiInteger(textCount, out var parsedCount))
                    count = parsedCount;
            }
            if (count <= 0) count = 1;
        }

        private static void ResolveNativeGiveNameAndCountGbk(
            ReadOnlySpan<byte> sourceName, int requestedCount,
            out ReadOnlyMemory<byte> itemNameGbkBytes, out string itemName,
            out int count)
        {
            var separator = sourceName.IndexOf((byte)':');
            var rawItemName = separator < 0
                ? sourceName.ToArray()
                : sourceName.Slice(0, separator).ToArray();
            itemNameGbkBytes = rawItemName;
            itemName = HUtil32.GbkEncoding.GetString(rawItemName);
            count = requestedCount;
            if (separator >= 0 && TryParseNativeDelphiIntegerGbk(
                    sourceName.Slice(separator + 1), out var parsedCount))
                count = parsedCount;
            if (count <= 0) count = 1;
        }

        private static bool TryParseNativeDelphiIntegerGbk(
            ReadOnlySpan<byte> text, out int value)
        {
            value = 0;
            if (text.IsEmpty) return false;
            var characters = new char[text.Length];
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] > 0x7F) return false;
                characters[index] = (char)text[index];
            }
            return TryParseNativeDelphiInteger(new string(characters),
                out value);
        }

        private static bool IsGbkLeadByte(byte value) =>
            value >= 0x81 && value <= 0xFE;

        private static bool IsGbkTrailByte(byte value) =>
            value >= 0x40 && value <= 0xFE && value != 0x7F;

        private static byte FoldAscii(byte value) =>
            value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + ((byte)'a' - (byte)'A'))
                : value;

        internal static bool TryParseNativeDelphiInteger(string text,
            out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;

            var index = 0;
            while (index < text.Length && text[index] == ' ') index++;

            var negative = false;
            if (index < text.Length && (text[index] == '+' || text[index] == '-'))
            {
                negative = text[index] == '-';
                index++;
            }
            if (index >= text.Length) return false;

            var hexadecimal = false;
            if (text[index] == '$')
            {
                hexadecimal = true;
                index++;
            }
            else if (text[index] == 'x' || text[index] == 'X')
            {
                hexadecimal = true;
                index++;
            }
            else if (text[index] == '0' && index + 1 < text.Length &&
                     (text[index + 1] == 'x' || text[index + 1] == 'X'))
            {
                hexadecimal = true;
                index += 2;
            }
            if (index >= text.Length) return false;

            if (hexadecimal)
            {
                uint bits = 0;
                var digits = 0;
                while (index < text.Length)
                {
                    var c = text[index++];
                    int digit;
                    if (c >= '0' && c <= '9') digit = c - '0';
                    else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                    else return false;

                    if (bits > 0x0FFFFFFF) return false;
                    bits = (bits << 4) | (uint)digit;
                    digits++;
                }
                if (digits == 0) return false;

                var parsed = unchecked((int)bits);
                value = negative ? unchecked(-parsed) : parsed;
                return true;
            }

            long magnitude = 0;
            var decimalDigits = 0;
            while (index < text.Length)
            {
                var c = text[index++];
                if (c < '0' || c > '9') return false;
                var limit = negative ? 2147483648L : int.MaxValue;
                var digit = c - '0';
                if (magnitude > (limit - digit) / 10) return false;
                magnitude = magnitude * 10 + digit;
                decimalDigits++;
            }
            if (decimalDigits == 0) return false;

            value = negative ? unchecked((int)-magnitude) : (int)magnitude;
            return true;
        }
    }
}

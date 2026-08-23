using System;
using GameSvr.PasEngine;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native @ChgEquipLevel (dispatch 229, core 0x006D6DEC).
    /// The command changes one of the three profession attributes on a qualifying
    /// bag item and stores the requested level as the native zero-based byte value.
    /// </summary>
    public static class NativeGmChgEquipLevel
    {
        public const uint CoreEa = 0x006D6DEC;
        public const uint DominantAttributeEa = 0x0078437C;
        public const uint RefreshItemEa = 0x0073CBD0;

        public const int ErrorColor = 0x38FF;
        public const int SuccessColor = 0xFFDB;

        public const string NoProfessionFeatureMessage = "你指定的装备不含有三职业的任何特征";
        public const string ShapeRejectedMessage = "该指令目前只适用于分担伤害套装装备";
        public const string SuccessPrefix = "成功改变你指定的装备：";
        public const string SuccessLevelSeparator = "之等级到：";
        public const string ItemNotFoundMessage = "在你的包裹中没有找到指定的物品";
        public const string LevelRangeMessage = "等级范围只能是: 1--5";

        // dword_6D6F74 is a 64-bit bitset in the unpacked 2.08 image.  The
        // core indexes it with (byte)(std.Shape + 0x78), so the accepted Shape
        // values are deliberately represented by the original bitset rather
        // than by a guessed item-type predicate.
        private const ulong DamageSharingSuitMask = 0x01C0701C0701C070UL;

        /// <summary>Native byte-add/index test used by sub_6D6DEC.</summary>
        public static bool IsDamageSharingSuitShape(byte shape)
        {
            var bit = (byte)(shape + 0x78);
            return bit <= 0x3F && ((DamageSharingSuitMask >> bit) & 1UL) != 0;
        }

        /// <summary>
        /// Returns the native dominant attribute kind: 1=DC, 2=MC, 3=SC,
        /// 4=Need, 0=not an eligible equipment mode.  Native comparisons use
        /// jge, therefore the first kind wins ties.
        /// </summary>
        public static int DetermineDominantAttributeKind(GoodItem standardItem,
            TUserItem userItem, out int dominantScore)
        {
            dominantScore = 0;
            if (standardItem == null || userItem == null ||
                userItem.btValue == null || userItem.btValue.Length < 14)
                return 0;

            var mode = standardItem.StdMode;
            if (mode != 15 && (mode < 19 || mode > 24) && (mode < 26 || mode > 28))
                return 0;

            var effective = new TStdItem();
            standardItem.GetStandardItem(ref effective);
            standardItem.GetItemAddValue(userItem, ref effective);

            var dc = HUtil32.LoWord(effective.DC) + HUtil32.HiWord(effective.DC);
            var mc = HUtil32.LoWord(effective.MC) + HUtil32.HiWord(effective.MC);
            var sc = HUtil32.LoWord(effective.SC) + HUtil32.HiWord(effective.SC);
            var need = HUtil32.LoWord(effective.Need) + HUtil32.HiWord(effective.Need);

            if (dc >= mc && dc >= sc && dc >= need)
            {
                dominantScore = dc;
                return 1;
            }
            if (mc >= sc && mc >= need)
            {
                dominantScore = mc;
                return 2;
            }
            if (sc >= need)
            {
                dominantScore = sc;
                return 3;
            }

            dominantScore = need;
            return 4;
        }

        /// <summary>
        /// Executes the native command against the invoking player's bag.
        /// Invalid native records are ignored rather than being reshaped; valid
        /// records always retain all bytes outside the four proven fields.
        /// </summary>
        public static bool Execute(TPlayObject player, string rawItemId, string rawLevel)
        {
            if (player == null)
                return false;

            rawItemId ??= string.Empty;
            rawLevel ??= string.Empty;
            var itemId = ParseOrDefault(rawItemId, 0);
            var requestedLevel = ParseOrDefault(rawLevel, 1);
            if (requestedLevel < 1 || requestedLevel > 5)
            {
                player.SysMsg(LevelRangeMessage, MsgColor.Red, MsgType.Hint);
                return false;
            }

            // sub_73D028 scans only self.m_ItemList and compares item+0x20
            // (MakeIndex); equipped/hero lists are intentionally excluded.
            var item = player.FindClientItemIn(player.m_ItemList, itemId, true);
            if (item == null)
            {
                player.SysMsg(ItemNotFoundMessage, MsgColor.Red, MsgType.Hint);
                return false;
            }

            var userEngine = M2Share.UserEngine;
            var standardItem = userEngine?.GetStdItem(item.wIndex);
            if (standardItem == null || !IsDamageSharingSuitShape(standardItem.Shape))
            {
                player.SysMsg(ShapeRejectedMessage, MsgColor.Red, MsgType.Hint);
                return false;
            }

            var kind = DetermineDominantAttributeKind(standardItem, item, out _);
            if (kind < 1 || kind > 3)
            {
                player.SysMsg(NoProfessionFeatureMessage, MsgColor.Red, MsgType.Hint);
                return false;
            }

            // Native stores level-1 in the selected slot and in item+0x36
            // (TUserItem.btValue[12]).
            var nativeLevel = (byte)(requestedLevel - 1);
            item.btValue[kind + 1] = nativeLevel;
            item.btValue[12] = nativeLevel;
            SyncNativeRecord(item);

            // sub_73CBD0 sends SM_UPDATEITEM before the success SysMsg.
            player.SendUpdateItem(item);
            player.SysMsg(SuccessPrefix + rawItemId + SuccessLevelSeparator + rawLevel,
                MsgColor.Green, MsgType.Hint);
            return true;
        }

        public static int ParseOrDefault(string text, int defaultValue)
        {
            return PasApiBridge.TryParseNativeDelphiInteger(text, out var value)
                ? value
                : defaultValue;
        }

        private static void SyncNativeRecord(TUserItem item)
        {
            if (item.NativeRecord == null || item.NativeRecord.Length != 208)
                return;

            // NativeRecord starts at item+0x20; btValue starts at item+0x2A.
            item.NativeRecord[0x0C] = item.btValue[2];
            item.NativeRecord[0x0D] = item.btValue[3];
            item.NativeRecord[0x0E] = item.btValue[4];
            item.NativeRecord[0x16] = item.btValue[12];
        }
    }
}

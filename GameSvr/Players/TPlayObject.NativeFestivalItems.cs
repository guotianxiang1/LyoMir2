using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeBirthdayCakeUseAddress = 0x0078A1D8;
        private const int NativeBirthdayCakeFireDuration = 0x157C0;
        private const int NativeRabbitPrizeBroadcastAddress = 0x00789D40;
        private const int NativeRabbitPrizeUseGateAddress = 0x00787D20;
        private const int NativeMooncakeGiftAddress = 0x006DE234;
        private const int NativeGiftBoxOpenAddress = 0x006DD758;

        private const string NativeBirthdayCakeBroadcastFormat =
            "本月寿星{0}在{1}[{2},{3}]施放生日蛋糕，祝福如东海寿比南山！";
        private const string NativeBirthdayCakeSelfHint = "生日快乐";
        private const string NativeRabbitFortuneMasterMsg =
            "恭喜你财缘深厚，兔年里适合拜师学艺，抓紧时间找到一个师傅，共同在兔年闯江湖，大展宏图！";
        private const string NativeRabbitFortuneApprenticeMsg =
            "恭喜你福缘深厚，兔年里适合收徒交友，抓紧时间去收一个弟子吧，与徒弟、朋友一起在兔年开心游戏！";
        private const string NativeMooncakeNoSpouseMsg =
            "您还没有配偶，此月饼盒只能送给自己的爱人！";
        private const string NativeMooncakeBagEmptyMsg =
            "您的包裹中没有中秋礼品";
        private const string NativeMooncakeSpouseBusyMsg =
            "您的爱人目前不能接受中秋礼品";
        private const string NativeMooncakeSpouseOfflineFmt =
            " 不在附近，你的中秋礼品无法赠送！";
        private const string NativeMooncakeBroadcastPrefix = "\u0002";
        private const string NativeMooncakeBroadcastSuffix =
            " 月饼盒，期望与爱人共度中秋良宵！！！ 祝大家中秋快乐！";
        private const string NativeGiftSelfBlessingMsg =
            "衷心的祝福不是来自自己，而是来自朋友";
        private const string NativeGiftTargetOfflineFmt = "找不到{0}";
        private const string NativeGiftCannotSendFmt = "不能送给{0}";
        private const string NativeGiftBagMissingFmt = "您的包裹中没有{0}";
        private const string NativeGiftRefinedBroadcastFmt =
            "{0}向{1}献上了一份精致礼品，并献上了真挚的祝福，祝其身体健康，万事如意，生意兴隆，在新的一年中财源广进、宏图大展！";

        /// <summary>
        /// <c>TBirthdayCake.Use</c> = <c>sub_78A1D8</c> @0x0078A1D8.
        /// </summary>
        private bool UseNativeBirthdayCake(TUserItem item)
        {
            if (item == null || m_PEnvir == null || M2Share.EventManager == null)
                return false;

            var cakeEvent = new CakeFireEvent(m_PEnvir, m_nCurrX, m_nCurrY,
                NativeBirthdayCakeFireDuration);
            M2Share.EventManager.AddEvent(cakeEvent);

            var broadcast = string.Format(NativeBirthdayCakeBroadcastFormat,
                m_sCharName, m_PEnvir.sMapDesc, m_nCurrX, m_nCurrY);
            M2Share.UserEngine?.SendBroadCastMsg(broadcast, MsgType.Notice);
            SysMsg(NativeBirthdayCakeSelfHint, MsgColor.Red, MsgType.Hint);
            return true;
        }

        /// <summary>
        /// <c>TRabbitPrize.Use</c> wrapper <c>sub_789D40</c> @0x00789D40 after
        /// gate <c>sub_787D20</c> @0x00787D20.
        /// </summary>
        private bool UseNativeRabbitYearPrize(TUserItem item, GoodItem stdItem)
        {
            if (item == null || stdItem == null)
                return false;
            if (!TryNativeRabbitPrizeUseGate())
                return false;

            var level = m_Abil.Level;
            if (level >= M2Share.g_Config.nMasterOKLevel)
            {
                SysMsg(NativeRabbitFortuneApprenticeMsg, MsgColor.Blue, MsgType.Hint);
            }
            else if (level >= M2Share.g_Config.nMinMasterLevel)
            {
                SysMsg(NativeRabbitFortuneMasterMsg, MsgColor.Blue, MsgType.Hint);
            }
            else
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Simplified faithful subset of <c>sub_787D20</c>: requires a live player,
        /// non-death state, resolvable prize template and spare bag slot.
        /// </summary>
        private bool TryNativeRabbitPrizeUseGate()
        {
            if (m_boDeath || m_PEnvir == null)
                return false;
            if ((m_ItemList?.Count ?? int.MaxValue) >= 48)
                return false;
            return true;
        }

        /// <summary>
        /// <c>TCoupleFeastBox.Use</c> = <c>sub_6DE234</c> @0x006DE234.
        /// Shape 7..9 correspond to use-mode 1..3 (@0x006DE325 add edx,6).
        /// </summary>
        private bool UseNativeMooncakeGift(GoodItem stdItem, TUserItem item)
        {
            if (stdItem == null || item == null)
                return false;

            if (!m_boMarried || string.IsNullOrEmpty(m_sDearName))
            {
                SysMsg(NativeMooncakeNoSpouseMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            var spouse = M2Share.UserEngine?.GetPlayObject(m_sDearName);
            if (spouse == null || spouse.m_boGhost)
            {
                SysMsg("找不到" + m_sDearName, MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (spouse.m_boDeath || spouse.m_PEnvir != m_PEnvir)
            {
                SysMsg(NativeMooncakeSpouseOfflineFmt.TrimStart(),
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (!spouse.CanTakeBagItem())
            {
                SysMsg(NativeMooncakeSpouseBusyMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            var mode = stdItem.Shape >= 7 ? stdItem.Shape - 6 : 1;
            if (mode is < 1 or > 3)
                return false;

            // Reward delivery uses native table @0x007D3C1C indexed by mode
            // (sub_74DE54 @0x006DE341); table contents are not statically recovered
            // here, so only the broadcast half is ported.

            var broadcast = NativeMooncakeBroadcastPrefix + m_sCharName
                            + NativeMooncakeBroadcastSuffix;
            M2Share.UserEngine?.SendBroadCastMsg(broadcast, MsgType.Notice);
            spouse.SysMsg(NativeMooncakeBroadcastPrefix + m_sCharName
                          + NativeMooncakeBroadcastSuffix,
                MsgColor.Red, MsgType.Hint);
            return true;
        }

        /// <summary>
        /// Gift-box family <c>sub_6DD758</c> @0x006DD758. Item names select the
        /// native mode byte (cl @0x006DD76E): 1=惊喜/精致, 2=兔年福袋, 3=兔年红包,
        /// 4=龙年红包/礼包(精致).
        /// </summary>
        private bool UseNativeGiftBox(GoodItem stdItem, TUserItem item)
        {
            if (stdItem == null || item == null)
                return false;

            if (!TryResolveNativeGiftBoxMode(stdItem.Name, out var mode))
                return false;

            var targetName = m_sDearName;
            if (string.IsNullOrEmpty(targetName))
            {
                SysMsg(NativeGiftSelfBlessingMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (string.Equals(targetName, m_sCharName, StringComparison.Ordinal))
            {
                SysMsg(NativeGiftSelfBlessingMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            var target = M2Share.UserEngine?.GetPlayObject(targetName);
            if (target == null || target.m_boGhost)
            {
                SysMsg(string.Format(NativeGiftTargetOfflineFmt, targetName),
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (!target.CanTakeBagItem())
            {
                SysMsg(string.Format(NativeGiftCannotSendFmt, targetName),
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            // Item grant uses native lookup sub_74DE54 @0x006DDB49 keyed by mode;
            // only the broadcast (@0x006DDC37 call 0x005F701C) is wired here.

            var broadcast = string.Format(NativeGiftRefinedBroadcastFmt,
                m_sCharName, targetName);
            M2Share.UserEngine?.SendBroadCastMsg(broadcast, MsgType.Notice);
            target.SysMsg(broadcast, MsgColor.Red, MsgType.Hint);
            return true;
        }

        private static bool TryResolveNativeGiftBoxMode(string itemName, out int mode)
        {
            mode = 0;
            if (string.IsNullOrEmpty(itemName))
                return false;

            if (itemName.Contains("惊喜") || itemName.Contains("精致"))
            {
                mode = 1;
                return true;
            }
            if (itemName.Contains("兔年福袋"))
            {
                mode = 2;
                return true;
            }
            if (itemName.Contains("兔年红包"))
            {
                mode = 3;
                return true;
            }
            if (itemName.Contains("龙年红包") || itemName.Contains("龙年礼包"))
            {
                mode = 4;
                return true;
            }

            return false;
        }

        private bool CanTakeBagItem()
        {
            return (m_ItemList?.Count ?? int.MaxValue) < 48;
        }
    }
}

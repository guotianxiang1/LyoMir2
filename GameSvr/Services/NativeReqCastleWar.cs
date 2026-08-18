using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// PAS <c>ReqCastleWar</c> / TPlayer script API — native <c>sub_6CA6AC</c> @0x006CA6AC.
    /// Gates: guild master (sub_6C4D94), not castle owner guild (sub_65A01C),
    /// not already on attacker list (sub_65B658/sub_65A36C), both
    /// <see cref="M2Share.g_Config.sZumaPiece"/> and hardcoded 金砖 @0x006CA874 present;
    /// then sub_65A3B8 append + consume both items + SendDefMessage ident 0x2845.
    /// </summary>
    public static class NativeReqCastleWar
    {
        /// <summary>Native hardcoded GBK item name @0x006CA874 (len=4).</summary>
        public const string NativeGoldBrickItemName = "金砖";

        private const string NotGuildMasterMsg = "你不是行会会长，不可申请攻城";
        private const string CastleOwnerGuildMsg = "沙巴克行会不可申请攻城";
        private const string AlreadyAppliedMsg =
            "你的行会已经申请攻城战争，快去沙巴克城门口的老人那里看看吧。";
        private const string InsufficientMaterialMsg =
            "你的材料不足，申请攻城必须提交1个祖玛头像和1个金砖";

        public static bool TryApply(TPlayObject player, TUserCastle castle)
        {
            if (player == null || castle == null || player.m_MyGuild == null)
            {
                return false;
            }

            // 0x6CA6BD call sub_6C4D94
            if (!player.IsGuildMaster())
            {
                player.SysMsg(NotGuildMasterMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            // 0x6CA6E7 call sub_65A01C — defending guild cannot apply
            if (castle.IsMember(player))
            {
                player.SysMsg(CastleOwnerGuildMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            // 0x6CA74A call sub_65B658 — already listed
            if (castle.IsAttackerGuild(player.m_MyGuild))
            {
                player.SysMsg(AlreadyAppliedMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            var zumaItem = player.CheckItems(M2Share.g_Config.sZumaPiece);
            var goldBrickItem = player.CheckItems(NativeGoldBrickItemName);
            if (zumaItem == null || goldBrickItem == null)
            {
                player.SysMsg(InsufficientMaterialMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            // 0x65B6B1 call sub_65A3B8 before item consumption
            if (!castle.AddAttackerInfo(player.m_MyGuild))
            {
                player.SysMsg(AlreadyAppliedMsg, MsgColor.Red, MsgType.Hint);
                return false;
            }

            // 0x6CA766..0x6CA7BE sub_768BE0(dx=0xA) ×2 — delete both items
            player.SendDelItems(zumaItem);
            player.DelBagItem(zumaItem.MakeIndex, M2Share.g_Config.sZumaPiece);
            player.SendDelItems(goldBrickItem);
            player.DelBagItem(goldBrickItem.MakeIndex, NativeGoldBrickItemName);

            // 0x6CA80C mov cx,0x2845 / call sub_765E68
            player.SendDefMessage((short)Grobal2.RM_MENU_OK, 0, 0, 0, 0, "");
            return true;
        }
    }
}

using SystemModule;

namespace GameSvr
{
    
    
    
    
    public class TGuildOfficial : NormNpc
    {
        public TGuildOfficial() : base()
        {
            this.m_btRaceImg = Grobal2.RCC_MERCHANT;
            this.m_wAppr = 8;
        }

        public override void Click(TPlayObject PlayObject)
        {
            base.Click(PlayObject);
        }

        protected override void GetVariableText(TPlayObject PlayObject, ref string sMsg, string sVariable)
        {
            base.GetVariableText(PlayObject, ref sMsg, sVariable);
            if (sVariable == "$REQUESTCASTLELIST")
            {
                var sText = "";
                IList<string> List = new List<string>();
                M2Share.CastleManager.GetCastleNameList(List);
                for (var i = 0; i < List.Count; i++)
                {
                    sText = sText + format("<{0}/@requestcastlewarnow{1}> {2}", List[i], i.ToString(), sText);
                }
                sText = sText + "\\ \\";
                sMsg = this.ReplaceVariableText(sMsg, "<$REQUESTCASTLELIST>", sText);
            }
        }

        public override void Run()
        {
            if (M2Share.RandomNumber.Random(40) == 0)
            {
                this.TurnTo((byte)M2Share.RandomNumber.Random(8));
            }
            else
            {
                if (M2Share.RandomNumber.Random(30) == 0)
                {
                    this.SendRefMsg(Grobal2.RM_HIT, this.m_btDirection, this.m_nCurrX, this.m_nCurrY, 0, "");
                }
            }
            base.Run();
        }

        public override void UserSelect(TPlayObject PlayObject, string sData)
        {
            var sMsg = string.Empty;
            var sLabel = string.Empty;
            const string sExceptionMsg = "[Exception] TGuildOfficial::UserSelect... ";
            base.UserSelect(PlayObject, sData);
            try
            {
                if (sData != "" && sData[0] == '@')
                {
                    sMsg = HUtil32.GetValidStr3(sData, ref sLabel, "\r");
                    var boCanJmp = PlayObject.LableIsCanJmp(sLabel);
                    if (!boCanJmp)
                    {
                        return;
                    }
                    if (TryGotoPascalLabel(PlayObject, sLabel))
                    {
                        return;
                    }
                    if (string.Compare(sLabel, M2Share.sBUILDGUILDNOW, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        ReQuestBuildGuild(PlayObject, sMsg);
                    }
                    else if (string.Compare(sLabel, M2Share.sSCL_GUILDWAR, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        ReQuestGuildWar(PlayObject, sMsg);
                    }
                    else if (string.Compare(sLabel, M2Share.sDONATE, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        DoNate(PlayObject);
                    }
                    else if (HUtil32.CompareLStr(sLabel, M2Share.sREQUESTCASTLEWAR, M2Share.sREQUESTCASTLEWAR.Length))
                    {
                        ReQuestCastleWar(PlayObject, sLabel.Substring(M2Share.sREQUESTCASTLEWAR.Length, sLabel.Length - M2Share.sREQUESTCASTLEWAR.Length));
                    }
                    else if (string.Compare(sLabel, M2Share.sEXIT, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        PlayObject.SendMsg(this, Grobal2.RM_MERCHANTDLGCLOSE, 0, this.ObjectId, 0, 0, "");
                    }
                    else if (string.Compare(sLabel, M2Share.sBACK, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        if (PlayObject.m_sScriptGoBackLable == "")
                        {
                            PlayObject.m_sScriptGoBackLable = M2Share.sMAIN;
                        }
                        this.GotoLable(PlayObject, PlayObject.m_sScriptGoBackLable, false);
                    }
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
        }

        
        
        
        
        
        
        private int ReQuestBuildGuild(TPlayObject PlayObject, string sGuildName)
        {
            var result = 0;
            sGuildName = sGuildName.Trim();
            TUserItem UserItem = null;
            if (sGuildName == "")
            {
                result = -4;
            }
            if (PlayObject.m_MyGuild == null)
            {
                if (PlayObject.m_nGold >= M2Share.g_Config.nBuildGuildPrice)
                {
                    UserItem = PlayObject.CheckItems(M2Share.g_Config.sWomaHorn);
                    if (UserItem == null)
                    {
                        result = -3;// '你没有准备好需要的全部物品。'
                    }
                }
                else
                {
                    result = -2;// '缺少创建费用。'
                }
            }
            else
            {
                result = -1;// '您已经加入其它行会。'
            }
            if (result == 0)
            {
                if (M2Share.GuildManager.AddGuild(sGuildName, PlayObject.m_sCharName))
                {
                    M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_205, M2Share.nServerIndex, sGuildName + '/' + PlayObject.m_sCharName);
                    PlayObject.SendDelItems(UserItem);
                    PlayObject.DelBagItem(UserItem.MakeIndex, M2Share.g_Config.sWomaHorn);
                    PlayObject.DecGold(M2Share.g_Config.nBuildGuildPrice);
                    PlayObject.GoldChanged();
                    PlayObject.m_MyGuild = M2Share.GuildManager.MemberOfGuild(PlayObject.m_sCharName);
                    if (PlayObject.m_MyGuild != null)
                    {
                        PlayObject.m_sGuildRankName = PlayObject.m_MyGuild.GetRankName(PlayObject, ref PlayObject.m_nGuildRankNo);
                        this.RefShowName();
                    }
                }
                else
                {
                    result = -4;
                }
            }
            if (result >= 0)
            {
                PlayObject.SendMsg(this, Grobal2.RM_BUILDGUILD_OK, 0, 0, 0, 0, "");
            }
            else
            {
                PlayObject.SendMsg(this, Grobal2.RM_BUILDGUILD_FAIL, 0, result, 0, 0, "");
            }
            return result;
        }

        
        
        
        
        
        
        private void ReQuestGuildWar(TPlayObject PlayObject, string sGuildName)
        {
            // Gate 1: Target guild exists
            Association targetGuild = M2Share.GuildManager.FindGuild(sGuildName);
            if (targetGuild == null)
            {
                PlayObject.SysMsg("行会 " + sGuildName + " 不存在!!!", MsgColor.Red, MsgType.Hint);
                return;
            }

            // Gate 2: Check gold availability (but don't deduct yet)
            if (PlayObject.m_nGold < M2Share.g_Config.nGuildWarPrice)
            {
                PlayObject.SysMsg("你没有足够的金币!!!", MsgColor.Red, MsgType.Hint);
                return;
            }

            // Gate 3: Is guild master
            if (!PlayObject.IsGuildMaster())
            {
                PlayObject.SysMsg("只有行会掌门人才能申请!!!", MsgColor.Red, MsgType.Hint);
                return;
            }

            // Gate 4: Has guild (implicit in IsGuildMaster, but explicit check for clarity)
            if (PlayObject.m_MyGuild == null)
            {
                PlayObject.SysMsg("你还没有加入行会!!!", MsgColor.Red, MsgType.Hint);
                return;
            }

            // Gate 5: Not own guild
            if (PlayObject.m_MyGuild == targetGuild)
            {
                PlayObject.SysMsg("不能向自己的行会宣战!!!", MsgColor.Red, MsgType.Hint);
                return;
            }

            // Gate 6: ServerIndex check
            if (M2Share.nServerIndex != 0)
            {
                PlayObject.SysMsg("这个命令不能在本服务器上使用!!!", MsgColor.Red, MsgType.Hint);
                return;
            }

            // All gates passed - now deduct gold and process
            PlayObject.DecGold(M2Share.g_Config.nGuildWarPrice);
            PlayObject.GoldChanged();
            PlayObject.ReQuestGuildWar(sGuildName);
        }

        private void DoNate(TPlayObject PlayObject)
        {
            PlayObject.SendMsg(this, Grobal2.RM_DONATE_OK, 0, 0, 0, 0, "");
        }

        private void ReQuestCastleWar(TPlayObject PlayObject, string sIndex)
        {
            var nIndex = HUtil32.Str_ToInt(sIndex, -1);
            if (nIndex < 0)
            {
                nIndex = 0;
            }
            var Castle = M2Share.CastleManager.GetCastle(nIndex);
            if (PlayObject.IsGuildMaster() && !Castle.IsMember(PlayObject))
            {
                var zumaItem = PlayObject.CheckItems(M2Share.g_Config.sZumaPiece);
                var goldBrickItem = PlayObject.CheckItems(
                    Services.NativeReqCastleWar.NativeGoldBrickItemName);
                if (zumaItem != null && goldBrickItem != null)
                {
                    if (Castle.AddAttackerInfo(PlayObject.m_MyGuild))
                    {
                        PlayObject.SendDelItems(zumaItem);
                        PlayObject.DelBagItem(zumaItem.MakeIndex,
                            M2Share.g_Config.sZumaPiece);
                        PlayObject.SendDelItems(goldBrickItem);
                        PlayObject.DelBagItem(goldBrickItem.MakeIndex,
                            Services.NativeReqCastleWar.NativeGoldBrickItemName);
                        this.GotoLable(PlayObject, "~@request_ok", false);
                    }
                    else
                    {
                        PlayObject.SysMsg("你现在无法请求攻城!!!", MsgColor.Red, MsgType.Hint);
                    }
                }
                else
                {
                    PlayObject.SysMsg(
                        "你的材料不足，申请攻城必须提交1个祖玛头像和1个金砖",
                        MsgColor.Red, MsgType.Hint);
                }
            }
            else
            {
                PlayObject.SysMsg("你的请求被取消!!!", MsgColor.Red, MsgType.Hint);
            }
        }

        protected override void SendCustemMsg(TPlayObject PlayObject, string sMsg)
        {
            base.SendCustemMsg(PlayObject, sMsg);
        }
    }
}

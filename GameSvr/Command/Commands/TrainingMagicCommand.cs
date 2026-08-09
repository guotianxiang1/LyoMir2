using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @TrainingMagic 人物名称 技能名称 [等级] [经验]  (UpUserSkill dispatch id 218, case@0x00625F6A
    // -> shim sub_6C7644 -> worker sub_73F500). Reversed 1:1 from M2Server_reunpacked_20260803.i64
    // (staging/idat_R_ap_skillexp_reload_20260803.md §ITEM C).
    // Shim sub_6C7644: token3=StrToInt(level,1) guard>=0; token4=StrToInt(exp,0); resolve player by
    //   name; found -> sub_73F500 + SysMsg "改变角色技能等级命令被执行"(0xFFDB); not found ->
    //   "…不在线或不在本服务器"(0x38FF).
    // Worker sub_73F500: search the player's EXISTING m_MagicList([player+0x500]) by def name; MATCH ->
    //   sub_4C88EC SETs level = min(token3, def.MaxLevel[def+0x1A]) into [magic+0xC] (STORE, not add) +
    //   recompute exp-to-next; if token4>0 sub_4C8910 adds that exp; sub_765E68 sends the skill packet
    //   (RM_MAGIC_LVEXP) + [vtbl+0x8C] RecalcAbilitys. NO MATCH -> loop exhausts -> does NOTHING
    //   (never appends a new skill).
    // FIX (was DIVERGENT): the prior C# ADDED a brand-new TUserMagic (m_MagicList.Add + SendAddMagic)
    //   when the skill was absent — native never creates one. Now: find-existing -> SET clamped btLevel
    //   (clamp = MagicInfo.btTrainLv, the same def cap the verified sub_73F500 sibling ChgHeroSkill uses)
    //   + optional raw exp + SendAddMagic + RecalcAbilitys; absent -> no-op.
    [GameCommand("TrainingMagic", "调整指定玩家技能", "人物名称 技能名称 [等级] [经验]", 10)]
    public class TrainingMagicCommand : BaseCommond
    {
        [DefaultCommand]
        public void TrainingMagic(string[] @Params, TPlayObject PlayObject)
        {
            var sHumanName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            var sSkillName = @Params != null && @Params.Length > 1 ? @Params[1] : "";
            // native shim defaults: level(token3)=1, exp(token4)=0; guard token3>=0.
            var nLevel = @Params != null && @Params.Length > 2 ? HUtil32.Str_ToInt(@Params[2], 1) : 1;
            var nExp = @Params != null && @Params.Length > 3 ? HUtil32.Str_ToInt(@Params[3], 0) : 0;
            if (string.IsNullOrEmpty(sHumanName) || string.IsNullOrEmpty(sSkillName) || nLevel < 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumanName);
            if (m_PlayObject == null)
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumanName), MsgColor.Red, MsgType.Hint);
                return;
            }
            // sub_73F500: locate the named skill in the player's EXISTING magic list; if absent, no-op
            // (native does NOT append a new skill).
            var Magic = M2Share.UserEngine.FindMagic(sSkillName);
            if (Magic != null)
            {
                for (var i = 0; i < m_PlayObject.m_MagicList.Count; i++)
                {
                    var UserMagic = m_PlayObject.m_MagicList[i];
                    if (UserMagic != null && UserMagic.MagicInfo != null && UserMagic.MagicInfo.wMagicID == Magic.wMagicID)
                    {
                        // sub_4C88EC: btLevel = min(level, def.MaxLevel) — STORE, not add.
                        UserMagic.btLevel = (byte)System.Math.Min(nLevel, (int)UserMagic.MagicInfo.btTrainLv);
                        // token4 (exp) optional: native calls sub_4C8910 (raw add + cascade) only when >0.
                        if (nExp > 0)
                        {
                            UserMagic.nTranPoint += nExp;
                            while (m_PlayObject.CheckMagicLevelup(UserMagic))
                            {
                                // cascading auto level-up (native sub_4C8910 while-loop)
                            }
                        }
                        m_PlayObject.SendAddMagic(UserMagic);
                        m_PlayObject.RecalcAbilitys();
                        break;
                    }
                }
            }
            // shim reports execution once the player was resolved (native 0xFFDB), regardless of match.
            PlayObject.SysMsg("改变角色技能等级命令被执行", MsgColor.Green, MsgType.Hint);
        }
    }
}

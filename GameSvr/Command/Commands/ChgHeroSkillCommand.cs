using System;
using GameSvr.CommandSystem;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    // @ChgHeroSkill 人物名称 技能名称 等级  (HeroPet/SkillEquip family, dispatch idx 228, perm 5,
    // case@0x006261D8 -> core sub_6D2E08 -> hero ChgSkillLv sub_73F500). Reversed 1:1 from
    // M2Server_unpacked_fixed.exe (staging update_clothes_4637_ida_work/wf2_out.txt, 2026-08-02):
    //   sub_6D2E08(player, humName, skillName, level):
    //     humName / skillName empty            -> silent return (no core call)
    //     level = StrToInt(param3)             (missing/invalid -> 0; native does not gate on level)
    //     target = FindByName(humName); target null OR target ghost([+0x73]) -> SysMsg 0x38FF "<name> 不在线 或者不在本服务器"
    //     hero = target[+0x0BB0]; hero null OR hero ghost                     -> SysMsg 0x38FF "<name> 的英雄不在线"
    //     sub_73F500(hero, skillName, ToLv=level, skillExp=0): find the named magic in the hero's
    //       magic list [hero+0x500], set its level (clamped to train cap), RecalcAbilitys, push the
    //       level update (RM_MAGIC_LVEXP = 0x278B/10123); returns true. success -> confirmation SysMsg
    //       (0xFFDB); not found -> failure SysMsg (0x38FF).
    // C# reuse: TBaseObject.RecalcAbilitys/SendMsg + UserEngine.FindHeroMagic — the exact proven
    // pattern of HeroObject.UpgradeHeroMagic (RM_MAGIC_LVEXP arg order). Conservation-safe: only
    // mutates a skill level clamped to btTrainLv.
    [GameCommand("ChgHeroSkill", "调整英雄技能", "人物名称 技能名称 等级", 5)]
    public class ChgHeroSkillCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgHeroSkill(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            var sSkillName = @Params.Length > 1 ? @Params[1] : "";
            var nLevel = @Params.Length > 2 ? HUtil32.Str_ToInt(@Params[2], 0) : 0;
            // native: empty humName/skillName -> silent return. (C# shows help for GM convenience.)
            if (string.IsNullOrEmpty(sHumName) || string.IsNullOrEmpty(sSkillName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var target = M2Share.UserEngine.GetPlayObject(sHumName);
            if (target == null || target.m_boGhost)
            {
                PlayObject.SysMsg(sHumName + " 不在线 或者不在本服务器", MsgColor.Red, MsgType.Hint);
                return;
            }
            var hero = target.m_HeroObject;
            if (hero == null || hero.m_boGhost)
            {
                PlayObject.SysMsg(sHumName + " 的英雄不在线", MsgColor.Red, MsgType.Hint);
                return;
            }
            // sub_73F500: locate the named skill in the hero's magic list and set its level.
            var magic = M2Share.UserEngine.FindHeroMagic(sSkillName);
            if (magic != null && hero.m_HeroMagicList != null)
            {
                foreach (var userMagic in hero.m_HeroMagicList)
                {
                    if (userMagic.MagicInfo.wMagicID == magic.wMagicID)
                    {
                        userMagic.btLevel = Math.Min(unchecked((byte)nLevel), magic.btTrainLv);
                        hero.RecalcAbilitys();
                        hero.SendMsg(hero, Grobal2.RM_MAGIC_LVEXP, 0, magic.wMagicID,
                            userMagic.btLevel, userMagic.nTranPoint, string.Empty);
                        // sub_73F500 SysMsg is to the actor (hero) whose skill changed,
                        // not the GM. The GM confirmation below is the shim, unpatched.
                        if (!new YanshenApi(target, null, M2Share.PluginManager).IsUpSkillSilentPatchOn())
                            hero.SysMsg(sSkillName + " 技能等级变更为：" + userMagic.btLevel,
                                MsgColor.Green, MsgType.Hint);
                        PlayObject.SysMsg(
                            $"{sHumName} 的英雄技能 {sSkillName} 已调整为 {userMagic.btLevel} 级",
                            MsgColor.Green, MsgType.Hint);
                        return;
                    }
                }
            }
            // sub_73F500 returned false: the hero does not have this skill.
            PlayObject.SysMsg($"{sHumName} 的英雄没有技能 {sSkillName}", MsgColor.Red, MsgType.Hint);
        }
    }
}

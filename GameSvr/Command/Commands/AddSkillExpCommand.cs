using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @AddSkillExp 人物名称 技能名称 经验值 [英雄标志]  (dispatch id 312, case@0x006271C4).
    // Reversed 1:1 from M2Server_reunpacked_20260803.i64 (staging/idat_R_ap_skillexp_reload_20260803.md
    // §ITEM B). Case body args: name(var_34)/skill(var_38)/exp(var_3C→StrToInt)/heroFlag(var_40).
    // Gate: name+skill+exp present + player found. `cmp [var_40],0; jnz` -> heroFlag tests raw arg
    // presence (no StrToInt), so any supplied 4th token routes to the hero:
    //   hero branch: [player+0x0BB0]=m_HeroObject; null hero -> jz loc_62B64C (silent no-op);
    //                else sub_744D4C(hero,skill,exp).
    //   player branch: sub_744D4C(player,skill,exp).
    // Both branches jmp to the empty-exit -> NO success SysMsg.
    // Worker sub_744D4C(obj,skillName,exp): exp>0 required; iterate obj.m_MagicList([obj+0x500]) and
    //   match by def name; on match sub_4C8910 does [magic+0x10] += exp (RAW add, NO x3 fast-train)
    //   then cascading auto level-up (while expToNext<=exp); sub_744E88 client skill update; if the
    //   cascade leveled (code 3) -> [vtbl+0x8C] RecalcAbilitys. Skill not present -> does NOTHING.
    // C# reuse: GetPlayObject + m_MagicList / hero m_HeroMagicList(==m_MagicList) + FindMagic /
    //   FindHeroMagic + raw nTranPoint add (NOT TrainSkill — it x3's on m_boFastTrain) + a
    //   CheckMagicLevelup cascade loop (each level pushes RM_MAGIC_LVEXP).
    [GameCommand("AddSkillExp", "增加技能经验", "人物名称 技能名称 经验值 [英雄标志]", 5)]
    public class AddSkillExpCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddSkillExp(string[] @Params, TPlayObject PlayObject)
        {
            var sHumName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            var sSkillName = @Params != null && @Params.Length > 1 ? @Params[1] : "";
            var nExp = @Params != null && @Params.Length > 2 ? HUtil32.Str_ToInt(@Params[2], 0) : 0;
            var boHero = @Params != null && @Params.Length > 3 && !string.IsNullOrEmpty(@Params[3]);
            // native worker requires exp>0; case body requires name/skill/exp present. (help red for GM.)
            if (string.IsNullOrEmpty(sHumName) || string.IsNullOrEmpty(sSkillName) || nExp <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var target = M2Share.UserEngine.GetPlayObject(sHumName);
            if (target == null)
            {
                // native: player not found -> control never reaches the worker (silent).
                return;
            }
            if (boHero)
            {
                var hero = target.m_HeroObject;
                if (hero == null || hero.m_HeroMagicList == null)
                {
                    return; // [player+0x0BB0] null -> silent no-op
                }
                var heroDef = M2Share.UserEngine.FindHeroMagic(sSkillName);
                if (heroDef == null)
                {
                    return;
                }
                var heroMagic = FindUserMagic(hero.m_HeroMagicList, heroDef.wMagicID);
                if (heroMagic != null)
                {
                    ApplyRawSkillExp(hero, heroMagic, nExp);
                }
                return;
            }
            var def = M2Share.UserEngine.FindMagic(sSkillName);
            if (def == null)
            {
                return;
            }
            var userMagic = FindUserMagic(target.m_MagicList, def.wMagicID);
            if (userMagic != null)
            {
                ApplyRawSkillExp(target, userMagic, nExp);
            }
        }

        // Locate an EXISTING learned skill in the object's own magic list by magic id.
        private static TUserMagic FindUserMagic(System.Collections.Generic.IList<TUserMagic> list, int wMagicID)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var um = list[i];
                if (um != null && um.MagicInfo != null && um.MagicInfo.wMagicID == wMagicID)
                {
                    return um;
                }
            }
            return null;
        }

        // sub_744D4C worker core: RAW exp add + cascading auto level-up + conditional recalc.
        private static void ApplyRawSkillExp(TBaseObject owner, TUserMagic userMagic, int nExp)
        {
            // sub_4C8910: [magic+0x10] += exp (RAW; NOT TrainSkill, which multiplies by 3 on m_boFastTrain).
            userMagic.nTranPoint += nExp;
            var leveled = false;
            while (owner.CheckMagicLevelup(userMagic))
            {
                leveled = true; // each level pushes RM_MAGIC_LVEXP (native sub_744E88 client update)
            }
            if (leveled)
            {
                owner.RecalcAbilitys(); // native: lvlup-code==3 -> [vtbl+0x8C] RecalcAbilitys
            }
            else
            {
                // exp changed without a level-up: still push the client skill update (native sub_744E88).
                owner.SendDelayMsg(owner, Grobal2.RM_MAGIC_LVEXP, 0, userMagic.MagicInfo.wMagicID,
                    userMagic.btLevel, userMagic.nTranPoint, "", 1000);
            }
        }
    }
}

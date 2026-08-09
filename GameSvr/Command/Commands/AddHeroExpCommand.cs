using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 原版 @AddHeroExp (dispatch idx 493, perm 4, case@0x0062926B)。给目标玩家的英雄授予固定 1 亿超级经验。
    /// native: 守卫「角色名非空 AND 目标在线」→ sub_6E2CC0(target, 100000000) → sub_687714(hero, 1e8, cl, a4=1)；
    /// 否则静默 no-op(全程不发 SysMsg)。金额硬编码 1e8。directMode=true(a4=1)绕过英雄 200 级自然上限。
    /// 执行器 TPlayObject.GrantNativeHeroExperience 已忠实移植 sub_687714(溢出守卫/升级循环/GetLevelExp阈值/
    /// 200级cap/战魂累加器)并经 PasDispatchShadowCompatCheck 审计——与 3 个 PAS 英雄经验 API 同一执行器。
    /// (证据: staging/gm_hero_pet_commands_20260731.md:49 + NativeGmHeroPetCommands.cs
    ///  NativeGmAddHeroExp/AddHeroExpFixedAmount + ida_hero_intimacy_exact.txt sub_6E2CC0。)
    /// </summary>
    [GameCommand("AddHeroExp", "增加玩家的英雄经验100000000", "角色名", 4)]
    public class AddHeroExpCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddHeroExp(string[] @Params, TPlayObject PlayObject)
        {
            var sHumName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHumName))
            {
                return; // 原版: 角色名空 → 静默 no-op(无 SysMsg)
            }
            var target = M2Share.UserEngine.GetPlayObject(sHumName);
            if (target == null)
            {
                return; // 原版: 目标离线 → 静默 no-op
            }
            var hero = target.m_HeroObject;
            if (hero == null)
            {
                return; // 原版 sub_6E2CC0: 目标无英雄([target+0xBB0]==0) → 返回 0, 无 SysMsg
            }
            // countAsFightExperience=false(GM 直接授予, 非战斗经验); directMode=true(a4=1, 超级经验)。
            target.GrantNativeHeroExperience(hero,
                NativeGmHeroPetCommands.AddHeroExpFixedAmount, false, true);
        }
    }
}

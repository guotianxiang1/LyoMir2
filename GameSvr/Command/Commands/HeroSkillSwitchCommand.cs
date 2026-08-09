using GameSvr.CommandSystem;

namespace GameSvr
{
    // @HeroSkillSwitch — NO native counterpart in this M2Server baseline. The command-name string
    // 'HeroSKillSwitch' (0x007D2355) and its help string '(@HeroSkillSwitch ' (0x007D2374) are DEAD:
    // both have ZERO xrefs (idat 2026-08-02, staging update_clothes_4637_ida_work/wf2_out.txt), so no
    // typed-constant command record registers the name and sub_622820 has no dispatch case — the
    // command simply does not exist in the original. Native-absent → shielded fail-closed (matches the
    // CommandAuditCheck protected-command convention; an accurate reason, not a misleading "not ported").
    [GameCommand("HeroSkillSwitch", "英雄技能开关", "人物名称 开关(0/1)", 3)]
    public class HeroSkillSwitchCommand : BaseCommond
    {
        [DefaultCommand]
        public void HeroSkillSwitch(string[] @Params, TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "HeroSkillSwitch",
                "原版无此 GM 命令(命令名与帮助串均为死字符串、零 xref，不存在派发)。");
        }
    }
}

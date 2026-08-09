using GameSvr.CommandSystem;

namespace GameSvr
{
    [GameCommand("GuildWar", "", 10)]
    public class GuildWarCommand : BaseCommond
    {
        [DefaultCommand]
        public void GuildWar(TPlayObject PlayObject)
        {
            // 原版裸命令 @GuildWar 不在战神 430 行 GM 命令表(TABLE=0x7B4654, count=430, stride=0x78)中。
            // sub_621F28 精确名查表未命中 -> 返回 0 且 outReqLevel(var_61)=0 -> sub_622820 路由到
            // def_622B15 静默 sink(0x0062B648: mov [ebp+var_D],0 -> 清理 epilogue，不发任何消息)。
            // 表外 @命令 连 "该命令需要N级GM才能使用" 权限提示都不触发(该提示要求 var_61>0，即命令须在表中)，
            // 原版对未知 @命令 完全静默；此前 C# 的失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 子串陷阱：表中有 GuildWarOn(id116)/GuildWarOff(id117)/ReportGuildWar(id118) 及中文 '行会战争'(id32)，
            // 但 sub_621F28 是精确名查表，裸命令 'GuildWar' 不匹配上述任何项。
            // 证据: staging/idat_pass_C_refill_unkcmd_plumbing_20260803.md §Item D (D.1-D.5, Tier-1 反汇编);
            //       staging/ida_award_case584_command_registry_20260720.txt (430 行命令表内精确匹配 'GuildWar' 为零命中)。
        }
    }
}

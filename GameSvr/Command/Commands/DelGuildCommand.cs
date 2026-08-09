using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("DelGuild", "删除指定行会名称", help: "行会名称", 5)]
    public class DelGuildCommand : BaseCommond
    {
        // 原版 @DelGuild (idx214, perm5) 为静默 no-op：派发命中 empty-exit case (0x0062B64C)，
        // 不删除行会、不广播 SS_206、不回消息。行会的创建/删除仅走 CM_GILD_* 数据库路径，
        // 忠实的战神移植【不会】用 @DelGuild 删除行会。此前的 C# 实现 GuildManager.DelGuild +
        // 跨服 SS_206 广播既是破坏性的、也与原版发散，现改为纯 no-op。
        // 证据: staging/gm_overimpl_drift_20260801.md #1 (RISKY top; "native empty case");
        //       staging/gm_currency_commands_20260731.md no-op sink 表 (DelGuild→empty-exit 0x0062B64C)。
        [DefaultCommand]
        public void DelGuild(string[] @Params, TPlayObject PlayObject)
        {
            // 原版 empty-exit case 0x0062B64C 静默 no-op（不删行会/不广播 SS_206/不回消息）。见上方证据。
        }
    }
}
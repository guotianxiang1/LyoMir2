using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @GuildForbid (idx404, perm4)。
    /// 原版 case@0x00628978 并非真实处理器：无论参数如何，仅回一条字面量 "Invalid"
    /// (SysMsg, 0x38FF 错误色)且【不做任何状态变更】—— 是一个从未落地的占位。
    /// 故此处忠实照搬: 恒定回 "Invalid"，不改任何行会状态、不写服务器日志。
    /// (此前的 C# 存根走 NativeCommandFailure.Report 打中文"尚未移植"并写控制台日志，
    ///  行为同为 no-op，但消息文本与原版不符——现对齐为原版的 "Invalid"。)
    /// 证据: staging/gm_guild_castle_commands_20260731.md (GuildForbid 404 = SysMsg "Invalid", no effect, 0x38FF);
    ///       staging/gm_overimpl_drift_20260801.md Appendix A ("native sends 'Invalid'")。
    /// </summary>
    [GameCommand("GuildForbid", "行会禁止(封禁)", "行会名称 状态(0/1)", 4)]
    public class GuildForbidCommand : BaseCommond
    {
        [DefaultCommand]
        public void GuildForbid(string[] @Params, TPlayObject PlayObject)
        {
            // 原版恒定回 "Invalid" 且无副作用(0x38FF 错误色 → MsgColor.Red)。
            PlayObject.SysMsg("Invalid", MsgColor.Red, MsgType.Hint);
        }
    }
}

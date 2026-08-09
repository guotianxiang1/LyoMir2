using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command @GuildWarOff (idx117, perm4).
    /// 原版为 nullsub —— 什么都不做、不回消息。四个行会战 GM 动词
    /// (GuildPoint/GuildWarOn/GuildWarOff/ReportGuildWar, idx115/116/117/118) 在二进制里
    /// 全是 nullsub 桩；只有 C# 版给 GuildWarOff 加了真实的双行会停战逻辑，与原版发散。
    /// 现改为纯 no-op 以保持 1:1。行会战的真实起/停由行会协议路径处理，不经此 GM 动词。
    /// 证据: staging/gm_overimpl_drift_20260801.md #2 (RISKY; "nullsub (nothing, no msg)")。
    /// </summary>
    [GameCommand("GuildWarOff", "结束行会战", "行会名称1 行会名称2", 4)]
    public class GuildWarOffCommand : BaseCommond
    {
        [DefaultCommand]
        public void GuildWarOff(string[] @Params, TPlayObject PlayObject)
        {
            // 原版 nullsub（什么都不做、不回消息）。行会战真实起停走行会协议路径。见上方证据。
        }
    }
}

using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 原版 @ReloadQuest (idx150, perm4) 派发命中 def_622B15 静默 no-op —— 不重载、不回消息。
    // 此前的 C# 实现 PasEngine.ClearCache()+LoadMapQuestMap()+成功提示 是一个热重载增强，
    // 与原版发散。按严格 1:1 改为纯 no-op(此命令无数据风险，纯属去掉原版没有的开发期便利)。
    // 证据: staging/gm_overimpl_drift_20260801.md #5 ("def_622B15 silent no-op")。
    [GameCommand("ReloadQuest", "重新加载 Pascal 任务脚本", 4)]
    public class ReloadQuestCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadQuest(TPlayObject PlayObject)
        {
            // 原版 def_622B15 静默 no-op（不重载、不回消息）。见上方证据——去掉原版没有的热重载便利。
        }
    }
}

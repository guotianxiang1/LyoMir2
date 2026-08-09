using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command @CellInfo (idx474, perm4).
    /// 原版派发命中 def_622B15 静默 no-op —— 不回任何信息。此前的 C# 实现是只读的格子诊断
    /// (读取地图格属性/对象数/尺寸)，虽无状态风险，但原版并不响应。按严格 1:1 改为纯 no-op。
    /// 证据: staging/gm_overimpl_drift_20260801.md #6 ("def_622B15 silent no-op"; SAFE-to-noop)。
    /// </summary>
    [GameCommand("CellInfo", "显示地图格子信息", "[X坐标 Y坐标]", 4)]
    public class CellInfoCommand : BaseCommond
    {
        [DefaultCommand]
        public void CellInfo(string[] @Params, TPlayObject PlayObject)
        {
            // 原版 def_622B15 静默 no-op（不回任何信息）。见上方证据注释——刻意留空以保持 1:1。
        }
    }
}

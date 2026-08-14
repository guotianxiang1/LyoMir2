using SystemModule;

namespace GameSvr.Plugins
{
    /// <summary>
    /// YanshenApi 扩展方法占位符 - 需要从相关 commit 提取完整实现
    /// </summary>
    public static class YanshenApiExtensions
    {
        public static bool PatchToggleOn(this YanshenApi api, string patchName)
        {
            // TODO: 从相关 commit 提取
            return false;
        }

        public static bool TryGetDeathEquipDropPatch(this YanshenApi api, bool isRedName, out int patchedRate, out int patchedCap)
        {
            // TODO: 从相关 commit 提取
            patchedRate = 0;
            patchedCap = 0;
            return false;
        }
    }
}

using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 原版增加自身灵符命令。
    /// Usage: @AddLinFu 灵符数量
    /// </summary>
    [GameCommand("AddLinFu", "增加自身灵符数量", "灵符数量", 4)]
    public class AddLinFuCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddLinFu(string[] @Params, TPlayObject PlayObject)
        {
            var value = @Params != null && @Params.Length > 0
                ? HUtil32.Str_ToInt(@Params[0], 1)
                : 1;
            if (value < 1)
                value = 1;

            PlayObject.m_nLingFu = unchecked(PlayObject.m_nLingFu + value);
            PlayObject.RefreshNativeLingFu();
        }
    }
}

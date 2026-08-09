using GameSvr.CommandSystem;
using GameSvr.Services;

namespace GameSvr
{
    [GameCommand("ClearNickLinfu", "清除所有的圣殿灵符", "", 4)]
    public class ClearNickLinfuCommand : BaseCommond
    {
        [DefaultCommand]
        public void ClearNickLinfu(string[] @Params, TPlayObject PlayObject)
        {
            YbDbClient.Instance.RequestClearNickLinfu(PlayObject);
        }
    }
}

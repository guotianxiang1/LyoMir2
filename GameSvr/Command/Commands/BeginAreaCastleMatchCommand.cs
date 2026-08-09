using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("BeginAreaCastleMatch", "开始区域城堡比赛", "", 4)]
    public class BeginAreaCastleMatchCommand : BaseCommond
    {
        [DefaultCommand]
        public void BeginAreaCastleMatch(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "BeginAreaCastleMatch",
                "原版沙巴克积分赛状态机尚未移植，未开启比赛。");
        }
    }
}

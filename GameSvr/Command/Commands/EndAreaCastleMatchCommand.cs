using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("EndAreaCastleMatch", "结束区域城堡比赛", "", 4)]
    public class EndAreaCastleMatchCommand : BaseCommond
    {
        [DefaultCommand]
        public void EndAreaCastleMatch(TPlayObject PlayObject)
        {
            NativeCommandFailure.Report(PlayObject, "EndAreaCastleMatch",
                "原版沙巴克积分赛结算状态机尚未移植，未结束比赛。");
        }
    }
}

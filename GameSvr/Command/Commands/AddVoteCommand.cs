using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("AddVote", "增加投票数", "人物名称 票数", 5)]
    public class AddVoteCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddVote(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            var nVote = @Params.Length > 1 ? HUtil32.Str_ToInt(@Params[1], 0) : 0;
            if (string.IsNullOrEmpty(sHumName) || nVote <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            NativeCommandFailure.Report(PlayObject, "AddVote",
                "原版主宰者投票事务尚未移植，未修改投票数。");
        }
    }
}

using GameSvr.CommandSystem;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SignInAct", "开启/关闭抽奖活动",
        "[开启活动|关闭活动]", 4)]
    public sealed class SignInActCommand : BaseCommond
    {
        [DefaultCommand]
        public void SignInAct(string[] parameters, TPlayObject playObject)
        {
            if (parameters == null || parameters.Length != 1 ||
                M2Share.SignActManager == null)
            {
                Send(playObject, "格式：SignInAct [开启活动,关闭活动]",
                    0xFF, 0x38);
                return;
            }

            string message;
            if (string.Equals(parameters[0], "开启活动",
                    StringComparison.OrdinalIgnoreCase))
            {
                message = M2Share.SignActManager.OpenActivity()
                    ? "清除数据成功"
                    : "清除数据失败";
            }
            else if (string.Equals(parameters[0], "关闭活动",
                         StringComparison.OrdinalIgnoreCase))
            {
                message = M2Share.SignActManager.CloseActivity() switch
                {
                    NativeSignActDrawResult.AlreadyDrawn => "已经开过奖了",
                    NativeSignActDrawResult.NoWinners => "没有人中奖",
                    NativeSignActDrawResult.Success => "成功开奖",
                    _ => "开奖更新sql失败"
                };
            }
            else
            {
                Send(playObject, "格式：SignInAct [开启活动,关闭活动]",
                    0xFF, 0x38);
                return;
            }

            Send(playObject, message, 0xDB, 0xFF);
        }

        private static void Send(TPlayObject player, string message,
            int foreground, int background)
        {
            player?.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                foreground, background, 0, message);
        }
    }
}

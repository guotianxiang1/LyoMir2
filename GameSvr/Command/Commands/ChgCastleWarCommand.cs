using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to start a castle siege war.
    /// Usage: @ChgCastleWar CastleName
    /// Calls CastleManager to find the castle and start the wall conquest war.
    /// </summary>
    [GameCommand("ChgCastleWar", "开启攻城战役", "城堡名称", 5)]
    public class ChgCastleWarCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgCastleWar(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sCASTLENAME = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sCASTLENAME))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var Castle = M2Share.CastleManager.Find(sCASTLENAME);
            if (Castle != null)
            {
                Castle.StartWallconquestWar();
                M2Share.MainOutMessage($"[攻城战] GM {PlayObject.m_sCharName} 开启了 {sCASTLENAME} 的攻城战");
                PlayObject.SysMsg($"[{sCASTLENAME} 攻城战已开启]", MsgColor.Green, MsgType.Hint);
            }
            else
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandSbkGoldCastleNotFoundMsg, sCASTLENAME), MsgColor.Red, MsgType.Hint);
            }
        }
    }
}

using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to end a castle siege war.
    /// Usage: @CastleWarEnd CastleName
    /// Complement to ForcedWallconquestWar. Calls StopWallconquestWar() on the specified castle.
    /// </summary>
    [GameCommand("CastleWarEnd", "结束攻城战役", "城堡名称", 10)]
    public class CastleWarEndCommand : BaseCommond
    {
        [DefaultCommand]
        public void CastleWarEnd(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sCASTLENAME = @Params.Length > 0 ? @Params[0] : "";
            if (sCASTLENAME == "")
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var Castle = M2Share.CastleManager.Find(sCASTLENAME);
            if (Castle != null)
            {
                if (Castle.m_boUnderWar)
                {
                    Castle.StopWallconquestWar();
                    M2Share.MainOutMessage($"[攻城战] GM {PlayObject.m_sCharName} 强制结束了 {sCASTLENAME} 的攻城战");
                    PlayObject.SysMsg($"[{sCASTLENAME} 攻城战已强制结束]", MsgColor.Green, MsgType.Hint);
                }
                else
                {
                    PlayObject.SysMsg($"[{sCASTLENAME}] 当前没有在进行攻城战。", MsgColor.Red, MsgType.Hint);
                }
            }
            else
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandSbkGoldCastleNotFoundMsg, sCASTLENAME), MsgColor.Red, MsgType.Hint);
            }
        }
    }
}

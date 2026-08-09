using GameSvr.CommandSystem;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("Rest", "调整当前玩家属下状态", 0)]
    public class ChangeSalveStatusCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChangeSalveStatus(TPlayObject PlayObject)
        {
            if (!PlayObject.m_boSlaveRelax &&
                new YanshenApi(PlayObject, null, M2Share.PluginManager).IsPetRestBlocked())
            {
                return;
            }

            PlayObject.m_boSlaveRelax = !PlayObject.m_boSlaveRelax;
            if (PlayObject.m_SlaveList.Count > 0)
            {
                if (PlayObject.m_boSlaveRelax)
                {
                    PlayObject.SysMsg(M2Share.sPetRest, MsgColor.Green, MsgType.Hint);
                }
                else
                {
                    PlayObject.SysMsg(M2Share.sPetAttack, MsgColor.Green, MsgType.Hint);
                }
            }
        }
    }
}

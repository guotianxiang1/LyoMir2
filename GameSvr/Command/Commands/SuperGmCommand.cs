using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to elevate self to super GM mode.
    /// Usage: @SuperGm
    /// Sets maximum permission level (10), enables superman mode, observer mode, etc.
    /// </summary>
    [GameCommand("SuperGm", "进入超级GM模式(最高权限+无敌+隐身)", 4)]
    public class SuperGmCommand : BaseCommond
    {
        [DefaultCommand]
        public void SuperGm(TPlayObject PlayObject)
        {
            PlayObject.m_btPermission = 10;
            PlayObject.m_boSuperMan = true;
            PlayObject.m_boObMode = true;
            PlayObject.m_boAdminMode = true;
            PlayObject.SysMsg("===== 超级GM模式已激活 =====", MsgColor.Green, MsgType.Hint);
            PlayObject.SysMsg("权限等级: 10 (最高)", MsgColor.Green, MsgType.Hint);
            PlayObject.SysMsg("无敌模式: 已开启", MsgColor.Green, MsgType.Hint);
            PlayObject.SysMsg("隐身模式: 已开启", MsgColor.Green, MsgType.Hint);
            PlayObject.SysMsg("管理员模式: 已开启", MsgColor.Green, MsgType.Hint);
            PlayObject.SysMsg("===============================", MsgColor.Green, MsgType.Hint);
            M2Share.MainOutMessage("[超级GM] " + PlayObject.m_sCharName + " 启用了超级GM模式");
        }
    }
}

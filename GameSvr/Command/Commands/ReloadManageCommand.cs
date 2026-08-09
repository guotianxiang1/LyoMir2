using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadManage", "重新加载脚本", 10)]
    public class ReloadManageCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadManage(TPlayObject PlayObject)
        {
            if (M2Share.PasEngine == null)
            {
                PlayObject.SysMsg("Pascal 脚本引擎未初始化。", MsgColor.Red, MsgType.Hint);
                return;
            }

            M2Share.PasEngine.ClearCache();
            M2Share.PasEngine.LoadNpcScriptMap();
            M2Share.PasEngine.LoadMapQuestMap();
            PlayObject.SysMsg("Pascal 脚本与映射已重新加载。", MsgColor.Green, MsgType.Hint);
        }
    }
}

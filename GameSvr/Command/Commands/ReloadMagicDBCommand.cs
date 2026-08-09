using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr.Command.Commands
{
    
    
    
    [GameCommand("ReloadMagicDB", "重新加载技能数据库", 10)]

    public class ReloadMagicDBCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadMonItems(TPlayObject PlayObject)
        {
            PlayObject.SysMsg(
                "原生人物/英雄技能定义只在启动时发布，拒绝运行期替换。",
                MsgColor.Red, MsgType.Hint);
        }
    }
}

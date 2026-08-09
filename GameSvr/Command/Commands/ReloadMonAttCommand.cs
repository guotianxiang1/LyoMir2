using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to reload monster attribute database.
    /// Usage: @ReloadMonAtt
    /// Calls CommonDB.LoadMonsterDB() to reload monster stats and attributes.
    /// </summary>
    [GameCommand("ReloadMonAtt", "重新加载怪物属性数据库", 4)]
    public class ReloadMonAttCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadMonAtt(TPlayObject PlayObject)
        {
            var nCode = M2Share.CommonDB.LoadMonsterDB();
            if (nCode >= 0)
            {
                PlayObject.SysMsg("怪物属性数据库重新加载完成。", MsgColor.Green, MsgType.Hint);
                M2Share.MainOutMessage($"[重新加载] 怪物属性数据库已由 GM {PlayObject.m_sCharName} 重新加载。");
            }
            else
            {
                PlayObject.SysMsg($"怪物属性数据库重新加载失败，错误码: {nCode}", MsgColor.Red, MsgType.Hint);
            }
        }
    }
}

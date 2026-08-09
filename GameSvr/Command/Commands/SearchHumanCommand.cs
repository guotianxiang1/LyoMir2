using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("SearchHuman", "搜索指定玩家所在地图XY坐标", "人物名称", 0)]
    public class SearchHumanCommand : BaseCommond
    {
        [DefaultCommand]
        public void SearchHuman(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || PlayObject == null)
                return;

            var sHumanName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHumanName) ||
                !PlayObject.m_boProbeNecklace && PlayObject.m_btPermission < 3)
                return;

            var currentTick = HUtil32.GetTickCount();
            if (!HasNativeSearchCooldownElapsed(currentTick,
                    PlayObject.m_dwProbeTick))
                return;

            PlayObject.m_dwProbeTick = currentTick;
            var target = M2Share.UserEngine.GetPlayObjectEx(sHumanName);
            if (target?.m_boGhost == true || target?.m_boReadyRun != true)
                target = null;
            string result;
            if (target == null)
            {
                result = "探测项链无法查出 " + sHumanName + " 所在的位置";
            }
            else if (target.m_PEnvir == PlayObject.m_PEnvir)
            {
                result = sHumanName + " 在本地图：" + target.m_nCurrX + ',' +
                    target.m_nCurrY + " 的位置上";
            }
            else
            {
                result = sHumanName + " 在其他地图上";
            }

            PlayObject.SendMsg(PlayObject, Grobal2.RM_SYSMESSAGE, 0,
                0xDB, 0xFF, 0, result);
        }

        internal static bool HasNativeSearchCooldownElapsed(int currentTick,
            int previousTick) =>
            unchecked((uint)(currentTick - previousTick)) > 10000U;
    }
}

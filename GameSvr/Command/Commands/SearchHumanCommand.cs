using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007B66F4 `09 "Searching"`，+0x18 = 30，+0x1C = 0。
    // jt[30] @0x00622B94 = 4e 3b 62 00 -> 0x00623B4E：
    //   0x00623B51 cmp byte [eax+0x1C3],0 / jne 0x00623B6A   ; 戴探测项链则直接放行
    //   0x00623B5D cmp byte [eax+0x675],3 / jb  0x0062B64C   ; 否则权限 < 3 静默退出
    //   0x00623B70 call 0x006CE56C
    // 旧命令名 SearchHuman 在全镜像三编码 0 命中；它此前靠 CommandManager 的传统
    // GOM 别名表映射成 Searching，而本 build 没有那张配置面。
    [GameCommand("Searching", "使用探测项链探测指定玩家角色的位置坐标(GMLevel >= 3)",
        "角色名", 0)]
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

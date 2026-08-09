using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("AddRebotsPlay", "增加机器人玩家", "数量", 10)]
    public class CreateAIUserCommand : BaseCommond
    {
        private const int MaxRobots = 500;
        private const int MaxPerRequest = 100;

        [DefaultCommand]
        public void AddAIUser(string[] @params, TPlayObject PlayObject)
        {
            if (@params == null || @params.Length != 1 ||
                !int.TryParse(@params[0], out var userCount) ||
                userCount <= 0 || userCount > MaxPerRequest)
            {
                PlayObject.SysMsg($"数量必须在 1 到 {MaxPerRequest} 之间。", MsgColor.Red, MsgType.Hint);
                return;
            }

            if (M2Share.UserEngine.RobotPopulation + userCount > MaxRobots)
            {
                PlayObject.SysMsg($"机器人总数不能超过 {MaxRobots}。", MsgColor.Red, MsgType.Hint);
                return;
            }
            for (var i = 0; i < userCount; i++)
            {
                short nX = 0;
                short nY = 0;
                var sMapName = M2Share.UserEngine.GetHomeInfo(ref nX, ref nY);
                M2Share.UserEngine.AddAILogon(new TAILogon()
                {
                    sCharName = "玩家" + RandomNumber.GetInstance().Random() + "号",
                    sConfigFileName = "",
                    sHeroConfigFileName = "",
                    sFilePath = M2Share.g_Config.sEnvirDir,
                    sConfigListFileName = M2Share.g_Config.sAIConfigListFileName,
                    sHeroConfigListFileName = M2Share.g_Config.sHeroAIConfigListFileName,
                    sMapName = sMapName,
                    nX = nX,
                    nY = nY
                });
            }
            if (userCount > 0)
            {
                M2Share.UserEngine.StartAI();
            }
        }
    }
}

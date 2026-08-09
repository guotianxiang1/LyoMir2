using GameSvr.CommandSystem;

namespace GameSvr.Command
{
    [GameCommand("ClearItemMap", "清除指定地图范围物品", "地图编号", 10)]
    public class ClearItemMapCommand : BaseCommond
    {
        [DefaultCommand]
        public void ClearItemMap(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sMap = @Params.Length > 0 ? @Params[0] : "";
            var sItemName = @Params.Length > 1 ? @Params[1] : "";
            var nX = @Params.Length > 2 ? Convert.ToInt32(@Params[2]) : 0;
            var nY = @Params.Length > 3 ? Convert.ToInt32(@Params[3]) : 0;
            var nRange = @Params.Length > 4 ? Convert.ToInt32(@Params[4]) : 0;
            if (sMap == "" || string.IsNullOrEmpty(sItemName) || nX < 0 || nY < 0 || nRange < 0 || !string.IsNullOrEmpty(sItemName) && sItemName[0] == '?')
            {
                
                return;
            }
            if (sItemName == "ALL")
            {
            }
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
        }
    }
}
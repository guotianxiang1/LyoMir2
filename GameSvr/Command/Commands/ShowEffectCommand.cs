using GameSvr.CommandSystem;

namespace GameSvr
{
    
    
    
    [GameCommand("ShowEffect", "播放特效", 10)]
    public class ShowEffectCommand : BaseCommond
    {
        [DefaultCommand]
        public void ShowEffect(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sEffect = @Params.Length > 0 ? @Params[0] : "";
            var sTime = @Params.Length > 1 ? @Params[1] : "";
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
            
        }
    }
}
using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>TMonSupport toggle wrapper sub_67D3DC.</summary>
    [GameCommand("ChgMonAtt", "启停怪物攻城", 4)]
    public class ChgMonAttCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgMonAtt(TPlayObject playObject)
        {
            var text = M2Share.UserEngine.ToggleNativeMonSupport();
            playObject.SendMsg(playObject, Grobal2.RM_SYSMESSAGE, 0,
                0xFF, 0x38, 0, text);
        }
    }
}

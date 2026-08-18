using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// TMonSupport reload wrapper sub_67D484.
    /// Usage: @ReloadMonAtt
    /// Reloads Share/Config/Thousand_mon.ini and reports its native status.
    /// </summary>
    [GameCommand("ReloadMonAtt", "重载怪物攻城配置", 4)]
    public class ReloadMonAttCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadMonAtt(TPlayObject PlayObject)
        {
            var text = M2Share.UserEngine.ReloadNativeMonSupport();
            PlayObject.SendMsg(PlayObject, Grobal2.RM_SYSMESSAGE, 0,
                0xFF, 0x38, 0, text);
        }
    }
}

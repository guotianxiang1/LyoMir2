using System.Globalization;
using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SetNickLF", "", "倍率", 4)]
    public sealed class SetNickLFCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetNickLF(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length != 1 ||
                !int.TryParse(@Params[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var multiplier) ||
                multiplier is < 1 or > 10)
            {
                PlayObject.SysMsg("必须将圣殿灵符的倍率设置在1-10之间！",
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            var shareDirectory = Path.GetFullPath(Path.Combine(M2Share.sRootPath,
                M2Share.g_Config.sBaseDir));
            if (!NativeNickLinFuState.TryEnableAndPersist(shareDirectory,
                multiplier, ref M2Share.NickLinFuState, out _))
            {
                return;
            }

            M2Share.UserEngine.SendServerGroupMsg(Grobal2.ISM_SETNICKLF, 0,
                multiplier.ToString(CultureInfo.InvariantCulture));
            PlayObject.SysMsg("圣殿灵符倍率成功设置成为" +
                multiplier.ToString(CultureInfo.InvariantCulture) + "倍！",
                MsgColor.Red, MsgType.Hint);
        }
    }
}

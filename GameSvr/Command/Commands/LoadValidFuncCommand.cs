using GameSvr.CommandSystem;
using GameSvr.PasEngine;
using SystemModule;

namespace GameSvr
{
    [GameCommand("LoadValidFunc", "重载脚本安全函数列表validScriptFunc.txt", "", 4)]
    public class LoadValidFuncCommand : BaseCommond
    {
        private const ushort SuccessColorWord = 0xFFDB;
        private const ushort FailureColorWord = 0x38FF;

        [DefaultCommand]
        public void LoadValidFunc(TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var count = ReloadValidScriptFunctions();
            if (count < 0)
            {
                SendNativeSysMsg(PlayObject,
                    "载入脚本安全函数列表失败", FailureColorWord);
                return;
            }

            SendNativeSysMsg(PlayObject,
                "载入脚本安全函数列表成功，共" + count + "个函数",
                SuccessColorWord);
        }

        private static int ReloadValidScriptFunctions() =>
            ReloadValidScriptFunctions(M2Share.sConfigPath);

        private static int ReloadValidScriptFunctions(string configPath)
        {
            return NativeValidScriptFunctionRegistry.Reload(configPath);
        }

        private static void SendNativeSysMsg(TPlayObject player,
            string message, ushort colorWord)
        {
            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                colorWord & 0xFF, colorWord >> 8, 0, message);
        }
    }
}

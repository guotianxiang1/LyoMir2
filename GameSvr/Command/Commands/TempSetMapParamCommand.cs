using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("TempSetMapParam", "设置地图属性",
        "地图代号 参数", 5)]
    public sealed class TempSetMapParamCommand : BaseCommond
    {
        private const int Success = 1;
        private const int UnsupportedAttribute = 100;
        private const string NativeHelp =
            "命令格式：@TempSetMapParam 地图名 属性 [1|0] " +
            "1表示增加属性，0表示取消属性";

        [DefaultCommand]
        public void TempSetMapParam(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length < 3 ||
                string.IsNullOrEmpty(@Params[0]) ||
                string.IsNullOrEmpty(@Params[1]) ||
                string.IsNullOrEmpty(@Params[2]))
            {
                PlayObject.SysMsg(NativeHelp, MsgColor.Blue, MsgType.Hint);
                return;
            }

            var mapName = @Params[0];
            var attribute = @Params[1];
            var state = HUtil32.Str_ToInt(@Params[2], 0);
            var environment = M2Share.MapManager.FindMap(mapName);
            if (environment == null)
            {
                PlayObject.SysMsg("没找到地图 " + mapName, MsgColor.Red,
                    MsgType.Hint);
                return;
            }

            var result = ApplyPickupAttribute(environment, attribute, state);
            if (result == Success)
            {
                var operation = state == 1 ? "增加地图属性=" : "取消地图属性=";
                PlayObject.SysMsg(operation + attribute + "，操作成功",
                    MsgColor.Blue, MsgType.Hint);
            }
            else if (result == UnsupportedAttribute)
            {
                PlayObject.SysMsg("该GM命令目前不支持此地图属性=" + attribute,
                    MsgColor.Red, MsgType.Hint);
            }
            else if (state == 0 || state == 1)
            {
                var operation = state == 1 ? "增加地图属性=" : "取消地图属性=";
                PlayObject.SysMsg(operation + attribute + "，操作失败",
                    MsgColor.Red, MsgType.Hint);
            }
        }

        internal static int ApplyPickupAttribute(Envirnoment environment,
            string attribute, int state)
        {
            if (environment?.Flag == null || string.IsNullOrEmpty(attribute) ||
                unchecked((uint)state) >= 2)
                return 0;

            if (!string.Equals(attribute, "pickup",
                    StringComparison.OrdinalIgnoreCase))
                return UnsupportedAttribute;

            environment.Flag.boPICKUP = state == 1;
            return Success;
        }
    }
}

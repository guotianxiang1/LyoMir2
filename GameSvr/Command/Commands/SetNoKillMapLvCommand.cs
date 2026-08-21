using GameSvr.CommandSystem;
using GameSvr.PasEngine;
using GameSvr.Plugins;
using System.Globalization;
using System.Text.Json;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to set the no-kill map level threshold.
    /// Usage: @SetNoKillMapLv Level
    /// </summary>
    [GameCommand("SetNoKillMapLv", "设置安全地图等级上限", "等级", 5)]
    public class SetNoKillMapLvCommand : BaseCommond
    {
        public override string Handle(string parameters, TPlayObject playObject = null)
        {
            if (playObject == null || playObject.m_btPermission >= GameCommand.nPermissionMin)
                return base.Handle(parameters, playObject);

            if (!playObject.m_boDeath || !TryParseId(parameters, out var id) ||
                !IsYanshenCallbackEnabled())
                return M2Share.g_sGameCommandPermissionTooLow;

            var scriptHost = M2Share.PasEngine;
            var scriptPath = scriptHost?.FindScriptFile("RunQuest");
            if (scriptPath == null ||
                !scriptHost.TryCallProcedure(scriptPath, "SetNoKillMapLv", playObject, null,
                    PasValue.FromInt(id)))
                return "复活脚本未配置或执行失败。";

            return string.Empty;
        }

        [DefaultCommand]
        public void SetNoKillMapLv(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject?.m_PEnvir?.Flag == null || @Params == null ||
                @Params.Length == 0 ||
                !PasApiBridge.TryParseNativeDelphiInteger(@Params[0], out var level))
                return;

            var mapFlag = PlayObject.m_PEnvir.Flag;
            if (!mapFlag.boUserNoKill)
            {
                PlayObject.SysMsg("该地图无法设定此命令", MsgColor.Green, MsgType.Hint);
                return;
            }

            mapFlag.UserNoKillLevelCap = unchecked((ushort)level);
            PlayObject.SysMsg(
                $"已成功设定等级上限为{mapFlag.UserNoKillLevelCap}级",
                MsgColor.Green, MsgType.Hint);
        }

        private static bool TryParseId(string parameters, out int id)
        {
            return int.TryParse(parameters, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        private static bool IsYanshenCallbackEnabled()
        {
            var manager = M2Share.PluginManager;
            var plugin = manager?.GetPlugin("YanshenCompat");
            if (plugin?.State != PluginState.Running || !plugin.IsInitialized)
                return false;

            var value = manager.GetNativeConfigValue("SetNoKillMapLv脚本触发");
            return value switch
            {
                bool enabled => enabled,
                byte number => number != 0,
                short number => number != 0,
                int number => number != 0,
                long number => number != 0,
                float number => number != 0,
                double number => number != 0,
                decimal number => number != 0,
                string text => double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var number) && number != 0,
                JsonElement json when json.ValueKind == JsonValueKind.True => true,
                JsonElement json when json.ValueKind == JsonValueKind.Number =>
                    json.TryGetDouble(out var number) && number != 0,
                JsonElement json when json.ValueKind == JsonValueKind.String =>
                    double.TryParse(json.GetString(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var number) && number != 0,
                _ => false
            };
        }
    }
}

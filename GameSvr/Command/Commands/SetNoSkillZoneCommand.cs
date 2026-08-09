using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SetNoSkillZone", "设置地图点能否使用技能",
        "left right top bot on/off", 5)]
    public class SetNoSkillZoneCommand : BaseCommond
    {
        private const string NativeUsage =
            "格式：SetNoSkillZone: left right top bot on/off (on表示禁止攻击)";

        [DefaultCommand]
        public void SetNoSkillZone(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var parameters = @Params == null
                ? Array.Empty<string>()
                : string.Join(' ', @Params).Split(new[] { ' ', ',', ':' },
                    StringSplitOptions.RemoveEmptyEntries);
            var left = ParseCoordinate(parameters, 0);
            var right = ParseCoordinate(parameters, 1);
            var top = ParseCoordinate(parameters, 2);
            var bottom = ParseCoordinate(parameters, 3);
            var mode = parameters.Length > 4 ? parameters[4] : string.Empty;
            var isOn = mode.Equals("on", StringComparison.OrdinalIgnoreCase);
            var isOff = mode.Equals("off", StringComparison.OrdinalIgnoreCase);

            if (PlayObject.m_PEnvir == null || left < 0 || right < 0 ||
                top < 0 || bottom < 0 || (!isOn && !isOff))
            {
                PlayObject.SysMsg(NativeUsage, MsgColor.Green, MsgType.Hint);
                return;
            }

            PlayObject.m_PEnvir.SetMapCellSkillFlag(left, right, top, bottom,
                isOn ? (byte)1 : (byte)0);
            PlayObject.SysMsg(isOn
                    ? "已设置该区域为不可使用技能状态"
                    : "该区域可以继续使用技能",
                MsgColor.Green, MsgType.Hint);
        }

        private static int ParseCoordinate(IReadOnlyList<string> parameters,
            int index)
        {
            return index < parameters.Count &&
                   int.TryParse(parameters[index], out var value)
                ? value
                : -1;
        }
    }
}

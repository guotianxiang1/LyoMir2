using SystemModule;

namespace GameSvr;

internal static class NativeCommandFailure
{
    public static void Report(TPlayObject playObject, string command, string reason)
    {
        var operatorName = playObject?.m_sCharName ?? "<server>";
        M2Share.MainOutMessage($"[命令未执行] {command}; 操作者={operatorName}; 原因={reason}");
        playObject?.SysMsg($"{command} 未执行：{reason}", MsgColor.Red, MsgType.Hint);
    }
}

namespace GameSvr.CommandSystem
{
    [GameCommand("ServerInfo", "查看服务器信息", 3)]
    public class ServerInfoCommand : BaseCommond
    {
        [DefaultCommand]
        public void ServerInfo(string[] @Params, TPlayObject PlayObject)
        {
            // 诚实 fail-closed：此前是一个【空方法体】(建了个未用的 StringBuilder 就返回)，
            // GM 得不到任何反馈。原版 @ServerInfo (idx194) 是 6 路子命令信息报告
            // (sub_718894/sub_64BB40/sub_67D9E4/sub_5FCBDC 等)，核心为 CoreBodyDeferred。
            // 现至少如实上报未移植，避免静默无响应。
            NativeCommandFailure.Report(PlayObject, "ServerInfo",
                "原版服务器信息(6 路子命令报告)尚未移植，未返回信息。");
        }
    }
}

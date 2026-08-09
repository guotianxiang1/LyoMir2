using SystemModule;

namespace GameSvr;

public sealed class GameSvrConfig
{
    public int nDBQueryID;
    public int nLoadDBCount;
    public int nSaveDBCount;
}

public sealed class TestDataServer
{
    public bool Connected { get; set; }

    public bool SendRequest(int queryId, ServerMessagePacket packet, object data)
    {
        return false;
    }
}

public static class M2Share
{
    public static TestDataServer DataServer;
    public static GameSvrConfig g_Config = new();
    public static int dwRunDBTimeMax;

    public static void ErrorMessage(string message)
    {
    }
}

using GameGate.Core;

namespace GameGate.Forms;

internal static class ClassicConfigCopy
{
    public static void Apply(GateConfig source, GateConfig target)
    {
        target.GatePort = source.GatePort;
        target.GateAddr = source.GateAddr;
        target.BackendIP = source.BackendIP;
        target.GameBackendIP = source.GameBackendIP;
        target.BackendPort = source.BackendPort;
        target.BackendPort2 = source.BackendPort2;
        target.MaxUser = source.MaxUser;
        target.MaxSend = source.MaxSend;
        target.ServeCount = source.ServeCount;
        target.Mode = source.Mode;
        target.WalkInterval = source.WalkInterval;
        target.AttackInterval = source.AttackInterval;
        target.CastInterval = source.CastInterval;
        target.TurnInterval = source.TurnInterval;
        target.CureInterval = source.CureInterval;
        target.ShopInterval = source.ShopInterval;
        target.NpcInterval = source.NpcInterval;
        target.SpeedNum = source.SpeedNum;
        target.GlobalSpeed = source.GlobalSpeed;
        target.WalkSpeedNum = source.WalkSpeedNum;
        target.MuteTime = source.MuteTime;
        target.BlackTime = source.BlackTime;
        target.SpellNum = source.SpellNum;
        target.Timeout0 = source.Timeout0;
        target.Timeout1 = source.Timeout1;
        target.Key1 = source.Key1;
        target.Key2 = source.Key2;
        target.Key3 = source.Key3;
        target.Key4 = source.Key4;
        target.Key5 = source.Key5;
        target.OffKey = source.OffKey;
        target.OffKeybot = source.OffKeybot;
        target.OpenNewTigerGate = source.OpenNewTigerGate;
        target.M2Path = source.M2Path;
        target.M2WatchInterval = source.M2WatchInterval;
        target.RebootM2WhenStuck = source.RebootM2WhenStuck;
        target.Title = source.Title;
    }
}

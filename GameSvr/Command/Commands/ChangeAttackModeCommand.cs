using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    internal static class AttackModeCommandSupport
    {
        public static string ApplyAttackMode(TPlayObject playObject, byte attackMode)
        {
            if (attackMode > TPlayObject.NativeAttackModeCorps)
                return string.Empty;

            playObject.m_btAttatckMode = attackMode;
            switch (playObject.m_btAttatckMode)
            {
                case TPlayObject.NativeAttackModeAll:
                    playObject.SysMsg(M2Share.sAttackModeOfAll, MsgColor.Green, MsgType.Hint);
                    break;
                case TPlayObject.NativeAttackModePeace:
                    playObject.SysMsg(M2Share.sAttackModeOfPeaceful, MsgColor.Green, MsgType.Hint);
                    break;
                case TPlayObject.NativeAttackModeGroup:
                    playObject.SysMsg(M2Share.sAttackModeOfGroup, MsgColor.Green, MsgType.Hint);
                    break;
                case TPlayObject.NativeAttackModeGild:
                    playObject.SysMsg(M2Share.sAttackModeOfGuild, MsgColor.Green, MsgType.Hint);
                    break;
                case TPlayObject.NativeAttackModeHostile:
                    playObject.SysMsg(M2Share.sAttackModeOfHostile, MsgColor.Green, MsgType.Hint);
                    break;
                case TPlayObject.NativeAttackModeCorps:
                    playObject.SysMsg(M2Share.sAttackModeOfCorps, MsgColor.Green, MsgType.Hint);
                    break;
            }

            // 战神 ChangeAttackMode GM handler @0x623A2E (inside sub_622820) sends
            // ident 0x221 (=545) through VMT+0x250 with the mode byte as nRecog.
            playObject.SendDefMessage(Grobal2.SM_ATTACKMODE, playObject.m_btAttatckMode, 0, 0, 0, "");
            return string.Empty;
        }

        public static byte NextAttackMode(byte currentMode)
        {
            if (currentMode > TPlayObject.NativeAttackModeCorps)
                return TPlayObject.NativeAttackModeAll;

            if (currentMode >= TPlayObject.NativeAttackModeCorps)
                return TPlayObject.NativeAttackModeAll;

            return (byte)(currentMode + 1);
        }
    }

    [GameCommand("AttackMode", "调整当前玩家攻击模式", 0)]
    public class ChangeAttackModeCommand : BaseCommond
    {
        [DefaultCommand]
        public string ChangeAttackMode(TPlayObject PlayObject)
        {
            return AttackModeCommandSupport.ApplyAttackMode(PlayObject, AttackModeCommandSupport.NextAttackMode(PlayObject.m_btAttatckMode));
        }
    }

    [GameCommand("AttackMode0", "切换到全体攻击模式", 0)]
    public class ChangeAttackModeAllCommand : BaseCommond
    {
        [DefaultCommand]
        public string ChangeAttackModeAll(TPlayObject PlayObject)
        {
            return AttackModeCommandSupport.ApplyAttackMode(PlayObject,
                TPlayObject.NativeAttackModeAll);
        }
    }

    [GameCommand("AttackMode1", "切换到和平攻击模式", 0)]
    public class ChangeAttackModePeaceCommand : BaseCommond
    {
        [DefaultCommand]
        public string ChangeAttackModePeace(TPlayObject PlayObject)
        {
            return AttackModeCommandSupport.ApplyAttackMode(PlayObject,
                TPlayObject.NativeAttackModePeace);
        }
    }

    [GameCommand("AttackMode2", "切换到编组攻击模式", 0)]
    public class ChangeAttackModeGroupCommand : BaseCommond
    {
        [DefaultCommand]
        public string ChangeAttackModeGroup(TPlayObject PlayObject)
        {
            return AttackModeCommandSupport.ApplyAttackMode(PlayObject, TPlayObject.NativeAttackModeGroup);
        }
    }

    [GameCommand("AttackMode3", "切换到行会及联盟攻击模式", 0)]
    public class ChangeAttackModeGildCommand : BaseCommond
    {
        [DefaultCommand]
        public string ChangeAttackModeGild(TPlayObject PlayObject)
        {
            return AttackModeCommandSupport.ApplyAttackMode(PlayObject, TPlayObject.NativeAttackModeGild);
        }
    }

    [GameCommand("AttackMode4", "切换到敌对攻击模式", 0)]
    public class ChangeAttackModeHostileCommand : BaseCommond
    {
        [DefaultCommand]
        public string ChangeAttackModeHostile(TPlayObject PlayObject)
        {
            return AttackModeCommandSupport.ApplyAttackMode(PlayObject, TPlayObject.NativeAttackModeHostile);
        }
    }

    [GameCommand("AttackMode5", "切换到战队攻击模式", 0)]
    public class ChangeAttackModeCorpsCommand : BaseCommond
    {
        [DefaultCommand]
        public string ChangeAttackModeCorps(TPlayObject PlayObject)
        {
            return AttackModeCommandSupport.ApplyAttackMode(PlayObject, TPlayObject.NativeAttackModeCorps);
        }
    }
}

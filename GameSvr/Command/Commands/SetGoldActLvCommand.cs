using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @SetGoldActLv (idx496, perm5) —— 设置金币活动/热血勇士等级。
    /// 原版 case@0x006292A1 是【inline】(非 CoreBodyDeferred): 按名字找角色(sub_652784)，
    /// 解析等级；等级>0 → 内联写 [char+0x181D]=lvl → GM 回复(0xFFDB)。
    /// [char+0x181D] 字段经 eng065 转储【字节确认】== C# m_btGoldActNextLevel:
    ///   激活(GoldActCredUse @0x007869EE/00786A0A: ==0→置1)、发奖(ReqItemByGoldAct
    ///   @0x006457F3/0064584B: 读/算/写回)、存档(PlayerLoad/Save @0x006B0627/006B13F1)
    ///   全部使用 +0x181D，与 C# m_btGoldActNextLevel 的消费端(TPlayObject.Operate.cs
    ///   激活、TPlayObject.NativeGoldGift 发奖、HumData 持久化)逐一对应。
    /// 证据: staging/gm_activity_commands_20260801.md (496 inline [char+6173]=lvl, 0xFFDB);
    ///       D:/loym2/staging/ida_eng065_goldgift_closure_20260718.txt (+0x181D 消费端反汇编)。
    /// </summary>
    [GameCommand("SetGoldActLv", "设置金币活动等级", "人物名称 等级", 5)]
    public class SetGoldActLvCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetGoldActLv(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            var nLevel = @Params.Length > 1 ? HUtil32.Str_ToInt(@Params[1], 0) : 0;
            if (string.IsNullOrEmpty(sHumName) || nLevel <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);   // 原版 find-char sub_652784
            if (m_PlayObject == null)
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumName), MsgColor.Red, MsgType.Hint);
                return;
            }
            // 原版内联写 [char+0x181D]=lvl (byte；等级>0 已在上面 gate)。
            m_PlayObject.m_btGoldActNextLevel = (byte)nLevel;
            PlayObject.SysMsg($"{sHumName} 的金币活动(热血勇士)等级已设置为 {nLevel}。", MsgColor.Green, MsgType.Hint);
        }
    }
}

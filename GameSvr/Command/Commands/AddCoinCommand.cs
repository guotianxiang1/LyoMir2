using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令：增加指定玩家的金币(金币/Gold)。
    /// 用法: @AddCoin 人物名称 数量
    ///
    /// 原版 @AddCoin (idx204, perm5, 帮助"增加角色的金币数量") 是一个纯转发 shim，
    /// 尾调用金币【增加】核心 sub_6C6B40，写入的是金币(Gold)字段，且 shim 本身
    /// 没有跨服回退。此前的 C# 实现错误地写入 m_nGamePoint(代币/token) 并额外挂了
    /// FindOtherServerUser 跨服查找 —— 两者都是原版 shim 所没有的偏差，现已纠正为
    /// 与金币核心/@AddGold 一致的 Gold 写入路径(封顶 m_nGoldMax，与 @AddGold 兄弟命令
    /// 相同的封顶/通知语义；核心是否封顶为 CoreBodyDeferred，此处沿用同族已验证行为)。
    /// 证据: staging/gm_currency_commands_20260731.md (AddCoin→sub_6C6B40 gold inc core;
    /// pure forwarder; §"Live C# stub drift" AddCoin currency mismatch)。
    /// </summary>
    [GameCommand("AddCoin", "增加角色的金币数量", "人物名称 数量", 5)]
    public class AddCoinCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddCoin(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            var nCount = @Params.Length > 1 ? HUtil32.Str_ToInt(@Params[1], 0) : 0;
            if (string.IsNullOrEmpty(sHumName) || nCount <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
            if (m_PlayObject != null)
            {
                if (m_PlayObject.m_nGold + nCount < m_PlayObject.m_nGoldMax)
                {
                    m_PlayObject.m_nGold += nCount;
                }
                else
                {
                    nCount = m_PlayObject.m_nGoldMax - m_PlayObject.m_nGold;
                    m_PlayObject.m_nGold = m_PlayObject.m_nGoldMax;
                }
                m_PlayObject.GoldChanged();
                PlayObject.SysMsg(sHumName + "的金币已增加" + nCount + ".", MsgColor.Green, MsgType.Hint);
                if (M2Share.g_boGameLogGold)
                {
                    M2Share.AddGameDataLog("14" + "\09" + PlayObject.m_sMapName + "\09" + (PlayObject.m_nCurrX).ToString() + "\09" + (PlayObject.m_nCurrY).ToString()
                        + "\09" + PlayObject.m_sCharName + "\09" + Grobal2.sSTRING_GOLDNAME + "\09" + (nCount).ToString() + "\09" + "1" + "\09" + sHumName);
                }
            }
            else
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumName), MsgColor.Red, MsgType.Hint);
            }
        }
    }
}

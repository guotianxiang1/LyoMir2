using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令：增加指定玩家金币。
    ///
    /// ⚠ CGLD-23 — 本命令在原版 GM 命令表中【不存在】，属 INVENTED。
    /// 全镜像多编码扫描（GBK / 裸 ASCII 大小写不敏感 / UTF-16LE）：
    ///   Delphi ShortString `07 41 64 64 47 6f 6c 64` ("AddGold")  → 0 命中
    ///   UTF-16LE `41 00 64 00 64 00 47 00 6f 00 6c 00 64 00`      → 0 命中
    ///   裸 ASCII `41 64 64 47 6f 6c 64`                            → 2 命中，均【不是】GM 命令：
    ///     0x72D085 = PAS 脚本接口声明帮助文本 "function AddGold(Value: integer): Boolean;"
    ///     0x73273C = Delphi 长字符串 `ff ff ff ff / 07 00 00 00 / 'AddGold' 00`，
    ///                注册于 @0x73163F `mov ecx,0x73273C / mov edx,0x6D791C / call 0x4F4180`，
    ///                即把脚本名 AddGold 绑到【IncGold(0x6D791C)】；紧邻下一条
    ///                @0x731650 把 "DecGold"(0x73274C) 绑到 DecGold(0x6C7D64)。
    /// 对照：GM 命令表在 0x7C7xxx 用 ShortString 记录，AddCoin @0x7C74F4
    ///   `07 'AddCoin'` + rec+0x18 = 0xCC(204) + rec+0x1C = 5(权限)，与上面两处
    ///   完全不同的注册表与编码。故「原版无 @AddGold 这个 GM 命令」成立。
    ///
    /// 已摘除：离线【持久化增发队列】。原生同族 @AddCoin 的离线分支
    /// (0x6C6C20-0x6C6C24: `mov cx,0x38FF` 红字 + 一条提示文本) 只打印提示，
    /// 没有跨服回退、没有落盘重放。而原生脚本侧的 AddGold 就是 IncGold —— 同步、
    /// 只对在线对象生效、返回 Boolean，全链路不存在任何离线队列。
    /// 命令本身的去留（下线 / 改注册为运营专用扩展）留给运营决策，但增发队列先摘。
    /// </summary>
    [GameCommand("AddGold", "调整指定玩家金币", "人物名称  金币数量", 10)]
    public class AddGoldCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddGold(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            TPlayObject m_PlayObject;
            var sHumName = @Params.Length > 0 ? @Params[0] : "";//玩家名称
            var nCount = @Params.Length > 1 ? Convert.ToInt32(@Params[1]) : 0;//金币数量
            var nServerIndex = 0;
            if (PlayObject.m_btPermission < 6)
            {
                return;
            }
            if (string.IsNullOrEmpty(sHumName) || nCount <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
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
                if (M2Share.UserEngine.FindOtherServerUser(sHumName, ref nServerIndex))
                {
                    PlayObject.SysMsg(sHumName + " 现在" + nServerIndex + "号服务器上", MsgColor.Green, MsgType.Hint);
                }
                else
                {
                    // CGLD-23: 原离线分支在此调用 M2Share.FrontEngine.AddChangeGoldList(...)，
                    // 把增金请求写入会落盘、会在目标角色上线后重放的队列 —— 金额无上界
                    // （仅受 m_nGoldMax 约束），是全子系统唯一确凿的凭空增发入口。
                    // 原生同族 @AddCoin 离线只打印红字提示，故此处与之对齐。
                    PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumName),
                        MsgColor.Red, MsgType.Hint);
                }
            }
        }
    }
}

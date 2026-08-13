using System.Globalization;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public class MirrorMessage
    {

        public MirrorMessage()
        {

        }

        public void ProcessData(int Ident, int serverNum, string Body)
        {
            switch (Ident)
            {
                case Grobal2.ISM_GROUPSERVERHEART:
                    ServerHeartMessage(serverNum, Body);
                    break;
                case Grobal2.ISM_USERSERVERCHANGE:
                    MsgGetUserServerChange(serverNum, Body);
                    break;
                case Grobal2.ISM_CHANGESERVERRECIEVEOK:
                    // 战神 222 (跳表 EA 0x6572FA -> sub_657700) = 唯一显式长度门 handler。
                    // native 语义 (字节级已证): ecx=[ebp+8]=len, edx=[ebp+0xc]=二进制帧。
                    //   0x65771C cmp ecx,0x24 / jl 退出 (len>=0x24=36); 0x405774 从 body[0]
                    //   取 pascal 串(名) -> GetPlayObject(0x652784); byte[body+0x10]!=0 门;
                    //   SendMsg 虚调 dx=0x285A (vtbl+0xD8); 再 0x768CEC(玩家, ecx=word
                    //   [body+0x20], edx=body+0x10 pascal 串(图), 栈: 1,0,word[body+0x22]) —
                    //   即按 名+目标图+x/y 定点召回/移动。
                    // fail-closed: native 222 读定长二进制结构 (名@0, 图@0x10, x@0x20,
                    // y@0x22); C# 跨服为文本协议, 本 ident 的 body 是换服握手文件名字符串
                    // (live 发送方 MsgGetUserServerChange 行 192 SendServerGroupMsg(
                    // ISM_CHANGESERVERRECIEVEOK, ..., ufilename)), 无二进制坐标载体, 表示不
                    // 兼容。保留 C# 换服握手接收 (与其 live 发送方匹配), 不以不可达的二进制
                    // 召回覆盖在用握手。
                    MsgGetUserChangeServerRecieveOk(serverNum, Body);
                    break;
                case Grobal2.ISM_USERLOGON:
                    MsgGetUserLogon(serverNum, Body);
                    break;
                case Grobal2.ISM_USERLOGOUT:
                    // 战神 202 = 反作弊惩罚 (跳表 sub_657110 EA 0x657208 -> wrapper
                    // sub_658384 -> core sub_653ED0)。native 语义 (字节级已证):
                    //   * wrapper: 若 [ebp+8]=len>0, 从 body 提名单串, 调 core, 第三参
                    //     ecx=[ebp+0x10]=帧 dword[ebx+8]=惩罚"时长/秒" (0x658384)。
                    //   * core sub_653ED0: sub_653cc4 遍历玩家表[+0x2c], 对 byte[+0x73]==0
                    //     且 CompareText(player.[+0xb33], body)==0 的每个玩家 (可多个,
                    //     同账号/IP), 施罚:
                    //       time>0 -> byte[+0x1829]=3 (惩罚档位); [+0x180c]=trunc(Now-
                    //         [+0x780])+7-time (0x653F6B/0x653F7F, 到期天数); SendMsg
                    //         cx=0x1D "设置外挂惩罚"(0x65403C)。
                    //       time<=0 -> byte[+0x1829]=0; [+0x180c]=0; "清除外挂惩罚"
                    //         (0x654054)。 'SD000'@0x65402C 经 0x696228/0x696528 查表。
                    // fail-closed (缺字段+缺载体+捏造风险, 遵铁律不改运行行为):
                    //   (1) 时长来自帧第三 dword (native arg3); C# 跨服为文本协议,
                    //       ProcessData(ident,serverNum,body) 无此载体 (serverNum 是发送
                    //       服索引, 见各 handler 的 sNum==nServerIndex 守卫), 无法定 set/
                    //       clear 亦无法算到期。
                    //   (2) [+0x1829]=m_btNativeCheatPenaltyTier 已存在, 但到期字段
                    //       [+0x180c] 全仓无对应成员 (缺字段); 日期基址 [+0x780] 亦未映射。
                    //   (3) 唯一 live 发送方是 UsrEngn.cs:1568 的登出广播 (charname, 无
                    //       时长), 全仓无反作弊形态的 202 发送方 -> 反作弊 core 不可达。
                    // 保留 C#-自洽的登出接收 (与其 live 发送方匹配); 反作弊语义待补齐
                    // [+0x180c]/日期基址字段 + 传输第三参后再接线。
                    MsgGetUserLogout(serverNum, Body);
                    break;
                case Grobal2.ISM_WHISPER:
                    MsgGetWhisper(serverNum, Body);
                    break;
                case Grobal2.ISM_GMWHISPER:
                    MsgGetGMWhisper(serverNum, Body);
                    break;
                case Grobal2.ISM_LM_WHISPER:
                    MsgGetLoverWhisper(serverNum, Body);
                    break;
                case Grobal2.ISM_SYSOPMSG:
                    MsgGetSysopMsg(serverNum, Body);
                    break;
                case Grobal2.ISM_ADDGUILD:
                    MsgGetAddGuild(serverNum, Body);
                    break;
                case Grobal2.ISM_DELGUILD:
                    MsgGetDelGuild(serverNum, Body);
                    break;
                case Grobal2.ISM_RELOADGUILD:
                    // 战神 207 = 服务器全局字符位图切换 (跳表 EA 0x657230 -> sub_658114)。
                    // native 语义 (字节级已证): 叶子只传 edx=[ebp+0x10]=帧 dword[ebx+8]=
                    // 新 37-bit 掩码; sub_658114 读全局对象 [0x7D7038], 保存旧 [+0]/[+4]
                    // (dword+byte=37 位), 写入新掩码, 然后对字符码 bl=0..0x24(36) 逐位比
                    // 较新旧 (0x65813F cmp al,0x27 是 bt 范围守卫, 0x65818A cmp bl,0x25 是
                    // 循环上界): 旧置新清 -> sub_658110(char,0); 旧清新置 -> sub_658110
                    // (char,1); 末尾 [0x7D5A68]->0x794F30 刷新。
                    // fail-closed: (1) 新掩码来自帧第三 dword (native arg3), C# 文本协议无
                    // 载体; (2) 全局 [0x7D7038] 位图对象与逐位回调 sub_658110/0x794F30 在
                    // C# 无对应模型 (缺全局状态); (3) 该 32-bit 掩码非文本可表示。故不移植
                    // native 位图 swap。 保留 C# 现有语义 (数字 body -> 信用卡 switchWord,
                    // live 发送方 CreditCardCommand.cs:31 SendServerGroupMsg(ISM_SERVERSWITCH
                    // =207); 非数字 -> 重载行会), 二者均有 live 发送方, 移除会破坏在用功能。
                    if (uint.TryParse(Body, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var switchWord))
                    {
                        (M2Share.CreditCardService ?? NativeCreditCardService.Disabled)
                            .TryApplySwitchWord(switchWord, true);
                    }
                    else
                    {
                        MsgGetReloadGuild(serverNum, Body);
                    }
                    break;
                case Grobal2.ISM_GUILDMSG:
                    MsgGetGuildMsg(serverNum, Body);
                    break;
                case Grobal2.ISM_CREDITCARD_CLEARALL:
                    // 战神 241 -> sub_655A18 (地址表 @0x657198[0x16], stub @0x65735C:
                    // eax=[0x7D6D50]/[eax]=UserEngine; call 0x655A18)。native 无条件
                    // 遍历在线玩家清信用卡, 从不解析 body (无 body 入参)。旧 C# 仅在
                    // 空 body 时清、非空 body 走行会战——行会战为 C# 扩展 (native 241
                    // 无此语义, 全仓亦无 241 发送侧), 移除以对齐 native。
                    (M2Share.CreditCardService ?? NativeCreditCardService.Disabled)
                        .ResetOnlineAll();
                    break;
                case Grobal2.ISM_CHATPROHIBITION:
                    MsgGetChatProhibition(serverNum, Body);
                    break;
                case Grobal2.ISM_CHATPROHIBITIONCANCEL:
                    MsgGetChatProhibitionCancel(serverNum, Body);
                    break;
                case Grobal2.ISM_CHANGECASTLEOWNER:
                    MsgGetChangeCastleOwner(serverNum, Body);
                    break;
                case Grobal2.ISM_RELOADCASTLEINFO:
                    MsgGetReloadCastleAttackers(serverNum);
                    break;
                case Grobal2.ISM_RELOADADMIN:
                    MsgGetReloadAdmin();
                    break;
                case Grobal2.ISM_MARKETOPEN:
                    MsgGetMarketOpen(true);
                    break;
                case Grobal2.ISM_MARKETCLOSE:
                    MsgGetMarketOpen(false);
                    break;
                case Grobal2.ISM_RELOADCHATLOG:
                    MsgGetReloadChatLog();
                    break;
                case Grobal2.ISM_DIVORCE:
                    // 战神 216 (跳表 EA 0x657294 -> sub_6579D8) = 离婚。本次逐字节复核:
                    // 提名 -> GetPlayObject -> 若 byte[+0xB94]!=0(已婚): 清 [+0xB94];
                    // SendMsg cx=0x278E(RM_MASTERRELATION)+7+dearName([+0xC48]); 清 [+0xC48];
                    // SysMsg cx=0xFFDB "你的配偶与你离婚了"@0x657AAC(len 0x12); RefShowName
                    // (0x7685E0)。C# MsgGetDivorce 已逐条对应 (顺序/串/参数一致), 忠实。
                    // (C# 多一道 serverNum==nServerIndex 守卫: native 路由下恒真, 已由
                    // AuditTools/MarryClusterCompatCheck 固化, 勿改。)
                    MsgGetDivorce(serverNum, Body);
                    break;
                case Grobal2.ISM_MENTOR_STUDENT_1:
                    // 战神 217 -> sub_657CF0: 徒弟(在他服)自行离开, 更新本服师父
                    MsgGetMentorStudentLeft(serverNum, Body);
                    break;
                case Grobal2.ISM_MENTOR_STUDENT_2:
                    // 战神 218 -> sub_657AC0: 师父(在他服)逐出徒弟, 更新本服徒弟
                    MsgGetMentorExpel(serverNum, Body);
                    break;
                case Grobal2.ISM_LM_DELETE:
                    // 战神 226 (索引表 @0x657160[24]=0x12 -> 地址表 @0x657198[0x12]
                    // -> stub @0x65731E -> sub_657888) = 徒弟出师的跨服镜像, 不是
                    // "情侣删除"。常量名 ISM_LM_DELETE 系旧命名, 见报告接线清单。
                    // 此前 C# 无该 case, 落 default 打印 "[Error]: ProcessOthGsMsg
                    // Ident=226" —— 与 native 的 REAL handler 不符。
                    MsgGetMentorGraduate(serverNum, Body);
                    break;
                case Grobal2.ISM_USER_INFO:
                    // 战神 221 (索引表 @0x657160[19]=0x0F -> 地址表[0x0F] ->
                    // stub @0x6572EA -> sub_6575D8) = 给本服 GM 转发文本通知。
                    // 此前与 214/215/219/220 一起折进空的 MsgGetUserMgr。
                    MsgGetGmNotice(serverNum, Body);
                    break;
                case Grobal2.ISM_TAG_SEND:
                    // 战神 219 (索引表 @0x657160[17]=0x0D -> 地址表[0x0D] ->
                    // stub @0x6572C4 -> sub_6581A4) = 三段式文本转发, 只有 SysMsg
                    // 那条腿可观测 (回帧腿落 sub_7138CC 空桩)。
                    MsgGetGmRelay(serverNum, Body);
                    break;
                case Grobal2.ISM_FRIEND_INFO:
                    // 战神 214 (stub @0x657287 -> sub_6579B0): 对第三个整型参数做
                    // 3 路 switch, 写全局 [[0x7D6010]] = 1/2/3。C# 传输层无第三个
                    // 整型参数载体, 且 [0x7D6010] 无 C# 模型 —— fail-closed, 保留
                    // 空处理 (不落 default sink: native 214 是 REAL handler,
                    // 打印 "[Error]" 反而与 native 不符)。见报告 214 条。
                    break;
                case Grobal2.ISM_TAG_RESULT:
                    // 战神 220 (stub @0x6572D9 -> sub_657E08): 通篇拼串, 终点
                    // `mov dx,0xDD; call 0x713890` -> sub_7138CC 是空桩
                    // (55 8B EC 5D C2 0C 00), 故本 build 上 220 无任何可观测效果。
                    // 空处理即忠实。
                    break;
                case Grobal2.ISM_FRIEND_DELETE:
                    // 215 在 native 是 SINK (索引表 @0x657160[13]=0)。C# 扩展保留
                    // 空处理, 与既有 C# 发送侧共存。
                    MsgGetUserMgr(serverNum, Body, Ident);
                    break;
                case Grobal2.ISM_RELOADMAKEITEMLIST:
                    MsgGetReloadMakeItemList();
                    break;
                case Grobal2.ISM_GUILDMEMBER_RECALL:
                    MsgGetGuildMemberRecall(serverNum, Body);
                    break;
                case Grobal2.ISM_RELOADGUILDAGIT:
                    MsgGetReloadGuildAgit(serverNum, Body);
                    break;
                case Grobal2.ISM_LM_LOGIN:
                    MsgGetLoverLogin(serverNum, Body);
                    break;
                case Grobal2.ISM_LM_LOGOUT:
                    MsgGetLoverLogout(serverNum, Body);
                    break;
                case Grobal2.ISM_LM_LOGIN_REPLY:
                    MsgGetLoverLoginReply(serverNum, Body);
                    break;
                case Grobal2.ISM_LM_KILLED_MSG:
                    MsgGetLoverKilledMsg(serverNum, Body);
                    break;
                case Grobal2.ISM_RECALL:
                    MsgGetRecall(serverNum, Body);
                    break;
                case Grobal2.ISM_REQUEST_RECALL:
                    MsgGetRequestRecall(serverNum, Body);
                    break;
                case Grobal2.ISM_REQUEST_LOVERRECALL:
                    MsgGetRequestLoverRecall(serverNum, Body);
                    break;
                case Grobal2.ISM_STANDARDTICK:
                    // 战神 240 (索引表 @0x657160[38]=0x15 -> 地址表 @0x657198[0x15]
                    // -> stub @0x65734F -> sub_657F3C) = 宗派邀请提示, 不是
                    // "标准时钟"。常量名 ISM_STANDARDTICK 系旧命名, 见报告接线清单。
                    // 此前 C# 无该 case, 落 default 打印 "[Error]: ProcessOthGsMsg
                    // Ident=240" —— 与 native 的 REAL handler 不符。
                    MsgGetSectInvite(serverNum, Body);
                    break;
                case Grobal2.ISM_GRUOPMESSAGE:
                    Console.WriteLine("跨服消息");
                    break;
                case Grobal2.ISM_CREDITCARD_CLEARMONTHLY:
                    (M2Share.CreditCardService ?? NativeCreditCardService.Disabled)
                        .ResetOnlineMonthly();
                    break;
                case Grobal2.ISM_IDENT_247:
                    // 战神 247 (跳表 EA 0x657378 -> sub_65805C) = 真实 handler (非 sink)。
                    // native 语义 (字节级已证): ecx=[ebp+8]=len, edx=[ebp+0xc]=二进制帧。
                    //   0x658067 call 0x78FE80 (恒返回 al=1, 使能桩) / test;
                    //   0x658070 dec ebx / cmp ebx,0xC / jne 退出 (门: len==0xD=13);
                    //   读 body 三 dword: eax=[body+0], ecx=[body+4], 栈=[body+8];
                    //   0x65808A call 0x699310(global[0x7D5D20], d0, d4, d8) —— 0x699310 用
                    //   IntToStr(0x40C89C)+0x405890 把两整数格式化, 读 [0x7D5C40] 写日志/DB。
                    // fail-closed: native body 是定长二进制帧 (三 dword); C# 跨服为文本协议
                    // (ProcessData 只有 serverNum+string body, 无二进制 dword 载体), 且全仓无
                    // 247 发送方 (SGRP-41: 无 M2Server 对 202..257 的发送侧)。无法忠实表示。
                    // 空操作而非落默认 error sink —— 因 native 247 是真实 handler, 落 sink 会
                    // 打印 "[Error] ProcessOthGsMsg Ident=247" 与 native 不符; 此处显式吞掉。
                    break;
                case Grobal2.ISM_SETNICKLF:
                    if (int.TryParse(Body, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var multiplier))
                    {
                        NativeNickLinFuState.TryApplyMirror(multiplier,
                            ref M2Share.NickLinFuState);
                    }
                    break;
                case Grobal2.ISM_GLORYLOG_FLUSH:
                    NativeGloryLogManager.Flush();
                    break;
                case Grobal2.ISM_MAKE_CATTLE_CRAZY:
                    NativeFireKingEventState.ForceLocally();
                    break;
                default:
                    // native 0x6573A0: IntToStr(ident) + dword_65745C
                    // "[Error]: ProcessOthGsMsg Ident=" (len-prefix 31 @ 0x657458)
                    M2Share.MainOutMessage("[Error]: ProcessOthGsMsg Ident=" + Ident);
                    break;
            }
        }

        private void ServerHeartMessage(int sNu, string Body)
        {

        }

        private void MsgGetUserServerChange(int sNum, string Body)
        {
            const string sExceptionMsg = "[Exception] TFrmSrvMsg::MsgGetUserServerChange";
            int shifttime = HUtil32.GetTickCount();
            string ufilename = Body;
            if (M2Share.nServerIndex == sNum)
            {
                try
                {
                    M2Share.UserEngine.AddSwitchData(new TSwitchDataInfo());
                    M2Share.UserEngine.SendServerGroupMsg(Grobal2.ISM_CHANGESERVERRECIEVEOK, M2Share.nServerIndex, ufilename);
                }
                catch
                {
                    M2Share.ErrorMessage(sExceptionMsg);
                }
            }
        }

        private void MsgGetUserChangeServerRecieveOk(int sNum, string Body)
        {
            var ufilename = Body;
            M2Share.UserEngine.GetISMChangeServerReceive(ufilename);
        }

        private void MsgGetUserLogon(int sNum, string Body)
        {
            var uname = Body;
            M2Share.UserEngine.OtherServerUserLogon(sNum, uname);
        }

        private void MsgGetUserLogout(int sNum, string Body)
        {
            var uname = Body;
            M2Share.UserEngine.OtherServerUserLogout(sNum, uname);
        }

        private void MsgGetWhisper(int sNum, string Body)
        {
            var uname = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                TPlayObject hum = M2Share.UserEngine.GetPlayObject(uname);
                if (hum != null)
                {
                    if (hum.m_boHearWhisper)
                    {
                        // Native carries the sender level as a separate argument of the
                        // ISM_WHISPER send (0x652DA3 movzx eax,word[ebx+0x278] pushed
                        // ahead of the body at 0x652DA7, 0x652DD6 mov dx,0xCB). This
                        // tree's ISM body has no level field, so the whisper Tag goes
                        // out as 0 on the cross-server path only.
                        hum.WhisperRe(Str, 0);
                    }
                }
            }
        }

        private void MsgGetGMWhisper(int sNum, string Body)
        {
            var uname = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                TPlayObject hum = M2Share.UserEngine.GetPlayObject(uname);
                if (hum != null)
                {
                    if (hum.m_boHearWhisper)
                    {
                        hum.WhisperRe(Str, 0);
                    }
                }
            }
        }

        private void MsgGetLoverWhisper(int sNum, string Body)
        {
            var uname = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                TPlayObject hum = M2Share.UserEngine.GetPlayObject(uname);
                if (hum != null)
                {
                    if (hum.m_boHearWhisper)
                    {
                        hum.WhisperRe(Str, 0);
                    }
                }
            }
        }

        private void MsgGetSysopMsg(int sNum, string Body)
        {
            M2Share.UserEngine.SendBroadCastMsg(Body, MsgType.System);
        }

        private void MsgGetAddGuild(int sNum, string Body)
        {
            var gname = string.Empty;
            var mname = HUtil32.GetValidStr3(Body, ref gname, HUtil32.Backslash);
            M2Share.GuildManager.AddGuild(gname, mname);
        }

        private void MsgGetDelGuild(int sNum, string Body)
        {
            var gname = Body;
            M2Share.GuildManager.DelGuild(gname);
        }

        private void MsgGetReloadGuild(int sNum, string Body)
        {
            var gname = Body;
            Association guild;
            if (sNum == 0)
            {
                guild = M2Share.GuildManager.FindGuild(gname);
                if (guild != null)
                {
                    guild.LoadGuild();
                    M2Share.UserEngine.GuildMemberReGetRankName(guild);
                }
            }
            else if (M2Share.nServerIndex != sNum)
            {
                guild = M2Share.GuildManager.FindGuild(gname);
                if (guild != null)
                {
                    guild.LoadGuildFile(gname + '.' + sNum);
                    M2Share.UserEngine.GuildMemberReGetRankName(guild);
                    guild.SaveGuildInfoFile();
                }
            }
        }

        private void MsgGetGuildMsg(int sNum, string Body)
        {
            var gname = string.Empty;
            string Str = Body;
            Str = HUtil32.GetValidStr3(Str, ref gname, HUtil32.Backslash);
            if (gname != "")
            {
                var g = M2Share.GuildManager.FindGuild(gname);
                if (g != null)
                {
                    g.SendGuildMsg(Str);
                }
            }
        }

        private void MsgGetGuildWarInfo(int sNum, string Body)
        {
            string Str;
            var gname = string.Empty;
            var warguildname = string.Empty;
            var StartTime = string.Empty;
            var remaintime = string.Empty;
            Association g;
            Association WarGuild;
            TWarGuild pgw;
            if (sNum == 0)
            {
                Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref gname, HUtil32.Backslash);
                Str = HUtil32.GetValidStr3(Str, ref warguildname, HUtil32.Backslash);
                Str = HUtil32.GetValidStr3(Str, ref StartTime, HUtil32.Backslash);
                remaintime = Str;
                if (gname != "" && warguildname != "")
                {
                    g = M2Share.GuildManager.FindGuild(gname);
                    WarGuild = M2Share.GuildManager.FindGuild(warguildname);
                    if (g != null && WarGuild != null)
                    {
                        int currenttick = HUtil32.GetTickCount();
                        if (M2Share.g_nServerTickDifference == 0)
                        {
                            M2Share.g_nServerTickDifference = Convert.ToInt32(StartTime) - currenttick;
                        }
                        pgw = null;
                        for (var i = 0; i < g.GuildWarList.Count; i++)
                        {
                            pgw = g.GuildWarList[i];
                            if (pgw != null)
                            {
                                if (pgw.Guild == WarGuild)
                                {
                                    pgw.Guild = WarGuild;
                                    pgw.dwWarTick = Convert.ToInt32(StartTime) - M2Share.g_nServerTickDifference;
                                    pgw.dwWarTime = Convert.ToInt32(remaintime);
                                    M2Share.MainOutMessage("[行会战] " + g.sGuildName + "<->" + WarGuild.sGuildName + ", 开战: " + StartTime + ", 持久: " + remaintime + ", 现在: " + pgw.dwWarTick + ", 时差: " + M2Share.g_nServerTickDifference);
                                    break;
                                }
                            }
                        }
                        if (pgw == null)
                        {
                            if (!g.GuildWarList.Select(x => x.Guild).Contains(WarGuild))
                            {
                                pgw = new TWarGuild();
                                pgw.Guild = WarGuild;
                                pgw.dwWarTick = int.Parse(StartTime) - M2Share.g_nServerTickDifference;
                                pgw.dwWarTime = int.Parse(remaintime);
                                g.GuildWarList.Add(pgw);
                            }
                            M2Share.MainOutMessage("[行会战] " + g.sGuildName + "<->" + WarGuild.sGuildName + ", 开战: " + StartTime + ", 持久: " + remaintime + ", 现在: " + (Convert.ToUInt32(StartTime) - M2Share.g_nServerTickDifference) + ", 时差: " + M2Share.g_nServerTickDifference);
                        }
                        g.RefMemberName();
                        g.UpdateGuildFile();
                    }
                }
            }
        }

        private void MsgGetChatProhibition(int sNum, string Body)
        {
            var whostr = string.Empty;
            var minstr = string.Empty;
            string Str = Body;
            Str = HUtil32.GetValidStr3(Str, ref whostr, HUtil32.Backslash);
            Str = HUtil32.GetValidStr3(Str, ref minstr, HUtil32.Backslash);
            if (whostr != "")
            {
                
                M2Share.CommandSystem.ExecCmd("Shutup", null);
            }
        }

        private void MsgGetChatProhibitionCancel(int sNum, string Body)
        {
            var whostr = Body;
            if (whostr != "")
            {
                
            }
        }

        private void MsgGetChangeCastleOwner(int sNum, string Body)
        {
            var guild = M2Share.GuildManager.FindGuild(Body);
            var castle = M2Share.CastleManager.GetCastle(0);
            if (guild != null && castle != null && castle.m_MasterGuild != guild)
            {
                castle.GetCastle(guild);
            }
        }

        private void MsgGetReloadCastleAttackers(int sNum)
        {
            M2Share.CastleManager.Initialize();
        }

        private void MsgGetReloadAdmin()
        {
            M2Share.LocalDB.LoadAdminList();
        }

        private void MsgGetReloadChatLog()
        {
            
        }

        private void MsgGetUserMgr(int sNum, string Body, int Ident_)
        {
            var UserName = string.Empty;
            string Str = Body;
            string msgbody = HUtil32.GetValidStr3(Str, ref UserName, HUtil32.Backslash);
            
        }

        private void MsgGetDivorce(int serverNum, string Body)
        {
            if (serverNum != M2Share.nServerIndex
                || string.IsNullOrEmpty(Body))
            {
                return;
            }

            var spouse = M2Share.UserEngine?.GetPlayObject(Body);
            if (spouse == null || !spouse.m_boMarried)
            {
                return;
            }

            var dearName = spouse.m_sDearName ?? string.Empty;
            spouse.m_boMarried = false;
            spouse.SendMsg(spouse, Grobal2.RM_MASTERRELATION, 0,
                7, 0, 0, dearName);
            spouse.m_sDearName = string.Empty;
            spouse.SysMsg("你的配偶与你离婚了", MsgColor.Red, MsgType.Hint);
            spouse.RefShowName();
        }

        private void MsgGetMentorStudentLeft(int sNum, string Body)
        {
            // 战神 sub_657CF0 (ident 217): body="师父名/徒弟名"。native 按 '/' 拆分;
            // C# 文本传输层用反斜杠作 body 内分隔 (同 MsgGetWhisper 等), 语义等价。
            // native 不校验 serverNum, GetPlayObject 空判即自然按服路由, 故不加守卫。
            var masterName = string.Empty;
            var studentName = HUtil32.GetValidStr3(Body, ref masterName,
                HUtil32.Backslash);
            TPlayObject.NativeMirrorStudentLeftMaster(masterName, studentName);
        }

        private void MsgGetMentorExpel(int sNum, string Body)
        {
            // 战神 sub_657AC0 (ident 218): body="师父名/徒弟名"。同上, 反斜杠拆分。
            var masterName = string.Empty;
            var studentName = HUtil32.GetValidStr3(Body, ref masterName,
                HUtil32.Backslash);
            TPlayObject.NativeMirrorMasterExpelStudent(masterName, studentName);
        }

        private void MsgGetGmNotice(int sNum, string Body)
        {
            // 战神 sub_6575D8 (ident 221): body="收信人名/正文"。
            // 0x6575F4 test ebx,ebx / je (净荷指针非空) + 0x6575F8 test ecx,ecx /
            // jle (净荷长度 > 0) —— 两道门在 C# 都是 "body 非空"。
            if (string.IsNullOrEmpty(Body))
            {
                return;
            }
            var recipientName = string.Empty;
            var text = HUtil32.GetValidStr3(Body, ref recipientName,
                HUtil32.Backslash);
            TPlayObject.NativeMirrorGmNotice(recipientName, text);
        }

        private void MsgGetGmRelay(int sNum, string Body)
        {
            // 战神 sub_6581A4 (ident 219): body="收信人名/第二字段/正文"。native 连
            // 拆两次 '/' (0x6581E0 / 0x6581FC), 第二字段只喂死腿, 正文是两次拆分后
            // 的余段。sub_6581A4 不读 serverNum。
            var recipientName = string.Empty;
            var rest = HUtil32.GetValidStr3(Body, ref recipientName,
                HUtil32.Backslash);
            var secondField = string.Empty;
            var text = HUtil32.GetValidStr3(rest, ref secondField,
                HUtil32.Backslash);
            TPlayObject.NativeMirrorGmRelayThreeField(recipientName, text);
        }

        private void MsgGetMentorGraduate(int sNum, string Body)
        {
            // 战神 sub_657888 (ident 226): body="师父名/徒弟名"。native 按 '/' 拆,
            // C# 文本传输层用反斜杠作 body 内分隔 (同 217/218), 语义等价。
            // sub_657888 序言仅 `mov ebx,edx` (0x657897), 从不读 ecx=serverNum,
            // 故不加守卫。
            var masterName = string.Empty;
            var studentName = HUtil32.GetValidStr3(Body, ref masterName,
                HUtil32.Backslash);
            TPlayObject.NativeMirrorStudentGraduated(masterName, studentName);
        }

        private void MsgGetSectInvite(int sNum, string Body)
        {
            // 战神 sub_657F3C (ident 240): body="被邀请人名/邀请人名"。同上, 反斜杠
            // 拆分; sub_657F3C 序言仅 `mov ebx,edx` (0x657F48), 无 serverNum 守卫。
            var recipientName = string.Empty;
            var inviterName = HUtil32.GetValidStr3(Body, ref recipientName,
                HUtil32.Backslash);
            TPlayObject.NativeMirrorSectInvite(recipientName, inviterName);
        }

        private void MsgGetReloadMakeItemList()
        {
            
            M2Share.LocalDB.LoadMakeItem();
        }

        private void MsgGetGuildMemberRecall(int sNum, string Body)
        {
            var dxstr = string.Empty;
            var dystr = string.Empty;
            var uname = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                Str = HUtil32.GetValidStr3(Str, ref dxstr, HUtil32.Backslash);
                Str = HUtil32.GetValidStr3(Str, ref dystr, HUtil32.Backslash);
                var dx = (short)HUtil32.Str_ToInt(dxstr, 0);
                var dy = (short)HUtil32.Str_ToInt(dystr, 0);
                var hum = M2Share.UserEngine.GetPlayObject(uname);
                if (hum != null)
                {
                    if (hum.m_boAllowGuildReCall)
                    {
                        hum.SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
                        hum.SpaceMove(Str, dx, dy, 0);
                    }
                }
            }
        }

        private void MsgGetReloadGuildAgit(int sNum, string Body)
        {
            
            
        }

        private void MsgGetLoverLogin(int sNum, string Body)
        {
            TPlayObject humlover;
            string Str;
            var uname = string.Empty;
            var lovername = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                Str = HUtil32.GetValidStr3(Str, ref lovername, HUtil32.Backslash);
                humlover = M2Share.UserEngine.GetPlayObject(lovername);
                if (humlover != null)
                {
                    int svidx = 0;
                    if (M2Share.UserEngine.FindOtherServerUser(uname, ref svidx))
                    {
                        M2Share.UserEngine.SendServerGroupMsg(Grobal2.ISM_LM_LOGIN_REPLY, svidx, lovername + '/' + uname + '/' + humlover.m_PEnvir.sMapDesc);
                    }
                }
            }
        }

        private void MsgGetLoverLogout(int sNum, string Body)
        {
            var uname = string.Empty;
            const string sLoverFindYouMsg = "正在找你...";
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                var lovername = Str;
                var hum = M2Share.UserEngine.GetPlayObject(lovername);
                if (hum != null)
                {
                    hum.SysMsg(uname + sLoverFindYouMsg, MsgColor.Red, MsgType.Hint);
                }
            }
        }

        private void MsgGetLoverLoginReply(int sNum, string Body)
        {
            var uname = string.Empty;
        }

        private void MsgGetLoverKilledMsg(int sNum, string Body)
        {
            var uname = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                var hum = M2Share.UserEngine.GetPlayObject(uname);
                if (hum != null)
                {
                    hum.SysMsg(Str, MsgColor.Red, MsgType.Hint);
                }
            }
        }

        private void MsgGetRecall(int sNum, string Body)
        {
            var dxstr = string.Empty;
            var dystr = string.Empty;
            var uname = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                Str = HUtil32.GetValidStr3(Str, ref dxstr, HUtil32.Backslash);
                Str = HUtil32.GetValidStr3(Str, ref dystr, HUtil32.Backslash);
                var dx = (short)HUtil32.Str_ToInt(dxstr, 0);
                var dy = (short)HUtil32.Str_ToInt(dystr, 0);
                var hum = M2Share.UserEngine.GetPlayObject(uname);
                if (hum != null)
                {
                    hum.SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
                    hum.SpaceMove(Str, dx, dy, 0);
                }
            }
        }

        private void MsgGetRequestRecall(int sNum, string Body)
        {
            var uname = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                var hum = M2Share.UserEngine.GetPlayObject(uname);
                if (hum != null)
                {
                    hum.RecallHuman(Str);
                }
            }
        }

        private void MsgGetRequestLoverRecall(int sNum, string Body)
        {
            var uname = string.Empty;
            if (sNum == M2Share.nServerIndex)
            {
                var Str = Body;
                Str = HUtil32.GetValidStr3(Str, ref uname, HUtil32.Backslash);
                var hum = M2Share.UserEngine.GetPlayObject(uname);
                if (hum != null)
                {
                    if (!hum.m_PEnvir.Flag.boNORECALL)
                    {
                        hum.RecallHuman(Str);
                    }
                }
            }
        }

        private void MsgGetMarketOpen(bool WantOpen)
        {
            
        }
    }
}

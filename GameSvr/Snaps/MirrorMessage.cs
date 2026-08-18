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

        // ===================================================================
        // 传输层第三个整型参数 (native 帧头 dword @ ISM+0x08)
        //
        // native 线格式 (收发两侧字节级复核, 底本 flat_image.bin):
        //   传输帧  magic dword 0x33AABB77 @+0 | kind word @+4 | payload len
        //           dword @+8 | payload @+0xC
        //           —— 解析器 0x713467 `cmp dword[edi],0x33AABB77`, 0x713475
        //           `mov eax,[edi+8]` (len), 0x7134B0 `mov dx,word[edi+4]` (kind),
        //           0x71346F `lea eax,[esi+0xC]` (payload 起点)。
        //   kind=2 的 payload 即 ISM 帧, 定长 12 字节头:
        //           word route @+0 | word ident @+2 | dword P1 @+4 | dword P2 @+8
        //           | body @+0xC
        //           —— 0x712EF6 `movzx edx,word[ebx]` (route; 0x6F=111 走
        //           ProcessOthGsMsg), 0x712F52 `mov dx,word[ebx+2]` (ident),
        //           0x712F4F `mov ecx,[ebx+4]` (P1, 派发器不读),
        //           0x712F3F `mov eax,[ebx+8]` + push -> handler 的 [ebp+0x10] (P2)。
        //   发送侧 sub_713890 @0x713890 编组出的正是同一组形参:
        //           0x7138A2 push [ebp+0xC](整型) / 0x7138AD push PChar(body) /
        //           0x7138B5 push Length(body) -> 0x7138CC(len, bodyPtr, nParam)
        //           —— 与 handler 的 [ebp+8]/[ebp+0xC]/[ebp+0x10] 逐槽对齐。
        //           (0x7138CC 在本 build 是空桩 `55 8B EC 5D C2 0C 00`, 故 native
        //            自身发不出 ISM 帧; 全部 26 个 sub_713890 调用点均编组后丢弃。
        //            route 111 亦无任何构造者 —— 0x713094/0x7130E8 两个 kind=2
        //            帧构造器的 13 个调用点用的是 60/62/66/375/384..391/401 这批
        //            DBServer 路由, 无一为 111。所以本仓不新增 C# 发送侧。)
        //
        // C# 线格式: `nCode/nServerIdx/sMsg`, 收侧 SnapsmService.DecodeSocStr /
        // SnapsmClient.DecodeSocStr 恰好按 '/' 拆两次, 余下整串即 Body。
        // 扩展方式: 对**native handler 确实读 [ebp+0x10] 且本仓已落地**的 ident,
        // 线格式变为 `nCode/nServerIdx/nParam/sMsg` —— 第三个字段就是 native 的
        // P2。两个 socket 类不需要改动 (它们只拆两次), 中转 hub
        // (SnapsmService.DecodeSocStr_SendOtherServer) 原样转发整串, 亦无需改动。
        // 见 CarriesNativeParam 的取用集合与其向后兼容论证。
        // ===================================================================

        /// <summary>
        /// 该 ident 的线上 Body 是否以「native 帧头第三个 dword」开头。
        ///
        /// 仅收录**跳表 stub 确实把 [ebp+0x10] 转给 handler、且本仓已按 native
        /// 落地**的 ident。native 侧读 [ebp+0x10] 的 stub 共 9 个 (202 0x657208 /
        /// 203 0x65721C / 207 0x657230 / 209 0x65723D / 214 0x657287 /
        /// 219 0x6572C4 / 224 0x65730A / 228 0x65733E / 249 0x657385)。
        /// </summary>
        private static bool CarriesNativeParam(int ident)
        {
            switch (ident)
            {
                // 202 stub 0x657208 `mov ecx,[ebp+0x10]` -> sub_658384 -> 惩罚天数
                case Grobal2.ISM_ANTICHEAT_PENALTY:
                // 203 stub 0x65721C `mov edx,[ebp+0x10]` -> sub_6582B0 -> 发信人等级
                case Grobal2.ISM_WHISPER:
                // 207 stub 0x657230 `mov edx,[ebp+0x10]` -> sub_658114 -> 37-bit 掩码
                case Grobal2.ISM_SINGLEQUOTE_SCAN:
                // 209 stub 0x65723D `mov ecx,[ebp+0x10]` -> sub_6580B8 -> 禁言秒数
                case Grobal2.ISM_CHATPROHIBITION:
                // 224 stub 0x65730A `mov ecx,[ebp+0x10]` -> sub_6574B4 -> 声望点数
                case Grobal2.ISM_MENTOR_REPUTATION:
                // 214 stub 0x657287 `mov ecx,[ebp+0x10]` -> sub_6579B0 -> 策略档位
                case Grobal2.ISM_GLOBAL_MODE_SET:
                // 228 stub 0x65733E `mov ecx,[ebp+0x10]` -> sub_657BCC -> 经验奖励
                case Grobal2.ISM_MENTOR_RECHARGE_REWARD:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 剥掉 Body 前面的整型字段。首字段不是整数时判定为「老三字段帧」,
        /// 原样返回并给 nParam = 0 —— 这样即使对端还在发旧格式也不会误拆。
        /// </summary>
        private static string TakeNativeParam(string body, out int nParam)
        {
            var head = string.Empty;
            var rest = HUtil32.GetValidStr3(body ?? string.Empty, ref head,
                HUtil32.Backslash);
            if (int.TryParse(head, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out nParam))
            {
                return rest;
            }
            nParam = 0;
            return body ?? string.Empty;
        }

        private static string TakeWhisperNativeParam(string body,
            out int nParam)
        {
            body ??= string.Empty;
            var firstSlash = body.IndexOf('/', StringComparison.Ordinal);
            if (firstSlash <= 0
                || !int.TryParse(body.AsSpan(0, firstSlash),
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out nParam))
            {
                nParam = 0;
                return body;
            }

            var rest = body[(firstSlash + 1)..];
            var recipientSlash = rest.IndexOf('/', StringComparison.Ordinal);
            var arrow = rest.IndexOf("=>", StringComparison.Ordinal);
            if (recipientSlash < 0 || (arrow >= 0 && recipientSlash > arrow))
            {
                // Legacy body="numericRecipient/sender=> text[/...]".
                nParam = 0;
                return body;
            }
            return rest;
        }

        public void ProcessData(int Ident, int serverNum, string Body)
        {
            var nParam = 0;
            if (CarriesNativeParam(Ident))
            {
                Body = Ident == Grobal2.ISM_WHISPER
                    ? TakeWhisperNativeParam(Body, out nParam)
                    : TakeNativeParam(Body, out nParam);
            }
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
                case Grobal2.ISM_CS_USERLOGOUT:
                    // C# 扩展 ident 198（窗口外）：跨服登出广播。原误用 native 202
                    // (ISM_ANTICHEAT_PENALTY); 发送方 UsrEngn.cs 已改到本 ident。
                    MsgGetUserLogout(serverNum, Body);
                    break;
                case Grobal2.ISM_ANTICHEAT_PENALTY:
                    // 战神 202 stub 0x657208 `mov ecx,[ebp+0x10]` -> sub_658384 @0x658384
                    // (len>0 提 body) -> sub_653ED0 @0x653ED0。线格式
                    // nCode/nServerIdx/nParam/sMsg，nParam=惩罚天数，sMsg=账号名。
                    // VA: 0x653F6B tier=3 / 0x653F74 sub_6D43C4 / 0x653F7F [+0x180c]。
                    TPlayObject.NativeMirrorAntiCheatPenalty(Body, nParam);
                    break;
                case Grobal2.ISM_WHISPER:
                    MsgGetWhisper(serverNum, nParam, Body);
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
                case Grobal2.ISM_CS_RELOADGUILD:
                    // C# 扩展 ident 199：跨服重载行会。原误用 native 207。
                    MsgGetReloadGuild(serverNum, Body);
                    break;
                case Grobal2.ISM_CS_SERVERSWITCH:
                    // C# 扩展 ident 258：switchWord 广播（信用卡/地图爆物等）。
                    if (uint.TryParse(Body, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var switchWordExt))
                    {
                        M2Share.ServerSwitches?.TryApplySwitchWord(
                            switchWordExt, out _);
                    }
                    break;
                case Grobal2.ISM_SINGLEQUOTE_SCAN:
                    // 战神 207 stub 0x657230 -> sub_658114 @0x658114；nParam=掩码低 32 位。
                    // VA: 0x65811C [0x7D7038] / 0x65813F cmp 0x27 / 0x65818A cmp 0x25 /
                    // 0x658196 call 0x794F30。sub_658110 @0x658110 本 build 空桩。
                    NativeSingleQuoteScanBitmap.ApplyMirrorMask(nParam);
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
                    // 战神 209 (stub @0x65723D -> sub_6580B8): body 是**裸角色名**
                    // (0x6580D1 _LStrFromPChar 后不拆分), 第三个 dword 是禁言秒数。
                    MsgGetChatProhibition(Body, nParam);
                    break;
                case Grobal2.ISM_CHATPROHIBITIONCANCEL:
                    // 战神 210 (stub @0x65724D -> sub_657FF8): body 是裸角色名, 无参。
                    MsgGetChatProhibitionCancel(Body);
                    break;
                case Grobal2.ISM_CHANGECASTLEOWNER:
                    MsgGetChangeCastleOwner(serverNum, Body);
                    break;
                case Grobal2.ISM_RELOADCASTLEINFO:
                    // 战神 212 stub 0x65726D -> sub_6577B0: body 非空 ->
                    // [[0x7D6214]] sub_65B6E0(行会名), 非全量 Initialize。
                    MsgGetReloadCastleAttackers(Body);
                    break;
                case Grobal2.ISM_RELOADADMIN:
                    MsgGetReloadAdmin();
                    break;
                case Grobal2.ISM_MENTOR_REPUTATION:
                    // 战神 224 (stub @0x65730A -> sub_6574B4) = 徒弟升级给师父加声望,
                    // 不是"开市场"。常量名 ISM_MARKETOPEN 系旧命名。
                    // body="师父名/徒弟名", 第三个 dword = 声望点数。
                    MsgGetMentorReputation(Body, nParam);
                    break;
                case Grobal2.ISM_MARKETCLOSE:
                    // 225 在 native 是 SINK (索引表 @0x657160[23]=0)。C# 扩展保留
                    // 空处理; 此前它与 224 共用 MsgGetMarketOpen(bool), 224 改按
                    // native 落地后该共用方法只剩这一个空调用方, 已就地内联。
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
                case Grobal2.ISM_MENTOR_GRADUATE:
                    // 战神 226 (索引表 @0x657160[24]=0x12 -> 地址表 @0x657198[0x12]
                    // -> stub @0x65731E -> sub_657888) = 徒弟出师的跨服镜像, 不是
                    // "情侣删除"。常量名 ISM_LM_DELETE 系旧命名, 见报告接线清单。
                    // 此前 C# 无该 case, 落 default 打印 "[Error]: ProcessOthGsMsg
                    // Ident=226" —— 与 native 的 REAL handler 不符。
                    MsgGetMentorGraduate(serverNum, Body);
                    break;
                case Grobal2.ISM_GM_NOTICE:
                    // 战神 221 (索引表 @0x657160[19]=0x0F -> 地址表[0x0F] ->
                    // stub @0x6572EA -> sub_6575D8) = 给本服 GM 转发文本通知。
                    // 此前与 214/215/219/220 一起折进空的 MsgGetUserMgr。
                    MsgGetGmNotice(serverNum, Body);
                    break;
                case Grobal2.ISM_TEXT_RELAY3:
                    // 战神 219 (索引表 @0x657160[17]=0x0D -> 地址表[0x0D] ->
                    // stub @0x6572C4 -> sub_6581A4) = 三段式文本转发, 只有 SysMsg
                    // 那条腿可观测 (回帧腿落 sub_7138CC 空桩)。
                    MsgGetGmRelay(serverNum, Body);
                    break;
                case Grobal2.ISM_GLOBAL_MODE_SET:
                    // 战神 214 (stub @0x657287 -> sub_6579B0)：对第三个整型参数做
                    // 3 路跳表，每臂经指针槽写同一个全局字节：
                    //   006579B0  83 EA 01     sub edx,1
                    //   006579B3  72 07        jb  0x6579BC   ; 参数 0 -> 写 1
                    //   006579B5  74 0E        je  0x6579C5   ; 参数 1 -> 写 2
                    //   006579B7  4A / 74 14   dec edx / je 0x6579CE ; 参数 2 -> 写 3
                    //   006579BA  EB 1A        jmp 0x6579D6   ; 其余 -> 直接 ret，不写
                    //   006579BC  A1 10 60 7D 00 / C6 00 01    mov eax,[0x7D6010] / mov byte[eax],1
                    //   006579C5  ...                          同上写 2
                    //   006579CE  ...                          同上写 3
                    // [0x7D6010] 是**指针槽**，其内容实测为 0x007D3A8C，即写的是
                    // byte[0x7D3A8C] —— 全服外挂惩罚策略档位。该字节的读点
                    // 0x6D8CD9 `A0 8C 3A 7D 00 mov al,[0x7D3A8C]` 紧接
                    // 0x6D8CDE `mov [edx+0x1829],al`，把它种进上报玩家的
                    // m_btNativeCheatPenaltyTier。故 214 = 跨服下发全服反外挂档位。
                    // 本端载体就是 TPlayObject.NativeCheatReportPolicyTier。
                    if (nParam >= 0 && nParam <= 2)
                    {
                        TPlayObject.NativeCheatReportPolicyTier = (byte)(nParam + 1);
                    }
                    break;
                case Grobal2.ISM_DEAD_LEG_220:
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
                case Grobal2.ISM_PLAYER_NOTICE:
                    // 战神 227 stub 0x65732E -> sub_657670: 收信人/正文, SysMsg Blue。
                    MsgGetPlayerNotice(Body);
                    break;
                case Grobal2.ISM_MENTOR_RECHARGE_REWARD:
                    // 战神 228 stub 0x65733E -> sub_657BCC: 师父名/徒弟名, nParam>=1000,
                    // sub_6C03F8 加经验 + SysMsg。
                    MsgGetMentorRechargeReward(Body, nParam);
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
                case Grobal2.ISM_SECT_INVITE:
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

        private void MsgGetWhisper(int sNum, int nSenderLevel, string Body)
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
                        // 0x658325 reads the low word of the ISM P2 dword and
                        // 0x65834A forwards it to sub_6C976C as the sender level.
                        hum.WhisperRe(Str, unchecked((ushort)nSenderLevel));
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

        private void MsgGetChatProhibition(string Body, int nParam)
        {
            // 战神 sub_6580B8: 0x6580D1 _LStrFromPChar(&s, body) -> 0x6580DE
            // eax=[[0x7D7104]] (BlockUsers.Dat 禁言管理器单例) -> 0x6580E7
            // sub_621B14(mgr, edx=s, ecx=[ebp+0x10])。native 无任何前置守卫
            // (序言只有 `mov esi,ecx / mov ebx,edx`), 空名交给管理器自己处理。
            NativeMirrorChatBan.Add(Body, nParam);
        }

        private void MsgGetChatProhibitionCancel(string Body)
        {
            // 战神 sub_657FF8: 0x658013 _LStrFromPChar(&s, body) -> 0x65801B
            // eax=[[0x7D7104]] -> 0x658022 sub_621CE4(mgr, edx=s)。stub 0x65724D
            // 虽然把 [ebp+8](长度) 送进 ecx, 但 sub_657FF8 序言只有 `mov ebx,edx`,
            // 从不读 ecx —— 故 210 无长度门、无第三参。
            NativeMirrorChatBan.Remove(Body);
        }

        private void MsgGetChangeCastleOwner(int sNum, string Body)
        {
            // sub_657810 @0x65783E calls the same sub_5E76F0 native
            // ASCII-folded guild lookup used by ident 212.
            var guild = M2Share.GuildManager.FindGuildNativeAscii(Body);
            var castle = M2Share.CastleManager.GetCastle(0);
            if (guild != null && castle != null && castle.m_MasterGuild != guild)
            {
                castle.GetCastle(guild);
            }
        }

        private void MsgGetReloadCastleAttackers(string Body)
        {
            M2Share.CastleManager.NativeMirrorReloadCastleAttacker(Body);
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

        private void MsgGetPlayerNotice(string Body)
        {
            if (string.IsNullOrEmpty(Body))
            {
                return;
            }

            // sub_4C6BA4 splits at the first '/', preserving both an empty first
            // field and every later slash in the message body.
            var separator = Body.IndexOf('/', StringComparison.Ordinal);
            var recipientName = separator >= 0 ? Body[..separator] : Body;
            var text = separator >= 0 ? Body[(separator + 1)..] : string.Empty;
            TPlayObject.NativeMirrorPlayerNotice(recipientName, text);
        }

        private void MsgGetMentorRechargeReward(string Body, int nParam)
        {
            var masterName = string.Empty;
            var studentName = HUtil32.GetValidStr3(Body ?? string.Empty,
                ref masterName, HUtil32.Backslash);
            TPlayObject.NativeMirrorMentorRechargeReward(masterName, studentName,
                nParam);
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

        private void MsgGetMentorReputation(string Body, int nParam)
        {
            // 战神 sub_6574B4 (ident 224): body="师父名/徒弟名"。
            // 0x6574DA _LStrFromPChar; 0x6574EB sub_4C6AEC(cl=0x2F='/') 拆首段/余段;
            // 0x6574F6 _LStrAsg(s := 余段)。native 用的分隔符就是 '/', 与 C# 顶层
            // 分隔同字符 —— 逐级左剥即可, 与 217/218/226/240 同构。
            var masterName = string.Empty;
            var studentName = HUtil32.GetValidStr3(Body, ref masterName,
                HUtil32.Backslash);
            TPlayObject.NativeMirrorMentorReputation(masterName, studentName,
                nParam);
        }
    }
}

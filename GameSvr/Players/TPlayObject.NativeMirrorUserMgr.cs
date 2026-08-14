using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ===================================================================
        // 跨服 OthGs 的 "UserMgr" 簇 —— ident 214/219/220/221。
        // C# 侧这四个 ident (加 SINK 的 215) 全部折进 MirrorMessage.MsgGetUserMgr,
        // 而该方法体是空的。逐个反汇编后, 四者其实互不相干:
        //
        //   214 -> stub 0x657287 (mov edx,[ebp+0x10]; call 0x6579B0)
        //          sub_6579B0 是一段 3 路整型 switch, 对全局 [[0x7D6010]] 写
        //          1/2/3。**未移植**: 判据是第三个整型参数 (C# 传输层无载体),
        //          且 [0x7D6010] 全局在 C# 无对应模型。详见报告 214 条。
        //   219 -> stub 0x6572C4 -> sub_6581A4   本文件 NativeMirrorGmRelayTwoField
        //   220 -> stub 0x6572D9 -> sub_657E08   **原生无可观测效果**, 见下。
        //   221 -> stub 0x6572EA -> sub_6575D8   本文件 NativeMirrorGmNotice
        //
        // ident 220 (sub_657E08) 通篇只做取串与拼接, 终点是
        // `mov dx,0xDD; call 0x713890` (0x657EDD)。sub_713890 只是把参数
        // 编组后 tail-call sub_7138CC, 而 sub_7138CC 的**全部函数体**是
        //   007138CC 55        push ebp
        //   007138CD 8BEC      mov  ebp,esp
        //   007138CF 5D        pop  ebp
        //   007138D0 C20C00    ret  0xC
        // —— 空桩。故 220 在本 build 上是彻底的无操作, C# 现有的空
        // MsgGetUserMgr 对 220 恰好忠实, 不需要改动 (报告标 FAITHFUL)。
        // 同一空桩也吃掉了 219 的第二条腿 (dx=0xE3, 0x658272), 故 219 只需移植
        // 它 SysMsg 那一条可观测腿。
        //
        // 形参坐标同 NativeMirrorMentor2.cs 顶部所述: [ebp+8]=payload 长度,
        // [ebp+0xC]=payload 指针, [ebp+0x10]=帧头第三个 dword。native 派发器没有
        // serverIdx 形参。219/221 都用 0x405708 按文本取串。
        // body 内子字段 native 用 '/' 拆 (0x4C6AEC 单字符 / 0x4C6BA4 字符集,
        // 单字符集下二者等价); C# 传输层 '/' 已被顶层协议占用, 故沿用反斜杠。
        // ===================================================================

        /// <summary>
        /// 战神 sub_6575D8 (OthGs ident 221): 向本服某个 <b>GM</b> 转发一条文本
        /// 通知。body="收信人名/正文"。native 独有的门是收信人 GMLevel &gt;= 3。
        /// </summary>
        internal static void NativeMirrorGmNotice(string recipientName,
            string text)
        {
            // native 的两道前置门 0x6575F4 `test ebx,ebx / je` (payload 指针非空)
            // 与 0x6575F8 `test ecx,ecx / jle` (payload 长度 > 0) 在 C# 的字符串
            // 世界里都塌缩成"body 非空", 已由调用方 MsgGetGmNotice 就地判掉 ——
            // body 原串只有那里才有。
            // (ecx 不是 serverIdx: 上游 0x713EE4 压的是 [ebx+4]-0xC = 净荷长度,
            //  native 派发器没有 serverIdx 形参。)

            // 0x65761E UserEngine.GetPlayObject(首段); 0x65762D test eax,eax / je
            var recipient = M2Share.UserEngine?.GetPlayObject(recipientName);
            if (recipient == null)
            {
                return;
            }

            // 0x657631 cmp byte[eax+0x675],3 / jb —— 无符号下界, GMLevel >= 3。
            // [obj+0x675] = m_btPermission (RTTI TPlayer.GMLevel, 写入点 0x6B1E80
            // 紧接 GetHumPermission sub_65583C; 同 AddItemToBag 0x6B73A3 一族)。
            if (recipient.m_btPermission < 3)
            {
                return;
            }

            // 0x65763A cx=0xFFDB; 0x65763E edx=余段; 0x657643 call [vmt+0xD4]
            // 0xFFDB 沿用离婚 sub_6579D8 (同一 cx, 已核验忠实) 的 Red/Hint 映射。
            recipient.SysMsg(text, MsgColor.Red, MsgType.Hint);
        }

        /// <summary>
        /// 战神 sub_6581A4 (OthGs ident 219): body="收信人名/第二字段/正文"。
        /// native 拆两次 '/' 后, 只有一条腿可观测 —— 给收信人发正文;
        /// 第二字段与第三个整型参数只喂给 0x658272 那条死腿 (sub_7138CC 空桩),
        /// 故不移植, 也不为它们臆造行为。
        /// </summary>
        internal static void NativeMirrorGmRelayThreeField(string recipientName,
            string text)
        {
            // 0x65820C cmp [ebp-8],0 / je —— native 只校验首段, 不校验正文。
            if (string.IsNullOrEmpty(recipientName))
            {
                return;
            }

            // 0x658212 UserEngine.GetPlayObject(首段); 0x658223 test ebx,ebx / je
            var recipient = M2Share.UserEngine?.GetPlayObject(recipientName);
            if (recipient == null)
            {
                return;
            }

            // 0x658227 cx=0xFCFF; 0x65822B edx=两次拆分后的余段; call [vmt+0xD4]
            // 0xFCFF -> FColor=0xFF/BColor=0xFC, 即 MsgColor.Blue (映射依据同
            // NativeLeaveMaster.cs 0x6C5FBA)。native 无收信人权限门。
            recipient.SysMsg(text, MsgColor.Blue, MsgType.Hint);
        }

        /// <summary>
        /// 战神 sub_657670 (OthGs ident 227): body="收信人名/正文"。
        /// 0x65768C test ebx,ebx / 0x657690 test ecx,ecx / jle 与 221 同形的前置门;
        /// 0x6576B1 sub_4C6BA4(cl='/' ) 拆分; 0x6576C0 GetPlayObject(首段);
        /// 0x6576C9 cx=0xFCFF call [vmt+0xD4] 把余段发给该玩家。无 GM 门 (221 有
        /// byte[+0x675]&gt;=3 @0x657631)。
        /// </summary>
        internal static void NativeMirrorPlayerNotice(string recipientName,
            string text)
        {
            if (string.IsNullOrEmpty(recipientName))
            {
                return;
            }

            var recipient = M2Share.UserEngine?.GetPlayObject(recipientName);
            if (recipient == null)
            {
                return;
            }

            recipient.SysMsg(text, MsgColor.Blue, MsgType.Hint);
        }
    }
}

using GameSvr.Plugins;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // Literal 0x6B63E0 / 0x6C9730, len 9: F1 F6 CC FD CB BD C1 C4 20.
        // Both whisper-monitor copies are prefixed with it.
        internal const string WhisperMonitorPrefix = "聆听私聊 ";

        // Colour word of the monitor copy: 0x6B4AD8 / 0x6C963C mov cx,0x38FF,
        // passed to sub_73C8F4 (= [VMT+0xD4]) which enqueues RM 10100 -> SM 100.
        // This tree's RM_SYSMESSAGE arm packs the colour from nParam1/nParam2, so
        // 0xFF/0x38 reproduces MakeWord = 0x38FF on the wire.
        internal const byte WhisperMonitorFColor = 0xFF;
        internal const byte WhisperMonitorBColor = 0x38;

        protected virtual void Whisper(string whostr, string saystr)
        {
            // 0x6C94FC has no 255-byte clamp. The old comment pointing at
            // 0x6BB8A4 was a misread: that VA is `E8 B7 C0 01 00 call 0x6d7960`
            // on the shout-gold path, not a whisper length gate.
            var svidx = 0;
            var PlayObject = M2Share.UserEngine.GetPlayObject(whostr);
            if (PlayObject != null)
            {
                // 0x6C954D 80 BF 2C 0D 00 00 00 cmp [edi+0xD2C],0 / je
                // then 0x6C9556..0x6C9579 SysMsg 0x38FF "无法向 "+name+" 发送信息"
                if (!PlayObject.m_boReadyRun)
                {
                    SysMsg("无法向 " + whostr + " 发送信息", MsgColor.Red, MsgType.Hint);
                    return;
                }
                // 0x6C9584 F6 87 9C 0B 00 00 01 test [edi+0xB9C],1 / jne deny
                // 0x6C95A0 call 0x6C97A8 blocklist (0x40BD78 CompareText)
                // deny text 0x6C9710 " 拒绝私聊"
                if (!PlayObject.m_boHearWhisper || PlayObject.IsBlockWhisper(m_sCharName)
                    || (PlayObject.m_dwChatShieldMask & 0x01u) != 0)
                {
                    SysMsg(whostr + " 拒绝私聊", MsgColor.Red, MsgType.Hint);
                    return;
                }
                // Native sub_6C94FC has no permission test here, and no offline
                // leaveword branch either: after the 0x6C9584 hear-whisper gate and the
                // 0x6C95A0 IsBlockWhisper gate it falls straight into the 0x6C95CE
                // concat and one single enqueue. The GM tier was invented - ident 103's
                // Param is the literal 0xFFFC (0x6B4AE4 68 FC FF 00 00), which is what
                // btWhisperMsgFColor/BColor (0xFC/0xFF) happened to produce on the
                // non-GM branch, so the tier never showed up as a wire difference.
                //   0x6C95F6 66 8B 93 78 02 00 00 mov dx,[ebx+0x278] -> wParam = speaker level
                //   0x6C95FE 0F B7 C0 / 50        movzx eax,ax; push -> nParam1 = 0 -> Series
                //   0x6C9610 8B D3                mov edx,ebx        -> BaseObject = speaker
                PlayObject.SendMsg(this, Grobal2.RM_WHISPER, m_Abil.Level, 0, 0, 0,
                    m_sCharName + "=> " + saystr);
                // The speaker's own whisper monitor gets a SysMsg, not a second
                // ident 103, and it repeats the recipient's body verbatim:
                //   0x6C9619 8B B3 44 19 00 00  mov esi,[ebx+0x1944]
                //   0x6C9623 80 7E 73 00        cmp byte [esi+0x73],0   ; ghost gate
                //   0x6C962C 8B 4D F8           mov ecx,[ebp-8]         ; "<name>=> <text>"
                //   0x6C962F BA 30 97 6C 00     mov edx,0x6C9730        ; "聆听私聊 "
                //   0x6C963C 66 B9 FF 38        mov cx,0x38FF
                //   0x6C9644 FF 96 D4 00 00 00  call [VMT+0xD4]
                // The recipient's monitor is served by the RM_WHISPER arm itself
                // (0x6B4A99), so it is not sent from here.
                if (m_GetWhisperHuman != null && !m_GetWhisperHuman.m_boGhost)
                {
                    m_GetWhisperHuman.SendMsg(m_GetWhisperHuman, Grobal2.RM_SYSMESSAGE, 0,
                        WhisperMonitorFColor, WhisperMonitorBColor, 0,
                        WhisperMonitorPrefix + m_sCharName + "=> " + saystr);
                }
            }
            else
            {
                if (M2Share.UserEngine.FindOtherServerUser(whostr, ref svidx))
                {
                    // 0x652DA3 movzx eax,word[player+0x278] / 0x652DA7 push eax:
                    // ISM 203 carries the speaker level in the native P2 dword.
                    M2Share.UserEngine.SendServerGroupMsg(Grobal2.ISM_WHISPER,
                        svidx, m_Abil.Level,
                        whostr + '/' + m_sCharName + "=> " + saystr);
                }
                else
                {
                    // 0x6C967B..0x6C9695 cx=0xFFDB, 0x6C9744 " : 无法查找"
                    SysMsg(whostr + " : 无法查找", MsgColor.Green, MsgType.Hint);
                }
            }
        }

        // Cross-server whisper delivery, native sub_6C976C
        // (eax = Self, edx = sender name, ecx = text, word [ebp+8] = sender level).
        // It has exactly one enqueue and no tier switch:
        //   0x6C9785 66 8B 45 08  mov ax,[ebp+8]  / 50 push  -> wParam = sender level
        //   0x6C978A 6A 00 x3                              -> nParam1..3 = 0
        //   0x6C9793 66 B9 2F 27  mov cx,0x272F              -> RM 10031
        //   0x6C9797 8B D3 / 8B C3 mov edx,ebx; mov eax,ebx  -> BaseObject = Self
        // The 0/1/2 selector was a misreading of that level word; ident 103's Param
        // is the literal 0xFFFC at 0x6B4AE4 and has no colour tier.
        public void WhisperRe(string SayStr, int nSenderLevel)
        {
            var sendwho = string.Empty;
            HUtil32.GetValidStr3(SayStr, ref sendwho, new string[] { "[", " ", "=", ">" });
            if (m_boHearWhisper && !IsBlockWhisper(sendwho)
                && (m_dwChatShieldMask & 0x01u) == 0)
            {
                SendMsg(this, Grobal2.RM_WHISPER, nSenderLevel, 0, 0, 0, SayStr);
            }
        }

        
        
        
        
        protected override void ProcessSayMsg(string sData)
        {
            bool boDisableSayMsg;
            var sC = string.Empty;
            var sCryCryMsg = string.Empty;
            var sParam1 = string.Empty;
            const string sExceptionMsg = "[Exception] TPlayObject.ProcessSayMsg Msg = {0}";
            try
            {
                // 0x6BB345 83 FF 01 jl exit; 0x6BB34E 81 FF B0 00 00 00 jg priv;
                // 0x6BB356 83 F8 50 jle ok; else 0x6BB35B 80 BB 75 06 00 00 04 jb exit.
                // Common path edi==Length==GBK bytes: priv<4 rejects >80, does not truncate.
                var nGbk = HUtil32.GbkEncoding.GetByteCount(sData);
                if (nGbk < 1)
                    return;
                if ((nGbk > 0xB0 || nGbk > 0x50) && m_btPermission < 4)
                    return;
                // 0x6BB526: dup [ebx+0xA74] same-text elapsed<0xBB8; rapid [ebx+0x682] elapsed<0x3E8.
                // dup>=2 => 0x6C912C edx=0x3C + 0x6BB9B8; rapid>=5 => same + 0x6BB9F4.
                // 眼神 屏蔽发言频繁禁言功能 NOPs both incs (0x6BB56A / 0x6BB579, 6 bytes
                // each → 90×6). Decay and the mute SysMsgs stay; counters just never rise.
                var now = HUtil32.GetTickCount();
                var elapsed = now - m_dwSayMsgTick;
                var skipFloodInc = new YanshenApi(this, null, M2Share.PluginManager).IsBlockSpamPatchOn();
                if (!skipFloodInc && sData == m_sOldSayMsg && elapsed < 0xBB8)
                    m_nSayMsgCount++;
                if (!skipFloodInc && elapsed < 0x3E8)
                    m_btSayRapidCount++;
                if (m_nSayMsgCount >= 2)
                {
                    m_nSayMsgCount = 0;
                    m_boDisableSayMsg = true;
                    m_dwDisableSayMsgTick = now + 60 * 1000;
                    // 0x6BB59E push 0x6BB9B8 + IntToStr(60) + 0x6BB9E4 "秒。"
                    // 眼神 禁止发言不提示 @0x6BB5CD memcpy 跳过 call [VMT+0xD4]（apply 0x100DB803）。
                    if (!YanshenConfig12Behaviors.BanChatSilent(this))
                        SysMsg("发送重复的话太频繁，当前已被禁言60秒。", MsgColor.Red, MsgType.Hint);
                    return;
                }
                if (m_btSayRapidCount >= 5)
                {
                    m_btSayRapidCount = 0;
                    m_boDisableSayMsg = true;
                    m_dwDisableSayMsgTick = now + 60 * 1000;
                    // 0x6BB5F6 push 0x6BB9F4 + IntToStr(60) + 0x6BB9E4 "秒。"
                    // 眼神 禁止发言不提示 @0x6BB625 memcpy 跳过 call [VMT+0xD4]（apply 0x100DB83A）。
                    if (!YanshenConfig12Behaviors.BanChatSilent(this))
                        SysMsg("说话太频繁，当前已被禁言60秒。", MsgColor.Red, MsgType.Hint);
                    return;
                }
                if (elapsed >= 0x7D0 && m_btSayRapidCount >= 1)
                    m_btSayRapidCount--;
                if (elapsed >= 0x1388 && m_nSayMsgCount >= 1)
                    m_nSayMsgCount--;
                m_dwSayMsgTick = now;
                m_sOldSayMsg = sData;
                if (HUtil32.GetTickCount() >= m_dwDisableSayMsgTick)
                {
                    m_boDisableSayMsg = false;
                }
                boDisableSayMsg = m_boDisableSayMsg;
                if (NativeMirrorChatBan.Contains(this.m_sCharName))
                {
                    boDisableSayMsg = true;
                }
                if (!(boDisableSayMsg || m_PEnvir.Flag.boNOCHAT))
                {
                    M2Share.AppendChatLog(m_sCharName, sData);
                    m_sOldSayMsg = sData;
                    if (sData.StartsWith("@@加速处理"))
                    {
                        M2Share.g_FunctionNPC?.GotoLable(this, "@加速处理", false);
                        return;
                    }
                    switch (sData[0])
                    {
                        case '/':
                            {
                                sC = sData.Substring(1, sData.Length - 1);
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                
                                sC = HUtil32.GetValidStr3(sC, ref sParam1, new[] { " " });
                                if (!m_boFilterSendMsg)
                                {
                                    Whisper(sParam1, sC);
                                }
                                return;
                            }
                        case '!':
                            {
                                if (sData.Length >= 1)
                                {
                                    if (sData[1] == '!') 
                                    {
                                        sC = sData.Substring(3 - 1, sData.Length - 2);
                                        SendGroupText(m_sCharName + ": " + sC);
                                        M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_208, M2Share.nServerIndex, m_sCharName + "/:" + sC);
                                        return;
                                    }
                                    if (sData[1] == '~' && m_MyGuild != null)
                                    {
                                        sC = sData.Substring(2, sData.Length - 2);
                                        m_MyGuild.SendGuildMsg(m_sCharName + ": " + sC);
                                        M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_208, M2Share.nServerIndex, m_MyGuild.sGuildName + '/' + m_sCharName + '/' + sC);
                                        return;
                                    }
                                    // 战神 sub_6BB2F8 @6BB74E cmp al,0x23 ('#') -> 6BB767
                                    // call sub_6F7B10 = CORPS (战队) chat, placed here so it is
                                    // tested after '!' and '~' and BEFORE the shout fallback,
                                    // exactly as native orders its compare chain. Without this
                                    // branch "!#text" fell through to the shout ladder below and
                                    // was broadcast to every player within 50 tiles.
                                    if (sData[1] == '#' && TryProcessNativeCorpsChat(sData))
                                    {
                                        return;
                                    }
                                }
                                if (!m_PEnvir.Flag.boQUIZ)
                                {
                                    // 0x6BB7A0 C6 45 FF 0F mov byte [ebp-1],0xF / 0x6BB7BD 69 C0 E8 03 00 00 imul 1000 / 0x6BB7C6 77 ja wait if 15000>elapsed
                                    if ((HUtil32.GetTickCount() - m_dwShoutMsgTick) >= 15 * 1000)
                                    {
                                        if (m_Abil.Level <= M2Share.g_Config.nCanShoutMsgLevel)
                                        {
                                            // 0x6BBA48 "喊话功能只有%d级以上才可以使用"
                                            // Format arg is the raw threshold at [0x7D6478]=7, not +1.
                                            SysMsg(format("喊话功能只有{0}级以上才可以使用",
                                                M2Share.g_Config.nCanShoutMsgLevel), MsgColor.Red, MsgType.Hint);
                                            return;
                                        }
                                        m_dwShoutMsgTick = HUtil32.GetTickCount();
                                        sC = sData.Substring(1, sData.Length - 1);
                                        sCryCryMsg = "(!)" + m_sCharName + ": " + sC;
                                        if (m_boFilterSendMsg)
                                        {
                                            SendMsg(null, Grobal2.RM_CRY, 0, 0, 0xFFFF, 0, sCryCryMsg);
                                        }
                                        else
                                        {
                                            M2Share.UserEngine.CryCry(Grobal2.RM_CRY, m_PEnvir, m_nCurrX, m_nCurrY, 50, M2Share.g_Config.btCryMsgFColor, M2Share.g_Config.btCryMsgBColor, sCryCryMsg);
                                        }
                                        return;
                                    }
                                    // 0x6BBA30 " 秒后才可以喊话" concatenated onto IntToStr(remain).
                                    SysMsg((15 - (HUtil32.GetTickCount() - m_dwShoutMsgTick) / 1000)
                                        + " 秒后才可以喊话", MsgColor.Red, MsgType.Hint);
                                    return;
                                }
                                SysMsg(M2Share.g_sThisMapDisableSendCyCyMsg, MsgColor.Red, MsgType.Hint);
                                return;
                            }
                    }
                    // 彩色文字: while obj+0xBD4 is running, 0x6C9406 diverts public
                    // speech to the coloured route -- a different ident (105, not
                    // 40) and the tier's colour word. The gate reads the COUNTDOWN,
                    // not the tier, so a stale tier byte alone changes nothing.
                    if (NativeColorSayIsActive())
                    {
                        var color = NativeColorSayCurrentColor();
                        // The server treats the colour as one opaque 16-bit token
                        // (no split anywhere on the native path); C# reaches the
                        // wire through a byte pair, so split it here only.
                        var fColor = unchecked((byte)(color & 0xFF));
                        var bColor = unchecked((byte)(color >> 8));
                        if (m_boFilterSendMsg)
                        {
                            SendMsg(this, Grobal2.RM_COLORHEAR, 0, fColor, bColor, 0,
                                m_sCharName + ':' + sData);
                        }
                        else
                        {
                            SendRefMsg(Grobal2.RM_COLORHEAR, 0, fColor, bColor, 0,
                                m_sCharName + ':' + sData);
                        }
                        return;
                    }
                    if (m_boFilterSendMsg)
                    {
                        SendMsg(this, Grobal2.RM_HEAR, 0, M2Share.g_Config.btHearMsgFColor, M2Share.g_Config.btHearMsgBColor, 0, m_sCharName + ':' + sData);// 如果禁止发信息，则只向自己发信息
                    }
                    else
                    {
                        base.ProcessSayMsg(sData);
                    }
                    return;
                }
                // Whisper mute is silent: 0x6C9523 80 7D 08 00 / jne exit.
                // Guild (0x6BB719) is the path that SysMsgs 0x6BBA18.
                if (sData.Length >= 1 && sData[0] == '/')
                    return;
                // 0x6BBA18 / 0x6C9758 "已经被禁止聊天"
                // 眼神 禁止发言不提示 @0x6C94A9 memcpy 跳过 DenySay 名单 SysMsg（apply 0x100DB874）。
                if (!YanshenConfig12Behaviors.BanChatSilent(this))
                    SysMsg("已经被禁止聊天", MsgColor.Red, MsgType.Hint);
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(format(sExceptionMsg, sData));
                M2Share.ErrorMessage(e.StackTrace);
            }
        }

        internal void ProcessUserLineMsg(string sData)
        {
            ProcessUserLineMsg(sData, null, 0);
        }

        internal void ProcessUserLineMsg(string sData, byte[] rawPayload,
            int bodyLength)
        {
            string sC;
            var sCMD = string.Empty;
            var sParam1 = string.Empty;
            var sParam2 = string.Empty;
            var sParam3 = string.Empty;
            var sParam4 = string.Empty;
            var sParam5 = string.Empty;
            var sParam6 = string.Empty;
            var sParam7 = string.Empty;
            TPlayObject PlayObject;
            int nFlag;
            int nValue;
            int nLen;
            const string sExceptionMsg = "[Exception] TPlayObject::ProcessUserLineMsg Msg = {0}";
            try
            {
                nLen = sData.Length;
                if (nLen <= 0)
                {
                    return;
                }
                if (m_boSetStoragePwd)
                {
                    m_boSetStoragePwd = false;
                    if (nLen > 3 && nLen < 8)
                    {
                        m_sTempPwd = sData;
                        m_boReConfigPwd = true;
                        SysMsg(M2Share.g_sReSetPasswordMsg, MsgColor.Green, MsgType.Hint);
                        SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                    }
                    else
                    {
                        SysMsg(M2Share.g_sPasswordOverLongMsg, MsgColor.Red, MsgType.Hint);// '输入的密码长度不正确!!!，密码长度必须在 4 - 7 的范围内，请重新设置密码。'
                    }
                    return;
                }
                if (m_boReConfigPwd)
                {
                    m_boReConfigPwd = false;
                    if (string.Compare(m_sTempPwd, sData, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        m_sStoragePwd = sData;
                        m_boPasswordLocked = true;
                        m_boCanGetBackItem = false;
                        m_sTempPwd = "";
                        SysMsg(M2Share.g_sReSetPasswordOKMsg, MsgColor.Blue, MsgType.Hint);
                    }
                    else
                    {
                        m_sTempPwd = "";
                        SysMsg(M2Share.g_sReSetPasswordNotMatchMsg, MsgColor.Red, MsgType.Hint);
                    }
                    return;
                }
                if (m_boUnLockPwd || m_boUnLockStoragePwd)
                {
                    if (string.Compare(m_sStoragePwd, sData, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        m_boPasswordLocked = false;
                        if (m_boUnLockPwd)
                        {
                            if (M2Share.g_Config.boLockDealAction)
                            {
                                m_boCanDeal = true;
                            }
                            if (M2Share.g_Config.boLockDropAction)
                            {
                                m_boCanDrop = true;
                            }
                            if (M2Share.g_Config.boLockWalkAction)
                            {
                                m_boCanWalk = true;
                            }
                            if (M2Share.g_Config.boLockRunAction)
                            {
                                m_boCanRun = true;
                            }
                            if (M2Share.g_Config.boLockHitAction)
                            {
                                m_boCanHit = true;
                            }
                            if (M2Share.g_Config.boLockSpellAction)
                            {
                                m_boCanSpell = true;
                            }
                            if (M2Share.g_Config.boLockSendMsgAction)
                            {
                                m_boCanSendMsg = true;
                            }
                            if (M2Share.g_Config.boLockUserItemAction)
                            {
                                m_boCanUseItem = true;
                            }
                            if (M2Share.g_Config.boLockInObModeAction)
                            {
                                m_boObMode = false;
                                m_boAdminMode = false;
                            }
                            m_boLockLogoned = true;
                            SysMsg(M2Share.g_sPasswordUnLockOKMsg, MsgColor.Blue, MsgType.Hint);
                        }
                        if (m_boUnLockStoragePwd)
                        {
                            if (M2Share.g_Config.boLockGetBackItemAction)
                            {
                                m_boCanGetBackItem = true;
                            }
                            SysMsg(M2Share.g_sStorageUnLockOKMsg, MsgColor.Blue, MsgType.Hint);
                        }
                    }
                    else
                    {
                        m_btPwdFailCount++;
                        SysMsg(M2Share.g_sUnLockPasswordFailMsg, MsgColor.Red, MsgType.Hint);
                        if (m_btPwdFailCount > 3)
                        {
                            SysMsg(M2Share.g_sStoragePasswordLockedMsg, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    m_boUnLockPwd = false;
                    m_boUnLockStoragePwd = false;
                    return;
                }
                if (m_boCheckOldPwd)
                {
                    m_boCheckOldPwd = false;
                    if (m_sStoragePwd == sData)
                    {
                        SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                        SysMsg(M2Share.g_sSetPasswordMsg, MsgColor.Green, MsgType.Hint);
                        m_boSetStoragePwd = true;
                    }
                    else
                    {
                        m_btPwdFailCount++;
                        SysMsg(M2Share.g_sOldPasswordIncorrectMsg, MsgColor.Red, MsgType.Hint);
                        if (m_btPwdFailCount > 3)
                        {
                            SysMsg(M2Share.g_sStoragePasswordLockedMsg, MsgColor.Red, MsgType.Hint);
                            m_boPasswordLocked = true;
                        }
                    }
                    return;
                }
                if (!sData.StartsWith("@"))
                {
                    ProcessSayMsg(sData);
                    return;
                }
                sC = sData.Substring(1, sData.Length - 1);
                sC = HUtil32.GetValidStr3(sC, ref sCMD, new[] { " ", ":", ",", "\t" });
                if (sC != "")
                {
                    sC = HUtil32.GetValidStr3(sC, ref sParam1, new[] { " ", ":", ",", "\t" });
                }
                if (sC != "")
                {
                    sC = HUtil32.GetValidStr3(sC, ref sParam2, new[] { " ", ":", ",", "\t" });
                }
                if (sC != "")
                {
                    sC = HUtil32.GetValidStr3(sC, ref sParam3, new[] { " ", ":", ",", "\t" });
                }
                if (sC != "")
                {
                    sC = HUtil32.GetValidStr3(sC, ref sParam4, new[] { " ", ":", ",", "\t" });
                }
                if (sC != "")
                {
                    sC = HUtil32.GetValidStr3(sC, ref sParam5, new[] { " ", ":", ",", "\t" });
                }
                if (sC != "")
                {
                    sC = HUtil32.GetValidStr3(sC, ref sParam6, new[] { " ", ":", ",", "\t" });
                }
                if (sC != "")
                {
                    sC = HUtil32.GetValidStr3(sC, ref sParam7, new[] { " ", ":", ",", "\t" });
                }
                
                if (string.Compare(sCMD, M2Share.g_GameCommand.PASSWORDLOCK.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!M2Share.g_Config.boPasswordLockSystem)
                    {
                        SysMsg(M2Share.g_sNoPasswordLockSystemMsg, MsgColor.Red, MsgType.Hint);
                        return;
                    }
                    if (m_sStoragePwd == "")
                    {
                        SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                        m_boSetStoragePwd = true;
                        SysMsg(M2Share.g_sSetPasswordMsg, MsgColor.Green, MsgType.Hint);
                        return;
                    }
                    if (m_btPwdFailCount > 3)
                    {
                        SysMsg(M2Share.g_sStoragePasswordLockedMsg, MsgColor.Red, MsgType.Hint);
                        m_boPasswordLocked = true;
                        return;
                    }
                    if (!string.IsNullOrEmpty(m_sStoragePwd))
                    {
                        SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                        m_boCheckOldPwd = true;
                        SysMsg(M2Share.g_sPleaseInputOldPasswordMsg, MsgColor.Green, MsgType.Hint);
                        return;
                    }
                    return;
                }
                if (TrySetAllowMarryCommand(sCMD)
                    || TrySetAllowMasterCommand(sCMD))
                {
                    return;
                }
                if (M2Share.CommandSystem.ExecCmd(sData, this, rawPayload,
                        bodyLength))
                {
                    return;
                }
                
                if (string.Compare(sCMD, M2Share.g_GameCommand.SETPASSWORD.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!M2Share.g_Config.boPasswordLockSystem)
                    {
                        SysMsg(M2Share.g_sNoPasswordLockSystemMsg, MsgColor.Red, MsgType.Hint);
                        return;
                    }
                    if (m_sStoragePwd == "")
                    {
                        SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                        m_boSetStoragePwd = true;
                        SysMsg(M2Share.g_sSetPasswordMsg, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sAlreadySetPasswordMsg, MsgColor.Red, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.UNPASSWORD.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!M2Share.g_Config.boPasswordLockSystem)
                    {
                        SysMsg(M2Share.g_sNoPasswordLockSystemMsg, MsgColor.Red, MsgType.Hint);
                        return;
                    }
                    if (!m_boPasswordLocked)
                    {
                        m_sStoragePwd = "";
                        SysMsg(M2Share.g_sOldPasswordIsClearMsg, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sPleaseUnLockPasswordMsg, MsgColor.Red, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.CHGPASSWORD.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!M2Share.g_Config.boPasswordLockSystem)
                    {
                        SysMsg(M2Share.g_sNoPasswordLockSystemMsg, MsgColor.Red, MsgType.Hint);
                        return;
                    }
                    if (m_btPwdFailCount > 3)
                    {
                        SysMsg(M2Share.g_sStoragePasswordLockedMsg, MsgColor.Red, MsgType.Hint);
                        m_boPasswordLocked = true;
                        return;
                    }
                    if (m_sStoragePwd != "")
                    {
                        SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                        m_boCheckOldPwd = true;
                        SysMsg(M2Share.g_sPleaseInputOldPasswordMsg, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sNoPasswordSetMsg, MsgColor.Red, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.UNLOCKSTORAGE.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!M2Share.g_Config.boPasswordLockSystem)
                    {
                        SysMsg(M2Share.g_sNoPasswordLockSystemMsg, MsgColor.Red, MsgType.Hint);
                        return;
                    }
                    if (m_btPwdFailCount > M2Share.g_Config.nPasswordErrorCountLock)
                    {
                        SysMsg(M2Share.g_sStoragePasswordLockedMsg, MsgColor.Red, MsgType.Hint);
                        m_boPasswordLocked = true;
                        return;
                    }
                    if (m_sStoragePwd != "")
                    {
                        if (!m_boUnLockStoragePwd)
                        {
                            SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                            SysMsg(M2Share.g_sPleaseInputUnLockPasswordMsg, MsgColor.Green, MsgType.Hint);
                            m_boUnLockStoragePwd = true;
                        }
                        else
                        {
                            SysMsg(M2Share.g_sStorageAlreadyUnLockMsg, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sStorageNoPasswordMsg, MsgColor.Red, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.UNLOCK.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!M2Share.g_Config.boPasswordLockSystem)
                    {
                        SysMsg(M2Share.g_sNoPasswordLockSystemMsg, MsgColor.Red, MsgType.Hint);
                        return;
                    }
                    if (m_btPwdFailCount > M2Share.g_Config.nPasswordErrorCountLock)
                    {
                        SysMsg(M2Share.g_sStoragePasswordLockedMsg, MsgColor.Red, MsgType.Hint);
                        m_boPasswordLocked = true;
                        return;
                    }
                    if (m_sStoragePwd != "")
                    {
                        if (!m_boUnLockPwd)
                        {
                            SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                            SysMsg(M2Share.g_sPleaseInputUnLockPasswordMsg, MsgColor.Green, MsgType.Hint);
                            m_boUnLockPwd = true;
                        }
                        else
                        {
                            SysMsg(M2Share.g_sStorageAlreadyUnLockMsg, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sStorageNoPasswordMsg, MsgColor.Red, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.__LOCK.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!M2Share.g_Config.boPasswordLockSystem)
                    {
                        SysMsg(M2Share.g_sNoPasswordLockSystemMsg, MsgColor.Red, MsgType.Hint);
                        return;
                    }
                    if (!m_boPasswordLocked)
                    {
                        if (m_sStoragePwd != "")
                        {
                            m_boPasswordLocked = true;
                            m_boCanGetBackItem = false;
                            SysMsg(M2Share.g_sLockStorageSuccessMsg, MsgColor.Green, MsgType.Hint);
                        }
                        else
                        {
                            SysMsg(M2Share.g_sStorageNoPasswordMsg, MsgColor.Green, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sStorageAlreadyLockMsg, MsgColor.Red, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.ALLOWDEARRCALL.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boCanDearRecall = !m_boCanDearRecall;
                    if (m_boCanDearRecall)
                    {
                        SysMsg(M2Share.g_sEnableDearRecall, MsgColor.Blue, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sDisableDearRecall, MsgColor.Blue, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.ALLOWMASTERRECALL.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boCanMasterRecall = !m_boCanMasterRecall;
                    if (m_boCanMasterRecall)
                    {
                        SysMsg(M2Share.g_sEnableMasterRecall, MsgColor.Blue, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sDisableMasterRecall, MsgColor.Blue, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.DATA.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    SysMsg(M2Share.g_sNowCurrDateTime + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), MsgColor.Blue, MsgType.Hint);
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.ALLOWMSG.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boHearWhisper = !m_boHearWhisper;
                    if (m_boHearWhisper)
                    {
                        m_dwChatShieldMask &= ~0x01u;
                        SysMsg(M2Share.g_sEnableHearWhisper, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        m_dwChatShieldMask |= 0x01u;
                        SysMsg(M2Share.g_sDisableHearWhisper, MsgColor.Green, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.LETSHOUT.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boBanShout = !m_boBanShout;
                    if (m_boBanShout)
                    {
                        m_dwChatShieldMask &= ~0x04u;
                        SysMsg(M2Share.g_sEnableShoutMsg, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        m_dwChatShieldMask |= 0x04u;
                        SysMsg(M2Share.g_sDisableShoutMsg, MsgColor.Green, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.LETTRADE.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boAllowDeal = !m_boAllowDeal;
                    if (m_boAllowDeal)
                    {
                        SysMsg(M2Share.g_sEnableDealMsg, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sDisableDealMsg, MsgColor.Green, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.BANGUILDCHAT.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boBanGuildChat = !m_boBanGuildChat;
                    if (m_boBanGuildChat)
                    {
                        m_dwChatShieldMask &= ~0x08u;
                        SysMsg(M2Share.g_sEnableGuildChat, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        m_dwChatShieldMask |= 0x08u;
                        SysMsg(M2Share.g_sDisableGuildChat, MsgColor.Green, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.LETGUILD.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boAllowGuild = !m_boAllowGuild;
                    if (m_boAllowGuild)
                    {
                        SysMsg(M2Share.g_sEnableJoinGuild, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sDisableJoinGuild, MsgColor.Green, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.AUTHALLY.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (IsGuildMaster())
                    {
                        m_MyGuild.m_boEnableAuthAlly = !m_MyGuild.m_boEnableAuthAlly;
                        if (m_MyGuild.m_boEnableAuthAlly)
                        {
                            SysMsg(M2Share.g_sEnableAuthAllyGuild, MsgColor.Green, MsgType.Hint);
                        }
                        else
                        {
                            SysMsg(M2Share.g_sDisableAuthAllyGuild, MsgColor.Green, MsgType.Hint);
                        }
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.ALLOWGUILDRECALL.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boAllowGuildReCall = !m_boAllowGuildReCall;
                    if (m_boAllowGuildReCall)
                    {
                        SysMsg(M2Share.g_sEnableGuildRecall, MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sDisableGuildRecall, MsgColor.Green, MsgType.Hint);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.AUTH.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (IsGuildMaster())
                    {
                        ClientGuildAlly();
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.AUTHCANCEL.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (IsGuildMaster())
                    {
                        ClientGuildBreakAlly(sParam1);
                    }
                    return;
                }
                if (string.Compare(sCMD, M2Share.g_GameCommand.MAPINFO.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    ShowMapInfo(sParam1, sParam2, sParam3);
                    return;
                }
                if (m_btPermission >= 2 && sData.Length > 2)
                {
                    if (m_btPermission >= 6 && sData[2] == M2Share.g_GMRedMsgCmd)
                    {
                        if (HUtil32.GetTickCount() - m_dwSayMsgTick > 2000)
                        {
                            m_dwSayMsgTick = HUtil32.GetTickCount();
                            sData = sData.Substring(2, sData.Length - 2);
                            if (sData.Length > M2Share.g_Config.nSayRedMsgMaxLen)
                            {
                                sData = sData.Substring(0, M2Share.g_Config.nSayRedMsgMaxLen);
                            }
                            if (M2Share.g_Config.boShutRedMsgShowGMName)
                            {
                                sC = m_sCharName + ": " + sData;
                            }
                            else
                            {
                                sC = sData;
                            }
                            M2Share.UserEngine.SendBroadCastMsg(sC, MsgType.GM);
                        }
                        return;
                    }
                }
                if (m_btPermission > 4)
                {
                    if (string.Compare(sCMD, M2Share.g_GameCommand.SETFLAG.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        PlayObject = M2Share.UserEngine.GetPlayObject(sParam1);
                        if (PlayObject != null)
                        {
                            nFlag = HUtil32.Str_ToInt(sParam2, 0);
                            nValue = HUtil32.Str_ToInt(sParam3, 0);
                            PlayObject.SetQuestFlagStatus(nFlag, nValue);
                            if (PlayObject.GetQuestFalgStatus(nFlag) == 1)
                            {
                                SysMsg(PlayObject.m_sCharName + ": [" + nFlag + "] = ON", MsgColor.Green, MsgType.Hint);
                            }
                            else
                            {
                                SysMsg(PlayObject.m_sCharName + ": [" + nFlag + "] = OFF", MsgColor.Green, MsgType.Hint);
                            }
                        }
                        else
                        {
                            SysMsg('@' + M2Share.g_GameCommand.SETFLAG.sCmd + " 人物名称 标志号 数字(0 - 1)", MsgColor.Red, MsgType.Hint);
                        }
                        return;
                    }
                    if (string.Compare(sCMD, M2Share.g_GameCommand.SETOPEN.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        PlayObject = M2Share.UserEngine.GetPlayObject(sParam1);
                        if (PlayObject != null)
                        {
                            nFlag = HUtil32.Str_ToInt(sParam2, 0);
                            nValue = HUtil32.Str_ToInt(sParam3, 0);
                            PlayObject.SetQuestUnitOpenStatus(nFlag, nValue);
                            if (PlayObject.GetQuestUnitOpenStatus(nFlag) == 1)
                            {
                                SysMsg(PlayObject.m_sCharName + ": [" + nFlag + "] = ON", MsgColor.Green, MsgType.Hint);
                            }
                            else
                            {
                                SysMsg(PlayObject.m_sCharName + ": [" + nFlag + "] = OFF", MsgColor.Green, MsgType.Hint);
                            }
                        }
                        else
                        {
                            SysMsg('@' + M2Share.g_GameCommand.SETOPEN.sCmd + " 人物名称 标志号 数字(0 - 1)", MsgColor.Red, MsgType.Hint);
                        }
                        return;
                    }
                    if (string.Compare(sCMD, M2Share.g_GameCommand.SETUNIT.sCmd, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        PlayObject = M2Share.UserEngine.GetPlayObject(sParam1);
                        if (PlayObject != null)
                        {
                            nFlag = HUtil32.Str_ToInt(sParam2, 0);
                            nValue = HUtil32.Str_ToInt(sParam3, 0);
                            PlayObject.SetQuestUnitStatus(nFlag, nValue);
                            if (PlayObject.GetQuestUnitStatus(nFlag) == 1)
                            {
                                SysMsg(PlayObject.m_sCharName + ": [" + nFlag + "] = ON", MsgColor.Green, MsgType.Hint);
                            }
                            else
                            {
                                SysMsg(PlayObject.m_sCharName + ": [" + nFlag + "] = OFF", MsgColor.Green, MsgType.Hint);
                            }
                        }
                        else
                        {
                            SysMsg('@' + M2Share.g_GameCommand.SETUNIT.sCmd + " 人物名称 标志号 数字(0 - 1)", MsgColor.Red, MsgType.Hint);
                        }
                        return;
                    }
                }
                SysMsg($"@{sCMD}此命令不正确，或没有足够的权限!!!", MsgColor.Red, MsgType.Hint);
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(format(sExceptionMsg, sData));
                M2Share.ErrorMessage(e.Message);
            }
        }

        private bool TrySetAllowMarryCommand(string command)
        {
            if (string.Equals(command, "拒绝求婚",
                    StringComparison.OrdinalIgnoreCase))
            {
                m_boAllowMarry = false;
                SysMsg("拒绝求婚 开", MsgColor.Green, MsgType.Hint);
                return true;
            }
            if (string.Equals(command, "允许求婚",
                    StringComparison.OrdinalIgnoreCase))
            {
                m_boAllowMarry = true;
                SysMsg("允许求婚 开", MsgColor.Green, MsgType.Hint);
                return true;
            }
            return false;
        }

        private bool TrySetAllowMasterCommand(string command)
        {
            if (string.Equals(command, "拒绝收徒",
                    StringComparison.OrdinalIgnoreCase))
            {
                m_boAllowMaster = false;
                SysMsg("拒绝收徒 开", MsgColor.Green, MsgType.Hint);
                return true;
            }
            if (string.Equals(command, "允许收徒",
                    StringComparison.OrdinalIgnoreCase))
            {
                m_boAllowMaster = true;
                SysMsg("允许收徒 开", MsgColor.Green, MsgType.Hint);
                return true;
            }
            return false;
        }
    }
}

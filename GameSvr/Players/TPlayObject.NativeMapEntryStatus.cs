using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // 战神 sub_6B6BEC @0x006B6BEC —— 进图/换图「状态刷新」函数。原生流程：先累加环境
        // 特征位 esi，再经 vmt+0x250(=SendDefMessage) 发 ident=0x2C4(SM_MYSTATUS=708) 特征字，
        // 期间/之后还发两组「进图通告」。特征字由既有的 RefUserState() 承担（该处 esi 位构成
        // 与本函数无关，属另一议题）；本仓库先前完全缺失的是这两组通告(MOVE-85 判 MISSING，
        // 全镜像 GBK+裸ASCII+UTF-16LE 三编码扫描 0 命中)：
        //   ① 超负重通告  (sub_6B6BEC bit3 块 @0x6B6C34..0x6B6C62)
        //   ② 三档「巅峰状态」通告 (@0x6B6C99..0x6B6D00，按本图 暴击等级+狂暴等级 之和分档)
        //
        // 两组通告都走 vmt+0xD4（硬编码 cx=0xFCFF）= SysMsg(RM_SYSMESSAGE, 前景0xFF/背景0xFC)，
        // 与马牌拒绝(MOVE-83/84 的 SendNativeHorseSystemMessage)字节同构——不可改用
        // SysMsg(Blue/Hint)，后者会套用配置色 btBlueMsgFColor/BColor 并在 boShowPreFixMsg 时
        // 前置 sHintMsgPreFix，皆非原生行为。原生两条通告经 vmt+0xD4 入消息队列(RM_SYSMESSAGE)，
        // 特征字经 vmt+0x250 直发 socket，故三者相对上线顺序与本函数/RefUserState 的调用先后无关。
        //
        // 原生三处调用点(独立反汇编 e8 rel32 扫描)：
        //   0x006B954D  进图序列(SM_LOGON=50 经 vmt+0x254 之后)     -> C# RM_LOGON 的 RefUserState() 处
        //   0x006B96C2  换图序列(SM_CHANGEMAP=634 经 vmt+0x250 之后) -> C# RM_NATIVE_CHANGEMAP 的 RefUserState() 处
        //   0x006B6B78  [self+0x4C4]("是否在FreePK区",=call 0x659FD4(Envir,X,Y)) setter 变化时刷新
        internal void SendNativeMapEntryStateMessages()
        {
            if (m_PEnvir == null)
            {
                return;
            }

            // ① 超负重通告 —— sub_6B6BEC @0x6B6C34（与 CM_RUN 权重闸 MOVE-17 同一对读点）：
            //   0x6B6C34  A1 38 70 7D 00        mov  eax,[0x7D7038]      ; ServerSwitches
            //   0x6B6C39  F6 40 02 80           test byte [eax+2],0x80   ; 全局超负重开关
            //   0x6B6C3D  74 25                 je   0x6B6C64            ; 关：不发
            //   0x6B6C3F  8B 83 28 01 00 00     mov  eax,[ebx+0x128]     ; 本图 Envir
            //   0x6B6C45  80 B8 B0 00 00 00 00  cmp  byte [eax+0xB0],0   ; RUNFLAG(=NativeCanRunWhileOverweight)
            //   0x6B6C4C  75 16                 jne  0x6B6C64            ; 本图豁免(可超负重跑)：不发
            //   0x6B6C51  66 B9 FF FC           mov  cx,0xFCFF
            //   0x6B6C55  BA 10 6D 6B 00        mov  edx,0x6B6D10        ; 串@0x6B6D10 len38
            //   0x6B6C5E  FF 97 D4 00 00 00     call [edi+0xD4]
            if (M2Share.ServerSwitches.IsBitSet(2, 0x80) &&
                !m_PEnvir.NativeCanRunWhileOverweight)
            {
                SendNativeMapEntryStateSysMessage(
                    "此地图包裹超负重状态下将不能跑动!!!!!!");
            }

            // ② 三档「巅峰状态」通告 —— sub_6B6BEC @0x6B6C99：
            //   0x6B6CA1  8A 90 B8 00 00 00     mov  dl,byte [Envir+0xB8]   ; 暴击等级 BreakLevel(byte,BREAKLEVEL图标)
            //   0x6B6CA7  0F B7 80 BA 00 00 00  movzx eax,word [Envir+0xBA] ; 狂暴等级 CrazyBreakLevel(word,CRAZYBREAKLEVEL)
            //   0x6B6CAE  03 D0                 add  edx,eax                ; total = 暴击等级 + 狂暴等级
            //   0x6B6CB0  8B C2                 mov  eax,edx
            //   0x6B6CB2  3D 96 00 00 00 / 7C 15  cmp eax,0x96 / jl 0x6B6CCE ; >=150 -> 巅峰战神
            //   0x6B6CCE  83 F8 32 / 7C 15        cmp eax,0x32 / jl 0x6B6CE8 ; >=50  -> 巅峰勇士
            //   0x6B6CE8  83 F8 0A / 7E 13        cmp eax,0x0A / jle 0x6B6D00; >10(严格) -> 攻击提升
            var total = m_PEnvir.BreakLevel + m_PEnvir.CrazyBreakLevel;
            if (total >= 0x96)
            {
                // 串@0x6B6D40 len48：半角逗号(0x2C)
                SendNativeMapEntryStateSysMessage(
                    "您在此地图临时获得巅峰战神状态,攻击能力大幅提升!");
            }
            else if (total >= 0x32)
            {
                // 串@0x6B6D7C len45：全角逗号(0xA3AC)
                SendNativeMapEntryStateSysMessage(
                    "您在此地图临时获得巅峰勇士状态，攻击能力提升!");
            }
            else if (total > 0x0A)
            {
                // 串@0x6B6DB4 len27
                SendNativeMapEntryStateSysMessage(
                    "您在此地图临时攻击能力提升!");
            }
        }

        private void SendNativeMapEntryStateSysMessage(string message)
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC, 0, message);
        }
    }
}

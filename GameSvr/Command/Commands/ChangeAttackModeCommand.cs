using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("AttackMode", "更改个人攻击模式", 0)]
    public class ChangeAttackModeCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChangeAttackMode(TPlayObject PlayObject)
        {
            // 注册表 0x007B6274 idx=26 perm=0，case@0x006239FA 全body只有 11 条指令：
            //   006239FD  80 b8 ed 0a 00 00 05  cmp byte [eax+0xAED],5
            //   00623A04  73 0b                 jae 0x623A11      ; >=5 归零
            //   00623A09  fe 80 ed 0a 00 00     inc byte [eax+0xAED]
            //   00623A14  c6 80 ed 0a 00 00 00  mov byte [eax+0xAED],0
            //   00623A2E  66 ba 21 02           mov dx,0x221      ; SM_ATTACKMODE=545
            //   00623A37  ff 93 50 02 00 00     call [ebx+0x250]
            //   00623A3D  e9 0a 7c 00 00        jmp 0x62B64C      ; 静默出口，无 SysMsg
            PlayObject.m_btAttatckMode =
                PlayObject.m_btAttatckMode >= TPlayObject.NativeAttackModeCorps
                    ? TPlayObject.NativeAttackModeAll
                    : (byte)(PlayObject.m_btAttatckMode + 1);
            PlayObject.SendDefMessage(Grobal2.SM_ATTACKMODE, PlayObject.m_btAttatckMode, 0, 0, 0, "");
        }
    }
}

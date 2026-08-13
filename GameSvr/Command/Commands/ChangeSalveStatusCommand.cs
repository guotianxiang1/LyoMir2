using GameSvr.CommandSystem;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("Rest", "调整当前玩家属下状态", 0)]
    public class ChangeSalveStatusCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChangeSalveStatus(TPlayObject PlayObject)
        {
            // 眼神「禁止宝宝休息」只替换 0x00623A73 那一条翻转指令
            //   80 B0 C7 04 00 00 01   xor byte [player+0x4C7],1
            // 换成 17 字节桩（安装点 0x100AABB6 call 0x10032FD0，续跑点 0x00623A7A）：
            //   80 B8 15 01 00 00 0F   cmp byte [player+0x115],0x0F
            //   74 07                  je  skip
            //   80 B0 C7 04 00 00 01   xor byte [player+0x4C7],1
            //   E9 <rel32>             jmp 0x00623A7A
            // 被跳过的只有翻转本身。0x00623A7D 起的
            //   cmp byte [player+0x4C7],0 / je / call [vtbl+0xD4]
            // 两条 SysMsg 在续跑点之后，照常发送，播报的是**未被改动**的状态；
            // 而且原指令是 xor 不是 set，所以两个切换方向都被拦下。
            if (!new YanshenApi(PlayObject, null, M2Share.PluginManager).IsPetRestBlocked())
            {
                PlayObject.m_boSlaveRelax = !PlayObject.m_boSlaveRelax;
            }

            if (PlayObject.m_SlaveList.Count > 0)
            {
                if (PlayObject.m_boSlaveRelax)
                {
                    PlayObject.SysMsg(M2Share.sPetRest, MsgColor.Green, MsgType.Hint);
                }
                else
                {
                    PlayObject.SysMsg(M2Share.sPetAttack, MsgColor.Green, MsgType.Hint);
                }
            }
        }
    }
}

using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ChgHideState", "改变隐身状态", 4)]
    public class ChgHideStateCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgHideState(TPlayObject PlayObject)
        {
            // 原版 sub_625076 (idx102, perm 4): 无参，切换 GM 自身隐身位 [+0x2E4]=m_boHideMode，
            // 广播身体特效 0x17 (vtbl+0x1A8)，不发 SysMsg / 不写 MainOutMessage。
            // (逆向证据: gm-playerattr staging/gm_player_attr_commands_20260801.md — case102 全反编译。)
            PlayObject.m_boHideMode = !PlayObject.m_boHideMode;
            // RM_DISAPPEAR 触发附近客户端重取角色以刷新隐身状态，效果等价；
            // 字节级精确的 vtbl+0x1A8/特效码 0x17
            // 广播待玩家 VMT 经 RTTI 隔离后再替换 (当前 dump 的 VMT 密度扫描被大指针区淹没)。
            PlayObject.SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
        }
    }
}

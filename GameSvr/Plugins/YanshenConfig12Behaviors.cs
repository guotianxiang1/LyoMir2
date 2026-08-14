using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 配置1 / 配置2 页原生补丁的 C# 等价层。每个 helper 只包含有字节佐证的开关；
    /// 其余键在 <see cref="YanshenConfig12Registry"/> 登记为 BLOCKED。
    /// </summary>
    internal static class YanshenConfig12Behaviors
    {
        /// <summary>免毒符 — 12×memcpy 抹掉 DoSpell(sub_6ED62C) 内毒符/护身符门槛。
        /// 首站 0x6ED945 <c>mov byte [ebp-6],1</c>（4 B，插件 apply 0x100DA719）。</summary>
        internal static bool AntiPoisonAmuletFree(TPlayObject player)
        {
            if (player == null || M2Share.PluginManager == null)
                return false;
            return new YanshenApi(player, null, M2Share.PluginManager)
                .IsAntiPoisonPatchOn();
        }

        /// <summary>删除技能不提示 — sub_6C772C @0x6C7797 成功 SysMsg 五字节改 <c>jmp 0x6C781D</c>
        /// （apply 0x100DB4A4，payload <c>E9 81 00 00 00</c>）。</summary>
        internal static bool DelSkillSilent(TPlayObject player)
        {
            if (player == null || M2Share.PluginManager == null)
                return false;
            return new YanshenApi(player, null, M2Share.PluginManager)
                .IsDelSkillSilentPatchOn();
        }

        /// <summary>禁止发言不提示 — 三处 <c>call [obj+0xD4]</c> SysMsg 被 memcpy 跳过：
        /// 0x6BB5CD、0x6BB625（ProcessSayMsg 刷屏禁言，apply 0x100DB803/0x100DB83A）、
        /// 0x6C94A9（DenySay 名单拦截提示，apply 0x100DB874）。</summary>
        internal static bool BanChatSilent(TPlayObject player)
        {
            if (player == null || M2Share.PluginManager == null)
                return false;
            return new YanshenApi(player, null, M2Share.PluginManager)
                .IsBanChatSilentPatchOn();
        }
    }
}

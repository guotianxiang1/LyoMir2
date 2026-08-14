using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 盘古1 页配置键 → 宿主语义接线（键关 = 不改宿主行为）。
    /// 证据来源：flat_image.bin dis + ys_gui_patch_atlas_20260813.md。
    /// </summary>
    internal static class YanshenPangu1Patches
    {
        /// <summary>
        /// 穿人穿怪：启用时 <c>0x768454</c> 被改成 <c>B0 01 C3</c>（谓词恒真），
        /// <c>0x6B30A3</c> 强制 <c>[player+0x3FE]=1</c> 并走 TRUE 广播臂。
        /// </summary>
        internal static bool ForcesPassThrough()
        {
            var pm = M2Share.PluginManager;
            return pm != null && new YanshenApi(null, null, pm).PatchToggleOn("穿人穿怪");
        }

        /// <summary>
        /// 屏蔽元宝增减信息：启用时在多处 <c>0x6F8288</c> 等 8 站点把 SysMsg/客户端
        /// 更新臂改成 unconditional jmp，C# 等价 = 不发 <c>RM_GAMEGOLDCHANGED</c>。
        /// </summary>
        internal static bool ShouldSuppressGameGoldClientMsg()
        {
            var pm = M2Share.PluginManager;
            return pm != null && new YanshenApi(null, null, pm).PatchToggleOn("屏蔽元宝增减信息");
        }

        /// <summary>
        /// 屏蔽属性提升提示：启用时 31 个宿主 VA（<c>0x741A21</c>…<c>0x74298C</c>）
        /// 的 SysMsg 序言被 jmp 跳过；C# 落点 =
        /// <see cref="Actors.TBaseObject"/> 状态臂 <c>SendNativeStateArmMsg</c> 的
        /// 「瞬间提高 / 回复正常」类提示。
        /// </summary>
        internal static bool ShouldSuppressAttrUpHint(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var pm = M2Share.PluginManager;
            if (pm == null) return false;
            if (!new YanshenApi(null, null, pm).PatchToggleOn("屏蔽属性提升提示")) return false;
            return text.Contains("瞬间提高", StringComparison.Ordinal)
                || text.Contains("回复正常", StringComparison.Ordinal)
                || text.Contains("提高恢复正常", StringComparison.Ordinal);
        }

        /// <summary>
        /// 屏蔽元宝数据库日志：启用时 <c>0x70F6DC</c> 序言 <c>55</c>→<c>C3</c>，
        /// <c>sub_70F6B4</c>→<c>sub_70F6DC</c> 的 DB 写路径整体 stub；C# 落点 =
        /// <c>LOG_GAMEGOLD</c> 的 <see cref="M2Share.AddGameDataLog"/> 四处。
        /// </summary>
        internal static bool ShouldSuppressGameGoldDbLog()
        {
            var pm = M2Share.PluginManager;
            return pm != null && new YanshenApi(null, null, pm).PatchToggleOn("屏蔽元宝数据库日志");
        }

        /// <summary>
        /// 摆摊穿人：启用时 <c>0x77931D</c> 的 <c>C6 00 02</c>→<c>C6 00 00</c>，
        /// 摊格 claim 仍走 sub_7792EC 但属性写 Walk 而非 LowWall。
        /// </summary>
        internal static bool StallCellsAllowPassThrough()
        {
            var pm = M2Share.PluginManager;
            return pm != null && new YanshenApi(null, null, pm).PatchToggleOn("摆摊穿人");
        }

        /// <summary>
        /// 土城摆摊 / 指定地图编号摆摊：启用时 <c>0x6E7C5F</c> 的 <c>75 1D</c> 改成
        /// <c>EB 1D</c>，跳过 <c>sub_7684A0</c> 位置闸（START 的 -9 路径）。
        /// </summary>
        internal static bool BypassStallPositionGate(TPlayObject player)
        {
            var pm = M2Share.PluginManager;
            if (pm == null || player == null) return false;
            var api = new YanshenApi(player, null, pm);
            return api.PatchToggleOn("土城摆摊") || api.PatchToggleOn("指定地图编号摆摊");
        }

        /// <summary>
        /// 指定地图编号摆摊：改 <c>sub_6E78D4</c> 在 <c>0x6E7900</c> 比较的地图名
        /// （配置写入 <c>0x6E7934</c>）；键关时出厂字面量 <c>"GA0"</c> @0x6E7934。
        /// </summary>
        internal static bool MapMatchesStallPolicy(TPlayObject player)
        {
            var pm = M2Share.PluginManager;
            if (pm == null || player?.m_PEnvir == null) return true;
            var api = new YanshenApi(player, null, pm);
            var expected = api.PatchToggleOn("指定地图编号摆摊")
                ? api.GetStallMapId().ToString()
                : "GA0"; // 0x6E7934 stock Delphi short-string literal
            return string.Equals(player.m_PEnvir.sMapName, expected,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 限制坐标区域和玩家等级摆摊 PRO：<c>IsStallAllowed</c> 已在 YanshenApi 建模。
        /// </summary>
        internal static bool StallLimitPermits(TPlayObject player)
        {
            var pm = M2Share.PluginManager;
            if (pm == null || player == null) return true;
            return new YanshenApi(player, null, pm).IsStallAllowed();
        }

        // --- PARAM_OF_PATCHED（父键已有引擎消费者）---

        /// <summary>
        /// 神兽_数量 — 插件改写宿主 <c>0x76EE99</c> 的 <c>push imm8</c>（见
        /// <see cref="YanshenApi.ShenShouSlaveCount"/> 注释）。键关时出厂 imm8 = 1。
        /// </summary>
        internal static int ShenShouSlaveCount(YanshenApi api)
        {
            if (api == null || !api.IsSummonShenShou())
                return 1;
            return YanshenApi.NativeSlaveCountImm8(
                api.GetParamInt("神兽_数量", 1));
        }

        /// <summary>
        /// 召唤骷髅_数量 — 同构 imm8 @<c>0x76EE1F</c>（<see cref="YanshenApi.KuLouSlaveCount"/>）。
        /// </summary>
        internal static int KuLouSlaveCount(YanshenApi api)
        {
            if (api == null || !api.IsSummonKuLou())
                return 1;
            return YanshenApi.NativeSlaveCountImm8(
                api.GetParamInt("召唤骷髅_数量", 1));
        }
    }
}

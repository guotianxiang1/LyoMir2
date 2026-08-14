using GameSvr;
using SystemModule;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 盘古2 页配置键 → 宿主语义（键关 = 出厂立即数/行为）。
    /// 证据：flat_image.bin dis + yanshen2_0_8 插件 apply 臂。
    /// </summary>
    internal static class YanshenPangu2Patches
    {
        // --- 攻城修改 @0x65BBFD/0x65BE24 start, @0x65BC09/0x65BE2C/0x65C3B1 end ---

        internal const int StockSiegeStartSec = 0x11940;   // 20:00, cmp imm32
        internal const int StockSiegeEndSec = 0x12E58;     // 21:30
        internal const int StockSiegeWarnSec = 0x12C00;    // end-600 @0x65BDF8
        internal const int StockSiegeCaptureSec = 0x11B98; // start+584 @0x65C6AF

        /// <summary>
        /// 插件 0x100B2FC0 组：start = hour*3600+minute*60；
        /// end = start + 攻城时长_分钟*60（≤0 保持出厂 0x12E58）；
        /// warn = end-600；capture = start+(0x11B98-0x11940)。
        /// </summary>
        public static bool TryGetSiegeDayClock(out int startSec, out int endSec,
            out int warnSec, out int captureSec)
        {
            startSec = StockSiegeStartSec;
            endSec = StockSiegeEndSec;
            warnSec = StockSiegeWarnSec;
            captureSec = StockSiegeCaptureSec;

            var pm = M2Share.PluginManager;
            if (pm == null)
                return false;
            var api = new YanshenApi(null, null, pm);
            if (!api.PatchToggleOn("攻城修改"))
                return false;

            int hour = api.GetSiegeModHour();
            int minute = api.GetSiegeModMinute();
            startSec = hour * 3600 + minute * 60;

            int durationMin = api.GetSiegeDuration();
            endSec = durationMin > 0
                ? startSec + durationMin * 60
                : StockSiegeEndSec;

            warnSec = endSec - 600;
            captureSec = startSec + (StockSiegeCaptureSec - StockSiegeStartSec);
            return true;
        }

        /// <summary>
        /// 申请攻城天数：插件写 float32 @0x65B6DC（出厂 3.0 = 0x40400000），
        /// 0x65B68B fadd dword ptr [0x65b6dc]。
        /// </summary>
        public static int SiegeRequestDays()
        {
            var pm = M2Share.PluginManager;
            if (pm == null)
                return M2Share.g_Config.nStartCastleWarDays;
            var api = new YanshenApi(null, null, pm);
            if (!api.PatchToggleOn("攻城修改"))
                return M2Share.g_Config.nStartCastleWarDays;
            return api.GetSiegeModDay();
        }

        // --- ServerSay函数 @0x728913：关 cmp edx,5 / 开 and edx,0xFFFF ---

        static readonly ushort[] StockServerSayColors =
        {
            0x38FF, 0xFFDB, 0xFCFF, 0xFDFF, 0xFFFF, 0xDF00
        };

        public static ushort ResolveServerSayColor(int color)
        {
            var pm = M2Share.PluginManager;
            if (pm != null &&
                new YanshenApi(null, null, pm).PatchToggleOn("ServerSay函数"))
            {
                return unchecked((ushort)(color & 0xFFFF));
            }

            if (color >= 0 && color < StockServerSayColors.Length)
                return StockServerSayColors[color];
            return 0x38FF;
        }

        public static void BroadcastServerSay(string msg, int color)
        {
            if (M2Share.UserEngine == null)
                return;
            var msgColor = (MsgColor)ResolveServerSayColor(color);
            M2Share.UserEngine.SendBroadCastMsgWithColor(msg, msgColor, MsgType.Notice);
        }

        // --- 火墙_时间：仅当 火墙设置时间上限 开时替换 0x7706B6 imul 前的秒数 ---

        public static bool TryGetFireWallHoldSeconds(out int seconds)
        {
            seconds = 0;
            var pm = M2Share.PluginManager;
            if (pm == null)
                return false;
            var api = new YanshenApi(null, null, pm);
            if (!api.IsFireWallTimeLimit())
                return false;
            seconds = api.FireWallTime();
            return seconds > 0;
        }

        // --- EQUIVALENT_BY_ABSENCE（零宿主读点，闭合不臆造）---

        /// <summary>
        /// 破复活：flat_image sfind「破复活」hits=0；面板要求脚本设属性（测试NPC-3.pas）。
        /// </summary>
        public static bool BreakRevivalIsScriptOnly()
        {
            var pm = M2Share.PluginManager;
            return pm != null &&
                new YanshenApi(null, null, pm).PatchToggleOn("破复活");
        }

        /// <summary>
        /// 删除英雄技能：宿主零读点；生产 config=0，插件侧脚本门。
        /// </summary>
        public static bool DelHeroSkillIsScriptGate()
        {
            var pm = M2Share.PluginManager;
            return pm != null &&
                new YanshenApi(null, null, pm).IsDelHeroSkillEnabled();
        }

        /// <summary>
        /// 名字变色：无 memcpy 站点；脚本 SetS(1,2,color)（Legacy23 面板原文）。
        /// </summary>
        public static bool NameColorUsesScriptS12()
        {
            var pm = M2Share.PluginManager;
            return pm != null &&
                new YanshenApi(null, null, pm).IsNameColor();
        }

        /// <summary>
        /// 等级禁言：无宿主阈值；禁言态在 S(1,1)=7/8（Legacy23 面板原文）。
        /// </summary>
        public static bool LevelMuteUsesScriptS11()
        {
            var pm = M2Share.PluginManager;
            return pm != null &&
                new YanshenApi(null, null, pm).IsLevelMute();
        }
    }
}

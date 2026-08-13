using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表第 611 条，记录基址 0x007D2354：
    //   0f 48 65 72 6f 53 4b 69 6c 6c 53 77 69 74 63 68   namelen=0x0F "HeroSKillSwitch"
    //   +0x18 = 63 02 00 00 -> idx 611      +0x1C = 03 00 00 00 -> perm 3
    //   +0x20 帮助长度 0x28 = "@HeroSkillSwitch 玩家名 英雄技能名 开/关"
    // 派发 jt[611]（跳表槽 0x006234A8 = c6 46 62 00）-> case@0x006246C6，真实现。
    // 旧注释说「命令名与帮助串零 xref，原版不存在此命令」：观察没错，推论是错的——注册表是
    // 430 条连续记录的数组，运行期靠 ebx 从 0x7B4654 每次 +0x120 遍历，任何单条记录本来就
    // 不会有独立 xref，该推理对全部 430 条无效。
    [GameCommand("HeroSkillSwitch", "英雄技能开关", "玩家名 英雄技能名 开/关 [1|0]", 3)]
    public class HeroSkillSwitchCommand : BaseCommond
    {
        // 0x0062BD10 len=46：40 48 65 72 6f 53 6b 69 6c 6c 53 77 69 74 63 68 20 cd e6 bc d2
        // c3 fb 20 d3 a2 d0 db bc bc c4 dc c3 fb 20 bf aa 2f b9 d8 20 5b 31 7c 30 5d
        private const string NativeUsage = "@HeroSkillSwitch 玩家名 英雄技能名 开/关 [1|0]";
        private const string SwitchOn = "开";   // 0x0062BCD8  bf aa
        private const string SwitchOff = "关";  // 0x0062BCE4  b9 d8

        [DefaultCommand]
        public void HeroSkillSwitch(string[] @Params, TPlayObject PlayObject)
        {
            var sTarget = @Params != null && @Params.Length > 0 ? @Params[0] : string.Empty;
            var sSkill = @Params != null && @Params.Length > 1 ? @Params[1] : string.Empty;
            var sState = @Params != null && @Params.Length > 2 ? @Params[2] : string.Empty;
            var sWho = @Params != null && @Params.Length > 3 ? @Params[3] : string.Empty;

            // 0x006246DA / 0x0062481E：p4 只认 "0"（主号）和 "1"（英雄），其余
            // 0x00624828 `jne 0x62B64C` 静默丢弃。
            var bHero = sWho == "1";
            if (!bHero && sWho != "0")
                return;

            // 0x006246FF `je 0x6247F1`：p1 为空 -> 0x38FF 红字用法提示。
            if (string.IsNullOrEmpty(sTarget))
            {
                PlayObject.SysMsg(NativeUsage, MsgColor.Red, MsgType.Hint);
                return;
            }

            // 0x00624718 `je 0x6247BA`：查不到玩家 -> 0x38FF 红字
            // "玩家"(0x0062BC3C) + p1 + "不在线"(0x0062BC4C)。
            var target = M2Share.UserEngine.GetPlayObject(sTarget);
            if (target == null)
            {
                PlayObject.SysMsg("玩家" + sTarget + "不在线", MsgColor.Red, MsgType.Hint);
                return;
            }

            // 0x0062472B：p3 不等于 "开" 就被强制改写成 "关"，之后 flag = (p3 == "开")。
            if (sState != SwitchOn)
                sState = SwitchOff;

            // 0x0062474F  call sub_73D458(eax=目标对象, edx=技能名, cl=flag)。
            // bHero 分支作用于 [target+0xBB0]（英雄对象）。sub_73D458 尚未移植，本仓也没有
            // 英雄技能开关位；成功后 0x0062479B 会用 "玩家%s的技能%s设为:%s"(0x0062BCF0)
            // 以 0x38FF 红字回报。只发那条回报而不真正改开关等于静默成功，故此处明确拒绝。
            NativeCommandFailure.Report(PlayObject, "HeroSkillSwitch",
                $"sub_73D458 英雄技能开关位尚未移植，{sTarget} 的技能 {sSkill} 未设为 {sState}。");
        }
    }
}

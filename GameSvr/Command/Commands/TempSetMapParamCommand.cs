using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("TempSetMapParam", "设置地图属性",
        "地图代号 参数", 5)]
    public sealed class TempSetMapParamCommand : BaseCommond
    {
        private const int Success = 1;
        private const int UnsupportedAttribute = 100;
        // 原生 parser A 唯一的非 1/非 0x64 返回：DROPTOMAP 括号参数为空。
        // 0x775124 C7 45 FC F4 FF FF FF  mov dword [ebp-4],-12
        private const int DropToMapMissingArgument = -12;
        private const string NativeHelp =
            "命令格式：@TempSetMapParam 地图名 属性 [1|0] " +
            "1表示增加属性，0表示取消属性";

        [DefaultCommand]
        public void TempSetMapParam(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length < 3 ||
                string.IsNullOrEmpty(@Params[0]) ||
                string.IsNullOrEmpty(@Params[1]) ||
                string.IsNullOrEmpty(@Params[2]))
            {
                PlayObject.SysMsg(NativeHelp, MsgColor.Blue, MsgType.Hint);
                return;
            }

            var mapName = @Params[0];
            var attribute = @Params[1];
            var state = HUtil32.Str_ToInt(@Params[2], 0);
            var environment = M2Share.MapManager.FindMap(mapName);
            if (environment == null)
            {
                PlayObject.SysMsg("没找到地图 " + mapName, MsgColor.Red,
                    MsgType.Hint);
                return;
            }

            var result = ApplyPickupAttribute(environment, attribute, state);
            if (result == Success)
            {
                var operation = state == 1 ? "增加地图属性=" : "取消地图属性=";
                PlayObject.SysMsg(operation + attribute + "，操作成功",
                    MsgColor.Blue, MsgType.Hint);
            }
            else if (result == UnsupportedAttribute)
            {
                PlayObject.SysMsg("该GM命令目前不支持此地图属性=" + attribute,
                    MsgColor.Red, MsgType.Hint);
            }
            else if (state == 0 || state == 1)
            {
                var operation = state == 1 ? "增加地图属性=" : "取消地图属性=";
                PlayObject.SysMsg(operation + attribute + "，操作失败",
                    MsgColor.Red, MsgType.Hint);
            }
        }

        internal static int ApplyPickupAttribute(Envirnoment environment,
            string attribute, int state)
        {
            if (environment?.Flag == null || string.IsNullOrEmpty(attribute) ||
                unchecked((uint)state) >= 2)
                return 0;

            // 上面这道门是原生包装器 sub_774D24：0x774D55 cmp dword [ebp-8],0 / je
            // （Trim 后为空则返 0）、0x774D5D sub eax,2 / jae（state 必须无符号 <2），
            // 通过后才 0x774D68 call sub_774D98 —— 即下面这条 token 分发链。
            // 分发链默认结果 1（0x774DBE mov dword [ebp-4],1），全链未命中则
            // 0x775BBE 覆写 0x64。
            //
            // 下面 8 个臂是 MFLG 残口补齐的同一批已证 token（字面串在两个 token 池
            // 里各一条，parser A/B 都有臂）。parser A 与 parser B 的区别是 A 带
            // on/off 双臂：`cmp edi,1 / jne` 走置位，`test edi,edi / jne` 走清零。
            // 字段本身的消费者一律 **未接线**（fail-closed，见 TMapFlag 各字段文档）：
            // 这里复刻的是 GM 命令自己的可观测行为——原版对这些 token 返 1（"操作
            // 成功"），而不是 0x64（"不支持"）。
            var flag = environment.Flag;
            var enable = state == 1;

            // DROPTOMAP(图名) -> [+0x65] + [+0x9C]
            //   0x7750D2 mov ecx,9 / 0x7750D7 mov edx,0x775CFC / 0x7750DE call 0x4C6E94
            //   on : 0x7750EC 置 1 -> 0x775104 call 0x4C6964 取参 -> 0x775112 存 [+0x9C]
            //        0x775117 cmp dword [ebx+0x9C],0 / jne 尾声；空则 0x775124 结果 -12
            //   off: 0x775138 写 0 + 0x775142 call 0x405500 (LStrClr [+0x9C])
            if (HUtil32.CompareLStr(attribute, "DROPTOMAP", "DROPTOMAP".Length))
            {
                flag.boDROPTOMAP = enable;
                if (!enable)
                {
                    flag.sDropToMap = string.Empty;
                    return Success;
                }
                var dropToMap = string.Empty;
                HUtil32.ArrestStringEx(attribute, '(', ')', ref dropToMap);
                flag.sDropToMap = dropToMap;
                return flag.sDropToMap == ""
                    ? DropToMapMissingArgument
                    : Success;
            }
            // MINGJIANG -> [+0x7A]；比较器 0x40BD78 全等
            //   0x7752A5 mov edx,0x775D9C / 0x7752AC call 0x40BD78
            //   on 0x7752BA 置 1 / off 0x7752CB 写 0
            if (attribute.Equals("MINGJIANG", StringComparison.OrdinalIgnoreCase))
            {
                flag.boMINGJIANG = enable;
                return Success;
            }
            // HACKQUEST -> [+0x7B]；0x7752D4 compare / on 0x7752E9 / off 0x7752FA
            if (attribute.Equals("HACKQUEST", StringComparison.OrdinalIgnoreCase))
            {
                flag.boHACKQUEST = enable;
                return Success;
            }
            // NOEXPLORE -> [+0x80]；0x775332 compare / on 0x775347 / off 0x77535B
            if (attribute.Equals("NOEXPLORE", StringComparison.OrdinalIgnoreCase))
            {
                flag.boNOEXPLORE = enable;
                return Success;
            }
            // NOHERO -> [+0x6E]；0x775706 mov ecx,6 / on 0x775720 / off 0x775731
            if (HUtil32.CompareLStr(attribute, "NOHERO", "NOHERO".Length))
            {
                flag.boNOHERO = enable;
                return Success;
            }
            // DREAMCASTLEMAP -> [+0x6F]；0x77573A mov ecx,0xE / on 0x775754 / off 0x775765
            if (HUtil32.CompareLStr(attribute, "DREAMCASTLEMAP",
                    "DREAMCASTLEMAP".Length))
            {
                flag.boDREAMCASTLEMAP = enable;
                return Success;
            }
            // UserNoKill -> [+0x71]，并在两臂上都把 word [+0x74] 清零
            //   0x775933 mov ecx,0xA / on 0x77594D 置 1 + 0x775951 清 word
            //                        / off 0x775964 写 0 + 0x775968 清 word
            if (HUtil32.CompareLStr(attribute, "UserNoKill", "UserNoKill".Length))
            {
                flag.boUserNoKill = enable;
                flag.UserNoKillLevelCap = 0;
                return Success;
            }
            // NEWMJNORMALPRIZE -> [+0x78]；0x7759DA mov ecx,0x10 / on 0x7759F4 / off 0x775A05
            if (HUtil32.CompareLStr(attribute, "NEWMJNORMALPRIZE",
                    "NEWMJNORMALPRIZE".Length))
            {
                flag.boNEWMJNORMALPRIZE = enable;
                return Success;
            }

            // 其余 token（SAFE / MINE / NOMAGIC / CHECKQUEST / … 约 40 个）仍未移植：
            // parser A 对它们有臂并返 1，C# 这里落到 0x64。缩窄范围是刻意的，
            // NativeTempSetMapParamPickupCheck 用 `SAFE` 锁住这条边界。
            if (!string.Equals(attribute, "pickup",
                    StringComparison.OrdinalIgnoreCase))
                return UnsupportedAttribute;

            environment.Flag.boPICKUP = state == 1;
            return Success;
        }
    }
}

using GameSvr.CommandSystem;
using GameSvr.Configs;
using System.Globalization;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ChgOpenGameTime", "修改开区时间", "XXXX-XX-XX", 5)]
    public class ChgGameOpenTimeCommand : BaseCommond
    {
        // 注册表 0x007CFBF4：namelen=0x0F，43 68 67 4f 70 65 6e 47 61 6d 65 54 69 6d 65
        // ("ChgOpenGameTime")，+0x18 idx=740 (e4 02 00 00)，+0x1C perm=5，帮助文本长度 0。
        // 原来 C# 注册成 "ChgGameOpenTime"（Game/Open 两词对调）——那个拼法在 430 条注册表里
        // 零命中，玩家敲不出来；镜像里唯一出现该拼法的地方是下面这条用法提示字符串本身，
        // 是原作者写提示时把词序写反了，可敲的名字以注册表记录为准。
        private const string NativeUsage = "命令格式：@ChgGameOpenTime XXXX-XX-XX";

        [DefaultCommand]
        public void ChgOpenGameTime(string[] @Params, TPlayObject PlayObject)
        {
            var sDate = @Params != null && @Params.Length > 0 ? @Params[0] : string.Empty;
            if (string.IsNullOrEmpty(sDate))
            {
                // case@0x0062B42E: 参数为空 ->
                //   0062B455  66 b9 ff 38          mov cx,0x38FF        ; 红
                //   0062B459  ba f4 e0 62 00       mov edx,0x0062E0F4   ; 长度前缀 37 的 GBK 串
                //   0062B463  ff 93 d4 00 00 00    call [ebx+0xD4]      ; SysMsg
                PlayObject.SysMsg(NativeUsage, MsgColor.Red, MsgType.Hint);
                return;
            }
            if (!DateTime.TryParseExact(sDate.Trim(), ServerConfig.OpenDayFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var openDay))
            {
                PlayObject.SysMsg(NativeUsage, MsgColor.Red, MsgType.Hint);
                return;
            }

            // 参数非空 -> 0062B44B call sub_6FA494(self, p1)，随后 jmp 0x62B64C。
            // sub_6FA494: FileExists(!Setup.txt) false -> silent; otherwise write the
            // canonical YYYY-MM-DD value and report the exact green confirmation.
            if (!M2Share.ServerConf.TryWriteOpenDay(openDay, out var formatted))
            {
                return;
            }

            M2Share.g_Config.OpenDay = openDay.Date;
            PlayObject.SysMsg("开区时间:" + formatted +
                "已写入!Setup.txt中, 下次启动仍有效", MsgColor.Green, MsgType.Hint);
        }
    }
}

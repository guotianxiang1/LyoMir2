using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @ChgNameClr (idx99, perm4) —— 设置【自身】名字颜色。
    /// 原版 case@0x00625025 (inline): self[+0x155] = Str_ToInt(p0, 默认0xFF) 的低字节；
    /// 随后刷新外观 sub_767548(=RefNameColor)；无 SysMsg。
    /// 字段: self[+0x155]=m_btNameColor(byte, TBaseObject.cs:246，默认255)；sub_767548=RefNameColor()。
    /// 证据: staging/gm_player_admin_commands_20260731.md
    ///       (ChgNameClr 99: "self[+0x155]=lowbyte(Str_ToInt(p0,0xFF)); refresh sub_767548; No SysMsg")。
    /// 补齐原因: census 有、C# 此前缺(missing 1:1)；纯自身字节字段写 + 刷新，无经济/物品风险。
    /// </summary>
    [GameCommand("ChgNameClr", "设置自身名字颜色", "颜色值(默认255)", 4)]
    public class ChgNameClrCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgNameClr(string[] @Params, TPlayObject PlayObject)
        {
            var sParam1 = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            // 原版 Str_ToInt(p0, 默认0xFF) 取低字节写入 name-color 字段(空参→255)。
            var nColor = HUtil32.Str_ToInt(sParam1, 0xFF);
            PlayObject.m_btNameColor = (byte)nColor;
            PlayObject.RefNameColor();   // = 原版 sub_767548 刷新外观
        }
    }
}

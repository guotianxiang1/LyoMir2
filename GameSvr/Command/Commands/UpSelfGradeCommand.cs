using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @UpSelfGrade (idx217, perm5) —— 原始(不封顶)设置【自身】等级。
    /// 原版 case@0x00625F17: lvl=Str_ToInt(p0,1)，RAW 写入等级字段 player[+0x278] 及镜像 +0x1FC
    /// (【无 clamp】，与 IncSelfLv 的 min(max(lvl,1),500) 相对)，随后 vtbl[0x240]=OnLevelChanged
    /// (与 IncSelfLv 同一 slot = 重算/客户端更新)；无 SysMsg。
    /// C# 建模: 照 @ChangeLevel(=IncSelfLv 的封顶版)【去掉 _MIN 封顶】—— raw 写 m_Abil.Level +
    /// HasLevelUp(1)(= OnLevelChanged 的 C# 等价，ChangeLevel 亦用它)；perm 5；不发 SysMsg、不额外记日志
    /// (原版 UpSelfGrade 契约只有 字段写 + vtbl[0x240]，无 SysMsg/无 log)。
    /// 证据: staging/gm_player_admin_commands_20260731.md
    ///       (UpSelfGrade 217: "raw to player[+0x278] & +0x1FC (NO clamp, cf. IncSelfLv), vtbl[0x240]. No SysMsg")。
    /// 补齐原因: 原版 census 有此命令、C# 此前缺失(missing 1:1)；本次新增以完成 native 等级命令对
    ///           IncSelfLv(封顶,perm4=@ChangeLevel) / UpSelfGrade(raw,perm5)。
    /// </summary>
    [GameCommand("UpSelfGrade", "原始设置自身等级(不封顶)", "等级", 5)]
    public class UpSelfGradeCommand : BaseCommond
    {
        [DefaultCommand]
        public void UpSelfGrade(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sParam1 = @Params.Length > 0 ? @Params[0] : "";
            var nLevel = HUtil32.Str_ToInt(sParam1, 1);
            // 原版 RAW 写(无数值封顶)；等级字段宽度 ushort，(ushort) 仅做宽度截断，不做 min/max 封顶。
            PlayObject.m_Abil.Level = (ushort)nLevel;
            PlayObject.HasLevelUp(1);   // = 原版 vtbl[0x240] OnLevelChanged(重算 + 客户端更新)
        }
    }
}

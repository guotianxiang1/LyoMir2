using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to change the caller's own job/class.
    /// Usage: @ChgmanKind JobName
    /// Native implementation (idx 97, perm 4): compares jobName against 4 job-name constants,
    /// sets player[+0x72]=m_btJob to matched index (0..3), recalculates stats (vtbl+0x240),
    /// sends SysMsg(ColorConfirm). If no match: silent no-op.
    /// </summary>
    [GameCommand("ChgmanKind", "更改自身职业", "职业名", 4)]
    public class ChgmanKindCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgmanKind(string[] @Params, TPlayObject PlayObject)
        {
            // 原版 @ChgmanKind→sub_6BE358 (case@0x625008, idx 97, perm 4): 单参 jobName，
            // 依次比对 4 个职业名常量（case-insensitive），首次匹配设置 player[+0x72]=matchedIndex(0..3)，
            // 格式化确认消息 (dword_6BE460)，发 SysMsg(ColorConfirm=0xFFDB)，然后用当前等级重算能力 (vtbl[0x240])。
            // 若 jobName 不匹配任何职业名→静默 no-op（无设置、无消息）。
            // (逆向证据: gm-playerattr staging/gm_player_attr_commands_20260801.md — sub_6BE358 全反编译，
            //  player[+0x72]=m_btJob 已由 RTTI 坐实。)

            if (@Params == null || @Params.Length < 1)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            var sJobName = @Params[0];
            if (string.IsNullOrEmpty(sJobName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            // 原版依次比对 4 个职业名字符串常量；C# 用 StringComparison.OrdinalIgnoreCase 复刻
            // case-insensitive 匹配。JobCount=4 但实际游戏只有 3 个职业 (Warr/Wizard/Taos → 0/1/2)，
            // 第 4 个槽位可能保留/未使用。参考 ChangeJobCommand.cs 的模式与 M2Share 常量。
            int matchedJobIndex = -1;

            if (string.Compare(sJobName, "Warr", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(sJobName, "Warrior", StringComparison.OrdinalIgnoreCase) == 0)
            {
                matchedJobIndex = M2Share.jWarr; // 0
            }
            else if (string.Compare(sJobName, "Wizard", StringComparison.OrdinalIgnoreCase) == 0)
            {
                matchedJobIndex = M2Share.jWizard; // 1
            }
            else if (string.Compare(sJobName, "Taos", StringComparison.OrdinalIgnoreCase) == 0 ||
                     string.Compare(sJobName, "Taoist", StringComparison.OrdinalIgnoreCase) == 0)
            {
                matchedJobIndex = M2Share.jTaos; // 2
            }

            // 原版：不匹配→静默 no-op (无消息、无设置)
            if (matchedJobIndex < 0)
            {
                return;
            }

            // 使用 NativeGmChgManKind dormant model 验证契约
            var outcome = NativeGmChgManKind.Evaluate(matchedJobIndex);
            if (!outcome.JobSet)
            {
                return;
            }

            // 设置 player[+0x72] = m_btJob
            PlayObject.m_btJob = (byte)outcome.NewJob;

            // 原版用当前等级重算能力 (vtbl+0x240)：RecalcLevelAbilitys() 对应 vtbl+0x240 stat recalc。
            // (ChangeJobCommand.cs:42 用 HasLevelUp(1) 也会触发重算，但原版明确说是 vtbl+0x240 with current level。)
            PlayObject.RecalcLevelAbilitys();

            // 发确认消息给 GM 自己 (ColorConfirm=0xFFDB=Green)
            PlayObject.SysMsg(M2Share.g_sGameCommandChangeJobHumanMsg, MsgColor.Green, MsgType.Hint);
        }
    }
}

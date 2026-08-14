using System;
using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // case @0x00625008 -> sub_6BE358 @0x006BE358: match job name -> player+0x72, SysMsg, vtbl+0x240
    [GameCommand("ChgmanKind", "更改自身职业", "职业名", 4)]
    public class ChgManKindCommand : BaseCommond
    {
        private static readonly string[] NativeJobNames =
        {
            "战士", "魔法师", "道士", "刺客",
        };

        [DefaultCommand]
        public void ChgManKind(string[] @params, TPlayObject playObject)
        {
            if (playObject == null)
                return;
            var jobName = @params != null && @params.Length > 0 ? @params[0] : string.Empty;
            if (string.IsNullOrEmpty(jobName))
            {
                playObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            var matched = -1;
            for (var i = 0; i < NativeJobNames.Length; i++)
            {
                if (string.Equals(jobName, NativeJobNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    matched = i;
                    break;
                }
            }

            var outcome = NativeGmChgManKind.Evaluate(matched);
            if (!outcome.JobSet)
                return;

            playObject.m_btJob = (byte)outcome.NewJob;
            playObject.RecalcAbilitys();
            playObject.SysMsg("职业更改为：" + NativeJobNames[outcome.NewJob],
                MsgColor.Yellow, MsgType.Hint);
        }
    }
}

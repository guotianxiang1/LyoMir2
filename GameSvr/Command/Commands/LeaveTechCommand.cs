using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @LeaveTech 角色名  (dispatch id 125, perm 4, case@0x006252B0 -> sub_6C5E08).
    // Reversed 1:1 from the 战神 image (M2Server.exe, ImageBase 0x400000):
    //
    //   6C5E0D  mov  ebx, edx            ; edx = the name argument (Delphi register call)
    //   6C5E0F  mov  esi, eax            ; eax = the invoking GM
    //   6C5E11  test ebx, ebx
    //   6C5E13  je   0x6C5E76            ; nil name -> return, NO message at all
    //   6C5E1E  call sub_652784          ; FindPlayer(g_UserEngine, name)
    //   6C5E25  test ebx, ebx
    //   6C5E27  je   0x6C5E63            ; not online -> failure line
    //   6C5E29  cmp  byte [ebx+0xB95], 0 ; target must actually be a student
    //   6C5E30  je   0x6C5E63            ; not a student -> the SAME failure line
    //   6C5E32  xor  edx, edx            ; mode = 0 (自行离开师门)
    //   6C5E36  call sub_6C5EC8          ; the one shared dissolve routine
    //   6C5E3B  mov  cx, 0xFFDB          ; then TWICE: once to the target...
    //   6C5E48  call [vmt+0xD4]          ;   ...(eax = ebx = target)
    //   6C5E4E  mov  cx, 0xFFDB          ; ...and once to the GM
    //   6C5E5B  call [vmt+0xD4]          ;   ...(eax = esi = GM)
    //   6C5E63  mov  cx, 0xFFDB          ; failure path, GM only (eax = esi)
    //
    // Strings (null-terminated PChar literals, GBK, byte-verified from the image):
    //   0x6C5E84 len 20 "离师操作已被系统接受"
    //   0x6C5EA4 len 35 "[失败] 角色不在有效范围或角色无师承"
    // cx 0xFFDB packs as FColor = cx & 0xFF = 0xDB, BColor = cx >> 8 = 0xFF, i.e.
    // the MsgColor.Green pair (btGreenMsgFColor 0xDB / btGreenMsgBColor 0xFF).
    // BOTH the success and the failure line use it -- the failure is NOT red.
    //
    // Note the shared-core detail: the "not a student" case and the "not online"
    // case are indistinguishable to the GM; native emits one string for both.
    // The dissolve itself is NativeLeaveMaster(0), the same mode-0 entry that PAS
    // NpcLeaveTec (0x6CB017) uses -- minus the 50,000 gold charge, which lives in
    // the PAS wrapper (0x6CB003), not in the GM path.
    [GameCommand("LeaveTech", "解除玩家与其师傅的师徒关系", "角色名", 4)]
    public class LeaveTechCommand : BaseCommond
    {
        // 0x6C5E84 -- sent to the target AND the GM on success.
        private const string NativeAcceptedMsg = "离师操作已被系统接受";

        // 0x6C5EA4 -- sent to the GM only, for both not-online and not-a-student.
        private const string NativeFailedMsg = "[失败] 角色不在有效范围或角色无师承";

        [DefaultCommand]
        public void LeaveTech(string[] @Params, TPlayObject PlayObject)
        {
            var sHumName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHumName))
            {
                // 0x6C5E13 je 0x6C5E76: a nil name returns silently. The shim's
                // own usage line is the C# convention for a missing argument and
                // is kept (native's caller never passes nil for a typed @command).
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }

            // 0x6C5E1E sub_652784 == GetPlayObject (runtime registry only; native
            // does NOT load an offline character here).
            var target = M2Share.UserEngine.GetPlayObject(sHumName);

            // 0x6C5E27 / 0x6C5E30: both misses collapse onto the one failure line.
            if (target == null || !target.m_boStudent)
            {
                PlayObject.SysMsg(NativeFailedMsg, MsgColor.Green, MsgType.Hint);
                return;
            }

            // 0x6C5E36 call sub_6C5EC8 with edx = 0 -> 自行离开师门.
            target.NativeLeaveMaster(0);

            // 0x6C5E3B..0x6C5E61: the same string to the target, then to the GM.
            target.SysMsg(NativeAcceptedMsg, MsgColor.Green, MsgType.Hint);
            PlayObject.SysMsg(NativeAcceptedMsg, MsgColor.Green, MsgType.Hint);
        }
    }
}

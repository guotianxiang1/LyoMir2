using System.Globalization;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // 战神 sub_657BCC (OthGs ident 228, stub @0x65733E mov ecx,[ebp+0x10]).
        // 0x657C01 sub_4C6AEC(cl='/' ) -> [ebp-4]=收信人, [ebp-8]=徒弟名; 两段皆非空;
        // 0x657C14 cmp esi,0x3E8 / jl 退出 (第三 dword >=1000);
        // 0x657C26 GetPlayObject(首段); 0x657C3D call sub_6C03F8(eax=player, edx=nParam,
        //   cl=0, [ebp+8]=0, [ebp+0xC]=1, [ebp+0x10]=0) == GrantNativePlayerExperience;
        // 0x657C42..0x657C74 拼 GBK 串 @0x657CAC+徒弟名+@0x657CC8+IntToStr(nParam),
        // cx=0xFCFF SysMsg。

        internal static void NativeMirrorMentorRechargeReward(string masterName,
            string studentName, int nParam)
        {
            if (string.IsNullOrEmpty(masterName)
                || string.IsNullOrEmpty(studentName))
            {
                return;
            }

            if (nParam < 1000)
            {
                return;
            }

            var master = M2Share.UserEngine?.GetPlayObject(masterName);
            if (master == null)
            {
                return;
            }

            master.GrantNativePlayerExperience(nParam, shareWithHero: false,
                countAsFightExperience: false, experienceMode: 0);

            var text = "恭喜，您曾经的徒弟" + studentName
                + "实力又进一步，“比奇国王”特赠您经验值"
                + nParam.ToString(CultureInfo.InvariantCulture);
            master.SysMsg(text, MsgColor.Blue, MsgType.Hint);
        }
    }
}

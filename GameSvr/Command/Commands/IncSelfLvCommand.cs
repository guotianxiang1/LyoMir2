using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native @IncSelfLv idx=104 perm=4, case@0x006250AB.
    /// Str_ToInt(p1,1) → max(n,1) via 0x004C7004 → min(n,500=0x1F4) via 0x004C700C;
    /// write [+0x278] and [+0x1FC]; vtbl+0x240(old,new). No SysMsg.
    /// Distinct from @UpSelfGrade (idx 217 perm 5) which writes raw with no clamp.
    /// </summary>
    [GameCommand("IncSelfLv", "提升自身等级(最大等级为500)", "等级数", 4)]
    public class IncSelfLvCommand : BaseCommond
    {
        [DefaultCommand]
        public void IncSelfLv(string[] @Params, TPlayObject PlayObject)
        {
            var sParam = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            var nLevel = HUtil32.Str_ToInt(sParam, 1);
            if (nLevel < 1)
                nLevel = 1;
            if (nLevel > 500)
                nLevel = 500;
            var oldLevel = PlayObject.m_Abil.Level;
            PlayObject.m_Abil.Level = (ushort)nLevel;
            PlayObject.HasLevelUp(oldLevel);
        }
    }
}

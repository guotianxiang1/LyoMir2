using GameSvr.CommandSystem;

namespace GameSvr
{
    // 天地合一 command family — native GM command table 0x7B4654 slots, all
    // dispatched from sub_622820. The C# CommandManager (reflection registrar,
    // CommandManager.cs:33) auto-discovers these [GameCommand] classes, so
    // registering by the native default names (NativeGmCommandRegistry idx23/24/25)
    // is the additive stand-in for the native dispatcher case bodies. No hotspot
    // file is touched. Logic lives in Players/TPlayObject.NativeTianDiHeYi.cs.

    /// <summary>
    /// idx24「允许天地合一」— case @0x00623990. Native both idx23 and idx24 jump
    /// to the same body (XOR of obj+0xBA4), so this and
    /// <see cref="DisableGroupRecallCommand"/> share one toggle method.
    /// </summary>
    [GameCommand("允许天地合一", "允许/拒绝被天地合一召回(自身开关)", 0)]
    public class EnableGroupRecallCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.NativeToggleGroupRecall();
        }
    }

    /// <summary>
    /// idx23「拒绝天地合一」— case @0x00623990 (identical XOR toggle as idx24).
    /// </summary>
    [GameCommand("拒绝天地合一", "允许/拒绝被天地合一召回(自身开关)", 0)]
    public class DisableGroupRecallCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.NativeToggleGroupRecall();
        }
    }

    /// <summary>
    /// idx25「天地合一」— case @0x006239D8. Executes the group-recall skill
    /// sub_6C7B28. The native case gate @0x6239DB is
    /// <c>(self+0x1b0 == 0xF) || callerPerm(bl) &gt;= 3</c>; self+0x1b0 is a
    /// foreign-class displacement never set to 0xF on a TPlayObject (see the
    /// census in TPlayObject.NativeTianDiHeYi.cs), so the gate reduces to caller
    /// permission &gt;= 3 with a silent skip below that (native jb 0x62B64C).
    /// The GM table permission (record+0x1C) is 0, so this is registered perm 0
    /// and the &gt;=3 case-gate is applied explicitly here.
    /// </summary>
    [GameCommand("天地合一", "召回已允许被召回的队员到身边(队长)", 0)]
    public class TianDiHeYiCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            if (BaseCommond.GetEffectivePermission(playObject) < 3)
            {
                return; // native 0x6239E7 jb 0x62B64C — silent below perm 3
            }
            playObject.NativeTianDiHeYiCommand();
        }
    }
}

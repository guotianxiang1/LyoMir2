using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 战神 GM dispatch index 401, perm 4, handler 0x6281AE. Registry record
    /// 0x7B6ED4 carries the name as an 18-byte GBK ShortString
    /// (<c>d6d8 d4d8 b4ab cbcd caaf bdfb d3c3 b5d8 cdbc</c>) =
    /// 重载传送石禁用地图, with the dispatch index dword 401 at +0x18 and the
    /// permission dword 4 at +0x1C. Index cross-checked against the jump table:
    /// <c>[0x622B1C + 401*4] == 0x6281AE</c>.
    /// <para>
    /// It takes NO arguments and reloads the 传送石 banned-map blacklist into the
    /// global TStringList at <c>[0x7D5918]</c>:
    /// </para>
    /// <code>
    /// 6281CC  call FileExists
    /// 6281D1  test al,al
    /// 6281D3  je   0x628204          ; missing -> the red refusal, list UNTOUCHED
    /// 6281FC  call [ecx+0x68]        ; TStrings.LoadFromFile (clears internally)
    /// 6281FF  jmp  0x62B64C          ; success is SILENT -- no message at all
    /// 628204  mov  cx,0x38FF
    /// 628208  mov  edx,0x62D204      ; "传送石禁用地图.txt 文件不存在！"
    /// 628213  call [ebx+0xD4]
    /// </code>
    /// <para>
    /// The GM types the record name verbatim, so the registered command is the
    /// Chinese literal itself. The former English name ReloadFixedCoordDisableMap
    /// scans to zero hits image-wide in GBK, UTF-8 and UTF-16LE.
    /// </para>
    /// <para>
    /// Note this command does NOT touch a player's remembered position. The
    /// anchor storage (rec 0x5AC / 0x5BC / 0x5BE and the matching object slots)
    /// has only five referencing functions image-wide and none of them is
    /// reachable from the 0x622B1C dispatch table, so no GM verb resets an
    /// anchor. Only the map GATE is reloadable.
    /// </para>
    /// </summary>
    [GameCommand("重载传送石禁用地图", "重新加载传送石禁用地图配置", "", 4)]
    public class ReloadFixedCoordDisableMapCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadFixedCoordDisableMap(TPlayObject PlayObject)
        {
            // Success is silent (0x6281FF jumps straight to the shared epilogue),
            // so only the failure leg speaks. Colour 0x38FF == MsgColor.Red per the
            // project's cx packing convention.
            if (!M2Share.LoadFixedCoordDisableMap())
            {
                PlayObject.SysMsg(M2Share.g_sNativeFixedCoordDisableMapMissing,
                    MsgColor.Red, MsgType.Hint);
            }
        }
    }
}

using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native dispatch 229.  Parameters are the bag item's MakeIndex and a
    /// requested level in the inclusive range 1..5.
    /// </summary>
    [GameCommand("ChgEquipLevel", "更改装备等级(1..5)", "物品ID 等级值", 5)]
    public sealed class ChgEquipLevelCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgEquipLevel(string[] @Params, TPlayObject PlayObject)
        {
            var rawItemId = @Params != null && @Params.Length > 0
                ? @Params[0] ?? string.Empty
                : string.Empty;
            var rawLevel = @Params != null && @Params.Length > 1
                ? @Params[1] ?? string.Empty
                : string.Empty;
            NativeGmChgEquipLevel.Execute(PlayObject, rawItemId, rawLevel);
        }
    }
}

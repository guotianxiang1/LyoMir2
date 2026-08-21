using GameSvr.CommandSystem;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 358, case 0x00627FD5. An empty first argument uses
    /// the invoking player; otherwise sub_652784 resolves a non-ghost,
    /// ReadyRun player before the virtual Die call at vtbl+0x84. Every miss
    /// is silent and later arguments are not read.
    /// </summary>
    [GameCommand("Die", "GM自杀或设置其他玩家死亡", "[角色名/空(自身)]", 5)]
    public sealed class DieCommand : BaseCommond
    {
        [DefaultCommand]
        public void Die(string[] @params, TPlayObject player)
        {
            if (player == null)
                return;

            var targetName = @params != null && @params.Length > 0
                ? @params[0]
                : string.Empty;
            var target = string.IsNullOrEmpty(targetName)
                ? player
                : M2Share.UserEngine?.GetNativeReadyPlayObject(targetName);

            target?.Die();
        }
    }
}

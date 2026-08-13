using SystemModule;

namespace GameSvr.PasEngine
{
    public partial class PasApiBridge
    {
        private bool TryUseNativeNick(IReadOnlyList<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            if (CurrentNpc == null || args.Count < 2 ||
                args[0].ObjVal is not TPlayObject player)
            {
                return false;
            }

            var manager = M2Share.NickPrizeManager;
            if (manager == null) return true;

            if (BagCapacity.Of(player) - player.m_ItemList.Count < 6)
            {
                CurrentNpc.GotoLable(player, "@NotEnoughBag", false);
                return true;
            }

            var useType = args[1].AsInt();
            var cost = useType switch
            {
                1 => 1,
                2 => 10,
                3 => 100,
                _ => 0
            };
            if (cost == 0) return true;

            if (player.m_nNickLinFu < cost)
            {
                CurrentNpc.GotoLable(player, "@NotEnoughNick", false);
                return true;
            }

            player.DecNativeNickLinFu(cost);
            if (manager.TrySelect(useType, out var prize, out _))
            {
                using var context = PushItemContext(player, CurrentNpc,
                    CurrentInputOk, CurrentInputStr, CurrentItem);
                _ = TryNativeGive(prize.Source, 1, false, false);
            }
            CurrentNpc.GotoLable(player, "@UseNick_OK", false);
            return true;
        }
    }
}

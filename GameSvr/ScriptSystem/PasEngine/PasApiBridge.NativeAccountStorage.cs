using SystemModule;

namespace GameSvr.PasEngine
{
    public partial class PasApiBridge
    {
        private bool CallAddNativeAccountStorageCapacity(
            List<PasValue> args, out PasValue result)
        {
            var applied = CurrentPlayer != null && args?.Count == 1
                && CurrentPlayer.AddNativeAccountStorageCapacity(args[0].AsInt());
            result = PasValue.FromBool(applied);
            return true;
        }

        private bool CallGetNativeAccountStorageCapacity(
            List<PasValue> args, out PasValue result)
        {
            result = PasValue.FromInt(CurrentPlayer != null && args?.Count == 0
                ? CurrentPlayer.GetNativeAccountStorageCapacity()
                : -1);
            return true;
        }

        private void QueueNativeAccountStorageClick(bool getBack)
        {
            if (CurrentPlayer == null || CurrentNpc == null) return;
            CurrentPlayer.SendMsg(CurrentNpc,
                getBack ? Grobal2.RM_USERGETBACKITEM
                    : Grobal2.RM_USERSTORAGEITEM,
                0, CurrentNpc.ObjectId, getBack ? 2 : 1, 0, "");
        }
    }
}

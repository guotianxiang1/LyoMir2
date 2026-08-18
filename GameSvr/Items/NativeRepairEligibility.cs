using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Execution-only repair eligibility from <c>sub_63EE14</c> plus the
    /// caller's <c>item+0xFC == 0</c> gate. Repair quotes deliberately do not
    /// use this predicate.
    /// </summary>
    internal static class NativeRepairEligibility
    {
        internal static bool CanExecute(TUserItem item, GoodItem stdItem,
            byte repairMode)
        {
            if (item == null || stdItem == null || item.NativeClassFc != 0)
            {
                return false;
            }

            if (stdItem.StdMode == 25 &&
                stdItem.Shape is not (1 or 2 or 5))
            {
                return false;
            }

            if (stdItem.StdMode is 1 or 2 or 3 or 7 || stdItem.StdMode > 150)
            {
                return false;
            }

            // Native mode 3 bypasses only the TEquipItem/+0x104 tail. The mode
            // is persisted by Click_RepairEx before the shared repair request.
            if (repairMode == 3)
            {
                return true;
            }

            if (!NativeItemFactory.IsClassOrDescendantOf(stdItem, "TEquipItem"))
            {
                return true;
            }

            return (item.NativeClass104 & 0x06) == 0;
        }
    }
}

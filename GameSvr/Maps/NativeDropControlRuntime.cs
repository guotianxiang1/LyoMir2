using SystemModule;

namespace GameSvr
{
    internal readonly record struct NativeDropControlPending(
        string ItemName, int ItemIndex, ushort Quantity);

    internal static class NativeDropControlRuntime
    {
        internal const int ScatterRange = 4;

        /// <summary>
        /// 战神 sub_71FA20 runs the monster's own drop table (segment 2) before the
        /// controlled world drop (segment 3).  Segment 2 occupies 0x71FCFF-0x71FEA1
        /// and 0x71FEA7 is its fallthrough successor, which the empty-table shortcut
        /// spells out:
        /// <code>
        /// 71FCFF  8B 45 FC / 8B 80 74 04 00 00  mov eax,[self+0x474]   ; segment 2 head
        /// 71FD08  8B 58 08                      mov ebx,[eax+8]        ; Count
        /// 71FD0B  4B / 85 DB
        /// 71FD0E  0F 8C 93 01 00 00             jl 0x71FEA7            ; empty -> segment 3
        /// ...
        /// 71FEA1  0F 85 70 FE FF FF             jne 0x71FD17           ; segment 2 loop tail
        /// 71FEA7  8D 45 E8 / 50                 lea eax,[ebp-0x18]     ; segment 3 head
        /// 71FEC8  E8 DF 2D 03 00                call 0x752CAC          ; world-drop lookup
        /// </code>
        /// Both segments feed the same RNG stream, so the order fixes which draw each
        /// one gets.
        /// </summary>
        internal static void RunInNativeOrder(Action ordinaryDrop,
            Action controlledDrop)
        {
            ordinaryDrop?.Invoke();
            controlledDrop?.Invoke();
        }

        internal static void TryScatter(TBaseObject dyingObject,
            TBaseObject itemCreator,
            IList<KeyValuePair<string, string>> scatteredItems)
        {
            if (dyingObject?.m_PEnvir == null || M2Share.UserEngine == null)
                return;

            try
            {
                var now = HUtil32.GetTickCount();
                ScatterPhase(dyingObject, itemCreator, scatteredItems,
                    SelectMap(dyingObject.m_PEnvir.NativeDropControl,
                        NativeDropControlType.Timed, now, NextRandom));
                ScatterPhase(dyingObject, itemCreator, scatteredItems,
                    SelectMap(dyingObject.m_PEnvir.NativeDropControl,
                        NativeDropControlType.Counted, now, NextRandom));
                ScatterPhase(dyingObject, itemCreator, scatteredItems,
                    SelectWorld(M2Share.NativeWorldDropControl,
                        NativeDropControlType.Timed,
                        dyingObject.m_sCharName, now, NextRandom));
                ScatterPhase(dyingObject, itemCreator, scatteredItems,
                    SelectWorld(M2Share.NativeWorldDropControl,
                        NativeDropControlType.Counted,
                        dyingObject.m_sCharName, now, NextRandom));
            }
            catch (Exception exception) when (exception is not
                                               OutOfMemoryException)
            {
                M2Share.ErrorMessage(
                    "[Exception] NativeDropControlRuntime::TryScatter " +
                    exception.Message);
            }
        }

        internal static IReadOnlyList<NativeDropControlPending> SelectMap(
            NativeDropControlState state, NativeDropControlType type, int now,
            Func<int, int> random)
        {
            var pending = new List<NativeDropControlPending>();
            if (state == null || random == null)
                return pending;

            state.VisitAll(type, record =>
            {
                if (type == NativeDropControlType.Timed)
                {
                    SelectTimed(record, now, pending);
                    return;
                }
                SelectCounted(record, false, random, pending);
            });
            return pending;
        }

        internal static IReadOnlyList<NativeDropControlPending> SelectWorld(
            NativeDropControlState state, NativeDropControlType type,
            string monsterName, int now, Func<int, int> random)
        {
            var pending = new List<NativeDropControlPending>();
            if (state == null || random == null)
                return pending;

            state.VisitBucket(type, monsterName, record =>
            {
                if (type == NativeDropControlType.Timed)
                {
                    SelectTimed(record, now, pending);
                    return;
                }
                SelectCounted(record, true, random, pending);
            });
            return pending;
        }

        private static void SelectTimed(NativeDropControlRecord record,
            int now, ICollection<NativeDropControlPending> pending)
        {
            var elapsed = unchecked((uint)(now - record.Tick));
            var interval = unchecked((uint)(record.PeriodOrRange * 1000));
            if (elapsed < interval)
                return;

            record.Tick = now;
            pending.Add(ToPending(record));
        }

        private static void SelectCounted(NativeDropControlRecord record,
            bool worldRule, Func<int, int> random,
            ICollection<NativeDropControlPending> pending)
        {
            record.Counter = unchecked((ushort)(record.Counter + 1));
            if (record.Counter == record.RandomThreshold)
                pending.Add(ToPending(record));

            var reset = worldRule
                ? (uint)record.Counter >= unchecked((uint)record.PeriodOrRange)
                : record.PeriodOrRange == record.Counter;
            if (!reset)
                return;

            record.RandomThreshold = unchecked((ushort)(
                random(record.PeriodOrRange) + 1));
            record.Counter = 0;
        }

        private static NativeDropControlPending ToPending(
            NativeDropControlRecord record)
        {
            return new NativeDropControlPending(record.ItemName,
                record.ItemIndex, record.Quantity);
        }

        private static void ScatterPhase(TBaseObject dyingObject,
            TBaseObject itemCreator,
            IList<KeyValuePair<string, string>> scatteredItems,
            IReadOnlyList<NativeDropControlPending> pending)
        {
            foreach (var entry in pending)
                ScatterEntry(dyingObject, itemCreator, scatteredItems, entry);
        }

        private static void ScatterEntry(TBaseObject dyingObject,
            TBaseObject itemCreator,
            IList<KeyValuePair<string, string>> scatteredItems,
            NativeDropControlPending pending)
        {
            Materialize(pending, scatteredItems, CreateNativeItem, NextRandom,
                (userItem, range, dieDrop, creator, dropCreator) =>
                    dyingObject.DropItemDown(userItem, range, dieDrop, creator,
                        dropCreator), itemCreator, dyingObject);
        }

        internal static void Materialize(NativeDropControlPending pending,
            IList<KeyValuePair<string, string>> scatteredItems,
            Func<int, (GoodItem StdItem, TUserItem UserItem)> createItem,
            Func<int, int> random,
            Func<TUserItem, int, bool, TBaseObject, TBaseObject, bool>
                placeItem,
            TBaseObject itemCreator,
            TBaseObject dropCreator)
        {
            var remaining = pending.Quantity;
            while (remaining != 0)
            {
                var created = createItem(pending.ItemIndex);
                var stdItem = created.StdItem;
                var userItem = created.UserItem;

                ushort emittedQuantity;
                if (stdItem?.StdMode == 7 && userItem != null)
                {
                    emittedQuantity = remaining;
                    userItem.Dura = remaining;
                    remaining = 0;
                }
                else
                {
                    emittedQuantity = 1;
                    remaining--;
                }

                InitializeForDrop(stdItem, userItem, random);
                if (userItem == null ||
                    !placeItem(userItem, ScatterRange, true, itemCreator,
                        dropCreator))
                {
                    continue;
                }

                scatteredItems?.Add(new KeyValuePair<string, string>(
                    pending.ItemName, emittedQuantity.ToString()));
            }
        }

        private static (GoodItem StdItem, TUserItem UserItem) CreateNativeItem(
            int itemIndex)
        {
            var stdItem = itemIndex > 0
                ? M2Share.UserEngine.GetStdItem(itemIndex)
                : null;
            if (stdItem == null)
                return (null, null);

            var makeIndex = M2Share.GetItemNumber();
            if (NativeItemFactory.GetClassName(stdItem) == null)
                return (stdItem, null);

            return (stdItem, new TUserItem
            {
                wIndex = unchecked((ushort)itemIndex),
                MakeIndex = makeIndex,
                Dura = stdItem.DuraMax,
                DuraMax = stdItem.DuraMax
            });
        }

        private static void InitializeForDrop(GoodItem stdItem,
            TUserItem userItem, Func<int, int> random)
        {
            if (stdItem == null || userItem == null ||
                UsesPileInitialization(stdItem))
            {
                return;
            }

            userItem.Dura = unchecked((ushort)HUtil32.Round(
                userItem.DuraMax / 100.0 * (20 + random(80))));
        }

        private static bool UsesPileInitialization(GoodItem stdItem)
        {
            return NativeItemFactory.GetClassName(stdItem) is
                "TBasePileItem" or "TLuckOil" or "TPneumaStone" or
                "TTaoFaLingAddExpItem" or "TGoldAcus" or "TShiMenCall" or
                "TSuperExpItem" or "TLevelBuffItem" or "TNewHappyCake" or
                "THeroJingmaiDrug" or "TPileFlower" or "THeroHypericum" or
                "THeroFileDragonScroll" or "THeroExpScroll" or
                "TJingXiuBook";
        }

        private static int NextRandom(int range)
        {
            return M2Share.RandomNumber?.Random(range) ?? 0;
        }
    }
}

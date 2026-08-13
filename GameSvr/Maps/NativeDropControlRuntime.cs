using SystemModule;

namespace GameSvr
{
    internal readonly record struct NativeDropControlPending(
        string ItemName, int ItemIndex, ushort Quantity);

    internal static class NativeDropControlRuntime
    {
        /// <summary>
        /// 本类的原生本体是 <c>sub_720278</c>（掉落控制派发器），**不是**
        /// <c>sub_71FA20</c> 段3。归属由记录布局唯一确定：
        /// <see cref="NativeDropControlRecord.ToNativeLayout"/> 的
        /// ItemName@+0x29 / Quantity@+0x52 / PeriodOrRange@+0x54 / ItemIndex@+0x58 /
        /// Counter@+0x5C / RandomThreshold@+0x5E / Tick@+0x60 逐字段对上
        /// <c>sub_77C580</c>（Counted）与 <c>sub_77C738</c>（Timed）：
        /// <code>
        /// 77C5ED  66 FF 40 5C           inc word [rec+0x5C]        ; Counter++
        /// 77C5F9  66 3B 42 5E           cmp ax,word [rec+0x5E]     ; == RandomThreshold ?
        /// 77C608  89 50 60              mov [rec+0x60],edx         ; Tick := now
        /// 77C62F  83 C2 29              add edx,0x29               ; ItemName
        /// 77C639  8B 40 58              mov eax,[rec+0x58]         ; ItemIndex
        /// 77C641  66 8B 40 52           mov ax,word [rec+0x52]     ; Quantity
        /// 77C7A8  2B 50 60              sub edx,[rec+0x60]         ; elapsed
        /// 77C7AD  69 40 54 E8 03 00 00  imul eax,[rec+0x54],0x3E8  ; PeriodOrRange*1000
        /// </code>
        /// 而段3 走的是另一套：<c>0x71FEC8 call 0x752CAC</c>，单例
        /// <c>[0x7D71F4]</c>，键是 <c>[PEnvir+0x44]</c>（地图名）加
        /// <c>word[self+0x278]</c>，返回的表由 <c>0x71FF24 call 0x74DE54</c>
        /// (MakeItemByName) 逐条造物 —— 与本类的四相 Select/Materialize 结构无关。
        ///
        /// <c>sub_720278</c> 的四相散落全部落到 <c>sub_72016C</c>
        /// （= <see cref="Materialize"/>），它的半径是立即数 **4**：
        /// <code>
        /// 720209  6A 01 / 6A 00 / 8B 45 F8 50 / 6A 00   push 1 / 0 / &amp;creator / nil
        /// 720213  B9 04 00 00 00                        mov ecx,4     ; landing radius
        /// 720218  8B D3 / 8B 45 F4                      mov edx,item / mov eax,dropper
        /// 72021D  E8 7E 86 04 00                        call 0x7688A0
        /// </code>
        /// ecx 在 <c>sub_7688A0</c> 序言 <c>0x7688B4 8B D9 mov ebx,ecx</c> 存活到
        /// <c>0x768907 53 push ebx</c>，即 <c>sub_768688</c> 的 <c>[ebp+0x10]</c> ——
        /// <c>0x7686B5 8B 45 10 / 0x7686BA 0F 8E A6 00 00 00 jle</c> 那圈求空地的环数界。
        ///
        /// 曾于 2026-08-14 被按「段3 = 本类」的误归属从 4 改成 3；
        /// <c>AuditTools/NativeDropControlRuntimeCheck</c> 第 104 行
        /// <c>Equal(4, failedRange, "native fixed scatter range")</c> 当场变红，
        /// 该断言即本值的既有钉子。
        /// </summary>
        internal const int ScatterRange = 4;

        /// <summary>
        /// ⚠️ 顺序未对齐 —— 本方法保留既有次序（ordinary 先、controlled 后），但那是
        /// 建立在「controlled = <c>sub_71FA20</c> 段3」这个**已被推翻**的归属上的
        /// （见 <see cref="ScatterRange"/>）。真实调用图是两个**兄弟**调用，掉落控制
        /// 整个跑在 <c>sub_71FA20</c> 之前：
        /// <code>
        /// ; sub_71F46C —— 怪物 VMT 槽 +0x1FC（123 个怪物 VMT 持有它，每个都过
        /// ;               Delphi 自指针自检 dword[VMT-0x4C]==VMT）
        /// 71F47E  E8 F5 0D 00 00   call 0x720278   ; 掉落控制四相（本类）
        /// 71F491  E8 8A 05 00 00   call 0x71FA20   ; 段1 专属链 / 段2 自有表 / 段3 / 金币
        /// </code>
        /// <c>sub_71FA20</c> 全镜像只有 <c>0x71F491</c> 这一个 E8 调用点、0 个 dword
        /// 引用，所以它不可能先于 <c>sub_720278</c> 跑；怪物 Die
        /// <c>0x71E3D2 / 0x71E3EF FF 96 FC 01 00 00 call [esi+0x1FC]</c> 派发到的是
        /// <c>sub_71F46C</c> 而不是 <c>sub_71FA20</c>。
        ///
        /// 未在本轮改正的原因：<c>sub_720278</c> 在 <c>sub_71FA20</c> **之外**，因此
        /// 不受段内那几道门约束（<c>0x71FA6C</c> 一次性哨兵、<c>0x71FA8A</c> 空掉落表
        /// 早退、<c>0x71FADA/0x71FAE3/0x71FAEC</c> 防沉迷三门），而 C# 把
        /// <c>TryScatter</c> 放在 <c>scatterBlocked</c> 里边。单独搬次序而不同时处理
        /// 门控，会落到「既不是原生也不是现状」的第三种状态。次序与门控要一并改，
        /// 属独立契约，见 <c>docs/drop33_owntable_scatter_20260814.md</c>。
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

            // Segment 3 builds its item through MakeItemByName sub_74DE54, which is
            // just a name lookup (sub_74C2D4 @0x74DE62) feeding the class factory
            // sub_74C338 @0x74DE77 — so the class constructor decides the seed.
            // The root constructor sub_783788 @0x7837E2-EA copies word[std+0x1C]
            // (DuraMax) into BOTH Dura and DuraMax, but the pile constructor
            // sub_7880F0 overwrites it right after the chained call:
            //   0078810D  E8 76 B6 FF FF     call 0x783788
            //   00788112  66 C7 46 26 01 00  mov word [esi+0x26],1
            // For a pile Dura is the stack count, so seeding it from DuraMax would
            // hand out DuraMax units per configured drop instead of one.
            return (stdItem, new TUserItem
            {
                wIndex = unchecked((ushort)itemIndex),
                MakeIndex = makeIndex,
                Dura = NativeItemFactory.IsPileItem(stdItem)
                    ? (ushort)1
                    : stdItem.DuraMax,
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

        /// <summary>
        /// The pile classes all carry <c>[VMT+0x28] = 0x7882B4</c>, a bare
        /// <c>ret</c>, so the drop hook leaves their Dura at the constructor's 1.
        /// The membership list used to be duplicated here; it now defers to
        /// <see cref="NativeItemFactory.IsPileItem"/> so the two cannot drift.
        /// </summary>
        private static bool UsesPileInitialization(GoodItem stdItem)
        {
            return NativeItemFactory.IsPileItem(stdItem);
        }

        private static int NextRandom(int range)
        {
            return M2Share.RandomNumber?.Random(range) ?? 0;
        }
    }
}

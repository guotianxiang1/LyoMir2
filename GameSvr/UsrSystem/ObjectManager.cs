using System.Collections.Concurrent;
using System.Diagnostics;
using SystemModule;

namespace GameSvr
{
    
    
    
    public class ObjectManager
    {
        [ThreadStatic]
        private static DeferredRegistration _currentDeferredRegistration;

        public sealed class DeferredRegistration : IDisposable
        {
            private enum RegistrationState
            {
                AwaitingConstruction,
                Pending,
                Committed,
                RolledBack,
                Disposed
            }

            private readonly ObjectManager _owner;
            private readonly DeferredRegistration _previous;
            private readonly int _ownerThreadId;
            private readonly object _syncRoot = new object();
            private RegistrationState _state;
            private TBaseObject _actor;
            private long _publicationGeneration;

            internal DeferredRegistration(ObjectManager owner,
                DeferredRegistration previous)
            {
                _owner = owner;
                _previous = previous;
                _ownerThreadId = Environment.CurrentManagedThreadId;
                _state = RegistrationState.AwaitingConstruction;
                _currentDeferredRegistration = this;
            }

            internal bool TryCapture(TBaseObject actor)
            {
                lock (_syncRoot)
                {
                    if (_state != RegistrationState.AwaitingConstruction
                        || Environment.CurrentManagedThreadId != _ownerThreadId
                        || !ReferenceEquals(_currentDeferredRegistration, this))
                    {
                        return false;
                    }

                    _actor = actor;
                    _state = RegistrationState.Pending;
                    _currentDeferredRegistration = _previous;
                    return true;
                }
            }

            internal bool IsOwnedBy(ObjectManager owner)
            {
                return ReferenceEquals(_owner, owner);
            }

            public bool TryCommit(TBaseObject expectedActor)
            {
                if (expectedActor == null) return false;

                lock (_syncRoot)
                {
                    if (_state != RegistrationState.Pending
                        || !ReferenceEquals(_actor, expectedActor))
                    {
                        return false;
                    }

                    if (!_owner.TryPublish(expectedActor.ObjectId,
                            expectedActor, out _publicationGeneration))
                    {
                        return false;
                    }

                    _state = RegistrationState.Committed;
                    return true;
                }
            }

            public bool TryRollback(TBaseObject expectedActor)
            {
                if (expectedActor == null) return false;

                lock (_syncRoot)
                {
                    if (!ReferenceEquals(_actor, expectedActor)) return false;

                    if (_state == RegistrationState.Pending)
                    {
                        _state = RegistrationState.RolledBack;
                        _actor = null;
                        return true;
                    }

                    if (_state != RegistrationState.Committed) return false;
                    _state = RegistrationState.RolledBack;
                    _actor = null;
                }

                return _owner.Remove(expectedActor.ObjectId, expectedActor,
                    _publicationGeneration);
            }

            public void Dispose()
            {
                lock (_syncRoot)
                {
                    if (_state == RegistrationState.Disposed) return;

                    if (_state == RegistrationState.AwaitingConstruction)
                    {
                        if (Environment.CurrentManagedThreadId
                            != _ownerThreadId)
                        {
                            throw new InvalidOperationException(
                                "An awaiting deferred registration must be disposed on its owner thread.");
                        }
                        if (!ReferenceEquals(_currentDeferredRegistration,
                                this))
                        {
                            throw new InvalidOperationException(
                                "Awaiting deferred registrations must be disposed in LIFO order.");
                        }

                        _currentDeferredRegistration = _previous;
                    }

                    // Pending actors were never published. Committed actors are
                    // intentionally retained unless TryRollback is explicit.
                    _actor = null;
                    _state = RegistrationState.Disposed;
                }
            }
        }

        public DeferredRegistration BeginDeferredRegistration()
        {
            // The scope captures exactly the next TBaseObject base construction
            // on this thread and detaches before the derived constructor runs.
            return new DeferredRegistration(this,
                _currentDeferredRegistration);
        }

        internal void RegisterConstructed(TBaseObject actor)
        {
            var registration = _currentDeferredRegistration;
            if (registration == null
                || !registration.IsOwnedBy(this))
            {
                Add(actor.ObjectId, actor);
                return;
            }

            if (!registration.TryCapture(actor))
            {
                throw new InvalidOperationException(
                    "Deferred object registration scope is invalid.");
            }
        }

        
        
        
        private readonly ConcurrentDictionary<int, TBaseObject> _actors = new ConcurrentDictionary<int, TBaseObject>();
        private readonly object _publicationSync = new object();
        private readonly Dictionary<int, long> _publicationGenerations = new();
        private long _nextPublicationGeneration;

        public void Add(int actorId, TBaseObject actor)
        {
            if (!TryPublish(actorId, actor, out _))
                throw new InvalidOperationException($"Duplicate actor ID: {actorId}");
        }

        private bool TryPublish(int actorId, TBaseObject actor,
            out long publicationGeneration)
        {
            publicationGeneration = 0;
            lock (_publicationSync)
            {
                if (!_actors.TryAdd(actorId, actor)) return false;
                publicationGeneration = ++_nextPublicationGeneration;
                _publicationGenerations[actorId] = publicationGeneration;
                return true;
            }
        }

        public TBaseObject Get(int actorId)
        {
            TBaseObject actor = null;
            if (_actors.TryGetValue(actorId, out actor))
            {
                return actor;
            }
            return actor;
        }

        internal TBaseObject[] SnapshotEnvironmentObjects(Envirnoment environment)
        {
            if (environment == null) return Array.Empty<TBaseObject>();
            return _actors.Values
                .Where(actor => ReferenceEquals(actor.m_PEnvir, environment))
                .ToArray();
        }

        public void Remove(int actorId)
        {
            TBaseObject ghostactor;
            lock (_publicationSync)
            {
                M2Share.PasEngine?.CancelDeferredCallsForObject(actorId);
                M2Share.PasEngine?.ClearMonsterScriptState(actorId);
                _actors.TryRemove(actorId, out ghostactor);
                _publicationGenerations.Remove(actorId);
            }
            if (ghostactor != null)
            {
                Debug.WriteLine($"清理死亡对象 名称:[{ghostactor.m_sCharName}] 地图:{ghostactor.m_sMapName} 坐标:{ghostactor.m_nCurrX}:{ghostactor.m_nCurrY}");
            }
        }

        public bool Remove(int actorId, TBaseObject expectedActor)
        {
            if (expectedActor == null) return false;

            lock (_publicationSync)
            {
                return RemoveExactNoLock(actorId, expectedActor, null);
            }
        }

        private bool Remove(int actorId, TBaseObject expectedActor,
            long expectedPublicationGeneration)
        {
            if (expectedActor == null || expectedPublicationGeneration <= 0)
                return false;

            lock (_publicationSync)
            {
                return RemoveExactNoLock(actorId, expectedActor,
                    expectedPublicationGeneration);
            }
        }

        private bool RemoveExactNoLock(int actorId, TBaseObject expectedActor,
            long? expectedPublicationGeneration)
        {
            if (expectedPublicationGeneration.HasValue
                && (!_publicationGenerations.TryGetValue(actorId,
                        out var currentGeneration)
                    || currentGeneration != expectedPublicationGeneration.Value))
                return false;

            var pair = new KeyValuePair<int, TBaseObject>(actorId,
                expectedActor);
            var removed = ((ICollection<KeyValuePair<int, TBaseObject>>)_actors)
                .Remove(pair);
            if (!removed) return false;

            _publicationGenerations.Remove(actorId);
            M2Share.PasEngine?.CancelDeferredCallsForObject(actorId);
            M2Share.PasEngine?.ClearMonsterScriptState(actorId);
            Debug.WriteLine($"清理死亡对象 名称:[{expectedActor.m_sCharName}] 地图:{expectedActor.m_sMapName} 坐标:{expectedActor.m_nCurrX}:{expectedActor.m_nCurrY}");
            return true;
        }

        // 已删除非原生的全局 ghost 扫描 ClearObject()（2026-08-03）。
        // 战神【没有】任何 id→对象的全局 ghost 扫描：它的对象释放是
        //   (a) 各逐类型 tick 循环负责【检测 ghost 位 [obj+0x73] 并摘链】
        //       （ProcessMon sub_67C150 循环2 @0x67C46F、循环3 @0x67C614），同时
        //       sub_67D8F0 把对象入队到【单一全局延迟释放 FIFO】
        //       （MonsterMgr+0x20/+0x24/+0x28，12 字节节点 {obj,tick,next}；入队点恰好 2 个，
        //        均在 ProcessMon 内：0x67C47E / 0x67C623），并调 vtable+0x7C 离场钩子；
        //   (b) 该 FIFO 由 ProcessMon 循环1 在 0x67C1BD `cmp eax,0x493E0`
        //       （= 300000ms = 5 分钟）后排空，sub_404690 = TObject.Free 真正销毁。
        // 即：检测在逐类型循环，释放集中且延迟 5 分钟。ClearObject 的 3 分钟
        // dwMakeGhostTime 门在战神中没有对应物。dwMakeGhostTime 现已【没有任何
        // 消费者】：TBaseObject.Base.cs 的"死亡→ghost"延迟也已按 0x766682 改为
        // 读 word[obj+0x38]（m_wNativeCorpseSeconds，构造默认 60 秒 @0x764E9E、
        // 刷怪时由 mongen.txt 第 8 列覆盖）；镜像里根本没有 "MakeGhostTime" 串。
        // 配置字段本身保留，仅为 ini 读写兼容。
        // 删除前置条件已补齐：MonGen 怪物/守卫/宝宝（UsrEngn.ProcessMonsters）、
        // QuestNPC（ProcessNpcs）、Merchant（ProcessMerchants）三处逐类型 reap 现在都
        // 调用 ObjectManager.Remove(id, expected)，其中的
        // PasEngine.CancelDeferredCallsForObject + ClearMonsterScriptState
        // （= vtable+0x7C 的对应物）因此得以保留。
        // 玩家/机器人/英雄/魔法塔怪/动态房对象/动态房 NPC 六类本来就在各自循环里调 Remove。
        // ClearObject 自带的 playCount/monsterCount 只喂一句 Debug.WriteLine（无人读），
        // 其 hero skip 也是多余的（英雄自回收），故无需保留。
    }
}

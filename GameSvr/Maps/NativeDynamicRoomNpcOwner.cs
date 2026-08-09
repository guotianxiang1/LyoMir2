namespace GameSvr
{
    public sealed class NativeDynamicRoomMaterializedNpc
    {
        public NativeDynamicRoomMaterializedNpc(NormNpc npc,
            NativeDynamicRoomDynamicNpcScriptBinding binding)
        {
            Npc = npc;
            Binding = binding;
        }

        public NormNpc Npc { get; }
        public NativeDynamicRoomDynamicNpcScriptBinding Binding { get; }
    }

    /// <summary>
    /// Transaction boundary required by the physical NPC owner. A committed
    /// journal is transferred to the owner and must not be mutated elsewhere.
    /// All state/claim members must be passive and must not call back into the
    /// owner; they are revalidated while the owner lock is held.
    /// Actor, map, and NPC-registry publication must all be committed and
    /// compensated by exact reference.
    /// </summary>
    public interface INativeDynamicRoomNpcMaterializationJournal
    {
        NativeDynamicRoomDefinition Definition { get; }
        Envirnoment Environment { get; }
        int PhysicalInstanceId { get; }
        IReadOnlyList<NativeDynamicRoomMaterializedNpc> Npcs { get; }
        bool IsCommitted { get; }
        bool IsDestroyed { get; }
        bool HasUnresolvedPublication { get; }
        // Claim operations must be atomic and must not invoke owner callbacks.
        bool TryClaimOwnership(object ownerCapability);
        bool IsOwnershipClaimedBy(object ownerCapability);
        bool TryReleaseOwnershipClaim(object ownerCapability);
        bool TryCommit();
        bool TryRollback();
        bool TryDestroyExact();
    }

    public interface INativeDynamicRoomNpcMaterializer
    {
        bool TryPrepare(
            NativeDynamicRoomDefinition definition,
            Envirnoment environment,
            int physicalInstanceId,
            IReadOnlyList<NativeDynamicRoomDynamicNpcScriptBinding> bindings,
            out INativeDynamicRoomNpcMaterializationJournal journal,
            out string diagnostic);
    }

    /// <summary>
    /// Monotonic proof that an exact physical room can no longer be leased by
    /// its manager. Only the manager-issued sealed implementation is accepted
    /// by the physical owner.
    /// </summary>
    public interface INativeDynamicRoomPhysicalRetirementPermit
    {
        NativeDynamicRoomPhysicalNpcOwnership PhysicalOwnership { get; }
        NativeDynamicRoomDefinition Definition { get; }
        Envirnoment Environment { get; }
        int PhysicalInstanceId { get; }
        bool IsRetiredExact { get; }
    }

    public sealed class NativeDynamicRoomPhysicalNpcOwnership
    {
        private readonly NativeDynamicRoomNpcOwner _owner;
        private readonly object _identity;

        internal NativeDynamicRoomPhysicalNpcOwnership(
            NativeDynamicRoomNpcOwner owner, object identity,
            NativeDynamicRoomDefinition definition,
            Envirnoment environment, int physicalInstanceId,
            NormNpc controller, IReadOnlyList<NormNpc> configuredNpcs)
        {
            _owner = owner;
            _identity = identity;
            Definition = definition;
            Environment = environment;
            PhysicalInstanceId = physicalInstanceId;
            Controller = controller;
            ConfiguredNpcs = configuredNpcs;
        }

        public NativeDynamicRoomDefinition Definition { get; }
        public Envirnoment Environment { get; }
        public int PhysicalInstanceId { get; }
        public NormNpc Controller { get; }
        public IReadOnlyList<NormNpc> ConfiguredNpcs { get; }
        public bool DestroyPending { get; internal set; }
        public bool IsDestroyed { get; internal set; }

        internal bool IsOwnedBy(NativeDynamicRoomNpcOwner owner,
            object identity)
        {
            return ReferenceEquals(_owner, owner)
                   && ReferenceEquals(_identity, identity);
        }

        internal bool IsOwnedBy(NativeDynamicRoomNpcOwner owner)
        {
            return ReferenceEquals(_owner, owner);
        }
    }

    public sealed class NativeDynamicRoomNpcActivationBinding
    {
        internal NativeDynamicRoomNpcActivationBinding(
            NativeDynamicRoomPhysicalNpcOwnership physicalOwnership,
            NativeDynamicRoomActivationLease lease,
            IReadOnlyList<NativeDynamicRoomPasScriptBindingHandle> routeHandles)
        {
            PhysicalOwnership = physicalOwnership;
            Lease = lease;
            RouteHandles = routeHandles;
        }

        public NativeDynamicRoomPhysicalNpcOwnership PhysicalOwnership { get; }
        public NativeDynamicRoomActivationLease Lease { get; }
        public IReadOnlyList<NativeDynamicRoomPasScriptBindingHandle>
            RouteHandles { get; }
        public bool IsRetired { get; internal set; }
    }

    /// <summary>
    /// Owns the two distinct dynamic-room NPC lifetimes. Physical NPCs belong
    /// to an exact environment/physical-instance pair. PAS routes belong to one
    /// exact activation lease and are retired at state 1. Physical destruction
    /// is an explicit, later operation and never occurs during route retirement.
    /// Startup transfers a committed materialization journal into this owner.
    /// </summary>
    public sealed class NativeDynamicRoomNpcOwner
    {
        private sealed class PhysicalEntry
        {
            public object Identity { get; init; }
            public INativeDynamicRoomNpcMaterializationJournal Journal { get; init; }
            public NativeDynamicRoomPhysicalNpcOwnership Token { get; init; }
            public NativeDynamicRoomMaterializedNpc[] Npcs { get; init; }
            public ActivationEntry Activation { get; set; }
            public bool DestroyInProgress { get; set; }
            public bool DestroyPending { get; set; }
        }

        private sealed class ActivationEntry
        {
            public NativeDynamicRoomNpcActivationBinding Token { get; init; }
            public bool Retiring { get; set; }
            public bool Retired { get; set; }
        }

        private readonly object _syncRoot = new();
        private readonly NativeDynamicRoomPasScriptRouteTable _routeTable;
        private readonly Dictionary<Envirnoment, PhysicalEntry> _physicalByEnvironment =
            new(ReferenceEqualityComparer.Instance);

        public NativeDynamicRoomNpcOwner(
            NativeDynamicRoomPasScriptRouteTable routeTable)
        {
            ArgumentNullException.ThrowIfNull(routeTable);
            _routeTable = routeTable;
        }

        public bool TryAdoptCommittedPublication(
            NativeDynamicRoomDefinition definition,
            Envirnoment environment,
            int physicalInstanceId,
            IReadOnlyList<NativeDynamicRoomDynamicNpcScriptBinding> bindings,
            INativeDynamicRoomNpcMaterializationJournal journal,
            out NativeDynamicRoomPhysicalNpcOwnership ownership)
        {
            ownership = null;
            if (!TryValidatePublication(definition, environment,
                    physicalInstanceId, bindings, journal,
                    out var published, out var controller,
                    out var configuredNpcs))
                return false;

            var identity = new object();
            try
            {
                if (!journal.TryClaimOwnership(identity))
                    return false;
            }
            catch
            {
                return false;
            }

            var adopted = false;
            try
            {
                lock (_syncRoot)
                {
                    if (_physicalByEnvironment.ContainsKey(environment)
                        || !journal.IsOwnershipClaimedBy(identity)
                        || !TryValidatePublication(definition, environment,
                            physicalInstanceId, bindings, journal,
                            out published, out controller,
                            out configuredNpcs))
                        return false;

                    ownership = new NativeDynamicRoomPhysicalNpcOwnership(
                        this, identity, definition, environment,
                        physicalInstanceId, controller,
                        Array.AsReadOnly(configuredNpcs));
                    _physicalByEnvironment.Add(environment, new PhysicalEntry
                    {
                        Identity = identity,
                        Journal = journal,
                        Token = ownership,
                        Npcs = published
                    });
                    adopted = true;
                    return true;
                }
            }
            catch
            {
                ownership = null;
                return false;
            }
            finally
            {
                if (!adopted)
                {
                    try
                    {
                        journal.TryReleaseOwnershipClaim(identity);
                    }
                    catch
                    {
                        // A failed release leaves the journal unavailable.
                    }
                }
            }
        }

        public bool TryAttachActivationBinding(
            NativeDynamicRoomPhysicalNpcOwnership ownership,
            NativeDynamicRoomActivationLease lease,
            IReadOnlyList<NativeDynamicRoomPasRouteRegistration> registrations,
            IReadOnlyList<NativeDynamicRoomPasScriptBindingHandle> routeHandles,
            out NativeDynamicRoomNpcActivationBinding activationBinding)
        {
            activationBinding = null;
            if (ownership == null || lease == null || registrations == null
                || routeHandles == null
                || !ReferenceEquals(ownership.Environment, lease.Environment)
                || !ReferenceEquals(ownership.Definition, lease.Definition)
                || !lease.IsCurrentActive())
                return false;

            NativeDynamicRoomPasRouteRegistration[] requests;
            NativeDynamicRoomPasScriptBindingHandle[] handles;
            try
            {
                requests = registrations.ToArray();
                handles = routeHandles.ToArray();
            }
            catch
            {
                return false;
            }

            lock (_syncRoot)
            {
                if (!TryGetExactPhysicalEntryNoLock(ownership, out var physical)
                    || physical.DestroyPending
                    || physical.DestroyInProgress
                    || !lease.IsCurrentActive()
                    || !ValidateActivationRoutesNoLock(physical, lease,
                        requests, handles, out var canonicalHandles))
                    return false;

                if (physical.Activation != null)
                {
                    var current = physical.Activation;
                    if (ReferenceEquals(current.Token.Lease, lease)
                        && SameHandles(current.Token.RouteHandles,
                            canonicalHandles))
                    {
                        activationBinding = current.Token;
                        return !current.Retired;
                    }
                    if (!current.Retired) return false;
                }

                activationBinding = new NativeDynamicRoomNpcActivationBinding(
                    ownership, lease,
                    Array.AsReadOnly(canonicalHandles));
                physical.Activation = new ActivationEntry
                {
                    Token = activationBinding
                };
                return true;
            }
        }

        public bool TryRetireActivationBinding(
            NativeDynamicRoomActivationLease lease)
        {
            if (lease == null || lease.IsCurrentActive()) return false;

            ActivationEntry activation;
            NativeDynamicRoomPasScriptBindingHandle[] handles;
            lock (_syncRoot)
            {
                if (lease.IsCurrentActive()
                    || !_physicalByEnvironment.TryGetValue(lease.Environment,
                        out var physical)
                    || physical.Activation == null
                    || !ReferenceEquals(physical.Activation.Token.Lease, lease))
                    return false;

                activation = physical.Activation;
                if (activation.Retired) return true;
                if (activation.Retiring) return false;
                activation.Retiring = true;
                handles = activation.Token.RouteHandles.ToArray();
            }

            var retired = true;
            foreach (var handle in handles)
            {
                try
                {
                    if (!handle.Released
                        && !_routeTable.Unregister(handle)
                        && !handle.Released)
                        retired = false;
                }
                catch
                {
                    retired = false;
                }
            }

            lock (_syncRoot)
            {
                if (!_physicalByEnvironment.TryGetValue(lease.Environment,
                        out var physical)
                    || !ReferenceEquals(physical.Activation, activation))
                    return false;

                activation.Retiring = false;
                if (retired)
                {
                    activation.Retired = true;
                    activation.Token.IsRetired = true;
                }
                return retired;
            }
        }

        public bool TryFullDestroy(
            NativeDynamicRoomPhysicalNpcOwnership ownership,
            INativeDynamicRoomPhysicalRetirementPermit retirementPermit)
        {
            if (!IsExactRetirementPermit(ownership, retirementPermit))
                return false;

            PhysicalEntry physical;
            lock (_syncRoot)
            {
                if (ownership.IsDestroyed && ownership.IsOwnedBy(this))
                    return true;
                if (!TryGetExactPhysicalEntryNoLock(ownership, out physical)
                    || !IsJournalOwnedBy(physical.Journal, physical.Identity)
                    || !IsExactRetirementPermit(ownership, retirementPermit)
                    || physical.DestroyInProgress
                    || ownership.Environment.DynamicRoomState != 0
                    || ownership.Environment.DynamicRoomPlayerCount != 0
                    || physical.Activation is { Retired: false })
                    return false;

                physical.DestroyPending = true;
                physical.DestroyInProgress = true;
                ownership.DestroyPending = true;
            }

            var destroyed = IsJournalDestroyComplete(physical.Journal);
            try
            {
                if (!destroyed)
                    physical.Journal.TryDestroyExact();
            }
            catch
            {
                // The journal status is authoritative after a callback fault.
            }
            destroyed = IsJournalDestroyComplete(physical.Journal);

            lock (_syncRoot)
            {
                if (!TryGetExactPhysicalEntryNoLock(ownership, out var current)
                    || !ReferenceEquals(current, physical))
                    return false;

                physical.DestroyInProgress = false;
                if (!destroyed) return false;

                ownership.DestroyPending = false;
                ownership.IsDestroyed = true;
                _physicalByEnvironment.Remove(ownership.Environment);
                return true;
            }
        }

        private static bool IsExactRetirementPermit(
            NativeDynamicRoomPhysicalNpcOwnership ownership,
            INativeDynamicRoomPhysicalRetirementPermit retirementPermit)
        {
            return ownership != null
                   && retirementPermit
                       is NativeDynamicRoomPhysicalRetirementPermit concrete
                   && concrete.IsRetiredExact
                   && ReferenceEquals(concrete.PhysicalOwnership,
                       ownership)
                   && ReferenceEquals(concrete.Definition,
                       ownership.Definition)
                   && ReferenceEquals(concrete.Environment,
                       ownership.Environment)
                   && concrete.PhysicalInstanceId
                       == ownership.PhysicalInstanceId;
        }

        private static bool IsJournalDestroyComplete(
            INativeDynamicRoomNpcMaterializationJournal journal)
        {
            try
            {
                return journal.IsDestroyed
                       && !journal.HasUnresolvedPublication;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsJournalOwnedBy(
            INativeDynamicRoomNpcMaterializationJournal journal,
            object ownerCapability)
        {
            try
            {
                return journal.IsOwnershipClaimedBy(ownerCapability);
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetPhysicalOwnership(Envirnoment environment,
            int physicalInstanceId,
            out NativeDynamicRoomPhysicalNpcOwnership ownership)
        {
            ownership = null;
            if (environment == null || physicalInstanceId < 0) return false;

            lock (_syncRoot)
            {
                if (!_physicalByEnvironment.TryGetValue(environment,
                        out var physical)
                    || physical.Token.PhysicalInstanceId != physicalInstanceId
                    || physical.Token.IsDestroyed)
                    return false;
                ownership = physical.Token;
                return true;
            }
        }

        public bool TryGetActivationBinding(
            NativeDynamicRoomActivationLease lease,
            out NativeDynamicRoomNpcActivationBinding activationBinding)
        {
            activationBinding = null;
            if (lease == null) return false;

            lock (_syncRoot)
            {
                if (!_physicalByEnvironment.TryGetValue(lease.Environment,
                        out var physical)
                    || physical.Activation == null
                    || !ReferenceEquals(physical.Activation.Token.Lease, lease))
                    return false;
                activationBinding = physical.Activation.Token;
                return true;
            }
        }

        public bool ContainsPhysicalNpcExact(
            NativeDynamicRoomPhysicalNpcOwnership ownership, NormNpc npc)
        {
            if (ownership == null || npc == null) return false;
            lock (_syncRoot)
            {
                return TryGetExactPhysicalEntryNoLock(ownership,
                           out var physical)
                       && physical.Npcs.Any(entry =>
                           ReferenceEquals(entry.Npc, npc));
            }
        }

        private bool TryGetExactPhysicalEntryNoLock(
            NativeDynamicRoomPhysicalNpcOwnership ownership,
            out PhysicalEntry physical)
        {
            physical = null;
            return ownership != null
                   && _physicalByEnvironment.TryGetValue(
                       ownership.Environment, out physical)
                   && ownership.PhysicalInstanceId
                       == physical.Token.PhysicalInstanceId
                   && ownership.IsOwnedBy(this, physical.Identity)
                   && ReferenceEquals(physical.Token, ownership);
        }

        private bool ValidateActivationRoutesNoLock(PhysicalEntry physical,
            NativeDynamicRoomActivationLease lease,
            NativeDynamicRoomPasRouteRegistration[] registrations,
            NativeDynamicRoomPasScriptBindingHandle[] handles,
            out NativeDynamicRoomPasScriptBindingHandle[] canonicalHandles)
        {
            canonicalHandles = null;
            if (registrations == null || handles == null
                || registrations.Length != physical.Npcs.Length
                || handles.Length != registrations.Length
                || physical.Journal.IsDestroyed
                || !physical.Journal.IsCommitted
                || physical.Journal.HasUnresolvedPublication)
                return false;
            if (!IsJournalOwnedBy(physical.Journal, physical.Identity))
                return false;

            var byNpc = new Dictionary<NormNpc, (
                NativeDynamicRoomDynamicNpcScriptBinding Binding,
                NativeDynamicRoomPasScriptBindingHandle Handle)>(
                ReferenceEqualityComparer.Instance);
            for (var index = 0; index < handles.Length; index++)
            {
                var registration = registrations[index];
                var handle = handles[index];
                if (registration?.Npc == null || registration.Binding == null
                    || handle == null || handle.Released
                    || !handle.IsOwnedBy(_routeTable)
                    || !ReferenceEquals(handle.ActivationLease, lease)
                    || !ReferenceEquals(handle.PlannedBinding,
                        registration.Binding)
                    || !ReferenceEquals(handle.Npc, registration.Npc)
                    || !byNpc.TryAdd(registration.Npc,
                        (registration.Binding, handle)))
                    return false;
            }

            canonicalHandles = new NativeDynamicRoomPasScriptBindingHandle[
                physical.Npcs.Length];
            for (var index = 0; index < physical.Npcs.Length; index++)
            {
                var materialized = physical.Npcs[index];
                var npc = materialized.Npc;
                var planned = materialized.Binding;
                if (!byNpc.TryGetValue(npc, out var route)
                    || !ReferenceEquals(route.Binding, planned))
                    return false;
                var handle = route.Handle;
                canonicalHandles[index] = handle;
                if (handle.NpcObjectId != npc.ObjectId
                    || !ReferenceEquals(npc.m_PEnvir, lease.Environment)
                    || npc.m_boGhost
                    || M2Share.ObjectManager == null
                    || !ReferenceEquals(M2Share.ObjectManager.Get(npc.ObjectId),
                        npc)
                    || !handle.BoundToCurrentActivation
                    || !handle.DefinitionMatchesActivation
                    || !handle.BoundToLeaseEnvironment
                    || !handle.HasCanonicalScriptPath
                    || handle.PlannedScriptPresent != planned.HasScript
                    || !string.Equals(handle.ScriptPath, planned.ScriptPath,
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                if (planned.HasScript
                    && !_routeTable.ValidateExpected(npc, handle, out _))
                    return false;
            }
            return true;
        }

        private static bool TryValidatePublication(
            NativeDynamicRoomDefinition definition,
            Envirnoment environment,
            int physicalInstanceId,
            IReadOnlyList<NativeDynamicRoomDynamicNpcScriptBinding> bindings,
            INativeDynamicRoomNpcMaterializationJournal journal,
            out NativeDynamicRoomMaterializedNpc[] published,
            out NormNpc controller,
            out NormNpc[] configuredNpcs)
        {
            published = null;
            controller = null;
            configuredNpcs = null;
            if (definition == null || environment == null
                || physicalInstanceId < 0 || bindings == null || journal == null
                || !environment.IsDynamicRoom
                || environment.DynamicRoomPhysicalInstanceId != physicalInstanceId
                || !string.Equals(environment.DynamicRoomName,
                    definition.RoomName, StringComparison.Ordinal)
                || !ReferenceEquals(journal.Definition, definition)
                || !ReferenceEquals(journal.Environment, environment)
                || journal.PhysicalInstanceId != physicalInstanceId
                || !journal.IsCommitted || journal.IsDestroyed
                || journal.HasUnresolvedPublication
                || definition.ConfiguredNpcs == null
                || journal.Npcs == null)
                return false;

            if (environment.DynamicRoomState != 0
                || environment.DynamicRoomPlayerCount != 0)
                return false;

            NativeDynamicRoomDynamicNpcScriptBinding[] planned;
            try
            {
                planned = bindings.ToArray();
                published = journal.Npcs.ToArray();
            }
            catch
            {
                return false;
            }

            if (planned.Length != definition.ConfiguredNpcs.Count + 1
                || published.Length != planned.Length)
                return false;

            var expectedConfigured = new HashSet<
                NativeDynamicRoomConfiguredNpcDefinition>(
                ReferenceEqualityComparer.Instance);
            foreach (var configured in definition.ConfiguredNpcs)
            {
                if (configured == null || !expectedConfigured.Add(configured))
                    return false;
            }

            var plannedSet = new HashSet<
                NativeDynamicRoomDynamicNpcScriptBinding>(
                ReferenceEqualityComparer.Instance);
            var plannedConfigured = new HashSet<
                NativeDynamicRoomConfiguredNpcDefinition>(
                ReferenceEqualityComparer.Instance);
            var hiddenCount = 0;
            foreach (var plannedBinding in planned)
            {
                if (plannedBinding == null
                    || !plannedSet.Add(plannedBinding)
                    || !ReferenceEquals(plannedBinding.Definition, definition))
                    return false;

                if (plannedBinding.Role
                    == NativeDynamicRoomDynamicNpcScriptRole.HiddenController)
                {
                    if (plannedBinding.ConfiguredNpc != null) return false;
                    hiddenCount++;
                }
                else if (plannedBinding.Role
                         == NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible)
                {
                    if (plannedBinding.ConfiguredNpc == null
                        || !expectedConfigured.Contains(
                            plannedBinding.ConfiguredNpc)
                        || !plannedConfigured.Add(plannedBinding.ConfiguredNpc))
                        return false;
                }
                else
                {
                    return false;
                }
            }
            if (hiddenCount != 1
                || plannedConfigured.Count != expectedConfigured.Count)
                return false;

            var seenBindings = new HashSet<
                NativeDynamicRoomDynamicNpcScriptBinding>(
                ReferenceEqualityComparer.Instance);
            var seenNpcs = new HashSet<NormNpc>(
                ReferenceEqualityComparer.Instance);
            var configuredByDefinition = new Dictionary<
                NativeDynamicRoomConfiguredNpcDefinition, NormNpc>(
                ReferenceEqualityComparer.Instance);
            foreach (var materialized in published)
            {
                if (materialized?.Npc == null || materialized.Binding == null
                    || !plannedSet.Contains(materialized.Binding)
                    || !seenBindings.Add(materialized.Binding)
                    || !seenNpcs.Add(materialized.Npc)
                    || materialized.Npc.ObjectId <= 0
                    || materialized.Npc.m_boGhost
                    || !ReferenceEquals(materialized.Npc.m_PEnvir,
                        environment)
                    || M2Share.ObjectManager == null
                    || !ReferenceEquals(M2Share.ObjectManager.Get(
                        materialized.Npc.ObjectId), materialized.Npc))
                    return false;

                if (materialized.Binding.Role
                    == NativeDynamicRoomDynamicNpcScriptRole.HiddenController)
                    controller = materialized.Npc;
                else if (!configuredByDefinition.TryAdd(
                             materialized.Binding.ConfiguredNpc,
                             materialized.Npc))
                    return false;
            }
            if (controller == null || seenBindings.Count != plannedSet.Count)
                return false;

            configuredNpcs = new NormNpc[definition.ConfiguredNpcs.Count];
            for (var index = 0; index < configuredNpcs.Length; index++)
            {
                if (!configuredByDefinition.TryGetValue(
                        definition.ConfiguredNpcs[index], out configuredNpcs[index]))
                    return false;
            }
            return true;
        }

        private static bool SameHandles(
            IReadOnlyList<NativeDynamicRoomPasScriptBindingHandle> left,
            IReadOnlyList<NativeDynamicRoomPasScriptBindingHandle> right)
        {
            if (left == null || right == null || left.Count != right.Count)
                return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (!ReferenceEquals(left[index], right[index])) return false;
            }
            return true;
        }
    }
}

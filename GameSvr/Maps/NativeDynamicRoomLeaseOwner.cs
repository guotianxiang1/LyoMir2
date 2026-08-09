namespace GameSvr
{
    public sealed class NativeDynamicRoomActivationLease
    {
        private readonly NativeDynamicRoomLeaseOwner _owner;
        private readonly object _definition;

        internal NativeDynamicRoomActivationLease(NativeDynamicRoomLeaseOwner owner,
            object definition, NativeDynamicRoomDefinition sourceDefinition,
            string roomName, int index, Envirnoment environment)
        {
            _owner = owner;
            _definition = definition;
            Definition = sourceDefinition;
            RoomName = roomName;
            Index = index;
            Environment = environment;
        }

        public string RoomName { get; }
        public NativeDynamicRoomDefinition Definition { get; }
        public int Index { get; }
        public Envirnoment Environment { get; }

        internal bool IsOwnedBy(NativeDynamicRoomLeaseOwner owner, object definition)
        {
            return ReferenceEquals(_owner, owner)
                   && ReferenceEquals(_definition, definition);
        }

        internal bool IsCurrentActive()
        {
            return _owner.IsCurrentActive(this, _definition);
        }
    }

    public sealed class NativeDynamicRoomLeaseOwner
    {
        private sealed class DefinitionOwner
        {
            public DefinitionOwner(string roomName,
                NativeDynamicRoomDefinition sourceDefinition)
            {
                RoomName = roomName;
                SourceDefinition = sourceDefinition;
            }

            public string RoomName { get; }
            public NativeDynamicRoomDefinition SourceDefinition { get; }
            public List<OwnedEnvironment> Environments { get; } = new();
        }

        private sealed class OwnedEnvironment
        {
            public DefinitionOwner Definition { get; init; }
            public Envirnoment Environment { get; init; }
            public byte State { get; set; }
            public bool Blocked { get; set; }
            public bool ExcludedFromBaseReuse { get; set; }
            public int LeaseIndex { get; set; }
            public NativeDynamicRoomActivationLease CurrentLease { get; set; }
            public object PhysicalRetirementIdentity { get; set; }
        }

        private readonly object _syncRoot = new();
        private readonly Dictionary<string, DefinitionOwner> _definitions =
            new(StringComparer.Ordinal);
        private readonly Dictionary<Envirnoment, OwnedEnvironment> _environments =
            new(ReferenceEqualityComparer.Instance);
        private int _activationIndex;

        public bool TryRegisterDefinition(string roomName)
        {
            return TryRegisterDefinition(roomName, null);
        }

        public bool TryRegisterDefinitionModel(
            NativeDynamicRoomDefinition definition)
        {
            return definition != null
                   && TryRegisterDefinition(definition.RoomName, definition);
        }

        private bool TryRegisterDefinition(string roomName,
            NativeDynamicRoomDefinition sourceDefinition)
        {
            if (string.IsNullOrEmpty(roomName)) return false;

            lock (_syncRoot)
            {
                return _definitions.TryAdd(roomName,
                    new DefinitionOwner(roomName, sourceDefinition));
            }
        }

        public bool TryAppendEnvironment(string roomName, Envirnoment environment,
            bool excludedFromBaseReuse = false)
        {
            if (string.IsNullOrEmpty(roomName) || environment == null) return false;

            lock (_syncRoot)
            {
                if (!_definitions.TryGetValue(roomName, out var definition)
                    || _environments.ContainsKey(environment))
                    return false;

                var owned = new OwnedEnvironment
                {
                    Definition = definition,
                    Environment = environment,
                    ExcludedFromBaseReuse = excludedFromBaseReuse,
                    LeaseIndex = -1
                };
                definition.Environments.Add(owned);
                _environments.Add(environment, owned);
                return true;
            }
        }

        public bool TrySetBlocked(Envirnoment environment, bool blocked)
        {
            if (environment == null) return false;

            lock (_syncRoot)
            {
                if (!_environments.TryGetValue(environment, out var owned)
                    || owned.PhysicalRetirementIdentity != null && !blocked)
                    return false;
                owned.Blocked = blocked;
                return true;
            }
        }

        public bool TrySetExcludedFromBaseReuse(Envirnoment environment, bool excluded)
        {
            if (environment == null) return false;

            lock (_syncRoot)
            {
                if (!_environments.TryGetValue(environment, out var owned)) return false;
                owned.ExcludedFromBaseReuse = excluded;
                return true;
            }
        }

        public bool TryFindBaseReusableEnvironment(string roomName,
            out Envirnoment environment)
        {
            environment = null;
            if (string.IsNullOrEmpty(roomName)) return false;

            lock (_syncRoot)
            {
                if (!_definitions.TryGetValue(roomName, out var definition)) return false;

                foreach (var owned in definition.Environments)
                {
                    if (owned.State != 0 || owned.Blocked
                        || owned.PhysicalRetirementIdentity != null
                        || owned.ExcludedFromBaseReuse)
                        continue;

                    environment = owned.Environment;
                    return true;
                }
            }
            return false;
        }

        public bool TryActivate(string roomName, Envirnoment environment,
            out NativeDynamicRoomActivationLease lease)
        {
            lease = null;
            if (string.IsNullOrEmpty(roomName) || environment == null) return false;

            lock (_syncRoot)
            {
                if (!_definitions.TryGetValue(roomName, out var definition)
                    || !_environments.TryGetValue(environment, out var owned)
                    || !ReferenceEquals(owned.Definition, definition)
                    || owned.State != 0
                    || owned.Blocked
                    || owned.PhysicalRetirementIdentity != null)
                    return false;

                // Base-selection exclusion is deliberately not an activation policy.
                _activationIndex = unchecked(_activationIndex + 1);
                owned.LeaseIndex = _activationIndex;
                owned.State = 2;
                lease = new NativeDynamicRoomActivationLease(this, definition,
                    definition.SourceDefinition, definition.RoomName,
                    owned.LeaseIndex, environment);
                owned.CurrentLease = lease;
                return true;
            }
        }

        public bool TryGetActiveEnvironment(string roomName, int leaseIndex,
            out Envirnoment environment)
        {
            environment = null;
            if (string.IsNullOrEmpty(roomName)) return false;

            lock (_syncRoot)
            {
                if (!_definitions.TryGetValue(roomName, out var definition)) return false;

                foreach (var owned in definition.Environments)
                {
                    if (owned.State != 2 || owned.LeaseIndex != leaseIndex
                        || owned.CurrentLease == null
                        || owned.CurrentLease.Index != leaseIndex)
                        continue;
                    environment = owned.Environment;
                    return true;
                }
            }
            return false;
        }

        public bool TrySetLeaseState(NativeDynamicRoomActivationLease lease,
            byte state)
        {
            if (lease == null || state is not (0 or 1)) return false;

            lock (_syncRoot)
            {
                if (!_environments.TryGetValue(lease.Environment, out var owned)
                    || !lease.IsOwnedBy(this, owned.Definition)
                    || !ReferenceEquals(owned.CurrentLease, lease)
                    || owned.LeaseIndex != lease.Index)
                    return false;

                if (owned.State == 2 && state != 1) return false;
                if (owned.State == 1 && state != 0) return false;
                if (owned.State is not (1 or 2)) return false;
                owned.State = state;
                if (state == 0) owned.CurrentLease = null;
                return true;
            }
        }

        public bool TryAbortActivation(NativeDynamicRoomActivationLease lease)
        {
            if (lease == null) return false;

            lock (_syncRoot)
            {
                if (!_environments.TryGetValue(lease.Environment, out var owned)
                    || !lease.IsOwnedBy(this, owned.Definition)
                    || !ReferenceEquals(owned.CurrentLease, lease)
                    || owned.LeaseIndex != lease.Index
                    || owned.State != 2)
                    return false;

                owned.State = 0;
                owned.CurrentLease = null;
                return true;
            }
        }

        internal bool TryBeginPhysicalRetirement(string roomName,
            Envirnoment environment, object retirementIdentity)
        {
            if (string.IsNullOrEmpty(roomName) || environment == null
                || retirementIdentity == null)
                return false;

            lock (_syncRoot)
            {
                if (!_definitions.TryGetValue(roomName, out var definition)
                    || !_environments.TryGetValue(environment, out var owned)
                    || !ReferenceEquals(owned.Definition, definition)
                    || owned.State != 0
                    || owned.CurrentLease != null
                    || owned.Blocked
                    || owned.PhysicalRetirementIdentity != null)
                    return false;

                owned.Blocked = true;
                owned.PhysicalRetirementIdentity = retirementIdentity;
                return true;
            }
        }

        internal bool TryCompletePhysicalRetirement(string roomName,
            Envirnoment environment, object retirementIdentity,
            bool expectDefinitionRetirement,
            out bool definitionRetired)
        {
            definitionRetired = false;
            if (string.IsNullOrEmpty(roomName) || environment == null
                || retirementIdentity == null)
                return false;

            lock (_syncRoot)
            {
                if (!_definitions.TryGetValue(roomName, out var definition)
                    || !_environments.TryGetValue(environment, out var owned)
                    || !ReferenceEquals(owned.Definition, definition)
                    || owned.State != 0
                    || owned.CurrentLease != null
                    || !owned.Blocked
                    || !ReferenceEquals(owned.PhysicalRetirementIdentity,
                        retirementIdentity)
                    || (definition.Environments.Count == 1)
                    != expectDefinitionRetirement
                    || !definition.Environments.Contains(owned))
                    return false;

                definition.Environments.Remove(owned);
                _environments.Remove(environment);
                if (expectDefinitionRetirement)
                {
                    _definitions.Remove(roomName);
                    definitionRetired = true;
                }
                return true;
            }
        }

        internal bool IsCurrentActive(NativeDynamicRoomActivationLease lease,
            object definition)
        {
            if (lease == null) return false;

            lock (_syncRoot)
            {
                return lease.IsOwnedBy(this, definition)
                       && _environments.TryGetValue(lease.Environment,
                           out var owned)
                       && ReferenceEquals(owned.Definition, definition)
                       && owned.State == 2
                       && owned.LeaseIndex == lease.Index
                       && ReferenceEquals(owned.CurrentLease, lease);
            }
        }
    }
}

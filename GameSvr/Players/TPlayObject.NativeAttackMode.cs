using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const byte NativeAttackModeAll = 0;
        internal const byte NativeAttackModePeace = 1;
        internal const byte NativeAttackModeGroup = 2;
        internal const byte NativeAttackModeGild = 3;
        internal const byte NativeAttackModeHostile = 4;
        internal const byte NativeAttackModeCorps = 5;

        protected readonly struct NativeAttackSocialState
        {
            public NativeAttackSocialState(bool selfHasCorps,
                bool targetHasCorps, bool sameCorps,
                bool selfHasGild, bool targetHasGild, bool sameGild,
                byte gildRelation)
            {
                SelfHasCorps = selfHasCorps;
                TargetHasCorps = targetHasCorps;
                SameCorps = sameCorps;
                SelfHasGild = selfHasGild;
                TargetHasGild = targetHasGild;
                SameGild = sameGild;
                GildRelation = gildRelation;
            }

            public bool SelfHasCorps { get; }
            public bool TargetHasCorps { get; }
            public bool SameCorps { get; }
            public bool SelfHasGild { get; }
            public bool TargetHasGild { get; }
            public bool SameGild { get; }
            public byte GildRelation { get; }
        }

        private void ChangeNativeAttackMode(int requestTag)
        {
            var mode = unchecked((byte)requestTag);
            if (mode > NativeAttackModeCorps) return;

            m_btAttatckMode = mode;
            SendDefMessage(Grobal2.CM_ATTACKMODE, mode, 0, 0, 0,
                string.Empty);
        }

        public override bool IsAttackTarget(TBaseObject baseObject)
        {
            if (baseObject == null || ReferenceEquals(baseObject, this))
                return false;
            if (baseObject is not TPlayObject target)
            {
                var owner = ResolveNativePlayerOwner(baseObject);
                if (owner == null) return base.IsAttackTarget(baseObject);
                if (ReferenceEquals(owner, this))
                    return m_btAttatckMode == NativeAttackModeAll;
                return IsNativePlayerAttackTarget(owner);
            }
            return IsNativePlayerAttackTarget(target);
        }

        private bool IsNativePlayerAttackTarget(TPlayObject target)
        {
            if (target.m_boAdminMode || target.m_boStoneMode) return false;

            var social = GetNativeAttackSocialState(target);
            return m_btAttatckMode switch
            {
                NativeAttackModeAll => true,
                NativeAttackModePeace => false,
                NativeAttackModeGroup => !IsGroupMember(target),
                NativeAttackModeGild =>
                    !social.SameGild &&
                    social.GildRelation != NativeCorpsService.GildUnion,
                NativeAttackModeHostile =>
                    IsNativeHostileAttackTarget(target, social),
                NativeAttackModeCorps => !social.SameCorps,
                _ => false
            };
        }

        public override bool IsProperFriend(TBaseObject baseObject)
        {
            if (baseObject == null) return false;
            if (baseObject is TPlayObject player)
                return IsNativePlayerFriend(player);
            var master = ResolveNativePlayerOwner(baseObject);
            if (master != null)
            {
                if (ReferenceEquals(master, this)) return true;
                return IsNativePlayerFriend(master);
            }
            return base.IsProperFriend(baseObject);
        }

        protected virtual NativeAttackSocialState
            GetNativeAttackSocialState(TPlayObject target)
        {
            var service = CorpsService;
            service.GetCombatRelation(GetCachedNativeUserId(),
                target?.GetCachedNativeUserId() ?? 0,
                out var selfHasCorps, out var targetHasCorps,
                out var sameCorps, out var selfHasGild,
                out var targetHasGild, out var sameGild,
                out var gildRelation);
            return new NativeAttackSocialState(selfHasCorps,
                targetHasCorps, sameCorps, selfHasGild, targetHasGild,
                sameGild, gildRelation);
        }

        private bool IsNativePlayerFriend(TPlayObject target)
        {
            var social = GetNativeAttackSocialState(target);
            return m_btAttatckMode switch
            {
                NativeAttackModeAll => true,
                NativeAttackModePeace => true,
                NativeAttackModeGroup => ReferenceEquals(target, this)
                                         || IsGroupMember(target),
                NativeAttackModeGild => ReferenceEquals(target, this)
                                        || social.SameGild
                                        || social.GildRelation ==
                                        NativeCorpsService.GildUnion,
                NativeAttackModeHostile => false,
                NativeAttackModeCorps => social.SameCorps
                                         || (!social.SelfHasCorps
                                             && !social.TargetHasCorps),
                _ => false
            };
        }

        private bool IsNativeHostileAttackTarget(TPlayObject target,
            NativeAttackSocialState social)
        {
            if (ReferenceEquals(target, m_DearHuman)
                || (!string.IsNullOrEmpty(m_sDearName)
                    && string.Equals(m_sDearName, target.m_sCharName,
                        StringComparison.OrdinalIgnoreCase))
                || social.SameCorps
                || (m_GroupOwner != null
                    && ReferenceEquals(m_GroupOwner, target.m_GroupOwner))
                || social.SameGild)
                return false;

            if (IsNativeFightZone(target) || target.m_boPKFlag)
                return true;
            // Native 0x683204 and 0x6846B0 test ONLY al==1 (allies) for PK prevention.
            // No native code tests al==2 (war) to enable PK. War relation does NOT
            // make targets attackable outside fight zones.
            return target.m_nPkPoint >= M2Share.g_Config.nPKPunishPoint;
        }

        private static TPlayObject ResolveNativePlayerOwner(
            TBaseObject target)
        {
            if (target?.m_Master is TPlayObject directMaster)
                return directMaster;
            return target?.GetMaster() as TPlayObject;
        }

        private static bool IsNativeFightZone(TPlayObject player)
        {
            if (player == null) return false;
            if (player.m_boInFreePKArea) return true;
            var flag = player.m_PEnvir?.Flag;
            return flag != null && (flag.boFightZone || flag.boFight3Zone);
        }
    }
}

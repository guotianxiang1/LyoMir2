using SystemModule;

namespace GameSvr
{
    
    
    
    public class FireBurnEvent : Event
    {
        /// <summary>Native <c>[obj+0x4C]</c>, the pulse timestamp.</summary>
        protected int m_fireRunTick = 0;

        /// <summary>
        /// Native <c>[obj+0x54]</c>. TFireBurnEvent's constructor seeds it at
        /// <c>0x7178AC C7 43 54 B8 0B 00 00 mov [ebx+0x54],0xBB8</c> and
        /// TBTFireBurnEvent's overwrites it at
        /// <c>0x717A81 C7 43 54 E8 03 00 00 mov [ebx+0x54],0x3E8</c>. Run compares
        /// against it with <c>0x7179C8 3B 43 54 cmp eax,[ebx+0x54]</c> /
        /// <c>0x7179CB 76 jbe</c>, i.e. strictly greater fires.
        /// </summary>
        protected int m_fireRunInterval = 0xBB8;

        internal MagicDamageContext Context { get; }

        public FireBurnEvent(TBaseObject Creat, int nX, int nY, int nType, int nTime, int nDamage) : base(Creat.m_PEnvir, nX, nY, nType, nTime, true)
        {
            NativeAppliesOnLanding = true; // 0x71788D: mov byte [self+0x34],1
            m_nDamage = nDamage;
            m_OwnBaseObject = Creat;
            Context = MagicDamageContext.Empty;
            ApplyNativeMapFireWallDuration();
        }

        /// <summary>
        /// Native <c>TFireBurnEvent.Create = sub_717854</c> in its full shape:
        /// <c>ecx</c> is the Envirnoment and the owner is a separate parameter
        /// (<c>0x717897 mov eax,[ebp+0x18] / 0x71789A mov [ebx+0x14],eax</c>), so a
        /// nil owner is legal. TBTFireBurnEvent's constructor uses exactly that —
        /// it pushes 0 for the owner at <c>0x717A52 6A 00</c> and only writes
        /// <c>[ebx+0x14]</c> itself afterwards at <c>0x717A88</c>.
        /// The public constructor above cannot express this because it derives the
        /// map from the owner.
        /// </summary>
        protected FireBurnEvent(Envirnoment envir, TBaseObject owner, int nX,
            int nY, int nType, int nTime, int nDamage)
            : base(envir, nX, nY, nType, nTime, true)
        {
            NativeAppliesOnLanding = true; // 0x71788D
            m_nDamage = nDamage;
            m_OwnBaseObject = owner;
            Context = MagicDamageContext.Empty;
            // 0x7178B3 cmp dword [ebx+0x14],0 / 0x7178B7 75 03 jne /
            // 0x7178B9 89 7B 20 mov [ebx+0x20],edi — a nil owner restores the raw
            // duration, undoing both the 0xAFC80 clamp and the AddToMap-failure
            // zeroing that the TMapEvent constructor may have applied.
            if (owner == null)
            {
                ContinueTime = nTime;
            }
            ApplyNativeMapFireWallDuration();
        }

        public FireBurnEvent(TBaseObject Creat, TUserMagic userMagic, int nX, int nY, int nType, int nTime, int nDamage) : base(Creat.m_PEnvir, nX, nY, nType, nTime, true)
        {
            NativeAppliesOnLanding = true; // 0x71788D
            m_nDamage = nDamage;
            m_OwnBaseObject = Creat;
            Context = MagicDamageContext.Capture(userMagic);
            m_nEventParam = userMagic?.MagicInfo?.btEffect ?? 0;
            ApplyNativeMapFireWallDuration();
        }

        private void ApplyNativeMapFireWallDuration()
        {
            // 0x7178BC..0x7178C9 reads Envir+0x88 after the base clamp and
            // owner-null restore, then overwrites +0x20 only when it is positive.
            var duration = m_Envir?.Flag?.MapFireWallBurnMs ?? 0;
            if (duration > 0)
            {
                ContinueTime = duration;
            }
        }

        public override bool ApplyTo(TBaseObject target)
        {
            TBaseObject owner = m_OwnBaseObject;
            if (owner == null || ReferenceEquals(owner, target))
            {
                return false;
            }
            if (owner.m_boGhost)
            {
                m_OwnBaseObject = null;
                return false;
            }
            if (!owner.IsProperTarget(target))
            {
                return false;
            }

            int damage = target.ResolveFullMagicDamage(owner, SpellsDef.SKILL_EARTHFIRE,
                false, Context, 1, 0, m_nDamage);
            if (damage > 0)
            {
                target.SendRefMsg(Grobal2.RM_STRUCK_MAG, damage, 0, 0,
                    owner.ObjectId, string.Empty);
            }
            return false;
        }

        public override void Run(int currentTick)
        {
            if (unchecked((uint)(currentTick - m_fireRunTick)) >
                unchecked((uint)m_fireRunInterval))
            {
                m_fireRunTick = currentTick;
                IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
                if (m_Envir != null)
                {
                    m_Envir.GetBaseObjects(m_nX, m_nY, true, BaseObjectList);
                    for (var i = 0; i < BaseObjectList.Count; i++)
                    {
                        ApplyTo(BaseObjectList[i]);
                    }
                }
                BaseObjectList.Clear();
                BaseObjectList = null;
            }
            base.Run(currentTick);
        }
    }
}

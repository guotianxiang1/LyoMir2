using SystemModule;

namespace GameSvr
{
    
    
    
    public class FireBurnEvent : Event
    {
        private int m_fireRunTick = 0;

        internal MagicDamageContext Context { get; }

        public FireBurnEvent(TBaseObject Creat, int nX, int nY, int nType, int nTime, int nDamage) : base(Creat.m_PEnvir, nX, nY, nType, nTime, true)
        {
            m_nDamage = nDamage;
            m_OwnBaseObject = Creat;
            Context = MagicDamageContext.Empty;
        }

        public FireBurnEvent(TBaseObject Creat, TUserMagic userMagic, int nX, int nY, int nType, int nTime, int nDamage) : base(Creat.m_PEnvir, nX, nY, nType, nTime, true)
        {
            m_nDamage = nDamage;
            m_OwnBaseObject = Creat;
            Context = MagicDamageContext.Capture(userMagic);
            m_nEventParam = userMagic?.MagicInfo?.btEffect ?? 0;
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
            if (unchecked((uint)(currentTick - m_fireRunTick)) > 3000u)
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

using SystemModule;

namespace GameSvr
{
    
    
    
    /// <summary>
    /// Native <c>TTrainer</c>, VMT 0x0067F4F0, instance size 1260; own fields
    /// [0x4D8, 0x4EC) = 20 bytes.
    ///
    /// A direct <c>TAnimal</c> child, not an NPC. Unlike TSuperGuard it overrides
    /// both inherited slots: VMT +0x018 Operate at 0x681CB4 and +0x19C
    /// IsProperTarget at 0x681C34.
    /// </summary>
    public class Trainer : AnimalObject
    {
        public int n564 = 0;
        private int m_dw568 = 0;
        private int n56C = 0;
        private int n570 = 0;

        public Trainer() : base()
        {
            m_dw568 = HUtil32.GetTickCount();
            n56C = 0;
            n570 = 0;
        }

        public override bool Operate(TProcessMessage ProcessMsg)
        {
            var result = false;
            if (ProcessMsg.wIdent == Grobal2.RM_STRUCK || ProcessMsg.wIdent == Grobal2.RM_MAGSTRUCK)
            {
                if (ProcessMsg.BaseObject == this.ObjectId)
                {
                    n56C += ProcessMsg.wParam;
                    m_dw568 = HUtil32.GetTickCount();
                    n570++;
                    this.ProcessSayMsg("破坏力为 " + ProcessMsg.wParam + ",平均值为 " + n56C / n570);
                }
            }
            if (ProcessMsg.wIdent == Grobal2.RM_MAGSTRUCK)
            {
                result = base.Operate(ProcessMsg);
            }
            return result;
        }

        /// <summary>
        /// 0x681C34 is <c>xor eax,eax / ret</c> — a constant-false holder, so a
        /// trainer is never a valid target. While the class sat under NormNpc the
        /// port got this from the blanket NormNpc arm of
        /// <see cref="AnimalObject.IsNativeMagic43Target"/>; native puts it in the
        /// slot, so it lives here now.
        /// </summary>
        internal override bool IsNativeMagic43Target(TPlayObject source)
        {
            return false;
        }

        public override void Run()
        {
            if (n570 > 0)
            {
                if ((HUtil32.GetTickCount() - m_dw568) > 3 * 1000)
                {
                    this.ProcessSayMsg("总破坏力为  " + n56C + ",平均值为 " + n56C / n570);
                    n570 = 0;
                    n56C = 0;
                }
            }
            base.Run();
        }
    }
}


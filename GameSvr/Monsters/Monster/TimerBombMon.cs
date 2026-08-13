namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 159 = TTimerBombMon，classref 0x6801CC -> VMT 0x680218，
    //    size 1248(0x4E0)，parent = TAnimal(0x71D51C)。
    //    工厂 sub_679F8C：索引表[159-0xB=0x94]=0x4C=76 ; jt[76]=0x67AA25 ; case body 全文：
    //      67AA25  B2 01 / A1 CC 01 68 00 / E8 F7 2D 0A 00 / 89 45 F8 / E9 06 03 00 00
    //      classref [0x6801CC] 唯一加载点；ctor = 0x71D828 = TAnimal.Create（本类**无自有 ctor**）。
    //    case 内无额外 RNG/字段写。
    //
    // VMT 差分(vs TAnimal) 9 项：
    //   +0x078(30) Initialize 0x6826EC：base.Initialize → [+0x4DA]=byte[+0x294] / [+0x4D8]=word[+0x29C]
    //              / m_boSuperMan(+0x2E1)=true / [+0x4DC]=0。
    //   +0x088(34) Run 0x6826A8：word[+0x4D8]==0xF→call 0x682748；==1→call 0x682768；dec [+0x4D8]；base.Run。
    //   +0x0A8(42) 0x682728：clamp word[+0x4D8] 上限 0x14。
    //   +0x0C8(50) 0x682990：dl==0x1A 时吞掉(免疫)，否则转父。
    //   +0x104(65) 0x6829A8：[ebp+0x10]!=4 的 setne 后转父 [vmt+0xA8]。
    //   +0x19C/1A0/1A4(103-105) = `xor eax,eax;ret`：属性虚槽恒 0。
    //   +0x1E8(122) CanAddNativeTimedAbility 0x682964：见下方【本类落地】。
    //
    // ── 本类落地 ────────────────────────────────────────────────────────────────
    // ① Initialize(+0x078)：忠实部分 = base.Initialize() + m_boSuperMan=true（+0x2E1 已钉，
    //    StoneFoxBossMon.cs:77 同源）。两条字段拷贝 [+0x294]->[+0x4DA]、[+0x29C]->[+0x4D8]
    //    是“定时炸弹倒计时”的新字段初值，C# 无 [+0x4D8]/[+0x4DA]/[+0x294]/[+0x29C] 命名落点，
    //    且其唯一消费者 Run 亦 fail-closed，故这两条 fail-closed（不写），只落 m_boSuperMan。
    // ② CanAddNativeTimedAbility(+0x1E8) 0x682964 全文：
    //      call 0x772F84(=base) / test al / je false ; sub bl,0x1A / je false ; sub bl,3 / jne true
    //    = base.CanAddNativeTimedAbility(t) && t!=26 && t!=29。与 AttackIceTower.cs:106 同一槽同一基址。
    //
    // ── fail-closed ────────────────────────────────────────────────────────────
    //   Run(+0x088)/+0xA8/+0xC8/+0x104 依赖未命名新字段 [+0x4D8] 与未定 helper(0x682748/0x682768)；
    //   +0x19C/1A0/1A4 属性虚槽 C# 非虚。均保留父实现。原先 race 159 落 default → nil。
    //   语义：定时炸弹(m_boSuperMan 无敌、倒计时到 0xF/1 触发爆炸)；C# 侧忠实 = 无敌 + 拒绝状态26/29，
    //   倒计时/爆炸 fail-closed。
    public class TimerBombMon : AnimalObject
    {
        public TimerBombMon() : base()
        {
        }

        public override void Initialize()
        {
            base.Initialize();
            // 倒计时新字段 [+0x294]->[+0x4DA] / [+0x29C]->[+0x4D8] fail-closed（无命名落点，Run 亦 FC）。
            m_boSuperMan = true;
        }

        internal override bool CanAddNativeTimedAbility(byte internalType)
        {
            return base.CanAddNativeTimedAbility(internalType) && internalType != 26 && internalType != 29;
        }
    }
}

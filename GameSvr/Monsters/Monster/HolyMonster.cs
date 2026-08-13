namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 151 与 race 170 = THolyMonster（**同一个类、同一 VMT 0x663060**，
    //    classref 0x663014，size 1304(0x518)，parent = TATMonster(0x65E7E4, size 0x4E8)）。
    //    工厂 sub_679F8C 两处 case 都指向同一 classref/ctor：
    //      race151：索引表[151-0xB=0x8C]=0x45=69 ; jt[69]=0x67A999 ；
    //        67A999 B2 01 / A1 14 30 66 00 / E8 07 19 FF FF(call 0x66C2AC) / 89 45 F8 / E9 92 03 00 00
    //      race170：索引表[170-0xB=0x9F]=0x56=86 ; jt[86]=0x67AAED ；
    //        67AAED B2 01 / A1 14 30 66 00 / E8 B3 17 FF FF(call 0x66C2AC) / 89 45 F8 / E9 3E 02 00 00
    //    两处 classref 都是 [0x663014]、ctor 都是 0x66C2AC；classref 全 CODE 段仅这 2 个加载点，
    //    ctor 唯一 E8 家族。case 内无额外 RNG/字段写。=> race 151 与 170 是同一怪的两个 race 号。
    //
    // 构造器 sub_66C2AC 全文（父 Create 之后）：
    //   0066C2C5  E8 CE A7 FF FF        call 0x666A98        ; = TATMonster.Create（= C# AtMonster()）
    //   0066C2CA  C6 86 F4 04 00 00 00  mov byte [esi+0x4F4],0 ; 新字段（见 fail-closed(ctor)）
    //   0066C2D1  C6 86 F5 04 00 00 01  mov byte [esi+0x4F5],1 ; 新字段
    //
    // ── fail-closed ────────────────────────────────────────────────────────────
    // (ctor) 新字段 byte[+0x4F4]=0、byte[+0x4F5]=1（父 size 0x4E8 之后），C# 无命名落点，
    //   其唯一消费者是下方 fail-closed 的 Operate/Run，故这两条 fail-closed（不建字段/不写）。
    // (VMT 覆写 6 项，vs TATMonster，全部依赖新字段 [+0x4F4]/[+0x4F8]/[+0x4E8]/[+0x50C] 或无 C# 入口槽)：
    //   +0x018(6)  Operate 0x66C7C8：处理消息 0x28AF，命中且 [+0x4F4]==0 时按 [+0x4F0]/[+0x4F2]
    //              发“圣言/定身”特效(call [vmt+0xE0]/[+0x90]/[+0xD8])——holy-seize 主逻辑。
    //   +0x084(33) Die 0x66C2F4：若持有目标 [+0x4F8] 未死则记 [tgt+0x50C]=tick；base.Die。
    //   +0x088(34) Run 0x66C8AC：[+0x4F4] 分支：未触发时 call 0x66C368/0x66C1B8 进入 seize；
    //              (tick-[+0x4E8])>0xEA60(60000) 时 call 0x66C254 释放；base(TATMonster.Run)。
    //   +0x094(37) 0x66C338：读 byte[+0x483] / div 15 的等级映射（属性虚槽）。
    //   +0x0B0(44) 0x66C71C：命中结算变体(call [tgt.vmt+0xE8]/0x76C1AC)，槽身份未定。
    //   +0x200(128) 0x66C11C：AttackTarget 变体（依赖 [+0x344] 目标、[+0x35C]/[+0x320] 节流）。
    //   —— 依赖未命名新字段 [+0x4F4]/[+0x4F8]/[+0x4E8] 与未定 helper，忠实移植会臆造，故全保留父实现。
    //
    // 语义：“圣物怪/定身怪”——收到特定消息时对目标施加圣言定身，60s 后释放。C# 侧行为退化为纯 AtMonster
    //   （父类搜敌 AI 正确）；具名类型使 race 151/170 脱离 default sink(0x67AE5E→nil)，并为 seize 机制留挂载点。
    public class HolyMonster : AtMonster
    {
        public HolyMonster() : base()
        {
        }
    }
}

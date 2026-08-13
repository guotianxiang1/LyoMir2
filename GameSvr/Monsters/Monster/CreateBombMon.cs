namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 160 = TCreateBombMon，classref 0x68045C -> VMT 0x6804A8，
    //    size 1248(0x4E0)，parent = TSoccerBall(0x67ED54, C# SoccerBall)。
    //    工厂 sub_679F8C：索引表[160-0xB=0x95]=0x4D=77 ; jt[77]=0x67AA39 ; case body 全文：
    //      67AA39  B2 01 / A1 5C 04 68 00 / E8 8B 7F 00 00 / 89 45 F8 / E9 F2 02 00 00
    //      classref [0x68045C] 唯一加载点；ctor 0x6829D0 唯一 E8 调用者。case 内无额外 RNG/字段写。
    //
    // 构造器 sub_6829D0 全文（父 Create 之后）：
    //   006829E9  E8 F6 E6 FF FF        call 0x6810E4        ; = TSoccerBall.Create（= C# SoccerBall()）
    //   006829EE  C6 86 DC 04 00 00 64  mov byte [esi+0x4DC],0x64 ; 新字段=100（见 fail-closed）
    //   006829F5  C6 86 DE 04 00 00 00  mov byte [esi+0x4DE],0    ; 新字段
    //   006829FC  C6 86 E1 02 00 00 01  mov byte [esi+0x2E1],1    ; m_boSuperMan = true
    //   00682A03  C6 86 DF 04 00 00 00  mov byte [esi+0x4DF],0    ; 新字段
    //   00682A0A  C6 86 DD 04 00 00 03  mov byte [esi+0x4DD],3    ; 新字段=3
    //   +0x2E1 = m_boSuperMan(StoneFoxBossMon.cs:77)。
    //
    // ── 本类落地 ────────────────────────────────────────────────────────────────
    // ① ctor：忠实 m_boSuperMan=true。四个新字段 byte[+0x4DC]=100/[+0x4DD]=3/[+0x4DE]=0/[+0x4DF]=0
    //    （父 SoccerBall size 0x4DC 之后）是“造弹计时/计数”，C# 无命名落点，其消费者 Run/Struck
    //    亦 fail-closed，故这四条 fail-closed（不建字段/不写）。
    // ② CanAddNativeTimedAbility(+0x1E8) 0x682E4C 全文：
    //      call 0x772F84(=base) / test al / je false ; sub bl,0x1A / je false ; sub bl,3 / jne true
    //    = base.CanAddNativeTimedAbility(t) && t!=26 && t!=29（与 159/AttackIceTower 同槽同基址）。
    //
    // ── fail-closed（VMT 差分 vs TSoccerBall 其余 8 项）────────────────────────────
    //   +0x088(34) Run 0x682A2C：按 [+0x4DE] 计数(达 0x1E→call 0x682AA0；==0xF→[+0x4D8]=8；
    //              ==5 且 [+0x4DF]→call 0x682B84)，inc [+0x4DE]，base(SoccerBall.Run)。
    //   +0x0A8(42) 0x682B5C：wIdent==0xD4 拦截。 +0x0C8(50) 0x682E78：dl==0x1A 吞掉。
    //   +0x104(65) 0x682E90：setne 后转父 [vmt+0xA8]。 +0x19C/1A0/1A4(103-105)=`xor eax,eax;ret`。
    //   +0x208(130) Struck 0x682B44：hiter 非空时 m_btDirection=hiter.m_btDirection 且 [+0x4DF]=1。
    //   —— 依赖未命名新字段 [+0x4DC..0x4DF]/[+0x4D8]，忠实移植会臆造，故保留父(SoccerBall)实现。
    //   语义：会“造炸弹”的球——无敌(m_boSuperMan)，被击(Struck)转向并置触发位，Run 里计数造弹。
    //   C# 忠实 = SoccerBall(滚动/被击转向) + m_boSuperMan + 拒绝状态26/29；造弹计数 fail-closed。
    //   原先 race 160 落 default(0x67AE5E) → nil。
    public class CreateBombMon : SoccerBall
    {
        public CreateBombMon() : base()
        {
            m_boSuperMan = true;
        }

        internal override bool CanAddNativeTimedAbility(byte internalType)
        {
            return base.CanAddNativeTimedAbility(internalType) && internalType != 26 && internalType != 29;
        }
    }
}

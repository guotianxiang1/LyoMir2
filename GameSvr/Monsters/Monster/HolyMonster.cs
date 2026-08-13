using SystemModule;

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
    //   +0x084(33) Die 0x66C2F4：**已移植**（见下方 Die 覆写）。SKILL-62 复核更正了此前
    //              “[+0x4F8] 是持有目标”的判读：全镜像 0x660000-0x680000 内 [+0x4F8] 的
    //              唯一写入点是 sub_66C630 @0x66C6BB `89 B3 F8 04 00 00`，而 sub_66C630 的
    //              第 2 参(edx)恰是造宠原语 sub_76EEF4 @0x76EF45 传进来的【召唤者】，
    //              所以 [+0x4F8] = 召唤者，[召唤者+0x50C] = wMagicID 62 的 30 秒门时间戳。
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

        /// <summary>原生新字段 dword[+0x4F8] —— 召唤者。写入点仅 sub_66C630 @0x66C6BB，
        /// 而 sub_66C630 只有两个调用者：造宠原语 sub_76EEF4 @0x76EF49（wMagicID 62，
        /// edx=施法玩家）与 sub_6CB6C4 @0x6CB74D（按 byte[+0x178]==0x97 分流的另一条
        /// 造宠链，本工程未移植，故此字段在那条链上保持 null，与原生“没走到写入点”等价）。</summary>
        private TBaseObject m_NativeHolyBeastSummoner;

        /// <summary>sub_66C630 @0x66C6BB `dword[slave+0x4F8] := master`。</summary>
        internal void NativeBindHolyBeastSummoner(TBaseObject summoner)
        {
            m_NativeHolyBeastSummoner = summoner;
        }

        // 原生 THolyMonster.Die = sub_66C2F4 (VMT 0x663060 槽 +0x84)，inherited 之前的全部动作：
        //   0066C2FB  8B B3 F8 04 00 00     mov esi,[ebx+0x4F8]        ; 召唤者
        //   0066C301  85 F6 / 74 28         test esi,esi / je 跳过
        //   0066C307  E8 9C 6A 10 00        call 0x772DA8              ; = `mov al,[eax+0x74]` 即 m_boDeath
        //   0066C30E  75 1D                 jne 跳过
        //   0066C316  80 78 73 00           cmp byte [eax+0x73],0      ; m_boGhost
        //   0066C31A  75 11                 jne 跳过
        //   0066C31C  E8 1F C0 D9 FF        call 0x408340              ; GetTickCount
        //   0066C327  89 82 0C 05 00 00     mov [edx+0x50C],eax        ; 召唤者的 30 秒门时间戳
        //   0066C32F  E8 88 1F 0B 00        call 0x71E2BC              ; inherited = TAnimal.Die
        // 原生对 [master+0x50C] 是【不看类型】直写的；C# 侧 +0x50C 只有 TPlayObject
        // (m_dwMagic62LastTick) 与 HeroObject (m_dwNativeHeroSinSuBackTick) 两个落点，
        // 而英雄那条 62 分支 (0x68E3FB) 直接调 master.MakeSlave、从不经 sub_66C630，
        // 所以英雄召出的圣兽 [+0x4F8] 恒为 nil、原生同样不写 —— 这里只处理玩家召唤者。
        public override void Die()
        {
            var summoner = m_NativeHolyBeastSummoner;               // 0x66C2FB
            if (summoner is TPlayObject player && !player.m_boDeath // 0x66C307
                && !player.m_boGhost)                               // 0x66C316
            {
                player.m_dwMagic62LastTick = HUtil32.GetTickCount(); // 0x66C31C-0x66C327
            }
            base.Die();                                             // 0x66C32F
        }
    }
}

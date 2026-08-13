namespace GameSvr
{
    // 战神字节证据 (Tier-1)：race 121 = TArmLightGuard，classref 0x662858 -> VMT 0x6628A4，
    //   size 0x4F8(1272)，parent = TScultureMonster(VMT 0x661644，= C# ScultureMonster)。
    //   工厂 sub_679F8C：race121-0xB=0x6E -> idx 0x31=49 -> jt[49]=0x67A809，case body 全文：
    //     67A809  B2 01              mov  dl,1
    //     67A80B  A1 58 28 66 00     mov  eax,[0x662858]   ; classref -> TArmLightGuard
    //     67A810  E8 AF 05 00 00     call 0x66ADC4         ; TArmLightGuard.Create
    //     67A815  89 45 F8           mov  [ebp-8],eax
    //     67A818  E9 22 05 00 00     jmp  0x67AD3F         ; case 内无额外 RNG/字段写
    //
    // 构造器 sub_66ADC4 全文（父 Create 之后 2 个写）：
    //   66ADDD  E8 9E D0 FF FF          call 0x667E80             ; TScultureMonster.Create（= base()）
    //   66ADE2  C7 46 78 08 00 00 00    mov  dword [esi+0x78],8   ; m_nViewRange = 8（覆写父类的 7）
    //   66ADEB  33 C0 / 89 86 E8 04 00 00
    //                                   mov  dword [esi+0x4E8],0   ; ← 新字段，见 fail-closed
    //   偏移锚点：+0x78 = m_nViewRange（TBigHeartMon.Create 0x68108A `mov [esi+0x78],0x10`
    //     对上 C# BigHeartMonster m_nViewRange=16；父 ScultureMonster 亦写 +0x78=7）。
    //
    // ⛔ fail-closed（有原生证据，但 C# 侧无落点，故只记录、绝不臆造）：
    //  (1) 4 个新字段 [+0x4E8](word)/[+0x4EC](word)/[+0x4F0](dword)/[+0x4F4](dword)：位于父类
    //      size 0x4E8 之后，是本类独有。它们是「石化时暂存真实属性」的备份槽——见下面 3 个覆写。
    //      C# ScultureMonster 及其祖先【无这 4 个字段】，补上属于改动共享字段模型，越界，故不实现。
    //  (2) VMT 槽 +0x014（slot5，父 = TCreature sub_76B42C）覆写 sub_66AD2C：消息处理器。
    //      收到 wIdent=0x15 且 wParam=0 时把 4 个备份槽还原回可见属性
    //        [+0x266]<-[+0x4E8](word) / [+0x28C]<-[+0x4EC](word) /
    //        [+0x320]<-[+0x4F0](m_nNextHitTime) / [+0x324]<-[+0x4F4](m_nWalkSpeed)，
    //      置 [+0x35C]=[+0x384]=GetTickCount+0x7D0(2000)，再 SendRefMsg(wIdent=0x27DC,
    //      wParam=1, m_nCurrX, m_nCurrY, 0, "") via [vmt+0xD8]（= 苏醒广播）。
    //  (3) VMT 槽 +0x198（slot102，父 = TAnimal sub_71F824）覆写 sub_66AE0C：石化/削弱变身。
    //      首帧把真实 [+0x266]/[+0x28C]/[+0x320]/[+0x324] 备份进 4 个新槽，随后按公式削弱可见属性
    //      （[+0x266]=Reduce(real-10,5)；[+0x28C]=real*8/10；hit/walk +1000ms）。
    //  (4) VMT 槽 +0x208（slot130，父 = TAnimal sub_71E208）覆写 sub_66AED0：攻击闸。
    //      若 m_TargetCret(+0x344)!=0 且 GetTickCount-[+0x348] > 0xBB8(3000ms) 才转调父 sub_71E208。
    //  上述 3 个虚槽在 C# 侧【均无对应虚方法】，且都读写 (1) 的新字段；在 C# 补钩子只会是死代码，reject。
    //
    // 结论：C# 侧行为 = ScultureMonster（石像怪，父类 AI 正确）。具名类型使 race 121 脱离工厂 default
    //   sink，并为上述石化/苏醒机制留下确定挂载点（4 新字段 + 虚槽 +0x14/+0x198/+0x208）。
    public class ArmLightGuard : ScultureMonster
    {
        public ArmLightGuard() : base()
        {
            m_nViewRange = 8;
        }
    }
}

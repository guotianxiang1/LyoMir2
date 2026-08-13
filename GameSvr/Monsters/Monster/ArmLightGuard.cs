using SystemModule;

namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 121 = TArmLightGuard，VMT 0x6628A4，size 1272 (0x4F8)，
    //    parent = TScultureMonster (VMT 0x661644, size 1256/0x4E8) —— 自有 4 个新 dword 字段
    //    [+0x4E8/+0x4EC/+0x4F0/+0x4F4]（原始属性备份，仅被下方两个 fail-closed 槽消费）。
    //    父链：TArmLightGuard→TScultureMonster→TMonster→TAnimal→TCreature→TBaseObj。
    //
    // 工厂 sub_679F8C 两级分派：race 121 (idx 110) -> group 49 -> handler 0x67A809。
    // case body 0x67A809..0x67A81C 全文 (20 字节)：
    //   67A809  B2 01              mov  dl,1
    //   67A80B  A1 58 28 66 00     mov  eax,[0x662858]   ; classref -> TArmLightGuard
    //   67A810  E8 AF 05 FF FF     call 0x66ADC4         ; TArmLightGuard.Create
    //   67A815  89 45 F8           mov  [ebp-8],eax
    //   67A818  E9 22 05 00 00     jmp  0x67AD3F         ; 汇入公共尾部
    // case 内【无】任何额外 RNG / 字段写。
    // 归属唯一性（穷尽判据，全 flat_image 扫描）：
    //   · classref [0x662858] 全 CODE 段加载点 = 1 处 (0x67A80B)
    //   · ctor 0x66ADC4 的 E8 rel32 调用者 = 1 处 (0x67A810)
    //
    // 构造器 sub_66ADC4 全文：
    //   66ADC4  55 8B EC 53 56                push ebp/mov ebp,esp/push ebx/push esi
    //   66ADC9  84 D2 / 74 08 / 83 C4 F0 / E8 33 9C D9 FF   Delphi ctor 序幕 (dl=1 -> 分配)
    //   66ADD5  8B DA / 8B F0                 ebx=dl 标志 / esi=Self
    //   66ADD9  33 D2 / 8B C6 / E8 9E D0 FF FF  xor edx,edx / call 0x667E80  ; = TScultureMonster.Create
    //                                          ;   (= C# ScultureMonster()：m_dwSearchTime=Random(1500)+1500,
    //                                          ;    m_nViewRange=7, m_boStoneMode=true, m_nCharStatusEx=STATE_STONE_MODE)
    //   66ADE2  C7 46 78 08 00 00 00          mov dword [esi+0x78],8    ; m_nViewRange := 8 (覆盖父类的 7)
    //   66ADE9  33 C0 / 89 86 E8 04 00 00      mov dword [esi+0x4E8],0   ; 备份标记清零 (Delphi 已零初始化, 冗余写)
    //   66ADF1..66AE0B                        ctor 收尾 / ret
    // [+0x78] == C# m_nViewRange：由父 TScultureMonster.Create @0x667EB1 `mov [esi+0x78],7`
    //   与 C# ScultureMonster 的 m_nViewRange=7 一一对应（同惯用法）。
    //
    // VMT 差分（child 0x6628A4 vs parent 0x661644，逐槽扫至 vtable 末尾 +0x210）：
    //   仅 3 个真实覆写（slot>=+0x210 皆为读过表尾的相邻数据/自指针/类名 ASCII，非方法槽）：
    //
    //   ── +0x208 = Struck（可覆写虚方法，本类落地）──────────────────
    //     child 0x66AED0 ; parent 0x71E208 = TAnimal.Struck
    //     (槽名 +0x208=Struck 见 GameSvr/Monsters/Monster/AttackIceTower.cs 的交叉标定)
    //     0066AED0  55 8B EC 53 56        push ebp/mov ebp,esp/push ebx/push esi
    //     0066AED5  8B F2 / 8B D8         esi=edx(Hiter) / ebx=Self
    //     0066AED9  83 BB 44 03 00 00 00  cmp dword [ebx+0x344],0      ; m_TargetCret==null?
    //     0066AEE0  74 12                 je  -> 调 base.Struck
    //     0066AEE2  E8 59 D4 D9 FF        call 0x408340               ; GetTickCount
    //     0066AEE7  2B 83 48 03 00 00     sub eax,[ebx+0x348]         ; - m_dwTargetFocusTick
    //     0066AEED  3D B8 0B 00 00        cmp eax,0xBB8 (3000)
    //     0066AEF2  76 09                 jbe -> return (忽略本次受击)
    //     0066AEF4  8B D6 / 8B C3 / E8 0B 33 0B 00  base.Struck(Hiter) = call 0x71E208
    //     0066AEFD  5E 5B 5D C3           ret
    //     语义：持有目标且"锁定"未满 3000ms 时，忽略受击（不让 base.Struck 改指向攻击者）。
    //     +0x344=m_TargetCret / +0x348=m_dwTargetFocusTick：由 SetTargetCreat sub_76719C
    //       @0x7671A2 `mov [ebx+0x344],edx` / @0x7671AD `mov [ebx+0x348],GetTickCount` 印证
    //       （= C# TBaseObject.SetTargetCreat）。base 0x71E208 头部 @0x71E218 写 [+0x338]=now
    //       (= m_dwStruckTick) 与 C# AnimalObject.Struck 同形。
    //
    //   ── +0x14 = SendTimedAbilityState 派发（fail-closed，不臆造）──
    //     child 0x66AD2C ; parent 0x76B42C（= 仅 SM_CHARSTATUSCHANGED 广播基, call 0x7729C4）
    //     体：先 call 0x76B42C 广播；若 (edx=stateId==0x15 && [ebp+8]==0，即 state 丢失)：
    //       从备份还原 +0x266/+0x28C/+0x320/+0x324 <- +0x4E8/+0x4EC/+0x4F0/+0x4F4；
    //       m_dwHitTick(+0x35C)=m_dwWalkTick(+0x384)=GetTickCount()+2000；
    //       call [vmt+0xD8] 广播 wParam=0x27DC 于 (m_nCurrX[+0x12C], m_nCurrY[+0x130])。
    //     fail-closed 原因：C# 把 +0x14 折进了 **私有非虚** SendTimedAbilityState(node, removed)
    //       （其签名/时机为 node/removed，而非 native 的 (stateId, gained)），怪物侧无可覆写入口；
    //       新增虚方法会改动 TBaseObject 公共签名，超出本轮范围（同 NoWinerAnimal +0x1B4 处置）。
    //
    //   ── +0x198 = 受伤伤害变换（fail-closed，不臆造）──────────────
    //     child 0x66AE0C ; parent 0x71F824（怪物基版：m_Master(+0x38C)==0 时 return dmg*3, 否则 dmg）
    //     调用点遍布法术/技能伤害区 0x68E–0x691xxx（`call [target.vmt+0x198](dmg)`）。
    //     体：对自身 MakePosion(stateId=0x15, ms=Random(6)+5, flag=1) [call vmt+0xC8]；
    //       首次受击(备份标记 +0x4E8==0)先快照 +0x266->+0x4E8, +0x28C->+0x4EC,
    //       +0x320->+0x4F0, +0x324->+0x4F4；随后削弱：
    //         +0x266 = _MIN(备份-10, 5)         [helper 0x4C700C: cmp edx,eax/jg/mov eax,edx/ret = _MIN]
    //         +0x28C = 备份_0x4EC * 8 / 10       (m_WAbil.DC 低字 ×0.8)
    //         +0x320 = 备份_0x4F0 + 1000         (m_nNextHitTime +1s)
    //         +0x324 = 备份_0x4F4 + 1000         (走速间隔 +1s)
    //       返回 dmg **原值不变**（0066AEC6 `mov eax,esi` / ret 4）。
    //     fail-closed 原因：C# 把 +0x198 伤害管线实现为 **非虚** 具体函数
    //       (TBaseObject.ApplyNativeUnionDamageReductions / HeroObject.ApplyNativeUnionTargetManaCost，
    //        见 GameSvr/Services/NativeTimedAbilityCombatConsumer.cs)，怪物侧无可覆写虚方法可替换；
    //       同 NoWinerAnimal 对 GetHitStruckDamage/GetMagStruckDamage 的处置。
    //     连带：state 0x15 的"受击削弱(+0x198 加)/到期还原(+0x14)"整体 fail-closed，故上述 4 个
    //       备份字段 [+0x4E8..+0x4F4] 在本类中无消费者 -> 不落 C# 字段（避免死代码）。
    //
    // 综上：本类忠实落地【类存在 + 构造器(m_nViewRange=8) + Struck(+0x208)】；
    // +0x14 / +0x198 两槽 fail-closed（附完整字节证据，绝不臆造）。
    // 原先 race 121 落工厂 default (0x67AE5E `xor eax,eax`) → 返回 nil，该怪根本不出现。
    public class ArmLightGuard : ScultureMonster
    {
        public ArmLightGuard() : base()
        {
            m_nViewRange = 8;
        }

        public override void Struck(TBaseObject hiter)
        {
            if (m_TargetCret != null && (HUtil32.GetTickCount() - m_dwTargetFocusTick) <= 3000)
            {
                return;
            }
            base.Struck(hiter);
        }
    }
}

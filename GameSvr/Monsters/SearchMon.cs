using SystemModule;

namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：TSearchMon，VMT 0x66D320，size 0x55C，classname "TSearchMon"
    //    （证据：VMT-0x2C=类名指针→"\x0aTSearchMon"；VMT-0x28=0x55C=size；VMT-0x24=vmtParent
    //     二级指针，rd32(0x71D4D0)=0x71D51C=TAnimal VMT）。→ C# 落父类 = AnimalObject（= TAnimal）。
    //    父链：TSearchMon→TAnimal(=AnimalObject)→…→TBaseObject。
    //    本类是 race 138-143 六个 Ice 怪的公共父。138-142 无自有 ctor，直接以本类 ctor
    //    sub_6735D4（= TSearchMon.Create）构造（工厂 handler 传各自 classref，Delphi 据 VMT 分配
    //    子类实例大小、跑基类 ctor 体）；143 TQuickKnifeIceMon 另有 ctor 0x6748CC（其内 call 0x6735D4）。
    //
    // 构造器 TSearchMon.Create = sub_6735D4 全文（Delphi ctor 序幕/收尾略）：
    //   6735ED  E8 36 A2 0A 00                 call 0x71D828  ; = TAnimal.Create (= C# AnimalObject())
    //   6735F2  C6 86 EE 02 00 00 00           mov byte [esi+0x2EE],0
    //   6735F9  C7 46 78 04 00 00 00           mov dword[esi+0x78],4        ; m_nViewRange = 4
    //   673600  C7 86 40 05 00 00 20 03 00 00  mov dword[esi+0x540],0x320   ; = 800
    //   67360A  C7 86 44 05 00 00 E8 03 00 00  mov dword[esi+0x544],0x3E8   ; = 1000
    //   673614  E8 27 4D D9 FF                 call 0x408340  ; GetTickCount
    //   673619  89 86 48 05 00 00              mov [esi+0x548],eax
    //   67361F  33 C0 / 89 86 4C 05 00 00      mov [esi+0x54C],0
    //   673627  33 C0 / 89 86 50 05 00 00      mov [esi+0x550],0
    //   67362F  C6 86 D8 04 00 00 01           mov byte [esi+0x4D8],1
    //   673636  33 C0 / 89 86 58 05 00 00      mov [esi+0x558],0
    //   偏移锚点：+0x78=m_nViewRange（AttackIceTower.cs:26 / ArmLightGuard.cs:32 印证）。
    //     下方 ctor 忠实落地 m_nViewRange=4。
    //
    // ── fail-closed：TSearchMon 自有新字段（父 TAnimal size=0x4D8 之后的槽）─────────────
    //   +0x4D8(byte=1) / +0x540(=0x320) / +0x544(=0x3E8) / +0x548(=tick) / +0x54C(=0) /
    //   +0x550(=0) / +0x558(=0)。这些字段的唯一消费者是 TSearchMon 的 Run(slot34) 与 12 个
    //   新虚槽(slot131-142)，二者在 C# 侧【全部 fail-closed】（见下），故不落 C# 字段（避免死代码；
    //   同 ArmLightGuard.cs:82 对无消费者字段的处置）。[+0x2EE]=0 是零初始化后的冗余写。
    //   注：TSearchMon 的 +0x4D8 与 TMonster 的 m_boWalkWaitLocked(+0x4D8) 只是【同偏移不同字段】——
    //   二者都直接继承 TAnimal(size 0x4D8)，各自的 +0x4D8 是彼此独立的自有槽；本类不继承 TMonster，
    //   不存在冲突。
    //
    // VMT 差分（child 0x66D320 vs parent TAnimal 0x71D51C）——三类覆写，全部 fail-closed：
    //  (A) 落在已知 C# 虚槽、但 body 依赖新字段/新虚槽：
    //   slot34 +0x88 Run -> 0x6739D4：`call base.Run(0x71E50C)` → `call sub_6736E0`(TSearchMon helper) →
    //     `call [vmt+0x40]`(can-act 谓词) → 读 +0x54C 新字段(4000ms 闸) → `call [vmt+0x234]`(slot142
    //     新虚槽) …通篇依赖新字段与新虚槽 → fail-closed（行为退化为 AnimalObject.Run）。
    //  (B) 落在 C# 无虚入口的伤害/属性/护甲槽（处置同 TAIMon / ArmLightGuard / NoWinerAnimal）：
    //   slot102 +0x198 -> 0x6738FC (parent 0x71F824)   slot103 +0x19C -> 0x6735B0 (parent 0x71F840)
    //   slot104 +0x1A0 -> 0x6735AC (parent 0x71F884)   slot105 +0x1A4 -> 0x6735A8 (parent 0x71F8A8)
    //   slot107 +0x1AC -> 0x673908 (parent 0x767B5C)   slot109 +0x1B4 -> 0x673D70 (parent 0x76C35C=护甲选择器)
    //   slot117 +0x1D4 -> 0x673DB0 (parent 0x76CCE8)
    //  (C) TSearchMon 新增的 12 个虚槽 slot131-142（+0x20C..+0x238）——C# TBaseObject 体系无对应
    //     虚方法入口（父 TAnimal 表尾止于 slot130），全部 fail-closed，仅登记 VA：
    //   slot131 +0x20C 0x67365C   slot132 +0x210 0x67383C   slot133 +0x214 0x673738
    //   slot134 +0x218 0x6737E0   slot135 +0x21C 0x673C74   slot136 +0x220 0x673C70
    //   slot137 +0x224 0x4035A4   slot138 +0x228 0x4035A4   slot139 +0x22C 0x4035A4
    //   slot140 +0x230 0x4035A4   slot141 +0x234 0x673464   slot142 +0x238 0x673468
    //     （slot137-140 的 0x4035A4 = Delphi 抽象/空基桩：这四个抽象槽由各 Ice 子类各自实现，
    //      见子类文件的 VMT 差分登记。）
    //
    // 综上：TSearchMon 因 C# 扁平化层级，其【自有行为（Run + 7 个伤害/属性槽 + 12 个新虚槽）】整体
    //   fail-closed；本类忠实落地【类存在 + 构造器（m_nViewRange=4）】，作为 race 138-143 的父类。
    //   原先这六个 race 因父类未移植落工厂 default(0x67AE5E xor eax,eax) → nil，六怪根本不出现。
    public class SearchMon : AnimalObject
    {
        public SearchMon() : base()
        {
            m_nViewRange = 4;   // 6735F9  mov dword [esi+0x78],4
        }
    }
}

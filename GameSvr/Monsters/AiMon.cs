using SystemModule;

namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：TAIMon，VMT 0x719CF4，size 0x5F0，classname "TAIMon"
    //    （证据：VMT-0x2C=类名指针→"\x06TAIMon"；VMT-0x28=0x5F0=size；VMT-0x24=vmtParent 二级指针，
    //     rd32(0x71D4D0)=0x71D51C=TAnimal VMT）。→ C# 落父类 = AnimalObject（= TAnimal；判定同源于
    //     AttackIceTower.cs / SkyArcher.cs：parent TAnimal → AnimalObject）。
    //    父链：TAIMon→TAnimal(=AnimalObject)→TCreature/TBaseObj/TObject(合并入 C# TBaseObject)。
    //    本类是 race 129 TShadowHero / race 130 TTaoistEngine 的公共父：二者 ctor 都
    //    `call 0x71AF1C`（= TAIMon.Create，见下）。
    //
    // 构造器 TAIMon.Create = sub_71AF1C 全文（Delphi ctor 序幕/收尾略）：
    //   71AF35  E8 EE 28 00 00                 call 0x71D828   ; = TAnimal.Create (= C# AnimalObject())
    //   71AF3A  C7 86 88 04 00 00 00 98 7F 33  mov dword [esi+0x488],0x337F9800  ; = 864000000
    //   71AF44  E8 F7 D3 CE FF                 call 0x408340   ; GetTickCount -> eax
    //   71AF49  89 86 84 04 00 00              mov  [esi+0x484],eax             ; +0x484 = AI 上次动作时戳闸
    //   71AF4F  C6 86 EE 02 00 00 00           mov  byte [esi+0x2EE],0          ; +0x2EE 清零(冗余)
    //   71AF56  8B 86 84 04 00 00              mov  eax,[esi+0x484]
    //   71AF5C  89 86 84 03 00 00              mov  [esi+0x384],eax             ; m_dwWalkTick = tick
    //   71AF62  8B 86 84 04 00 00              mov  eax,[esi+0x484]
    //   71AF68  89 86 5C 03 00 00              mov  [esi+0x35C],eax             ; m_dwHitTick  = tick
    //   偏移锚点：+0x35C=m_dwHitTick / +0x384=m_dwWalkTick（AnimalObject.cs:48/52 逐字印证）。
    //   语义：TAnimal.Create 给新怪 m_dwHitTick/m_dwWalkTick = tick + Random(3000) 的出生错峰
    //     （AnimalObject.cs:43-58）；TAIMon 用【同一次 GetTickCount 的纯 tick】覆盖二者 = 取消错峰
    //     （英雄引擎出生即可动）。下方 ctor 忠实复现（一次取值，两处赋同值）。
    //
    // ── fail-closed（有字节、C# 无对应可覆写入口/无消费者，绝不臆造）─────────────────
    //  ① [+0x488]=0x337F9800(=864000000ms≈10天) 与 [+0x484]=GetTickCount：这是 TAIMon 的 AI 动作
    //     间隔/上次动作时戳。唯一消费者是本类 fail-closed 的属性/AI 覆写与子类 fail-closed 的
    //     Run/Struck —— 例如 ShadowHero.Run @0x71B85D `sub eax,[+0x484] / cmp eax,[+0x488] / jae`。
    //     C# 侧这些消费者全部 fail-closed（见下 & 子类文件），故不落 C# 字段（避免死代码；同
    //     ArmLightGuard.cs:82 对无消费者备份字段的处置）。+0x484 的别名风险另见
    //     TBaseObject.NativeColdTime.cs:79（玩家侧同偏移是冷却时戳，怪物侧才是 AI 闸）。
    //  ② [+0x2EE]=0 是 Delphi 实例内存零初始化后的冗余写，C# 无需落地（默认即 0）。其语义 =
    //     GetHitStruckDamage 的护甲加成标志（TBaseObject.cs:5823 `cmp byte [ebx+0x2EE],1`）。
    //
    // VMT 差分（child 0x719CF4 vs parent TAnimal 0x71D51C，逐槽扫至表尾）——仅 4 项真实覆写，
    //   全部落在【伤害/属性管线】虚槽；C# 侧把这条管线实现为【非虚】具体函数，怪物侧无可覆写
    //   虚方法入口 → 4 项全部 fail-closed：
    //   slot102 +0x198 -> 0x71B2F0 (parent 0x71F824)
    //   slot103 +0x19C -> 0x71B304 (parent 0x71F840)
    //   slot104 +0x1A0 -> 0x71B300 (parent 0x71F884)
    //   slot105 +0x1A4 -> 0x71B2FC (parent 0x71F8A8)
    //   槽身份依据：WalkMon.cs:30-31 / KingFireDragon.cs:29-30 已把这四槽（父 0x71F824/0x71F840/
    //     0x71F884/0x71F8A8）标定为"属性/受伤伤害变换"族；ArmLightGuard.cs:67-81 记录 +0x198 的
    //     C# 落点是【非虚】ApplyNativeUnionDamageReductions 等（见 NativeTimedAbilityCombatConsumer.cs），
    //     没有可替换的虚方法；新增虚方法会改动 TBaseObject 公共签名，超出本轮范围。宁缺毋滥，不覆写。
    //
    // 综上：TAIMon 因 C# 扁平化层级，其【自有行为（4 个伤害/属性覆写）】整体 fail-closed；
    //   本类忠实落地【类存在 + 构造器（取消 hit/walk 出生错峰）】，作为 race 129/130 的可实例化父类。
    //   原先 race 129/130 因父类未移植落工厂 default(0x67AE5E xor eax,eax) → nil，两怪根本不出现。
    public class AiMon : AnimalObject
    {
        public AiMon() : base()
        {
            // sub_71AF1C @0x71AF44-0x71AF68：单次 GetTickCount，覆盖 base 的 tick+Random(3000) 错峰。
            var dwTick = HUtil32.GetTickCount();
            m_dwWalkTick = dwTick;   // 71AF5C  mov [esi+0x384],eax
            m_dwHitTick = dwTick;    // 71AF68  mov [esi+0x35C],eax
        }
    }
}

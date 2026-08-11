using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        // POIS-09 / POIS-10 — 战神 TCreature.Run 里的 2500ms 中毒扣血块。
        // 权威字节 sub_76B6F0 @0x76BD33-0x76BE1C（staging/_poison_work/f76B6F0.txt）:
        //
        //   76BD33  mov eax,[ebp-4] / sub eax,[esi+0x28] / cmp eax,0x9C4
        //   76BD3E  jb 0x76BE20                  ; 反极性 => elapsed >= 2500 才走
        //   76BD44  mov [esi+0x28],eax           ; 硬置位(非累加)
        //   76BD4A  xor eax,eax / mov [ebp-0xC],eax   ; rec := nil
        //   76BD4F  mov dl,6    / call 0x772960  ; HasState(0x06)
        //   76BD5A  je 0x76BD88                  ; 未中 -> 下一档
        //   76BD5C  mov dl,6    / call 0x773B98  ; FindNode(0x06) -> rec
        //   76BD68  mov edx,0x4C4B40             ; = 5,000,000
        //   76BD6D  mov eax,[esi+0x2B0]          ; MaxHP
        //   76BD73  call 0x4C700C                ; MIN(MaxHP, 5000000)  有符号
        //   76BD78  mov ecx,0x64 / cdq / idiv ecx ; / 100
        //   76BD83  mov [edx+0x0A],eax           ; 写回 rec.Value
        //   76BD86  jmp 0x76BDF5
        //   76BD88  mov dl,1 ... 同形 但 mov ecx,0x1E ; / 30
        //   76BDC1  mov dl,0x1C ; 只取 rec，不改 Value（施法者给的量）
        //   76BDDC  mov dl,0x1F ; 同 0x1C
        //   76BDF5  cmp [ebp-0xC],0 / je 0x76BE20     ; 四档都没中 -> 什么都不做
        //   76BDFB  mov edi,[rec+0x0A] / inc edi      ; dmg = Value + 1
        //   76BE02  test edi,edi / jle 0x76BE20       ; <=0 不打
        //   76BE0A  call [vmt+0x1B0](self, edi)       ; = DamageHealth
        //   76BE12  mov [esi+0x10],0                  ; 清 HP 回复预算
        //   76BE17  mov [esi+0x14],0                  ; 清 MP 回复预算
        //
        // 四档是 if/else-if 链（每档 jmp 到汇合点 0x76BDF5），所以【一个 tick 只服务
        // 一档】，优先级 0x06 > 0x01 > 0x1C > 0x1F。这不是四段独立的伤害叠加。
        //
        // ⚠️ 两个易错常量，均按字节核过:
        //  * 0x4C4B40 = 5,000,000（不是 1,000,000 —— staging 汇总文档
        //    status_poison_accuracy_20260810.md:75 写成 1000000 是错的）。
        //  * 0x4C700C 是 MIN(`cmp edx,eax; jg`)，其紧邻孪生 0x4C7004 才是 MAX；
        //    两者只差 jl/jg，落错入口会得到反义函数。
        //
        // 0x06 与 0x01 两档【每 tick 覆写】rec.Value，即施法者给的量被 MaxHP 派生量
        // 顶掉；0x1C/0x1F 两档保留施法者的量。伤害一律 Value+1。

        /// <summary>0x4C4B40 — MaxHP 参与除法前的上限钳。</summary>
        internal const int NativePoisonMaxHpCap = 0x4C4B40;

        /// <summary>MaxHP 缩放中毒档：0x06 除以 100，0x01 除以 30。</summary>
        internal const byte NativePoisonStateMaxHpOver100 = 0x06;
        internal const byte NativePoisonStateMaxHpOver30 = 0x01;

        /// <summary>施法者供量档：Value 原样使用，不被覆写。</summary>
        internal const byte NativePoisonStateCasterValueLow = 0x1C;
        internal const byte NativePoisonStateCasterValueHigh = 0x1F;

        /// <summary>
        /// <c>MIN(MaxHP, 5000000) / divisor</c>，有符号截断除法，对应
        /// <c>0x4C700C</c> 后接 <c>cdq/idiv</c>。
        /// </summary>
        internal static int GetNativePoisonMaxHpAmount(int maxHp, int divisor)
        {
            return Math.Min(maxHp, NativePoisonMaxHpCap) / divisor;
        }

        /// <summary>
        /// 按 0x06 &gt; 0x01 &gt; 0x1C &gt; 0x1F 的优先级选出本 tick 生效的中毒档，
        /// 并（仅对前两档）把 MaxHP 派生量写回节点。返回 false = 四档皆无。
        /// </summary>
        private bool TryResolveNativePoisonTickDamage(out int damage)
        {
            damage = 0;
            var node = FindNativePoisonNode(NativePoisonStateMaxHpOver100);
            if (node != null)
            {
                node.Value = GetNativePoisonMaxHpAmount(m_WAbil.MaxHP, 100);
            }
            else
            {
                node = FindNativePoisonNode(NativePoisonStateMaxHpOver30);
                if (node != null)
                {
                    node.Value = GetNativePoisonMaxHpAmount(m_WAbil.MaxHP, 30);
                }
                else
                {
                    node = FindNativePoisonNode(NativePoisonStateCasterValueLow)
                        ?? FindNativePoisonNode(NativePoisonStateCasterValueHigh);
                }
            }

            if (node == null)
            {
                return false;
            }

            // 0x76BDFB mov edi,[rec+0x0A] / inc edi
            damage = unchecked(node.Value + 1);
            return true;
        }

        /// <summary>
        /// <c>HasState(id)</c> 门 + <c>FindNode(id)</c>，即
        /// <c>0x772960</c> 后接 <c>0x773B98</c>。<c>FindTimedAbilityInternal</c>
        /// 本身就是先查位集再遍历 <c>obj+0xDC</c> 链表，语义一致。
        /// </summary>
        private TimedAbilityNode FindNativePoisonNode(byte internalType)
        {
            return FindTimedAbilityInternal(internalType);
        }
    }
}

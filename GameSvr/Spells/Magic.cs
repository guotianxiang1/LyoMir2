using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    public static class Magic
    {
        public static int MPow(TUserMagic UserMagic)
        {
            // ✅ 已由战神字节证据独立验证 —— 无 `+1` 是战神形态,不只是 ref 结论。
            // 战神 EA: sub_4C8658 @0x4C8665-0x4C8673(MPow 融合在威力函数首段,镜像里【没有】独立 MPow 函数;
            // sub_4C8648 是唯一 caller @0x4C864E):
            //   4C8661  mov   edi,[eax]                 ; MagicInfo*
            //   4C8665  mov   al, byte [edi+0x16]       ; wMaxPower
            //   4C8668  movzx esi,byte [edi+0x15]       ; wPower
            //   4C866C  sub   eax,esi                   ; wMaxPower - wPower
            //   4C866E  call  sub_403B4C                ; Delphi Random(EAX) -> [0,EAX)
            //   4C8673  add   eax,esi                   ; + wPower
            //   => MPow = wPower + Random(wMaxPower - wPower)      —— 【无 "+1"】(sub 之后直接 call,无 inc)
            // 两字段均按【字节】读取(尽管命名带 w),即原生记录里各占 1 字节。
            // 唯一性已核实: 全 CODE 段扫 "[reg+0x15] 与 [reg+0x16] 在 12 字节内配对读" 两种字节序共 5 处命中,
            // 只有 0x4C8665 是 `+0x16` 在前的 MPow 形状(另 4 处 0x60B1AD/0x67CB4A/0x71EA3D/0x751331 非 MagicInfo)。
            // 证据: staging/merchant_money_native_exact_20260804.md §7 + discovery_spellapply_20260803.md 行 55-56。
            // 并列 ref 引用(保留,勿删;来源=GameOfMir 参考分支,非战神,仅算术形态线索):
            //   Magic.pas:57 `Result := wPower + Random(wMaxPower - wPower);` —— 恰好与战神同形。
            //   (该 ref 分支源码内部的地址标注已删除:那是 ref 自己的地址,不是战神 EA,曾有被当成
            //    Tier-1 引用的风险;战神真实 EA 就是上面的 0x4C8665。)
            // 此前多了 "+1"，把随机域从 [0, max-pow) 放宽成 [0, max-pow]，威力上限比原版高一点。
            // wMax < wPower 时原生 `sub eax,esi` 得到负数，直接 `call Random`：
            //   4C866C  2B C6              sub eax, esi
            //   4C866E  E8 D9 B4 F3 FF     call 0x403B4C
            // 无 test/jle。返回值按无符号高 32 位，随后 `add eax,esi` 再作为
            // **有符号** 32 位参与 `imul (btLevel+1)` @0x4C868E、`fild` @0x4C8693、
            // `fdiv dword [0x4C86B8]` (=4.0f)、`call 0x403574` ROUND，加上
            // defRoll+btDefPower 后以 32 位 EAX 返回。全程没有钳位。
            // 活路径是 CalculateNativeMagicProducerSkillPower（同一函数）。
            // Math.Max(1,...) 是发明的守卫：负数时 C# 抽 Random(1)=0 而原生抽大数。
            return unchecked(UserMagic.MagicInfo.wPower + M2Share.RandomNumber.Random(UserMagic.MagicInfo.wMaxPower - UserMagic.MagicInfo.wPower));
        }

        // Native sub_4C8658 via wrapper sub_4C8648 (raw btLevel). The MPow roll is
        // FUSED into the same native function, so callers must not roll MPow first.
        // Divisor is the hardcoded float32 4.0 at [0x4C86B8] (raw 00 00 80 40) —
        // NOT (btTrainLv + 1). btTrainLv (+0x1A) is never read in the body; in
        // native it is only a level CAP. Unified onto the byte-audited
        // TPlayObject.CalculateNativeMagicProducerSkillPower.
        // See staging/spellpower_formula_exact_20260803.md.
        public static int GetPower(TUserMagic UserMagic)
        {
            return TPlayObject.CalculateNativeMagicProducerSkillPower(UserMagic);
        }

        // Native sub_4C8764: Round(nInt * (2*effLevel + 6) / 12.0f) + btDefPower +
        // Random(btDefMaxPower - btDefPower), default-power roll drawn FIRST and the
        // level factor being the EFFECTIVE level (sub_4C896C). No btTrainLv divisor
        // and no nInt/3 split exist in the native body.
        public static int GetPower13(int nInt, TUserMagic UserMagic)
        {
            return TPlayObject.CalculateNativeMagicProducer13Power(UserMagic, nInt);
        }

        public static ushort GetRPow(int wInt)
        {
            ushort result;
            if (HUtil32.HiWord(wInt) > HUtil32.LoWord(wInt))
            {
                result = (ushort)(M2Share.RandomNumber.Random(HUtil32.HiWord(wInt) - HUtil32.LoWord(wInt) + 1) + HUtil32.LoWord(wInt));
            }
            else
            {
                result = HUtil32.LoWord(wInt);
            }
            return result;
        }

        
        
        
        
        
        
        
        
        // Native sub_73E93C — the single generic charm gate. It TESTS and
        // CONSUMES in one body; C# keeps the CheckAmulet/UseAmulet split, so the
        // two halves must agree with this one function.
        //   73E95F  mov  dl,9                        ; slot 9 (U_BUJUK) ONLY —
        //                                            ;   U_ARMRINGL is never read
        //   73E967  call sub_75EC20                  ; GetUseItem(9)
        //   73E970  je   fail                        ; nil charm
        //   73E972  cmp  dword [edi+0x1C],0          ; StdItem nil
        //   73E97A  mov  edx,[0x75E3F8]              ; TBujuk class
        //   73E980  call sub_404828                  ; Delphi `is`
        //   73E987  je   fail
        //   73E989  imul eax,[ebp-4],0x64            ; nCount * 100
        //   73E98D  mov  dx,word [edi+0x26]          ; Dura (u16)
        //   73E994  add  ecx,0x32                    ; Dura + 50
        //   73E997  cmp  eax,ecx
        //   73E999  jg   fail                        ; PASS iff nCount*100 <= Dura+50
        // The old C# predicate was `HUtil32.Round(Dura / 100.0) >= nCount`, whose
        // banker's rounding sends the exact .5 case DOWN: over Dura in [0,1200]
        // and nCount in {1,2,5} it diverges at exactly Dura=50,nCount=1 and
        // Dura=450,nCount=5 — native ALLOWS those casts, C# refused them.
        // Type test: StdMode 25 + Shape 5 == native `is TBujuk`, proven through
        // the item factory sub_74C338 (StdMode bytetab @0x74C374 -> 0x74D066 is
        // reached ONLY by StdMode 25) whose Shape switch @0x74D07B maps
        // Shape 1,2 -> new TPoisons and Shape 5 -> new TBujuk. The `nType == 2`
        // arm is therefore the TPoisons shape set {1,2} — tightened from the old
        // `Shape <= 2`, which wrongly admitted Shape 0 (a different class).
        public static bool CheckAmulet(TPlayObject PlayObject, int nCount, int nType, ref short Idx)
        {
            // 免毒符：DoSpell 内 12 处 je/call 被 NOP，缺符也视为通过（首站 0x6ED945）。
            if (YanshenConfig12Behaviors.AntiPoisonAmuletFree(PlayObject))
            {
                Idx = Grobal2.U_BUJUK;
                return true;
            }
            var result = false;
            Idx = 0;
            var charm = PlayObject.m_UseItems[Grobal2.U_BUJUK];
            if (charm != null && charm.wIndex > 0)
            {
                var amuletStdItem = M2Share.UserEngine.GetStdItem(charm.wIndex);
                if (amuletStdItem != null && amuletStdItem.StdMode == 25 &&
                    nCount * 100 <= charm.Dura + 50)
                {
                    switch (nType)
                    {
                        case 1:
                            if (amuletStdItem.Shape == 5)
                            {
                                Idx = Grobal2.U_BUJUK;
                                result = true;
                            }
                            break;
                        case 2:
                            if (amuletStdItem.Shape >= 1 &&
                                amuletStdItem.Shape <= 2)
                            {
                                Idx = Grobal2.U_BUJUK;
                                result = true;
                            }
                            break;
                    }
                }
            }
            return result;
        }

        
        
        
        
        
        
        
        // The consume half of native sub_73E93C (the C# split keeps it separate).
        // The amount IS `nCount * 100` here — `imul eax,[ebp-4],0x64` @0x73E989 —
        // which is why UseAmulet keeps that multiplication. (Only the inline
        // 施毒术 path @0x6ED986 uses a bare literal 100; that path no longer
        // routes through this function.)
        //   73E9A0  cmp  eax,edx                     ; nCount*100 vs Dura
        //   73E9A2  jge  0x73E9C9                    ; >= -> the shortfall branch
        //   73E9A4  sub  word [edi+0x26],ax          ; Dura -= nCount*100
        //   73E9BA  mov  cx,0x278D                   ; RM_DURACHANGE
        //   73E9C9  push 0 / push 0xA
        //   73E9D1  mov  ecx,0x73EA14                ; "持久耗尽" (LOG field)
        //   73E9D6  mov  dl,9
        //   73E9DE  call sub_75F27C                  ; remove from slot 9
        // sub_75F27C nulls the slot pointer, recalcs abilities (sub_75F4F4 is an
        // unconditional `mov al,1; ret`, then sub_75EE78 + VMT+0x8C), calls
        // VMT+0x268, and writes a game-data log — it does NOT merely zero Dura
        // and keep the object as the old C# body did. Note the shortfall test is
        // `>=`, so an exact `nCount*100 == Dura` consumes the charm entirely.
        public static void UseAmulet(TPlayObject PlayObject, int nCount, int nType, ref short Idx)
        {
            if (YanshenConfig12Behaviors.AntiPoisonAmuletFree(PlayObject))
                return;
            var charm = PlayObject.m_UseItems[Idx];
            if (charm == null)
            {
                return;
            }
            var dura = (ushort)(nCount * 100);
            if (dura < charm.Dura)
            {
                charm.Dura -= dura;//减少护身符持久即数量
                PlayObject.SendMsg(PlayObject, Grobal2.RM_DURACHANGE, Idx, charm.Dura, charm.DuraMax, 0, "");
            }
            else
            {
                PlayObject.m_UseItems[Idx] = null;
                PlayObject.RecalcAbilitys();
                PlayObject.SendDelItems(charm);
                var StdItem = M2Share.UserEngine.GetStdItem(charm.wIndex);
                M2Share.AddNativeGameDataLog(PlayObject, 0x0A,
                    StdItem == null ? string.Empty : StdItem.Name,
                    charm.MakeIndex, 1, "持久耗尽");
            }
        }
    }
}


using SystemModule;

namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 145 = TAttackIceTower，VMT 0x66E938，size 1248 (0x4E0)，
    //    parent = TAnimal(0x71D51C，size 1240 = 0x4D8) —— 自身两个 dword 字段 0x4D8 / 0x4DC。
    //
    // 工厂 sub_679F8C 两级分派：索引表[145-0xB=0x86] = 0x3F = 63 ; jt[63] = 0x67A921。
    // case body 0x67A921..0x67A934 全文 (20 字节)：
    //   67A921  B2 01              mov  dl,1
    //   67A923  A1 EC E8 66 00     mov  eax,[0x66E8EC]   ; classref -> TAttackIceTower
    //   67A928  E8 17 A3 FF FF     call 0x674C44         ; TAttackIceTower.Create
    //   67A92D  89 45 F8           mov  [ebp-8],eax
    //   67A930  E9 0A 04 00 00     jmp  0x67AD3F
    // case 内无额外 RNG / 字段写。归属唯一性（穷尽判据）：
    //   · classref [0x66E8EC] 全 CODE 段加载点 = 1 处 (0x67A924)
    //   · ctor 0x674C44 的 E8 rel32 调用者全扫 = 1 处 (0x67A928)
    //
    // 构造器 sub_674C44 全文 (95 字节)，Delphi ctor 序幕/收尾略：
    //   674C5D  E8 C6 8B 0A 00                call 0x71D828  ; = TAnimal.Create (C# AnimalObject())
    //   674C62  E8 D9 36 D9 FF                call 0x408340  ; GetTickCount
    //   674C67  89 86 DC 04 00 00             mov  [esi+0x4DC],eax
    //   674C6D  C6 46 75 01                   mov  byte [esi+0x75],1        ; m_boStickMode = true
    //   674C71  66 C7 86 6C 02 00 00 FA 00    mov  word [esi+0x26C],0xFA    ; m_wEffectResistance = 250
    //   674C7A  C6 86 54 01 00 00 00          mov  byte [esi+0x154],0       ; m_btDirection = 0
    //   674C81  C7 46 78 03 00 00 00          mov  dword [esi+0x78],3       ; m_nViewRange = 3
    //   偏移映射与 IceDoor.cs 同源：+0x75 = m_boStickMode、+0x26C = m_wEffectResistance、
    //   +0x154 = m_btDirection、+0x78 = m_nViewRange (TBaseObject.cs:112/148/298)。
    //
    // VMT 差分 (132 槽 + 8 个负偏移标准槽逐槽比对 parent TAnimal) 共 6 项：
    //   +0x018 Operate   -> 0x674D70   ← 本类落地
    //   +0x084 Die       -> 0x674D20   ← fail-closed，见下
    //   +0x088 Run       -> 0x674D80   ← fail-closed，见下
    //   +0x0C8 ?         -> 0x674D78   ← fail-closed，见下
    //   +0x1E8 CanAddNativeTimedAbility -> 0x674D74   ← 本类落地
    //   +0x208 Struck    -> 0x674E4C   ← 本类落地
    // 槽位名依据：+0x018/+0x084/+0x088/+0x208 由已移植类交叉标定
    // (TBeeQueen/TSpiderHouseMon 唯一覆写 +0x018 ↔ C# Operate；TZilKinZombi/TCastleDoor
    //  +0x084 ↔ C# Die；TSoccerBall 恰两槽 +0x088/+0x208 ↔ C# Run/Struck)；
    // +0x1E8 由 TBaseObject.TimedAbility.cs:474 已记录的 "VMT+0x1E8 @ EA 0x772F84" 锚定，
    // 本类该槽 parent 值正是 0x772F84，逐字吻合。
    //
    // ── fail-closed 明细（有字节、但 C# 侧无对应可覆写入口，不臆造）─────────────
    // ① +0x084 Die -> sub_674D20 (23 字节)：
    //      674D20  55 8B EC 53 / 8B D8        push ebp/mov ebp,esp/push ebx / ebx=Self
    //      674D26  8B C3 / E8 77 FF FF FF     call 0x674CA4      ; 私有 helper（见下）
    //      674D2D  8B C3 / E8 88 95 0A 00     call 0x71E2BC      ; = TAnimal.Die (base.Die())
    //      674D34  5B 5D C3
    //    helper sub_674CA4 (122 字节) 遍历可见对象链表 [self+0x388]，对每个
    //    IsProperTarget(sub_767498) 命中的目标：edi = [tgt+0x2AC] div 10；
    //    [tgt+0x2AC] -= edi；call 0x76B4F8(tgt, self, edi, 0xC8)；若 [tgt+0x178] != 0
    //    再 call 0x76B518(tgt, self, edi)。0x76B4F8 / 0x76B518 / 字段 [+0x2AC]
    //    在 C# 侧均无已建立的对应，故整条 Die 覆写 fail-closed —— 不覆写，
    //    行为退化为纯 base.Die()（少了这段“死亡时按 10% 反伤全场”）。
    // ② +0x088 Run -> sub_674D80 (203 字节)：结构已完全解出——
    //      base.Run() → m_btDirection=0 → tick=GetTickCount()
    //      若 (tick - [self+0x88]) > 0x1388(5000) 且 [self+0x74]==0：
    //          call 0x765DEC(self, tick)  ; 刷新可见对象链表
    //          [self+0x88] = tick
    //          遍历 [self+0x388]，逐个调 helper sub_674D38，任一命中则
    //          SendRefMsg(RM_HIT 0x2714, m_btDirection, m_nCurrX, m_nCurrY, 0, "")
    //      若 (tick - [self+0x4DC]) > 0x3A98(15000)：[self+0x2AC] = 0
    //    helper sub_674D38：target 非空且 IsProperTarget → 置 true 并
    //      `push 1 / mov cx,3 / mov dl,0x1D / call [target.VMT+0xC8]`。
    //    卡点：槽 +0xC8（基类 0x76B3C8，形参 dl=状态号 / cx=秒数 / [ebp+8]=值）
    //    以及字段 [+0x74]、[+0x2AC] 在 C# 侧都没有已确立的映射。
    //    宁缺毋滥：不落地半个 Run。
    // ③ +0x0C8 -> sub_674D78 = `55 8B EC 5D C2 04 00`（空函数，ret 4）：
    //    语义明确 = 本怪【完全免疫】+0xC8 那条状态施加路径。但该槽在 C# 无入口，
    //    与 ② 同因 fail-closed。
    // ─────────────────────────────────────────────────────────────────────
    //
    // 原先 race 145 落工厂 default(0x67AE5E `xor eax,eax`) → 返回 nil，攻击冰塔根本不出现。
    public class AttackIceTower : AnimalObject
    {
        /// <summary>战神 dword [self+0x4D8]：Struck 广播的 10 秒节流时间戳。ctor 不写它，
        /// 故初值 0 —— 第一次被击必定触发广播。</summary>
        public int n4D8 = 0;

        /// <summary>战神 dword [self+0x4DC]：出生时刻 GetTickCount()，由 Run 的
        /// 15 秒闸读取（见类注释 ②）。</summary>
        public int n4DC = 0;

        public AttackIceTower() : base()
        {
            n4DC = HUtil32.GetTickCount();
            m_boStickMode = true;
            m_wEffectResistance = 250;
            m_btDirection = 0;
            m_nViewRange = 3;
        }

        // 战神 VMT+0x018 = sub_674D70，函数体只有一个字节 `C3` —— 裸 ret，
        // 【不调用】TAnimal 的 0x71DEE8。可证的语义是：本怪的消息队列条目一律不被处理。
        // Delphi 里 Boolean 结果取 AL，裸 ret 时 AL = 进入时 EAX 的低字节 = Self 指针低字节，
        // 即原生返回值本身是不确定的；此处取 true（消息判定为“已消费”）以免在 C# 侧
        // 造成重排队循环，与“不处理任何消息”的可证语义一致。
        public override bool Operate(TProcessMessage ProcessMsg)
        {
            return true;
        }

        // 战神 VMT+0x1E8 = sub_674D74 = `33 C0 C3`（xor eax,eax / ret）—— 恒返回 False，
        // 即本怪拒绝一切定时状态附加。基类实现是 0x772F84
        // (TBaseObject.TimedAbility.cs:474 已记录的同一地址)。
        internal override bool CanAddNativeTimedAbility(byte internalType)
        {
            return false;
        }

        // 战神 VMT+0x208 = sub_674E4C (69 字节) 全文：
        //   674E4C  55 8B EC 53              push ebp / mov ebp,esp / push ebx
        //   674E50  8B D8                    mov  ebx,eax            ; Self（edx 仍是 hiter，未被改写）
        //   674E52  8B C3 / E8 AF 93 0A 00   call 0x71E208           ; = TAnimal.Struck(hiter)
        //   674E59  E8 E2 34 D9 FF           call 0x408340           ; GetTickCount
        //   674E5E  8B D0                    mov  edx,eax
        //   674E60  2B 93 D8 04 00 00        sub  edx,[ebx+0x4D8]
        //   674E66  81 FA 10 27 00 00        cmp  edx,0x2710         ; 10000
        //   674E6C  72 20                    jb   0x674E8E           ; 无符号 -> 未满 10s 直接退出
        //   674E6E  89 83 D8 04 00 00        mov  [ebx+0x4D8],eax
        //   674E74  6A 16                    push 0x16               ; nParam1 = 22
        //   674E76  6A 00 6A 00 6A 00 6A 00  push 0 ×4               ; nParam2/nParam3/sMsg/末位
        //   674E7E  33 C9                    xor  ecx,ecx            ; wParam = 0
        //   674E80  66 BA 05 29              mov  dx,0x2905          ; = 10501 = RM_10501
        //   674E84  8B C3 / 8B 18            mov  eax,ebx / mov ebx,[eax]
        //   674E88  FF 93 D8 00 00 00        call [VMT+0xD8]         ; = SendRefMsg
        //   674E8E  5B 5D C3
        // 入参序标定：+0xD8 的栈参按 Delphi register 约定【左→右】压栈，由已移植的
        // TBigHeartMon 两处真值互证 —— 0x68105C 处 `push [x] / push [y] / 0 / 0 / 0`
        // 对应 BigHeartMonster.cs:20 SendRefMsg(RM_HIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "")，
        // 0x68101B 处 `push [x] / push [y] / 1 / 0 / 0` 对应 BigHeartMonster.cs:35
        // SendRefMsg(RM_10205, 0, x, y, 1, "")。故第 1 个压栈值就是 nParam1。
        public override void Struck(TBaseObject hiter)
        {
            base.Struck(hiter);
            // 原生只取一次 GetTickCount（eax 先比较后回写），这里保持同一取值。
            var dwTick = HUtil32.GetTickCount();
            if ((dwTick - n4D8) >= 10000)
            {
                n4D8 = dwTick;
                SendRefMsg(Grobal2.RM_10501, 0, 0x16, 0, 0, "");
            }
        }
    }
}

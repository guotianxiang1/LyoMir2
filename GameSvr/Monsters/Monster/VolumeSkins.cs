using SystemModule;

namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 248 = TVolumeSkins，VMT 0x660BEC，size 0x4F0(1264)，
    //    parent = TATMonster(0x65E7E4, size 0x4E8) —— 自有字段 = [+0x4E8] 与 [+0x4EC] 两个 dword。
    //    工厂跳表 sub_679F8C：索引表[248-0xB=0xED]=0x74=116 ; jt[116]=0x67AD1F ; case 全文：
    //      67AD1F  B2 01              mov  dl,1
    //      67AD21  A1 A0 0B 66 00     mov  eax,[0x660BA0]   ; classref(=VMT-0x4C) -> TVolumeSkins
    //      67AD26  E8 F1 CC FF FF     call 0x667A1C         ; TVolumeSkins.Create
    //      67AD2B  89 45 F8           mov  [ebp-8],eax
    //      67AD2E  EB 0F              jmp  0x67AD3F
    //    case 内无额外 RNG、无额外字段写。
    //
    // 构造器 sub_667A1C 全文：
    //   667A31  33 D2 / 8B C6 / E8 5E F0 FF FF   call 0x666A98         ; = TATMonster.Create
    //   667A3A  33 C0
    //   667A3C  89 86 E8 04 00 00                mov  [esi+0x4E8],eax  ; = 0
    //   667A42  B8 1E 00 00 00 / E8 00 C1 D9 FF  mov eax,0x1E / Random ; Random(30)
    //   667A4C  85 C0 / 75 0A                    test eax,eax / jne 跳过
    //   667A50  C7 86 E8 04 00 00 01 00 00 00    mov  dword [esi+0x4E8],1
    //   => m_nRebornLeft = (Random(30) == 0) ? 1 : 0 ；[+0x4EC] 由 NewInstance 清零。
    //   RNG 序：先父 ctor 的 Random(1500)(TATMonster)，再本 ctor 的 Random(30)。
    //
    // VMT 差异集（vs TATMonster）恰两槽：
    //   slot[33] +0x084 = Die  = 0x667A78 （父 = TAnimal.Die 0x71E2BC）
    //   slot[34] +0x088 = Run  = 0x667AB0 （父 = TATMonster.Run 0x666AE4）
    //
    // Die 覆写 sub_667A78 全文：
    //   667A80  E8 37 68 0B 00        call 0x71E2BC              ; inherited Die
    //   667A85  83 BB E8 04 00 00 00  cmp  dword [ebx+0x4E8],0
    //   667A8C  7E 16                 jle  0x667AA4              ; 有符号 <=0 则跳过
    //   667A8E  B8 05 00 00 00 / E8   mov eax,5 / call Random    ; Random(5)
    //   667A98  69 C0 E8 03 00 00     imul eax,eax,0x3E8         ; *1000
    //   667A9E  89 83 EC 04 00 00     mov  [ebx+0x4EC],eax       ; 复活延时(0..4 秒)
    //   667AA4  FF 8B E8 04 00 00     dec  dword [ebx+0x4E8]
    //   —— 注意 dec 在闸【外】，无论抽没抽延时都减一次。
    //
    // Run 覆写 sub_667AB0 全文：
    //   667AB7  call GetTickCount -> esi
    //   667AC0  2B 83 80 00 00 00     sub eax,[ebx+0x80]         ; tick - m_dwSearchTick
    //   667AC6  3B 43 7C / 76 0F      cmp eax,[ebx+0x7C] / jbe   ; > m_dwSearchTime (无符号)
    //   667ACB  89 B3 80 00 00 00     mov [ebx+0x80],esi         ; m_dwSearchTick = tick
    //   667AD5  E8 12 E3 0F 00        call 0x765DEC              ; SearchViewRange
    //   667ADA  80 7B 74 00 / 74 6B   cmp byte [ebx+0x74],0 / je ; 必须 m_boDeath
    //   667AE0  80 7B 73 00 / 75 65   cmp byte [ebx+0x73],0 / jne; 必须 !m_boGhost
    //   667AE6  83 BB E8 04 00 00 00 / 7C 5C   cmp [ebx+0x4E8],0 / jl ; 有符号 >=0
    //   667AEF  B2 1A / E8 68 AE 10 00 / 75 4F call 0x772960(dl=0x1A) / jne ; 状态 26 不得在身
    //   667AFC  83 BB 88 03 00 00 00 / 74 46   cmp [ebx+0x388],0 / je ; 可见对象链表头非空
    //   667B05  2B B3 30 03 00 00     sub esi,[ebx+0x330]        ; tick - m_dwDeathTick
    //   667B0B  3B B3 EC 04 00 00 / 72 38      cmp esi,[ebx+0x4EC] / jb ; 无符号 >= 延时
    //   667B13  8B 83 2C 01 00 00 / 50         push [ebx+0x12C]  ; nX
    //   667B1A  8B 83 30 01 00 00 / 50         push [ebx+0x130]  ; nY
    //   667B21  6A 00                          push 0            ; nRange
    //   667B23  6A 01                          push 1            ; nCount
    //   667B25  6A 00                          push 0            ; boMakeGenRecord
    //   667B27  A1 9C 5D 7D 00 / 8B 00         mov eax,[[0x7D5D9C]]   ; UserEngine 全局
    //   667B2E  B9 60 7B 66 00                 mov ecx,0x667B60  ; 字面量 "灵兽皮卷"
    //                                          ; (len 8 @0x667B5C, GBK C1E9 CADE C6A4 BEED)
    //   667B33  8B 93 28 01 00 00              mov edx,[ebx+0x128]    ; m_PEnvir
    //   667B39  E8 8E 42 01 00                 call 0x67BDCC     ; RegenMonster
    //   667B3E  FF 8B E8 04 00 00              dec dword [ebx+0x4E8]
    //   667B46  E8 15 05 10 00                 call 0x768060     ; MakeGhost
    //   667B4B  8B C3 / E8 92 EF FF FF         call 0x666AE4     ; inherited TATMonster.Run
    //
    // 偏移映射依据（均为仓库既有考证）：+0x73=m_boGhost / +0x74=m_boDeath
    // (TPlayObject.Base.cs 1852)、+0x330=m_dwDeathTick(TBaseObject.Base.cs 842)、
    // +0x128/+0x12C/+0x130 = m_PEnvir/m_nCurrX/m_nCurrY(Monster.cs 召回块)、
    // +0x7C/+0x80 = m_dwSearchTime/m_dwSearchTick(TMonster.Create 0x66613F/0x66615C)、
    // +0x388 = 可见对象链表头(0x6E244D 建链、0x66C3EE 沿 +0x10 遍历、结点 +0xC 是对象)
    // = C# m_VisibleActors，BigHeartMonster.cs:48 已用同一 `Count > 0` 写法；
    // sub_765DEC=SearchViewRange、sub_768060=MakeGhost、sub_772960=HasNativeActiveState、
    // sub_67BDCC=RegenMonster(引擎,地图,怪名,X,Y,范围,只数,建刷新点) —— 与
    // Monster.MakeClone 走的 RegenMonsterByName 同一入口。
    //
    // 语义：三十分之一的"皮卷怪"带一次复活额度。死后随机 0~4 秒、且身边还有可见对象时，
    //   在原地刷出一只【灵兽皮卷】，然后自己立刻变 ghost 收尾（不等 dwMakeGhostTime）。
    //   没抽中额度的（ctor 得 0）在 Die 里被 dec 成 -1，Run 的 `>=0` 闸就永远不成立。
    // fail-closed：VMT 差异只有这两槽，没有别的覆写；不臆造掉落/属性差异。
    public class VolumeSkins : AtMonster
    {
        /// <summary>战神 [+0x4E8]：剩余复活额度，见类头 ctor / Die / Run 三处。</summary>
        private int m_nRebornLeft;

        /// <summary>战神 [+0x4EC]：Die 里抽出的复活延时（毫秒），实例分配时为 0。</summary>
        private int m_dwRebornDelay;

        /// <summary>
        /// 战神 0x667B5C 的 Delphi 长字符串常量（长度 8，GBK 字节
        /// <c>C1 E9 CA DE C6 A4 BE ED</c>），复活时按名字刷怪。
        /// </summary>
        private const string NativeRebornMonName = "灵兽皮卷";

        public VolumeSkins() : base()
        {
            m_nRebornLeft = 0;
            if (M2Share.RandomNumber.Random(30) == 0)
            {
                m_nRebornLeft = 1;
            }
        }

        public override void Die()
        {
            base.Die();
            if (m_nRebornLeft > 0)
            {
                m_dwRebornDelay = M2Share.RandomNumber.Random(5) * 1000;
            }
            m_nRebornLeft--;
        }

        public override void Run()
        {
            var dwTick = HUtil32.GetTickCount();
            if ((dwTick - m_dwSearchTick) > m_dwSearchTime)
            {
                m_dwSearchTick = dwTick;
                SearchViewRange();
            }
            if (m_boDeath && !m_boGhost && m_nRebornLeft >= 0
                && !HasNativeActiveState(0x1A)
                && m_VisibleActors.Count > 0
                && (dwTick - m_dwDeathTick) >= m_dwRebornDelay)
            {
                M2Share.UserEngine.RegenMonsterByName(m_PEnvir, m_nCurrX, m_nCurrY,
                    NativeRebornMonName);
                m_nRebornLeft--;
                MakeGhost();
            }
            base.Run();
        }
    }
}

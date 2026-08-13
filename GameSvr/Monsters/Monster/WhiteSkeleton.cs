using SystemModule;

namespace GameSvr
{
    public class WhiteSkeleton : AtMonster
    {
        public bool m_boIsFirst = false;

        public WhiteSkeleton() : base()
        {
            m_boIsFirst = true;
            this.m_boFixedHideMode = true;
            this.m_nViewRange = 6;
        }

        // ✅ 战神字节证据 (Tier-1)：TWhiteSkeleton 覆写的是 **SearchTarget(vmt+0x08C)**，
        // 不是 RecalcAbilitys(vmt+0x0C8)。VMT 0x660E80(size 1264, parent TATMonster)
        // 覆写集只有两项：Run=0x667DB8、SearchTarget=0x667D48。
        // sub_667D48 逐字节：
        //   667D50  E8 1B 62 0B 00  call sub_71DF70   ; TAnimal.SearchTarget(基类)
        //   667D57  E8 04 00 00 00  call sub_667D60   ; = 本文件的 sub_4AAD54
        protected override bool SearchTarget()
        {
            var result = base.SearchTarget();
            sub_4AAD54();
            return result;
        }

        public override void Run()
        {
            if (m_boIsFirst)
            {
                m_boIsFirst = false;
                this.m_btDirection = 5;
                this.m_boFixedHideMode = false;
                this.SendRefMsg(Grobal2.RM_DIGUP, this.m_btDirection, this.m_nCurrX, this.m_nCurrY, 0, "");
                // ✅ 战神 Run sub_667DB8 的 [+0x4E8] 首次出现分支 @0x667E05
                // `E8 56 FF FF FF call sub_667D60` —— 出现后立刻跑一次 helper。
                // 原先 C# 此处漏掉了这一步（helper 只挂在 RecalcAbilitys 上），
                // 所以刚钻出地面的白骨的攻击/移动间隔停留在 MonInitialize 的值。
                sub_4AAD54();
            }
            base.Run();
        }

        private void sub_4AAD54()
        {
            // ✅ 战神 sub_667D60 逐字节 —— 注意 **先把 m_btSlaveMakeLevel 钳到 3**：
            //   667D66  8A 83 83 04 00 00  mov al,byte [ebx+0x483]   ; m_btSlaveMakeLevel
            //   667D6C  3C 03              cmp al,3
            //   667D6E  76 07              jbe 0x667D77               ; <=3 走原值
            //   667D70  B8 03 00 00 00     mov eax,3                  ; >3 一律当 3
            //   667D77  25 FF 00 00 00     and eax,0xFF
            //   667D7C  69 D0 58 02 00 00  imul edx,eax,0x258         ; *600
            //   667D82  B9 B8 0B 00 00     mov ecx,0xBB8              ; 3000
            //   667D87  2B CA              sub ecx,edx                ; 3000 - lvl*600
            //   667D89  mov [ebx+0x320],ecx                          ; m_nNextHitTime
            //   667D8F  69 C0 FA 00 00 00  imul eax,eax,0xFA          ; *250 (同一钳后值)
            //   667D95  BA B0 04 00 00     mov edx,0x4B0              ; 1200
            //   667D9A  2B D0              sub edx,eax                ; 1200 - lvl*250
            //   667D9C  mov [ebx+0x324],edx                          ; m_nWalkSpeed
            //   667DA2  call sub_408340 ; add eax,0x7D0              ; m_dwWalkTick=tick+2000
            // 少了这个钳位时 lvl>3 会让两个间隔变【负数】(lvl=5 → 3000-3000=0、1200-1250=-50)，
            // 负的 m_nWalkSpeed/m_nNextHitTime 会让 (now-tick)>interval 恒真 = 白骨无冷却。
            var lvl = this.m_btSlaveMakeLevel > 3 ? 3 : this.m_btSlaveMakeLevel;
            this.m_nNextHitTime = 3000 - lvl * 600;
            this.m_nWalkSpeed = 1200 - lvl * 250;
            this.m_dwWalkTick = HUtil32.GetTickCount() + 2000;
        }
    }
}


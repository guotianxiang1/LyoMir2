using SystemModule;

namespace GameSvr
{
    public class AnimalObject : TBaseObject
    {
        
        
        
        public int m_nNotProcessCount = 0;
        public short m_nTargetX = 0;
        public short m_nTargetY = 0;
        public bool m_boRunAwayMode = false;
        public int m_dwRunAwayStart = 0;
        public int m_dwRunAwayTime = 0;

        /// <summary>
        /// Native TAnimal <c>+0x48C</c>. TAnimal.Create initializes it to 10;
        /// TPlayer.MakeSlave overwrites it with the raw signed
        /// <c>hpAfterSlave</c> percentage.
        /// </summary>
        internal int m_nNativeHpAfterSlavePercent = 10;

        /// <summary>
        /// Native TAnimal <c>+0x450</c>, set when slave royalty expires.
        /// This offset is unrelated to the TPlayer sibling-layout revive tick.
        /// </summary>
        internal bool m_boNativeSlaveRoyaltyExpired;

        public virtual void Attack(TBaseObject TargeTBaseObject, byte nDir)
        {
            base.AttackDir(TargeTBaseObject, 0, nDir);
        }

        // 怪物 mover sub_71F0F4（VMT+0x30，TAnimal / TMonster / TAIMon / 卫士 / TFieldHero 共用）
        // 的边界比人形 sub_741224 松整整一格 —— MOVE-42，四个跳转逐条对照：
        //   0x71F141  test esi,esi        / jl fail  -> newX >= 0        （人形 0x741276 是 jle，> 0）
        //   0x71F14F  cmp esi,[eax+0x3C]  / jg fail  -> newX <= Width    （人形 0x741284 是 jge，< Width）
        //   0x71F158  test edi,edi        / jl fail  -> newY >= 0
        //   0x71F166  cmp edi,[eax+0x40]  / jg fail  -> newY <= Height
        // 于是怪物可以"尝试"第 0 行/列和 Width/Height，x==Width 那一格再由
        // MoveToMovingObject 自己的 [0,Width) 闸拒掉；净效果是怪物能站上第 0 行/列，玩家不能。
        // 原版注记明确要求"不要给两个 mover 共用一个边界 helper"，故此处单独 override。
        // 人形类（TPlayObject / HeroObject）继承自本类，必须各自把人形边界 override 回去。
        protected override bool WalkToInBounds(short nNX, short nNY)
        {
            return nNX >= 0 && nNX <= m_PEnvir.wWidth
                && nNY >= 0 && nNY <= m_PEnvir.wHeight;
        }

        public AnimalObject() : base()
        {
            m_nNotProcessCount = 0;
            m_nTargetX = -1;
            this.m_btRaceServer = Grobal2.RC_ANIMAL;
            // MONAI-01 — 原生 TAnimal.Create sub_71D828 是 tick **加** Random(3000)，不是减：
            //   0071D880  E8 BB AA CE FF        call 0x408340        ; GetTickCount -> esi
            //   0071D88D  B8 B8 0B 00 00        mov  eax,0xBB8       ; 3000
            //   0071D892  E8 B5 62 CE FF        call 0x403B4C        ; Random
            //   0071D897  03 C6                 add  eax,esi         ; <-- 加
            //   0071D899  89 87 5C 03 00 00     mov  [edi+0x35C],eax ; m_dwHitTick
            //   0071D89F  B8 B8 0B 00 00        mov  eax,0xBB8
            //   0071D8A4  E8 A3 62 CE FF        call 0x403B4C
            //   0071D8A9  03 C6                 add  eax,esi         ; <-- 加
            //   0071D8AB  89 87 84 03 00 00     mov  [edi+0x384],eax ; m_dwWalkTick
            // 消费端都是有符号比较（TMonster.Run 0x66632D `cmp ecx,[edx+0x324]` + `jle`；
            // AttackTarget 0x71E94B `cmp edx,[ebx+0x320]` + `jle`），所以 tick 在未来会让
            // elapsed 为负、比较恒不成立 —— 原版靠这个给每只新怪 0..3000ms 的随机出生错峰。
            // 写成减号则错峰消失，全服怪物出生即可走可打。
            this.m_dwHitTick = HUtil32.GetTickCount() + M2Share.RandomNumber.Random(3000);
            this.m_dwWalkTick = HUtil32.GetTickCount() + M2Share.RandomNumber.Random(3000);
            this.m_dwSearchEnemyTick = HUtil32.GetTickCount();
            m_boRunAwayMode = false;
            m_dwRunAwayStart = HUtil32.GetTickCount();
            m_dwRunAwayTime = 0;
            m_nNativeHpAfterSlavePercent = 10; // 0x71D8B7
            m_boNativeSlaveRoyaltyExpired = false; // 0x71D85F
        }

        internal override bool IsNativeMagic43Target(TPlayObject source)
        {
            // These C# classes map to native VMT+0x19C constant-false holders.
            // SuperGuard is the one flattened exception: native TSuperGuard is
            // a direct TAnimal child and inherits the accepting slot.
            if (this is TPlayObject || this is HeroObject ||
                this is TFieldHero || this is AiMon || this is SearchMon ||
                this is WalkMon || this is FoxBossMon ||
                this is FourteenYearBossMon ||
                this is WorldCupPreMatchMon || this is HuoSheMonster ||
                this is MirDotaMatchBossMon || this is KingFireDragon ||
                this is SuicideBat || this is FireCracker ||
                this is QingLong || this is BaiHu || this is ItemAttMon ||
                this is TimerBombMon || this is CreateBombMon ||
                (this is NormNpc && !(this is SuperGuard)))
            {
                return false;
            }

            // TAnimal.IsProperTarget slot @0x71F840, as called by skill 43.
            return source != null && !m_boDeath &&
                   !ReferenceEquals(this, source) && m_Master == null &&
                   m_btRaceServer > Grobal2.RC_ANIMAL &&
                   m_Abil.Level <= source.m_Abil.Level;
        }

        protected virtual void GotoTargetXY()
        {
            byte nDir;
            int n10;
            int n14;
            int n20;
            int nOldX;
            int nOldY;
            if (this.m_nCurrX != m_nTargetX || this.m_nCurrY != m_nTargetY)
            {
                n10 = m_nTargetX;
                n14 = m_nTargetY;
                nDir = Grobal2.DR_DOWN;
                if (n10 > this.m_nCurrX)
                {
                    nDir = Grobal2.DR_RIGHT;
                    if (n14 > this.m_nCurrY)
                    {
                        nDir = Grobal2.DR_DOWNRIGHT;
                    }
                    if (n14 < this.m_nCurrY)
                    {
                        nDir = Grobal2.DR_UPRIGHT;
                    }
                }
                else
                {
                    if (n10 < this.m_nCurrX)
                    {
                        nDir = Grobal2.DR_LEFT;
                        if (n14 > this.m_nCurrY)
                        {
                            nDir = Grobal2.DR_DOWNLEFT;
                        }
                        if (n14 < this.m_nCurrY)
                        {
                            nDir = Grobal2.DR_UPLEFT;
                        }
                    }
                    else
                    {
                        if (n14 > this.m_nCurrY)
                        {
                            nDir = Grobal2.DR_DOWN;
                        }
                        else if (n14 < this.m_nCurrY)
                        {
                            nDir = Grobal2.DR_UP;
                        }
                    }
                }
                nOldX = this.m_nCurrX;
                nOldY = this.m_nCurrY;
                this.WalkTo(nDir, false);
                n20 = M2Share.RandomNumber.Random(3);
                // MONAI-12 — GotoTargetXY sub_71DDD0 的转向重试是 7 次，不是 8：
                //   0071DE93  C7 45 F4 07 00 00 00  mov  [ebp-0xC],7
                //   0071DE9A  ...（循环体：仍在原地才改向再 WalkTo）
                //   0071DEDB  FF 4D F4              dec  [ebp-0xC]
                //   0071DEDE  75 BA                 jne  0x71DE9A
                // C# 原先 `for i = DR_UP..DR_UPLEFT`（0..7 含）多试一次方向。
                for (var i = 0; i < 7; i++)
                {
                    if (nOldX == this.m_nCurrX && nOldY == this.m_nCurrY)
                    {
                        if (n20 != 0)
                        {
                            nDir++;
                        }
                        else if (nDir > 0)
                        {
                            nDir -= 1;
                        }
                        else
                        {
                            nDir = Grobal2.DR_UPLEFT;
                        }
                        if (nDir > Grobal2.DR_UPLEFT)
                        {
                            nDir = Grobal2.DR_UP;
                        }
                        this.WalkTo(nDir, false);
                    }
                }
            }
        }

        public override bool Operate(TProcessMessage ProcessMsg)
        {
            if (ProcessMsg.wIdent == Grobal2.RM_STRUCK)
            {
                var struckObject = M2Share.ObjectManager.Get(ProcessMsg.nParam3);
                if (ProcessMsg.BaseObject == this.ObjectId && struckObject != null)
                {
                    this.SetLastHiter(struckObject);
                    Struck(struckObject);
                    this.BreakHolySeizeMode();
                    if (this.m_Master != null && struckObject != this.m_Master && struckObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        this.m_Master.SetPKFlag(struckObject);
                    }
                    if (M2Share.g_Config.boMonSayMsg)
                    {
                        this.MonsterSayMsg(struckObject, MonStatus.UnderFire);
                    }
                }
                return true;
            }
            return base.Operate(ProcessMsg);
        }

        public override void Run()
        {
            base.Run();
        }

        public virtual void Struck(TBaseObject Hiter)
        {
            byte btDir = 0;
            this.m_dwStruckTick = HUtil32.GetTickCount();
            if (Hiter != null)
            {
                if (this.m_TargetCret == null || this.GetAttackDir(this.m_TargetCret, ref btDir) || M2Share.RandomNumber.Random(6) == 0)
                {
                    if (this.IsProperTarget(Hiter))
                    {
                        this.SetTargetCreat(Hiter);
                    }
                }
            }
            if (this.m_boAnimal)
            {
                int newVal = this.m_nMeatQuality - M2Share.RandomNumber.Random(300);
                this.m_nMeatQuality = (ushort)(newVal < 0 ? 0 : newVal);
            }
            // Native TAnimalObject.Struck (M2Server sub_71E208 @0x0071E208): the hit-tick advance is GATED
            // on Level<50 (this binary compiles the stock ObjBase.pas:2678 "//if m_Abil.Level<50" LIVE:
            //   cmp si,32h / jnb ...   at 0x0071E291) and has NO timer clamp. The prior C#
            // "now + m_nNextHitTime" clamp was a non-native invention; restore the native gated add.
            if (this.m_Abil.Level < 50)
            {
                this.m_dwHitTick = this.m_dwHitTick + (150 - HUtil32._MIN(130, (int)this.m_Abil.Level * 4));
            }
        }

        protected void HitMagAttackTarget(TBaseObject TargeTBaseObject, int nHitPower, int nMagPower, bool boFlag)
        {
            int nDamage;
            TBaseObject BaseObject;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            this.m_btDirection = M2Share.GetNextDirection(this.m_nCurrX, this.m_nCurrY, TargeTBaseObject.m_nCurrX, TargeTBaseObject.m_nCurrY);
            this.m_PEnvir.GetBaseObjects(TargeTBaseObject.m_nCurrX, TargeTBaseObject.m_nCurrY, false, BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                BaseObject = BaseObjectList[i];
                if (this.IsProperTarget(BaseObject))
                {
                    nDamage = 0;
                    int physicalDamage = BaseObject.GetHitStruckDamage(this, nHitPower);
                    physicalDamage = BaseObject.ApplyNativePhysicalCritical(this, physicalDamage);
                    nDamage += physicalDamage;
                    nDamage += BaseObject.GetMagStruckDamage(this, nMagPower);
                    if (nDamage > 0)
                    {
                        BaseObject.StruckDamage(nDamage, this);
                        BaseObject.SendDelayMsg(Grobal2.RM_STRUCK, Grobal2.RM_10101, (ushort)nDamage, BaseObject.m_WAbil.HP, BaseObject.m_WAbil.MaxHP, this.ObjectId, "", 200);
                    }
                }
            }
            BaseObjectList.Clear();
            BaseObjectList = null;
            this.SendRefMsg(Grobal2.RM_HIT, this.m_btDirection, this.m_nCurrX, this.m_nCurrY, 0, "");
        }

        protected override void DelTargetCreat()
        {
            base.DelTargetCreat();
            m_nTargetX = -1;
            m_nTargetY = -1;
        }

        protected virtual bool SearchTarget()
        {
            TBaseObject BaseObject = null;
            TBaseObject BaseObject18 = null;
            int nC;
            var n10 = 999;
            for (var i = 0; i < this.m_VisibleActors.Count; i++)
            {
                BaseObject = this.m_VisibleActors[i].BaseObject;
                // MONAI-13 — sub_71DA70 扫描臂用 sub_772DA8 = `mov al,[eax+0x74]; ret`
                // （TBaseObject.cs 已钉 +0x74 = m_boDeath），不是 +0x73 ghost。
                if (!BaseObject.m_boDeath)
                {
                    if (this.IsProperTarget(BaseObject) && (!BaseObject.m_boHideMode || this.m_boCoolEye))
                    {
                        // 战神 sub_71DA70 @0x0071DA70: view-range box gate uses strictly-greater-than
                        // comparison (actors AT viewRange are included, beyond it are excluded).
                        // SPAWN-25: C# was missing this native range check entirely.
                        if (Math.Abs(this.m_nCurrX - BaseObject.m_nCurrX) > this.m_nViewRange
                            || Math.Abs(this.m_nCurrY - BaseObject.m_nCurrY) > this.m_nViewRange)
                            continue;
                        nC = Math.Abs(this.m_nCurrX - BaseObject.m_nCurrX) + Math.Abs(this.m_nCurrY - BaseObject.m_nCurrY);
                        if (nC < n10)
                        {
                            n10 = nC;
                            BaseObject18 = BaseObject;
                        }
                    }
                }
            }
            if (BaseObject18 != null)
            {
                this.SetTargetCreat(BaseObject18);
                // 0071DC04  C6 45 FB 01  mov byte [ebp-5],1  then 0071DCA8  8A 45 FB  mov al,[ebp-5]
                return true;
            }
            return false;
        }

        protected void sub_4C959C()
        {
            TBaseObject BaseObject;
            TBaseObject Creat = null;
            var n10 = 999;
            for (var i = 0; i < this.m_VisibleActors.Count; i++)
            {
                BaseObject = this.m_VisibleActors[i].BaseObject;
                if (BaseObject.m_boDeath)
                {
                    continue;
                }
                if (!this.IsProperTarget(BaseObject)) continue;
                var nC = Math.Abs(this.m_nCurrX - BaseObject.m_nCurrX) + Math.Abs(this.m_nCurrY - BaseObject.m_nCurrY);
                if (nC >= n10) continue;
                n10 = nC;
                Creat = BaseObject;
            }
            if (Creat != null)
            {
                this.SetTargetCreat(Creat);
            }
        }

        protected virtual void SetTargetXY(short nX, short nY)
        {
            m_nTargetX = nX;
            m_nTargetY = nY;
        }

        protected virtual void Wondering()
        {
            if (M2Share.RandomNumber.Random(20) != 0) return;
            if (M2Share.RandomNumber.Random(4) == 1)
            {
                this.TurnTo((byte)M2Share.RandomNumber.Random(8));
            }
            else
            {
                this.WalkTo(this.m_btDirection, false);
            }
        }
    }
}


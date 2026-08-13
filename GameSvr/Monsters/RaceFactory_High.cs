namespace GameSvr
{
    // ============================================================================
    // race-high 批：高段怪物 race 145..255 的工厂映射（AddCreature sub_679F8C 复刻）。
    //
    // ─ 工厂两级分派（VA 0x679F8C，flat_image.bin base 0x400000）─────────────────
    //   movzx eax,byte[monRec+0x14]      ; race
    //   add   eax,-0x0B                    ; race-11
    //   cmp   eax,0xEE / ja 0x67AE5E       ; >0xEE(=238) 越界 -> default sink（返回 nil）
    //   mov   al,byte[eax+0x67A026]        ; 索引表 race-11 -> group
    //   jmp   dword[eax*4+0x67A115]        ; 跳表 group -> handler
    //   每个 handler 形如 `mov dl,1 / mov eax,[classref] / call ctor / mov [ebp-8],eax
    //   / jmp 0x67AD3F(公共尾部)`。default(group0)=0x67AE5E=`xor eax,eax`->nil，
    //   即“该怪根本不出现”。公共尾部 0x67AD3F 是 race 无关的 AddCreature 收尾
    //   （置 m_PEnvir/坐标/m_btDirection=Random(8) 等），已在 UserEngine.AddBaseObject
    //   的 `if(Cert!=null){...}` 段实现，故本批**不复刻尾部**。
    //   race→classref→ctor→VMT 主表见 staging/_gp3/wd1_races.txt、q6_factory.txt；
    //   本轮以 staging/_gp3/rc_vmt.py 逐类复算 chain/parent/slot-diff（vmtParent@-36、
    //   vmtInstanceSize@-40、vmtClassName@-44），产物 race_high_audit.txt / race_high_deep.txt。
    //
    // ─ 挂钩法（集成方在 UsrEngn.cs:2800 处并列加一行，本文件绝不改 UsrEngn.cs）──────
    //   现状 UserEngine.AddBaseObject（UsrEngn.cs:2800）：
    //       TryCreateRaceA(nMonRace, out Cert);
    //       if (Cert == null) switch (nMonRace) { ... }
    //   集成方在 `TryCreateRaceA(...)` 之后、switch 之前插入并列的一行：
    //       if (Cert == null) TryCreateRaceHigh(nMonRace, out Cert);
    //   命中即认领；未命中回落既有 switch，对既有 race 行为零影响。本批**只认领新类**，
    //   已在 switch 里接好的 8 个 race（145/150/152/175/181/247/248/249）一律不重复认领。
    //
    // ─ 铁律：有据不臆造；父类未移植 / 覆写落在 C# 无可覆写入口 / 依赖未命名新字段 →
    //   fail-closed（建类+已证 ctor+覆写留证据注释；父类没移植的连类都不建）。
    //   已考证的 slot→C# 虚方法字典（正偏移，槽号=偏移/4；由已合入类交叉标定）：
    //     +0x018(6)=Operate  +0x078(30)=Initialize  +0x084(33)=Die  +0x088(34)=Run
    //     +0x1E8(122)=CanAddNativeTimedAbility(byte)  +0x204(129)=Attack  +0x208(130)=Struck
    //     +0x1EC(123)=AddState  +0x1FC(127)=散金转发(C# NativeScatterGoldCapped)  +0x0D8=SendRefMsg
    //   fail-closed 常见槽（C# 非虚 / 无入口）：+0x0B8 推挤谓词、+0x0C8 状态施加、
    //     +0x1B4 受击伤害选择器、+0x19C/+0x1A0/+0x1A4 属性虚槽、+0x090/+0x094/+0x0B0/+0x200
    //     以及一切 parent 之上的“新增虚槽(131+)”。
    // ============================================================================
    //
    // ================== A. 已认领并新建（父类已移植，本批产出 .cs）==================
    //   race 149  FireCracker          : AnimalObject        (TAnimal,   ctor 0x66BBC8)
    //   race 151  HolyMonster          : AtMonster           (TATMonster,ctor 0x66C2AC)
    //   race 170  HolyMonster          : （与 151 同类同 VMT 0x663060，复用）
    //   race 153  SuicideBatEx         : SuicideBat          (TSuicideBat,ctor 0x66BAE8)
    //   race 155  SuperKingFireDragon  : KingFireDragon      (TKingFireDragon,共享 ctor 0x66B658)
    //   race 157  MonSingleMagFox      : AtMonster           (TATMonster,ctor=TATMonster.Create)
    //   race 159  TimerBombMon         : AnimalObject        (TAnimal, ctor=TAnimal.Create)
    //   race 160  CreateBombMon        : SoccerBall          (TSoccerBall,ctor 0x6829D0)
    //   race 167  SuperSkeleton        : WhiteSkeleton       (TWhiteSkeleton,共享 ctor 0x667CE8)
    //   race 174  FoxBossMon           : AnimalObject        (TAnimal, ctor=TAnimal.Create)
    //   race 178  QingLong             : AtMonster           (TATMonster,ctor=TATMonster.Create)
    //   race 179  BaiHu                : AtMonster           (TATMonster,ctor=TATMonster.Create)
    //   race 233  ItemAttMon           : Monster             (TMonster, ctor=TMonster.Create)
    //   race 236  WorldCupPreMatchMon  : AnimalObject        (TAnimal, ctor 0x6688FC)
    //   race 242  ElementMon           : Monster             (TMonster, ctor 0x668F78)
    //   race 243  FourteenYearBossMon  : AnimalObject        (TAnimal, ctor 0x6680EC)
    //   race 244  MirDotaMatchBossMon  : AnimalObject        (TAnimal, ctor 0x669428)
    //   race 245  HuoSheMonster        : AnimalObject        (TAnimal, ctor 0x6689AC)
    //   （各类的 classref 唯一加载点 + ctor 唯一 E8 调用者见各自 .cs 头；case body 均为
    //    纯 `call ctor / jmp 尾部`，无额外 RNG/字段写——已在 deep dump 逐条核对。）
    //
    // ================== B. fail-closed：父类未在 C# 移植（连类都不建）================
    //   ⛔ parent = TSearchMon（VMT 0x66D320，parent TAnimal，143 槽，比 TAnimal 多 slot131-142
    //      共 12 个新虚槽；C# grep 无 class SearchMon/TSearchMon）及其派生：
    //        146 TKingOfIceMon   147 TSnowHuman     148 TPowerIceMan
    //        154 TKingOfIceMonBB (←TKingOfIceMon)
    //        161 TKingOfBlackFox 162 TKingOfRedFox  163 TKingOfWhiteFox  164 TStoneOfFoxSoul
    //        165 TFoxMoonEyeMon  166 TKingOfFoxMoon 171 TBlackFox 172 TRedFox 173 TWhiteFox
    //        176 TPhysicalImmuneMon 177 TMagicImmuneMon           （以上 parent=TFoxMoonMonster←TSearchMon）
    //        183 TPanJunLeader 184 TEvilMaster 185 TPanJunSuperMaster 186 TPanJunWarrior
    //        187 TPanJunSuperMaster2 188 TBogMon                  （parent=TBlackFox/TRedFox/…←TFoxMoonMonster）
    //        189 TBogPlagueFang 190 TBogPlagueToad 191 TBogPlagueHerald（parent=TBogPlagueMon←TSearchMon）
    //   ⛔ parent = TAIMon（VMT 0x719CF4，parent TAnimal，AI/英雄引擎基类；C# grep 无 class AIMon）：
    //        156 TYueLing  168 THuoLing  182 TSuperHuoLing(←THuoLing)
    //   ⛔ parent = TCastleFlag（VMT 0x67F79C，parent TAnimal；C# grep 无 class CastleFlag）：
    //        180 TNormalCastleFlag
    //   处置：先把 TSearchMon / TAIMon / TCastleFlag（各自新字段+新虚槽）移植为 C# 基类后，
    //         才能补这些子类。本轮不建 .cs、不接线，宁缺毋滥。
    //
    // ================== C. 跳过：已存在且已在 UsrEngn.cs switch 认领 ================
    //   145 AttackIceTower(3029) 150 FireKingMonster(3079) 152 NoWinerAnimal(3091)
    //   175 StoneFoxBossMon(3113) 181 StoneMonster(3129) 247 ParalyzationMon(3121)
    //   248 VolumeSkins(3136) 249 GoldbarPig(3143)   —— 本 High 批不再认领，避免抢占既有行为。
    //
    // ================== D. 无专类（原生即落 default sink，返回 nil）=================
    //   230：group 108 的 handler 直接就是公共尾部 0x67AD3F（无 `mov eax,[classref]/call ctor`），
    //        [ebp-8] 恒为 0 → Cert=nil，与 default 等效，无怪可建。
    //   158 169 192-229 231 232 234 235 237-241 246：group 0 → 0x67AE5E → nil。
    //   250-255：race-11 = 0xEF..0xF4 > 0xEE，越界 → 0x67AE5E → nil（跳表根本不含）。
    // ============================================================================
    public partial class UserEngine
    {
        // 返回 true 表示本批认领并已构造该 race 的实例（含原生 case body 的 ctor 后逻辑；
        // 本批所有 case body 均无 ctor 后逻辑）。未认领返回 false，交回既有 switch。
        private bool TryCreateRaceHigh(int nMonRace, out TBaseObject cert)
        {
            cert = null;
            switch (nMonRace)
            {
                // race 149 TFireCracker : AnimalObject —— 见 Monster/FireCracker.cs
                case 149:
                    cert = new FireCracker();
                    break;

                // race 151 & 170 THolyMonster : AtMonster（同一 VMT 0x663060）—— 见 Monster/HolyMonster.cs
                case 151:
                case 170:
                    cert = new HolyMonster();
                    break;

                // race 153 TSuicideBatEx : SuicideBat —— 见 Monster/SuicideBatEx.cs
                case 153:
                    cert = new SuicideBatEx();
                    break;

                // race 155 TSuperKingFireDragon : KingFireDragon —— 见 Monster/SuperKingFireDragon.cs
                case 155:
                    cert = new SuperKingFireDragon();
                    break;

                // race 157 TMonSingleMagFox : AtMonster —— 见 Monster/MonSingleMagFox.cs
                case 157:
                    cert = new MonSingleMagFox();
                    break;

                // race 159 TTimerBombMon : AnimalObject —— 见 Monster/TimerBombMon.cs
                case 159:
                    cert = new TimerBombMon();
                    break;

                // race 160 TCreateBombMon : SoccerBall —— 见 Monster/CreateBombMon.cs
                case 160:
                    cert = new CreateBombMon();
                    break;

                // race 167 TSuperSkeleton : WhiteSkeleton —— 见 Monster/SuperSkeleton.cs
                case 167:
                    cert = new SuperSkeleton();
                    break;

                // race 174 TFoxBossMon : AnimalObject —— 见 Monster/FoxBossMon.cs
                case 174:
                    cert = new FoxBossMon();
                    break;

                // race 178 TQingLong : AtMonster —— 见 Monster/QingLong.cs
                case 178:
                    cert = new QingLong();
                    break;

                // race 179 TBaiHu : AtMonster —— 见 Monster/BaiHu.cs
                case 179:
                    cert = new BaiHu();
                    break;

                // race 233 TItemAttMon : Monster —— 见 Monster/ItemAttMon.cs
                case 233:
                    cert = new ItemAttMon();
                    break;

                // race 242 TElementMon : Monster —— 见 Monster/ElementMon.cs
                case 242:
                    cert = new ElementMon();
                    break;

                // race 236 TWorldCupPreMatchMon : AnimalObject —— 见 Monster/WorldCupPreMatchMon.cs
                case 236:
                    cert = new WorldCupPreMatchMon();
                    break;

                // race 243 TFourteenYearBossMon : AnimalObject —— 见 Monster/FourteenYearBossMon.cs
                case 243:
                    cert = new FourteenYearBossMon();
                    break;

                // race 244 TMirDotaMatchBossMon : AnimalObject —— 见 Monster/MirDotaMatchBossMon.cs
                case 244:
                    cert = new MirDotaMatchBossMon();
                    break;

                // race 245 THuoSheMonster : AnimalObject —— 见 Monster/HuoSheMonster.cs
                case 245:
                    cert = new HuoSheMonster();
                    break;
            }
            return cert != null;
        }
    }
}

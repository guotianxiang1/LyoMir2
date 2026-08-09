// NativeHeroJobAbilityCurve — 玩家英雄三个职业子类的 VMT+0x2B8 能力成长曲线。
//
// 权威二进制: D:/loym2/staging/M2Server_reunpacked_20260803.exe (ImageBase 0x400000)
// 完整逐字节证据: staging/herojobs_fix_20260804.md
//
// ===========================================================================
// 这是什么
// ===========================================================================
// THeroAct 及其三个职业子类 TWarHero/TMagHero/TTaosHero 的 VMT 只有 9 个槽不同
// (+0x298..+0x2B8),其中 +0x2B8 是【能力成长曲线】:一个纯函数,把等级映射成
// 能力块 (actor+0x1E8) 里的 MaxHP/MaxMP/三种负重/DC/MC/SC/AC/MAC。
//
//   VMT 槽    实现              类
//   +0x2B8   sub_692618        TWarHero      (VMT 0x685968)
//   +0x2B8   sub_69367C        TTaosHero     (VMT 0x685CA0)
//   +0x2B8   sub_694BF0        TMagHero      (VMT 0x685FD8)
//   +0x2B8   sub_69036C        THeroAct 基类  = 单字节 `C3` 裸 ret(空桩)
//
// 三个子类的函数体第一条就显式调用基类空桩 (@0x69262D / @0x693691 / @0x694C05),
// 是真实指令但无副作用 —— 这里照抄为注释,不需要建模。
//
// ===========================================================================
// 谁调它:驱动 sub_690300 = THeroAct VMT+0x2C(能力初始化槽)
// ===========================================================================
// sub_690300 全镜像【零个】直接调用者 (扫遍 CODE 段所有 E8 rel32);它的 7 处数据引用
// 全是 VMT 槽: 0x68565C/0x685994/0x685CCC/0x686004 (THeroAct + 三子类)
// 以及 0x5F55D4/0x5F5910/0x5F5C50 (三个 TSec*Hero)。
// 0x68565C - 0x685630 = 0x2C ⇒ 它就是 VMT+0x2C,三个子类原样继承。
// 活的调用点: 0x687218、0x687E61、0x687F47 (分别在 THeroAct VMT+0x240 与 +0x078 里),
// 每处都是 `call [edx+0x2C]` 紧跟 `call [edx+0x8C]`,前面是
// `movzx edx, word [ebx+0x1FC]` → `call sub_6884C0` → `mov [ebx+0x244], eax`,
// 即等级驱动的重算。所以这条路是活的,不是死代码。
//
// sub_690300 逐字节 (0x690300-0x690368):
//   690305  lea  esi, [eax+0x1E8]              ; 能力块
//   69030B  mov  edx, dword ptr [eax+0x128]    ; 【地图对象】,不是武器记录
//   690311  test edx,edx / je 0x690357         ; 无地图 -> 不加盖
//   690315  cmp  word [edx+0xC0], 0 / jbe 0x690357
//   69031F  mov  ecx, dword ptr [eax+0x68C]    ; 主人玩家
//   690325  mov  cx,  word ptr [ecx+0x278]     ; 主人等级
//   69032C  cmp  cx,  word ptr [edx+0xBE] / jbe 0x690357
//   690335  mov  cx,  word ptr [esi+0x14]      ; 英雄等级
//   690339  cmp  cx,  word ptr [edx+0xC0] / jbe 0x690357
//   690342  mov  cx,  word ptr [edx+0xC0]      ; ECX(lo) = 地图英雄等级上限
//   690349  mov  dx,  word ptr [esi+0x14]      ; EDX(hi) = 英雄真实等级
//   69034F  call dword ptr [ebx+0x2B8]         ; 加盖调用
//   690357  mov  dx,  word ptr [esi+0x14]      ; 常态: hi = lo = 英雄等级
//   69035B  mov  ecx, edx
//   69035F  call dword ptr [ebx+0x2B8]         ; 不加盖调用
//
// ⚠ [+0x128] 是【地图】(TEnvirnoment),不是武器记录 —— 所以它紧邻已证实的
// [+0x12C]=CurrX / [+0x130]=CurrY。两个地图字段由地图配置 token 解析器写入:
//   [map+0xBE] = LIMITPLAYERLEVEL  (`mov edx,0x775EF4` @0x7757D2,串在 0x775EF4;
//                写入 `mov [ebx+0xBE],ax` @0x77580A,清零 @0x77581E)
//   [map+0xC0] = LIMITHEROLEVEL    (`mov edx,0x775F10` @0x775831,串在 0x775F10;
//                写入 @0x775869)
// 同一解析块里的兄弟 token 佐证该区域就是地图旗标解析器: CRAZYBREAKLEVEL /
// AUTORELIVE / NOEQUIPRELIVE / NOC2C / NOHERO / DREAMCASTLEMAP / UNIFIEDLEVEL /
// LIMITPLAYERLEVEL / LIMITHEROLEVEL / NOMAGIC / TRIGGERBOMB。
// 故 (hi, lo) 的真实语义是:两者都等于英雄等级,除非所在地图设了 LIMITHEROLEVEL
// (且主人等级高于 LIMITPLAYERLEVEL、英雄等级高于该上限),此时 lo 变成地图上限。
// 这是【地图对成长曲线的等级封顶】,与武器毫无关系。
//
// ⚠ 寄存器命名陷阱: 三个函数体的序言绑定【不一样】——
//   TWarHero  @0x692621  mov esi,ecx / mov edi,edx   => si = lo, di = hi
//   TTaosHero @0x693685  mov edi,ecx / mov esi,edx   => di = lo, si = hi
//   TMagHero  @0x694BF9  mov edi,ecx / mov esi,edx   => di = lo, si = hi
// 战士从 si 取 HP/MP、道士法师从 di 取 —— 但因为绑定相反,三者取的都是【lo】(被封顶轴),
// 负重带取的都是 hi。把 si/di 当成三个函数里含义相同就会得出"每个职业封顶的属性不同"
// 的错误结论,并让其中两职业的封顶轴反掉。
//
// ===========================================================================
// 舍入与钳位
// ===========================================================================
// sub_403574 = `sub esp,8 / fistp qword [esp] / wait / pop eax / pop edx / ret`
//   —— 默认控制字下的 fistp = 【银行家舍入】(half-to-even)。C# 侧 HUtil32.Round
//   (SystemModule/HUtil32.cs:125, MidpointRounding.ToEven) 已经一致。
// sub_4C7004 = Max: `cmp edx,eax / jl +2 / mov eax,edx / ret`
// sub_4C700C = Min: `cmp edx,eax / jg +2 / mov eax,edx / ret`
// 三种负重都 Min 钳到 0xFFDC = 65500。
//
// ⚠ MaxMP 里的 2.2 是【80 位 x87 扩展精度】常量,用 `fld xword` (DB 2D) 载入,
// 不是 `fmul dword`: TMagHero @0x694C8F 读 [0x694DD8]、TTaosHero @0x693715 读
// [0x693874],两处原始字节都是 `cd cc cc cc cc cc cc 8c 00 40` = 2.2。
// 按 float32 读这段池子会解成垃圾 (-107374184.0) 从而把 2.2 整个漏掉,
// 那样法师/道士的 MaxMP 会差 2.2 倍。
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 玩家英雄三职业(战/法/道)的 VMT+0x2B8 成长曲线,纯函数。
    /// 职业字节取值来自工厂 switch @0x6521FD 的 <c>record[+0x22]</c>:
    /// 0=TWarHero、1=TMagHero、2=TTaosHero(与 M2Share.jWarr/jWizard/jTaos 一致)。
    /// </summary>
    public static class NativeHeroJobAbilityCurve
    {
        /// <summary>三种负重共同的 Min 钳位上限 0xFFDC(= 65500)。</summary>
        public const int WeightCap = 0xFFDC;

        /// <summary>
        /// 一次 +0x2B8 调用写出的全部能力值。字段名对应能力块偏移
        /// (+0x4C MaxHP、+0x54 MaxMP、+0x64/+0x6C/+0x74 三负重、
        /// +0x28/+0x2C DC、+0x30/+0x34 MC、+0x38/+0x3C SC、
        /// +0x18/+0x1C AC、+0x20/+0x24 MAC)。
        /// </summary>
        public readonly struct Result
        {
            public int MaxHp { get; init; }
            public int MaxMp { get; init; }
            public int MaxWeight { get; init; }
            public int MaxWearWeight { get; init; }
            public int MaxHandWeight { get; init; }
            public int DcLow { get; init; }
            public int DcHigh { get; init; }
            public int McLow { get; init; }
            public int McHigh { get; init; }
            public int ScLow { get; init; }
            public int ScHigh { get; init; }
            public int AcLow { get; init; }
            public int AcHigh { get; init; }
            public int MacLow { get; init; }
            public int MacHigh { get; init; }
        }

        /// <summary>
        /// 原版 <c>sub_690300</c>(THeroAct VMT+0x2C)算出的 (hi, lo) 对。
        /// 常态两者都是英雄等级;只有当地图 LIMITHEROLEVEL 生效时 lo 变成地图上限。
        /// <paramref name="mapHeroLevelLimit"/> / <paramref name="mapPlayerLevelLimit"/>
        /// 对应 [map+0xC0] / [map+0xBE];C# 侧尚无这两个地图字段,故默认 0 = 未设置,
        /// 走 0x690357 的不加盖分支(与当前 C# 行为一致,不发明封顶)。
        /// </summary>
        public static (int Hi, int Lo) ResolveLevelPair(int heroLevel,
            int masterLevel, int mapHeroLevelLimit = 0, int mapPlayerLevelLimit = 0)
        {
            // 690315: cmp word [map+0xC0],0 / jbe -> 未设 LIMITHEROLEVEL 就不加盖
            // 69032C: 主人等级必须【高于】 LIMITPLAYERLEVEL
            // 690339: 英雄等级必须【高于】 LIMITHEROLEVEL
            if (mapHeroLevelLimit > 0 &&
                masterLevel > mapPlayerLevelLimit &&
                heroLevel > mapHeroLevelLimit)
            {
                // 690342/690349: lo = 地图上限, hi = 英雄真实等级
                return (heroLevel, mapHeroLevelLimit);
            }

            // 690357/69035B: hi = lo = 英雄等级
            return (heroLevel, heroLevel);
        }

        /// <summary>
        /// 按职业字节派发到对应子类曲线。职业字节即工厂 @0x652208 读的
        /// <c>record[+0x22]</c>;>=3 时原版 @0x652215 直接不建对象,故此处抛错
        /// 而不是静默回落到某个职业。
        /// </summary>
        public static Result Calculate(int job, int hi, int lo) => job switch
        {
            0 => CalculateWarrior(hi, lo),   // TWarHero  sub_692618
            1 => CalculateWizard(hi, lo),    // TMagHero  sub_694BF0
            2 => CalculateTaoist(hi, lo),    // TTaosHero sub_69367C
            _ => throw new ArgumentOutOfRangeException(nameof(job), job,
                "native factory @0x652215 creates NO hero for job >= 3"),
        };

        /// <summary>
        /// <c>TWarHero.+0x2B8</c> = <c>sub_692618</c> (0x692618-0x6927E5)。
        /// 浮点池 @0x6927E8: 3.5 / 2.0 / 10.0 / 20.0 / 3.0 / 13.0(全为 float32)。
        /// </summary>
        public static Result CalculateWarrior(int hi, int lo)
        {
            // 69262D  call sub_69036C   ; 基类空桩 `C3`,无副作用
            int maxHp, maxMp;

            // 69263A  cmp si,0xC8 / jbe 0x692665
            if (lo > 200)
            {
                int e = lo - 200;
                // 692649  imul edx,eax,0xE2 / add edx,0x5C4E
                maxHp = e * 226 + 23630;
                // 692658  shl eax,2 / add eax,0x2C7
                maxMp = e * 4 + 711;
            }
            else
            {
                // 69266E  fmul [0x6927E8]=3.5 -> Round -> add 0xB
                maxMp = HUtil32.Round(lo * 3.5) + 11;
                // 692688  fdiv 2.0 / fadd 10.0 / fdiv 20.0 / faddp / fmulp -> Round -> add 0x32
                maxHp = HUtil32.Round((lo / 2.0 + 10.0 + lo / 20.0) * lo) + 50;
                // 6926BB  cmp si,0x3C / jbe ; 6926C7 lea eax,[eax+eax*2] = *3 ; 6926CA SUB
                if (lo > 60)
                    maxHp -= 3 * (lo - 60);
            }

            // 6926D6/692705/692734  fdiv 3.0 / 20.0 / 13.0, 各自 Round + 50/15/12, Min 0xFFDC
            int maxWeight = Math.Min(HUtil32.Round(hi / 3.0 * hi) + 50, WeightCap);
            int maxWearWeight = Math.Min(HUtil32.Round(hi / 20.0 * hi) + 15, WeightCap);
            int maxHandWeight = Math.Min(HUtil32.Round(hi / 13.0 * hi) + 12, WeightCap);

            // 692764  div ecx=5(无符号)
            int d5 = lo / 5;

            return new Result
            {
                MaxHp = maxHp,
                MaxMp = maxMp,
                MaxWeight = maxWeight,
                MaxWearWeight = maxWearWeight,
                MaxHandWeight = maxHandWeight,
                // 692775/692784  Max(d5-1, 1) / Max(d5, 1) —— 注意下限是 1(法/道是 0)
                DcLow = Math.Max(d5 - 1, 1),
                DcHigh = Math.Max(d5, 1),
                // 692789-6927A2  MC/SC/CC 全部清零
                McLow = 0, McHigh = 0,
                ScLow = 0, ScHigh = 0,
                // 6927A7  ab[+0x18]=0 ; 6927B6  ab[+0x1C]= lo/7 (div ecx=7 @0x6927B4)
                AcLow = 0,
                AcHigh = lo / 7,
                // 6927BB/6927C0  MAC 清零
                MacLow = 0, MacHigh = 0,
            };
        }

        /// <summary>
        /// <c>TMagHero.+0x2B8</c> = <c>sub_694BF0</c> (0x694BF0-0x694DCA)。
        /// float32 池 @0x694DCC: 15.0 / 5.0 / 2.0 / … / 100.0 / 90.0;
        /// 另有 80 位扩展常量 <c>xword [0x694DD8] = 2.2</c>(`fld` @0x694C8F)。
        /// </summary>
        public static Result CalculateWizard(int hi, int lo)
        {
            // 694C05  call sub_69036C   ; 基类空桩
            int maxHp, maxMp;

            // 694C12  cmp di,0xC8 / jbe 0x694C3D
            if (lo > 200)
            {
                int e = lo - 200;
                // 694C21  imul edx,eax,0x3E / add edx,0x1EED
                maxHp = e * 62 + 7917;
                // 694C2D  imul eax,eax,0xB4 / add eax,0x483D
                maxMp = e * 180 + 18493;
            }
            else
            {
                // 694C46  fdiv 15.0 / fadd 5.0 / fmulp -> Round -> add 0x32
                maxHp = HUtil32.Round((lo / 15.0 + 5.0) * lo) + 50;
                // 694C68  cmp di,0x3C / jbe ; 694C74 imul eax,eax,0x1E = *30 ; 694C77 ADD
                if (lo > 60)
                    maxHp += 30 * (lo - 60);
                // 694C83  fdiv 5.0 / fadd 2.0 / fld xword 2.2 / fmulp / fmulp -> Round -> add 0xD
                maxMp = HUtil32.Round((lo / 5.0 + 2.0) * 2.2 * lo) + 13;
            }

            // 694CB6/694CE5/694D14  fdiv 5.0 / 100.0 / 90.0, Round + 50/15/12, Min 0xFFDC
            int maxWeight = Math.Min(HUtil32.Round(hi / 5.0 * hi) + 50, WeightCap);
            int maxWearWeight = Math.Min(HUtil32.Round(hi / 100.0 * hi) + 15, WeightCap);
            // 0x694D11 fild / 0x694D14 fdiv 90.0 / fild / fmulp / 0x694D25 @ROUND
            // / 0x694D2A add eax,0x0C. The quotient stays in an 80-bit register,
            // so a double chain double-rounds; /90 diverges at hi 105 and 795.
            // /5 and /100 above are provably tie-free, hence plain Round.
            int maxHandWeight = Math.Min(
                HUtil32.RoundDivMulExtended(hi, 90) + 12, WeightCap);

            // 694D44  div ecx=7
            int d7 = lo / 7;

            return new Result
            {
                MaxHp = maxHp,
                MaxMp = maxMp,
                MaxWeight = maxWeight,
                MaxWearWeight = maxWearWeight,
                MaxHandWeight = maxHandWeight,
                // 694D4D/694D5C  Max(d7-1, 0) / Max(d7, 1) —— 下限 0(`xor edx,edx` @0x694D4B)
                DcLow = Math.Max(d7 - 1, 0),
                DcHigh = Math.Max(d7, 1),
                // 694D69/694D78  MC 与 DC 同式
                McLow = Math.Max(d7 - 1, 0),
                McHigh = Math.Max(d7, 1),
                // 694D82-694DA5  SC/CC/AC/MAC 全清零
                ScLow = 0, ScHigh = 0,
                AcLow = 0, AcHigh = 0,
                MacLow = 0, MacHigh = 0,
            };
        }

        /// <summary>
        /// <c>TTaosHero.+0x2B8</c> = <c>sub_69367C</c> (0x69367C-0x693865)。
        /// float32 池 @0x693868: 6.0 / 10.0 / 8.0 / … / 4.0 / 50.0 / 42.0;
        /// 另有 80 位扩展常量 <c>xword [0x693874] = 2.2</c>(`fld` @0x693715)。
        /// </summary>
        public static Result CalculateTaoist(int hi, int lo)
        {
            // 693691  call sub_69036C   ; 基类空桩
            int maxHp, maxMp;

            // 69369E  cmp di,0xC8 / jbe 0x6936C5
            if (lo > 200)
            {
                int e = lo - 200;
                // 6936AD  imul eax,eax,0x6E = *110,复用同一个乘积
                int scaled = e * 110;
                // 6936B2  add edx,0x3419 -> MaxHP ; 6936BB add eax,0x2B05 -> MaxMP
                maxHp = scaled + 13337;
                maxMp = scaled + 11013;
            }
            else
            {
                // 6936CE  fdiv 6.0 / fadd 10.0 / fmulp -> Round -> add 0x32
                maxHp = HUtil32.Round((lo / 6.0 + 10.0) * lo) + 50;
                // 6936F0  cmp di,0x3C / jbe ; 6936FC mov edx,eax / shl eax,5 / add eax,edx
                //         = *33(不是 *32:原值被 edx 保存后加回) ; 693703 ADD
                if (lo > 60)
                    maxHp += 33 * (lo - 60);
                // 69370F  fdiv 8.0 / fld xword 2.2 / fmulp / fmulp -> Round -> add 0xD
                maxMp = HUtil32.Round(lo / 8.0 * 2.2 * lo) + 13;
            }

            // 69373C/69376B/69379A  fdiv 4.0 / 50.0 / 42.0, Round + 50/15/12, Min 0xFFDC
            int maxWeight = Math.Min(HUtil32.Round(hi / 4.0 * hi) + 50, WeightCap);
            // 0x693768 fild / 0x69376B fdiv 50.0 / fild / fmulp / 0x69377C @ROUND
            // / 0x693781 add eax,0x0F. Extended-precision quotient; /50 diverges
            // at hi 55/415/805/855/905. /4 and /42 are provably tie-free.
            int maxWearWeight = Math.Min(
                HUtil32.RoundDivMulExtended(hi, 50) + 15, WeightCap);
            int maxHandWeight = Math.Min(HUtil32.Round(hi / 42.0 * hi) + 12, WeightCap);

            // 6937CA  div ecx=7
            int d7 = lo / 7;
            // 69382E  div ecx=6
            int d6 = lo / 6;

            return new Result
            {
                MaxHp = maxHp,
                MaxMp = maxMp,
                MaxWeight = maxWeight,
                MaxWearWeight = maxWearWeight,
                MaxHandWeight = maxHandWeight,
                // 6937D3/6937E2  Max(d7-1, 0) / Max(d7, 1)
                DcLow = Math.Max(d7 - 1, 0),
                DcHigh = Math.Max(d7, 1),
                // 6937EC/6937F1  MC 清零
                McLow = 0, McHigh = 0,
                // 6937F9/693808  SC 与 DC 同式(战士这里是全零,法师是 MC 同式)
                ScLow = Math.Max(d7 - 1, 0),
                ScHigh = Math.Max(d7, 1),
                // 693812-69381C  CC/AC 清零
                AcLow = 0, AcHigh = 0,
                // 693834  sar eax,1 + adc eax,0 = 有符号 /2 ; 69383F  inc esi
                MacLow = d6 >> 1,
                MacHigh = d6 + 1,
            };
        }
    }
}

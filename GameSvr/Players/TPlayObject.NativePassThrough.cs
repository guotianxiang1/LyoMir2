using System;

namespace GameSvr
{
    // MOVE-71 / MOVE-72 / MOVE-73 — NOTHROUGH 穿透系统（安全区穿人）。
    //
    // 原版把「本对象此刻能否穿过占格者」算成一个布尔，缓存在 Obj[+0x3FE]，由
    // 玩家 tick sub_6B2D38 每帧重算并「仅在变化时」回写，随后各移动原语读该缓存
    // 当作 MoveToMovingObject 的 boIgnoreOccupancy。MOVE-71 明确点名：把 NOTHROUGH
    // 直接内联进 walkability 判定是本子系统「最容易移植错」的一处——正确模型是
    //   Envir[+0x84] → sub_768454 → 缓存 Obj[+0x3FE] → mover 第三参 → boIgnoreOccupancy
    // 本文件复刻判定 sub_768454 与其安全区子判定 sub_7684DC，并持有缓存字段本体；
    // 缓存的重算与回写在 TPlayObject.NativeThroughOccupancyTick.cs（唯一写点）。
    //
    // 极性（MOVE-71）：NOTHROUGH 置位 → 判定 FALSE → 缓存=0 → 走 occupancy 扫描
    // （撞人撞怪，不可穿越）；NOTHROUGH 清零且其它条件满足 → 判定 TRUE → 缓存=1 →
    // 整段 occupancy 扫描被跳过（可穿越）。占用扫描的跳过闸见
    //   0x779870  80 7D 08 00        cmp byte [ebp+8],0    ; boIgnoreOccupancy
    //   0x779874  0F 85 4C 01 00 00  jne 0x7799C6          ; 非 0 → 跳过扫描
    // C# 对应 Envirnoment.MoveToMovingObjectCore 的 `if (!boFlag && mapCell)` 分支。
    public partial class TPlayObject
    {
        // off_7D6970 -> *[0] = 「安全区穿人范围」(0..50)。GM @ThroughRange 运行时写入：
        //   0x6252F0  A1 70 69 7D 00  mov eax,[0x7D6970]   ; eax = 指针 P
        //   0x6252F5  89 18           mov [eax],ebx        ; *P = n (0..50, 0x6252E7 cmp 0x32)
        // sub_768454 读同一处：0x768474 mov eax,[0x7D6970] / 0x768479 mov eax,[eax]。
        // 该指针在已 dump 的镜像区间内没有启动初始化写点（全镜像仅 2 处引用：此写点
        // 与 sub_768454 的读点），故运行时默认值无二进制铁证——按 fail-closed 取 0，
        // 即 nRange<=0，跳过 RedHome / StartPoint 两臂（见 NativeSafeZoneThroughTest）。
        // 现 GM @ThroughRange 命令族仍是「模型层」（NativeGmMonsterMapCommands），尚未
        // 实时写回本字段；接入实时写回属另一契约范围，这里先给出可被写回的权威存储。
        public static int NativeSafeZoneThroughRange = 0;

        // 原版 Obj[+0x3FE]：穿透判定缓存。全镜像 28 个访问点里**写点只有一个**，
        // 就是玩家 tick sub_6B2D38：
        //   0x6B308E  E8 ..            call 0x768454        ; al = 重算判定
        //   0x6B3096  3A 82 FE 03 00 00 cmp al,[edx+0x3FE]  ; 与旧缓存比较
        //   0x6B309C  74 43            je  0x6B30E1         ; 未变 → 不写、不发消息
        //   0x6B30A3  88 91 FE 03 00 00 mov [ecx+0x3FE],dl  ; 变了才回写缓存
        // 变化时另发 SM_2821(0xB05) 广播（TRUE: push 6/1/0/0；FALSE: push 6/0/0/0，
        // 经 vmt[+0x250]）。这一整段现由 NativeTickThroughOccupancyTransition() 复刻
        // （TPlayObject.NativeThroughOccupancyTick.cs），接在玩家 tick 里；各 mover
        // 与原版一样**只读**本字段，不再自行刷新（MOVE-73）。
        public bool m_boThroughOccupancyCache;

        // 原版 sub_768454(Self) -> Boolean。返回「本对象能否穿过占格者」。
        //   0x76845C  call 0x772EB8     ; 无条件穿透授予 → 立刻 TRUE
        //   0x768461  84 C0 / 75 33     ; test al / jne 0x768498 (mov al,1; ret)
        //   0x768465  mov eax,[ebx+0x128] ; Envir
        //   0x76846B  80 B8 84 00 00 00 00 cmp byte [eax+0x84],0 ; NOTHROUGH（全镜像唯一读点）
        //   0x768472  75 1F             ; jne 0x768493 (xor eax,eax; ret) → NOTHROUGH 置位 → FALSE
        //   0x768474  mov eax,[0x7D6970]/[eax] ; ThroughRange
        //   0x76848A  call 0x7684DC     ; 委派安全区子判定 sub_7684DC(Self, X, Y, range)
        internal bool NativeComputeThroughOccupancy()
        {
            // sub_772EB8：m_boObMode || 处于体状态 0x3C(60)。已在 TBaseObject 建模。
            if (HasNativeCellPassThroughGrant())
            {
                return true;
            }
            var envir = m_PEnvir;
            if (envir == null || envir.Flag == null)
            {
                // 原版 0x768465 直接解引用 Envir[+0x128]/[+0x84]，从不判空；这里判空
                // 属移植安全兜底，返回 FALSE（fail-closed：不凭空放行穿透）。
                return false;
            }
            // 0x76846B：NOTHROUGH 置位 → 不可穿越。
            if (envir.Flag.boNOTHROUGH)
            {
                return false;
            }
            // 0x768474/0x768488：edx=Obj[+0x12C]=m_nCurrX，ecx=Obj[+0x130]=m_nCurrY，
            // 第四参=ThroughRange。
            return NativeSafeZoneThroughTest(envir, m_nCurrX, m_nCurrY, NativeSafeZoneThroughRange);
        }

        // 原版 sub_7684DC(Self, nX(edx), nY(ecx), nRange([ebp+8])) -> Boolean。ret 4。
        // 判定 (nX,nY) 是否落在「可穿人」的安全区内（半径 nRange）：
        //   0x7684F6  8A 58 5C          mov bl,[eax+0x5C]     ; SAFE（整图安全）
        //   0x7684F9  84 DB / 75 0F     ; SAFE 置位 → 跳过 sub_7684A0，bl=SAFE(非0)
        //   0x768505  call 0x7684A0     ; SAFE 清零时：SafeZoneList 多边形(sub_696D7C)
        //   0x76850C  84 DB / 75 62     ; bl 非 0 → 返回 TRUE
        //   0x768510  85 FF / 7E 5E     ; nRange<=0 → 返回 bl(FALSE)（RedHome/起点两臂被跳过）
        //   0x76851D  BA 88 85 76 00    ; 地图名 == 字面量 "3"（0x768588）→ RedHome 半径臂
        //   0x76852C  2D 4D 03 00 00    ; sub eax,0x34D(845=RedHomeX) → abs，闸 cmp/jl 含界
        //   0x76853D  2D A2 02 00 00    ; sub eax,0x2A2(674=RedHomeY) → abs，闸 cmp/jl 含界
        //   0x768567  call 0x696E48     ; 上述皆否 → 起点表扫描（sub_696E48）
        // 各臂皆无副作用，布尔或的次序不影响结果；此处按原版闸序：SAFE→多边形→
        // nRange 闸→RedHome→起点。RedHome 常量用 g_Config.sRedHomeMap/nRedHomeX/nRedHomeY
        // （默认 "3"/845/674，与 0x768588/0x34D/0x2A2 逐字节吻合，沿用 MFLG-17 既定约定）。
        internal static bool NativeSafeZoneThroughTest(Envirnoment envir, int nX, int nY, int nRange)
        {
            if (envir == null || envir.Flag == null)
            {
                return false;
            }
            // 0x7684F6 SAFE：整图安全 → 可穿。
            if (envir.Flag.boSAFE)
            {
                return true;
            }
            // 0x7684A0 -> sub_696D7C：SafeZoneList 多边形包含（与 nRange 无关）。
            if (M2Share.SafeZoneList != null)
            {
                for (var i = 0; i < M2Share.SafeZoneList.Count; i++)
                {
                    if (M2Share.SafeZoneList[i].Contains(envir.sMapName, nX, nY))
                    {
                        return true;
                    }
                }
            }
            // 0x768510：nRange<=0 → 到此为止（RedHome/起点两臂被 jle 跳过）。
            if (nRange <= 0)
            {
                return false;
            }
            // 0x76851D RedHome：地图名 "3" + |x-845|<=nRange 且 |y-674|<=nRange（含界）。
            if (!string.IsNullOrEmpty(M2Share.g_Config.sRedHomeMap) &&
                string.Equals(envir.sMapName, M2Share.g_Config.sRedHomeMap,
                    StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(nX - M2Share.g_Config.nRedHomeX) <= nRange &&
                Math.Abs(nY - M2Share.g_Config.nRedHomeY) <= nRange)
            {
                return true;
            }
            // 0x768567 sub_696E48：本图起点表半径 nRange 扫描。
            if (M2Share.StartPointList != null)
            {
                for (var i = 0; i < M2Share.StartPointList.Count; i++)
                {
                    var sp = M2Share.StartPointList[i];
                    if (sp != null && sp.m_sMapName == envir.sMapName &&
                        Math.Abs(nX - sp.m_nCurrX) <= nRange &&
                        Math.Abs(nY - sp.m_nCurrY) <= nRange)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

    }
}

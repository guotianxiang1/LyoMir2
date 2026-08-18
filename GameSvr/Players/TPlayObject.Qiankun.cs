using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    // =====================================================================================
    // 乾坤包 / 鸿福袋 子系统 (Qiankun bag) —— 1:1 复刻
    //
    // 证据底本：D:/loym2/staging/_reunpack_work/flat_image.bin，ImageBase 0x400000，
    //           文件偏移 = VA - 0x400000。capstone 5.0.7。全程未编译。
    // 分发树：dispatcher sub_6D7D68，selector root 0x6D805C，default(原生 no-op) 0x6DBC2C。
    //
    // 【玩家字段三件套】(xref 由 tools/qk_xref.py 扫描 +0x9F4/0x9F8/0x9FC 得到)
    //   [self+0x9F4] int    —— 当前选择游标/已发计数 (index)
    //       006E68F7  FF86F4090000  inc dword ptr [esi+0x9f4]     ; apply: index++
    //       006E6ECF  8983F4090000  mov  dword ptr [ebx+0x9f4],eax(=0) ; reset -> 0
    //       006E6E00  3B93F4090000  cmp  edx,dword ptr [ebx+0x9f4] ; 3285: count 与游标比较
    //   [self+0x9F8] Delphi 动态数组 —— 当前选择列表 (元素=鸿福袋奖励项)
    //       006E680A  8D83F8090000  lea  eax,[ebx+0x9f8]          ; SetLength 目标
    //       006E6EAC  8D83F8090000  lea  eax,[ebx+0x9f8]          ; reset: SetLength(_,0)
    //       006E692C  8B86F8090000  mov  eax,dword ptr [esi+0x9f8]; apply: 读数组
    //   [self+0x9FC] 指针 —— 鸿福袋"配置对象" (bag definition)
    //       006E67F6  89BBFC090000  mov  dword ptr [ebx+0x9fc],edi; open: 存入查表结果
    //       006E6EC7  8983FC090000  mov  dword ptr [ebx+0x9fc],eax(=0) ; reset -> 0
    //       006E68DA  8B86FC090000  mov  eax,dword ptr [esi+0x9fc]; apply: 读 config
    //   [self+0xAF0] Cardinal —— 加油点 (JiaYouPoint)。已由 TPlayObject.Base.cs m_dwJiaYouPoint
    //       建模；apply @0x6E6A4B 对其累加(带 0xFFFFFFFF 上限)。本处不重复声明。
    //
    // 【config 对象结构】(来自运行期管理器 [0x7D64B8] 按物品名查表 0x753660 -> 0x753C8C 取元素)
    //   [cfg+0x04] AnsiString  bag 名称 (广播用, 3286 @0x6E6C84)
    //   [cfg+0x08] int         条目总数 count (loop bound: apply @0x6E68E8, SM2956 header)
    //   [cfg+0x10] int         价位/档次 -> 荣耀点 = 该值*100 (3285 @0x6E6E1E imul,0x64)
    //   [cfg+0x14] int         每次开启赠送的加油点 delta (apply @0x6E6952..)
    // 【list 元素结构】(SM2956 记录填充 @0x6E6A65；3286 读取 @0x6E6BC3)
    //   [elem+0x00] AnsiString name    [elem+0x04] int count
    //   [elem+0x08] StdItem*  ([+0x14]=类型字节)   [elem+0x10] int field10
    //   SM 2956 body 记录 = 24 字节 {ShortString[15] name; int field10; int count}
    //
    // 【可移植性判定 —— 关键不变量】
    //   [self+0x9FC] 仅由 CM 3283(鸿福袋开启, worker 0x6E67B0) 写入非零值，其来源是全局配置
    //   管理器 [0x7D64B8] 按物品名的运行期查表 (0x753660)。该配置数据由服务端配置文件加载
    //   (GM 命令 sub_7536B4 "reload QianKun bag" 佐证, 见 NativeGmItemExtraCommands.cs)，
    //   镜像内无字节可导出。CM 3283 与配置管理器均未移植，因此本移植中 [self+0x9FC] 恒为 null、
    //   [self+0x9F8] 列表恒空。由此:
    //     - apply(0x6E68A8) 的函数体永不执行 (config-null 顶部即 return)，SM 2956 带 body 帧
    //       不可达 —— 无需 (也不得) 伪造其 config 派生 body。
    //     - CM 3284 无条件发 SM 2957(全零) —— 完全可移植、可观测、忠实。
    //     - CM 3285 config-null -> 顶部 return (无 SM) —— 忠实无副作用。
    //     - CM 3286 列表空 -> 所需格=0, 容量门(SM2958)不可触发, 授予循环空转 -> 落到 reset(SM2957)。
    //   config 被填充时的完整逻辑 (荣耀点扣减/物品授予/RM广播/SM2956/SM2958) 已在下方各 handler
    //   注释中以三件套登记，作为 fail-closed 边界 (= "乾坤包配置装载 CM 3283 + 管理器 [0x7D64B8]")。
    //
    // 【取代 fail-closed 项】cm-C(1dc3e309) 曾以独立文件把 CM 3284 复刻为 SendDefMessage(2957,...)，
    //   并把 3285/3286/3287/3288 登记为 fail-closed(静默丢弃)。本文件给出乾坤包字段模型 + 3284/3285/
    //   3286 的忠实处理器，取代其 3285/3286 的 fail-closed 项；3287/3288 见文件尾(独立特性, 仍 fail-closed)。
    // =====================================================================================
    public partial class TPlayObject
    {
        /// <summary>[self+0x9F4] 乾坤包当前选择游标 / 已发计数。</summary>
        private int m_nQiankunSelIndex;

        /// <summary>[self+0x9F8] 乾坤包当前选择列表 (Delphi 动态数组的 C# 映射)。
        /// 仅由未移植的 CM 3283(鸿福袋开启)填充，故本移植中恒空。</summary>
        private readonly List<QiankunSelEntry> m_QiankunSelList = new List<QiankunSelEntry>();

        /// <summary>[self+0x9FC] 鸿福袋配置对象引用。来源为运行期配置管理器 [0x7D64B8]
        /// (未移植)，故本移植中恒为 null。</summary>
        private object m_QiankunBagRef;

        /// <summary>乾坤包选择列表元素 —— 原生 list@0x9F8 的元素布局
        /// {name@0; count@4; StdItem*@8; field10@0x10}。本移植中永不实例化(列表恒空)，
        /// 仅为忠实建模字段偏移。</summary>
        private sealed class QiankunSelEntry
        {
            public string Name { get; set; }      // [elem+0x00]
            public int Count { get; set; }        // [elem+0x04]
            public object StdItem { get; set; }    // [elem+0x08]  ([StdItem+0x14]=类型字节)
            public int Field10 { get; set; }       // [elem+0x10]
        }

        // -------------------------------------------------------------------------------------
        // CM 3284 (0x0CD4)  handler 0x6DA650 → worker 0x6E6EA4 —— 乾坤包选择列表重置
        //
        // handler 0x6DA650:
        //   006DA650  8B45FC        mov eax,[ebp-4]   ; self
        //   006DA653  E84CC80000    call 0x6E6EA4
        //   006DA658  E9CF150000    jmp 0x6DBC2C      ; -> default(return)
        // worker 0x6E6EA4 (bytes 558BEC538BD86A008D83F8090000B9010000008B159CB374):
        //   006E6EAC  8D83F8090000  lea eax,[ebx+0x9F8]
        //   006E6EB2  B901000000    mov ecx,1
        //   006E6EB7  8B159CB37400  mov edx,[0x74B39C]              ; 动态数组元素类型
        //   006E6EBD  E8A2FDD1FF    call 0x406C64                  ; SetLength(list@0x9F8, 0)
        //   006E6EC7  8983FC090000  mov [ebx+0x9FC],eax(=0)         ; config = null
        //   006E6ECF  8983F4090000  mov [ebx+0x9F4],eax(=0)         ; index  = 0
        //   006E6ED5  6A00 x4       push 0                          ; Param/Tag/Series/sMsg = 0
        //   006E6EDD  33C9          xor ecx,ecx                     ; Recog = 0
        //   006E6EDF  66BA8D0B      mov dx,0x0B8D                   ; Ident = 2957
        //   006E6EE7  FF9350020000  call [self_vtbl+0x250]          ; SendDefMessage (no body)
        //   006E6EEF  C3            ret
        // 忠实映射：清空三字段后无条件发 SM 2957 全零帧 (全镜像唯一发送点 0x6E6EE7)。
        // -------------------------------------------------------------------------------------
        private void ClientNativeCm3284QiankunReset()
        {
            m_QiankunSelList.Clear();     // SetLength(list@0x9F8, 0)
            m_QiankunBagRef = null;       // [self+0x9FC] = 0
            m_nQiankunSelIndex = 0;       // [self+0x9F4] = 0
            SendDefMessage(Grobal2.SM_2957, 0, 0, 0, 0, string.Empty); // 0x6E6EE7
        }

        // -------------------------------------------------------------------------------------
        // CM 3285 (0x0CD5)  handler 0x6DA638 → worker 0x6E6DE8 —— 乾坤包·荣耀点更换
        //
        // handler 0x6DA638:
        //   006DA638  8B45CC        mov eax,[ebp-0x34]              ; wire record
        //   006DA63B  6683780601    cmp word ptr [eax+6],1          ; Param==1 ?
        //   006DA640  0F94C2        sete dl                         ; dl = (Param==1)
        //   006DA643  8B45FC        mov eax,[ebp-4]                 ; self
        //   006DA646  E89DC70000    call 0x6E6DE8                   ; worker(self, dl)
        // worker 0x6E6DE8:
        //   006E6DF3  8B83FC090000  mov eax,[ebx+0x9FC]             ; config
        //   006E6DF9  85C0/746D     test eax,eax / je 0x6E6E6A      ; config==null -> ret (无 SM)
        //   006E6DFD  8B5008        mov edx,[config+8]              ; count
        //   006E6E00  3B93F4090000  cmp edx,[ebx+0x9F4]            ; count <= index -> ret
        //   006E6E08  8B7010        mov esi,[config+0x10]           ; 价位 (esi)
        //   006E6E0D  85F6/7E52     test esi,esi / jle 0x6E6E61     ; esi<=0 -> 免费直接 apply
        //   006E6E0F  807DFF00/7435 cmp [ebp-1],0 / je 0x6E6E4A     ; dl(Param==1)?
        //     dl!=0 分支:
        //       006E6E15  6A01/6A01     push 1; push 1
        //       006E6E19  68786E6E00    push 0x6E6E78                ; "乾坤包更换奖励"
        //       006E6E1E  6BCE64        imul ecx,esi,0x64           ; 荣耀点 = 价位*100
        //       006E6E21  BA7C270000    mov edx,0x277C(=10108)       ; vsId
        //       006E6E28  E88FB1FFFF    call 0x6E1FBC               ; DecGloryPoint -> al
        //       al!=0: mov dl,1; call 0x6E68A8 (apply)   al==0: SysMsg "您没有足够的荣耀点"
        //     dl==0 分支 (0x6E6E4A): push esi/0/0; ecx=0x277C; dx=0x7D(=125); call 0x6D3694
        //   0x6E6E61 (esi<=0): mov dl,1; call 0x6E68A8 (apply)
        //
        // 忠实性：config 恒 null(见头注释) -> 与原生一致地在顶部 return，不发任何 SM。
        // config 被填充时的荣耀点扣减/apply(SM2956) 依赖未移植配置数据 -> fail-closed 边界。
        // -------------------------------------------------------------------------------------
        private void ClientNativeCm3285QiankunGloryExchange(bool bParam1)
        {
            _ = bParam1; // dl = (Param==1)，仅在 config 非空路径使用
            if (m_QiankunBagRef == null)
            {
                return; // 0x6E6DFB: config==null -> ret (原生此路径无 SM，忠实)
            }

            // config 非空需先移植 CM 3283 + 配置管理器 [0x7D64B8]；apply(0x6E68A8) 的 SM 2956
            // body 依赖 config 数据，无法凭镜像推导 -> fail-closed 并登记。
            NativeQiankunFailClosed.Drop(Grobal2.CM_3285, m_sCharName);
        }

        // -------------------------------------------------------------------------------------
        // CM 3286 (0x0CD6)  handler 0x6DA65D → worker 0x6E6B54 —— 乾坤包·领取奖励
        //
        // handler 0x6DA65D: mov eax,[ebp-4](self); call 0x6E6B54
        // worker 0x6E6B54 (两趟):
        //   趟1 计算所需背包格数 nNeed ([ebp-4]):
        //     遍历 list@0x9F8; 元素名=="英雄经验" 且无英雄([self+0xBB0]==0) -> SysMsg
        //       "请先将您的英雄召唤出来！" 并 return;
        //     若物品可叠加(0x751AA4) 跳过; 否则 [StdItem+0x14]>=0x96 -> nNeed++,
        //       else nNeed += count。
        //   容量门: eax=GetBagFreeSlotCount(0x7441D8); if eax < nNeed -> 0x6E6CE4:
        //     006E6CEE  66BA8E0B  mov dx,0xB8E(=2958); call [vtbl+0x250] (Param=1)
        //     然后 SysMsg "请确保您的包裹有"+nNeed+"个空位!"; return。
        //   趟2 (格数足够) 遍历 list: AddItem(0x6C87B4); 若 config.ShouldBroadcast(0x753D88)
        //     -> RM 广播 "恭喜：%s在开启%s的时候获得%s:%d" (0x5F701C, type=0x64)。
        //   循环末: 006E6CDB  call 0x6E6EA4  (= CM3284 reset -> SM 2957)。
        //
        // 忠实性：list 恒空(见头注释) -> nNeed=0；GetBagFreeSlotCount()>=0 恒成立故 SM2958 分支
        //   不可触发；授予循环空转；落到 reset(0x6E6EA4) -> SM 2957 全零。此为原生在"列表为空"
        //   状态下的可观测响应，忠实复刻。列表非空(需 CM 3283 填充)时的授予/广播/SM2958/SM2956
        //   依赖未移植配置 -> fail-closed 边界。
        // -------------------------------------------------------------------------------------
        private void ClientNativeCm3286QiankunCollect()
        {
            if (m_QiankunSelList.Count == 0)
            {
                // 原生: nNeed=0 -> 容量门(SM2958)不可触发 -> 授予循环空转 -> 落到 reset 0x6E6EA4。
                ClientNativeCm3284QiankunReset(); // -> SM 2957 全零
                return;
            }

            // 列表非空需先移植 CM 3283(鸿福袋开启) + 配置管理器；授予链(0x6C87B4)/RM 广播
            // (0x5F701C)/SM2958/SM2956 均依赖未移植配置 -> fail-closed 并登记。
            NativeQiankunFailClosed.Drop(Grobal2.CM_3286, m_sCharName);
        }

        // -------------------------------------------------------------------------------------
        // 分发挂钩：TPlayObject.Message 的 default 链在 Q3 之前调用本 helper，
        // 因而本处是 3284..3288 的唯一运行时 owner；Q3 仅保留不可达 fallback。
        // 3285/3287/3288 remain under the existing Q3 fail-closed routes. If the
        // missing config-backed 3283 loader is implemented later, this grouped
        // helper can replace those routes without duplicating switch ownership.
        // -------------------------------------------------------------------------------------
        internal bool TryHandleQiankunCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_3284:
                    ClientNativeCm3284QiankunReset();
                    return true;
                case Grobal2.CM_3285:
                    // nParam2 = Param (word[record+6]); 原生 dl = (Param==1)。
                    ClientNativeCm3285QiankunGloryExchange(processMessage.nParam2 == 1);
                    return true;
                case Grobal2.CM_3286:
                    ClientNativeCm3286QiankunCollect();
                    return true;
                case Grobal2.CM_3287:
                    ClientNativeCm3287ShowItemNearby();
                    return true;
                case Grobal2.CM_3288:
                    ClientNativeCm3288SelfItemBroadcast();
                    return true;
                default:
                    return false;
            }
        }

        // =================================================================================
        // 独立特性：附近/自身物品 RM 广播 (CM 3287/3288) —— 与乾坤包字段无关(不碰 0x9F4/8/C)。
        // 任务将其归入"乾坤包簇"，但反汇编证实二者是相邻分发臂上的另一子系统。仍 fail-closed。
        // =================================================================================

        // -------------------------------------------------------------------------------------
        // CM 3287 (0x0CD7)  handler 0x6DA895 → worker 0x6E8734 —— 向附近玩家展示物品
        //
        // handler: 折 Series(push word[rec+0xA]) + MakeLong(Param,Tag)(0x408D40) -> ecx；
        //          edx=Recog[rec+0]; eax=self; call 0x6E8734 (ret 4)。
        // worker 0x6E8734(self, Recog, MakeLong(Param,Tag), Series[stack]):
        //   FindItem(0x73CF08, combo) -> edi; 空则 ret。
        //   GetPlayObject(UserEngine[0x7D6784], Recog) (0x649A58/0x64A844) -> esi; 空则 ret。
        //   同图([+0x128]) && 距离<=15(0x7743E0) 否则 ret。
        //   按 Series: 0 -> 依 [item+0x38] 与 阈值10 算 值(*100000 或原值,置 flag);
        //             2 -> GetItemValue(0x78472C)。
        //   发 RM 0x3004(=12292) 经 SendRefMsg 0x765E68 广播(target=esi)。
        //
        // fail-closed 依据：RM 0x3004 的客户端语义、[item+0x38] 与 GetItemValue(0x78472C) 的物品
        //   估值语义无法从服务端镜像求证；RM 0x3004 常量在 C# 未定义。凭分发臂强行发包会捏造线格式，
        //   违反铁律 -> 静默丢弃并登记。
        // -------------------------------------------------------------------------------------
        private void ClientNativeCm3287ShowItemNearby()
        {
            NativeQiankunFailClosed.Drop(Grobal2.CM_3287, m_sCharName);
        }

        // -------------------------------------------------------------------------------------
        // CM 3288 (0x0CD8)  handler 0x6DA8C4 → worker 0x6E8820 —— 自身物品广播
        //
        // handler: 同 3287 构造 (Recog, MakeLong(Param,Tag), Series)；call 0x6E8820 (ret 4)。
        // worker 0x6E8820: 依 combo 与 Series 选择分支，均以 target=self 发 RM 0x3005(=12293)
        //   经 SendRefMsg 0x765E68 广播；Series==1 走 0x6EB2A8；值由 0x6E88C0/0x6E8C1C 计算。
        //
        // fail-closed 依据：同 3287 —— RM 0x3005 客户端语义与值计算 helper 不可证；RM 0x3005 在 C#
        //   未定义。静默丢弃并登记。
        // -------------------------------------------------------------------------------------
        private void ClientNativeCm3288SelfItemBroadcast()
        {
            NativeQiankunFailClosed.Drop(Grobal2.CM_3288, m_sCharName);
        }
    }
}

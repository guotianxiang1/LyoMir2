using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    // =====================================================================================
    // 免费回收装备 子系统 (Free-Recycle Equipment) —— 1:1 复刻
    //
    // 证据底本：D:/loym2/staging/_reunpack_work/flat_image.bin，ImageBase 0x400000，
    //           文件偏移 = VA - 0x400000。capstone 5.0.7。全程未编译 (无 dotnet build)。
    // 分发树：dispatcher sub_6D7D68，selector root 0x6D805C，default(原生 no-op) 0x6DBC2C。
    //
    // 【取代 fail-closed 项】cm-4 曾把 CM 4173 整条登记为 fail-closed(静默丢弃, 见
    //   NativeCmTailFailClosed.cs 与 TPlayObject.NativeCmTailProtocol.cs 的
    //   ClientNativeFreeRecycleEquip())。本文件反汇编 worker 0x6E600C 后, 把其中**完全可从
    //   镜像推导**的路径 (物品查找 + "未找到" 回包) 复刻为忠实实现, 只把依赖运行期配置表的
    //   "可回收判定 + 删除/结算" 保留为 fail-closed 边界。cm-4 的常量/登记复用不重复。
    //
    // -------------------------------------------------------------------------------------
    // 【入口】CM 4173 (0x104D)  leaf 0x6DB068 → worker 0x6E600C
    //   006DB068  8B45CC        mov eax,[ebp-0x34]      ; wire record
    //   006DB06B  668B5008      mov dx,[eax+8]          ; Tag  = word[record+8]  (hi)
    //   006DB06F  8B45CC        mov eax,[ebp-0x34]
    //   006DB072  668B4006      mov ax,[eax+6]          ; Param= word[record+6]  (lo)
    //   006DB076  E8C5DCD2FF    call 0x408D40           ; = MakeLong(lo=Param, hi=Tag)
    //   006DB07B  8BD0          mov edx,eax             ; edx = key
    //   006DB07D  8B45FC        mov eax,[ebp-4]         ; eax = self
    //   006DB080  E887AF0000    call 0x6E600C           ; worker(self, key)
    //   006DB085  E9A20B0000    jmp 0x6DBC2C
    // 0x408D40 逐位: eax = (Param & 0xFFFF) | ((Tag & 0xFFFF) << 16) == HUtil32.MakeLong(Param,Tag)。
    //
    // 【数据流三件套】(偏移 → C# 映射 → 语义)
    //   ┌ [self+0x508]      → m_ItemList                     背包主物品 TList (NativeItemMerge SelfItemListOffset=0x508)
    //   ├ [item+0x18]       → TUserItem.ClientItemID          客户端会话物品 id (sub_73CF08 按 +0x18 查找; FindClientItemIn)
    //   ├ [item+0x1C]+4     → GetStdItem(item.wIndex).Name    物品名 (0x784568 取名; StdItem 指针在 +0x1C)
    //   ├ [item+0x20]       → TUserItem.MakeIndex             记录首字段 (LegacyUserItem208Codec: record@item+0x20)
    //   ├ [0x7D5F0C]        → (未建模)                        "免费回收物品表"管理器 (下述 fail-closed 边界)
    //   └ [self.vmt+0x250]  → SendDefMessage(SM_4352,...)     回包发送器 sub_6D7CB0 (Recog=self 指针 → ObjectId)
    //
    // 【worker 0x6E600C 控制流】(eax=self, edx=key)
    //   006E6037  xor ebx,ebx                              ; found = null
    //   006E603C  mov eax,[self+0x508]                     ; bag = m_ItemList
    //   006E6045  趟1: 遍历 bag, TList.Get(0x424D4C); 命中 [item+0x18]==key 则 found=item
    //   006E6085  趟2: 完全相同的第二趟 (同一 list、同一谓词) —— 冗余重复, 单次搜索即忠实
    //   006E60C1  test ebx,ebx / je 0x6E6172               ; 未找到 → 回包(A)
    //   006E60CE  call 0x784568(item,@name)                ; 取物品名
    //   006E60D7  mov eax,[0x7D5F0C]; mov eax,[eax]        ; "免费回收物品表"管理器
    //   006E60DD  call 0x611340(mgr, name)                 ; = mgr[+4].vmt+0x8C(name) → bool (是否可回收)
    //   006E60E4  test al,al / je 0x6E6156                 ; 不可回收 → 回包(B)
    //   006E60EB  call 0x425020(bag,item)                  ; TList.Remove (从背包摘除, 不释放)
    //   006E610F  call 0x768BE0(self,10,name,[item+0x20],1,"免费回收装备")
    //                                                      ; 写游戏日志 → 0x79D3D8([0x7D5ECC] 日志管理器)
    //                                                      ; == M2Share.AddGameDataLog(...) (参 MagicManager.ConsumeSpentPoisonCharm)
    //   006E6136  call 0x40DCC0 (Format "您已成功将%s物品回收!", name)
    //   006E6147  call [self.vmt+0xD4](cx=0x38FF, msg)     ; SysMsg 通告 (成功)
    //   006E614F  call 0x404690(item)                      ; 释放物品对象
    //   —— 三个出口 ——
    //   (A) 0x6E6172: push 1/0/0/0; ecx=self; dx=0x1100; call [vmt+0x250]  → SM 4352 Recog=self Param=1 空 body
    //   (B) 0x6E6156: push 0/0/0/0; ecx=self; dx=0x1100; call [vmt+0x250]  → SM 4352 Recog=self Param=0 空 body
    //   (C) 成功: 无 SM 4352, 仅 0x38FF 通告 "您已成功将%s物品回收!" (删物品副作用)
    //   注: 全 worker 无冷却门、无次数门、无声望/金币结算 —— "回收所得"为纯删除 (免费处置)。
    //
    // 【fail-closed 边界 —— 关键不变量】
    //   可回收判定 0x611340 走全局管理器 [0x7D5F0C]。该管理器由统一加载器 0x793124 在 0x793383 处
    //   以配置名 "免费回收" (@0x7937D4) 注册, GM 命令 0x61139C 可重载并回 "已经重新载入免费回收物品表."
    //   (@0x62BBF0) —— 即其成员是**服务端配置文件运行期加载的可回收物品名表**, 镜像内无字节可导出,
    //   且 C# 端无任何建模 (全库 0 处引用 0x7D5F0C)。因此对"已找到的物品"无法判定其是否可回收:
    //     - 出口(C) 会按该表删除物品 —— 破坏性操作, 绝不可凭猜测执行;
    //     - 出口(B) 断言"不可回收"(Param=0) —— 同样是对未知配置状态的臆造。
    //   据铁律 (回收规则不可证处 fail-closed, 绝不捏造): 物品已找到时既不删除也不回包, 登记缺口。
    //   完全可推导的出口(A)"未找到 → SM 4352 Param=1"则忠实复刻 (无副作用、可观测)。
    // =====================================================================================
    public partial class TPlayObject
    {
        // -------------------------------------------------------------------------------------
        // 分发挂钩 —— 属 Q4/Tail 段。集成方(协调者)请在 TPlayObject.Message.cs 的 Operate()
        //   default 臂, 于 TryHandleNativeCmTailProtocol 之前追加本调用:
        //
        //     default:
        //         if (!TryHandleInlayCm(ProcessMsg)
        //             && !TryHandleQiankunCm(ProcessMsg)
        //             && !TryHandleNativeSocialProtocol(ProcessMsg)
        //             && !TryHandleFreeRecycleCm(ProcessMsg)          // <-- 新增此行 (须在 Tail 之前)
        //             && !TryHandleNativeCmTailProtocol(ProcessMsg)
        //             && !TryHandleNativeCmQ1(ProcessMsg)
        //             && ...)
        //         {
        //             result = base.Operate(ProcessMsg);
        //         }
        //         break;
        //
        // 说明：本方法只认领 CM 4173, 其余一律返回 false 交回原链, 绝不改变既有行为。插在
        //   TryHandleNativeCmTailProtocol 之前, 使 CM 4173 走本忠实处理器而非 cm-4 的整条 fail-closed;
        //   cm-4 文件不改动 (其 ClientNativeFreeRecycleEquip() 对 4173 变为不可达)。
        // -------------------------------------------------------------------------------------
        internal bool TryHandleFreeRecycleCm(TProcessMessage processMessage)
        {
            if (processMessage.wIdent != Grobal2.CM_4173)
            {
                return false;
            }

            // leaf 0x6DB068: key = MakeLong(lo=Param=word[record+6], hi=Tag=word[record+8])。
            // 尾段约定: nParam2 = Param, nParam3 = Tag。
            int nClientItemId = HUtil32.MakeLong(processMessage.nParam2, processMessage.nParam3);
            FreeRecycleEquipWorker(nClientItemId);
            return true;
        }

        /// <summary>
        /// worker 0x6E600C(self, key) —— 免费回收装备。key = 客户端会话物品 id ([item+0x18])。
        /// 忠实复刻可从镜像推导的路径; "可回收判定 + 删除"依赖未建模的运行期配置表 [0x7D5F0C]
        /// ("免费回收物品表") → fail-closed。
        /// </summary>
        private void FreeRecycleEquipWorker(int nClientItemId)
        {
            // 0x6E6045/0x6E6085: 在 m_ItemList ([self+0x508]) 按 [item+0x18]==key 查找 (sub_73CF08 形状,
            // 原生内联两趟且第二趟为同 list 同谓词的精确重复 → 单次搜索即忠实)。仅搜背包, 不含身上装备/英雄,
            // 无 MakeIndex 回退 (makeIndexOnly:false = 按 ClientItemID 匹配)。
            var item = FindClientItemIn(m_ItemList, nClientItemId, false);
            if (item == null)
            {
                // 出口(A) 0x6E6172: [vmt+0x250] 发 SM 4352 (0x1100), Recog=self, Param=1, Tag/Series=0, 空 body。
                // sub_6D7CB0 把 self 指针写入 Recog → C# 忠实映射为 ObjectId。
                SendDefMessage(Grobal2.SM_4352, ObjectId, 1, 0, 0, string.Empty);
                return;
            }

            // 已找到该物品: 出口(B)/(C) 均取决于 [0x7D5F0C] "免费回收物品表"(运行期配置载入, 镜像无字节、
            // C# 未建模)。既不能凭空断言"不可回收"(Param=0), 更不能凭空删除物品(出口 C) → fail-closed 登记。
            NativeCmTailFailClosed.Drop(Grobal2.CM_4173, m_sCharName);
        }
    }
}

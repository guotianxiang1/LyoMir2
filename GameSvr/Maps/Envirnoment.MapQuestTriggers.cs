using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 地图 QuestNPC 的四个脚本触发点：<c>@OnEnter</c> / <c>@OnLeave</c> / <c>@OnDie</c> /
    /// <c>@OnReLive</c>。
    ///
    /// 原生类树（VMT SelfPtr 反解，vmtSelfPtr = -0x4C，vmtParent = -0x24）：
    ///   TObject
    ///     └ TEnvironment            VMT=0x77477C  InstanceSize=212 (0xD4)  唯一构造点 0x695B8E
    ///         ├ TDynEnvir           VMT=0x5FB264  InstanceSize=264 (0x108) 构造点 0x5FE2E9
    ///         │   ├ TDynSuperForceMapEnvir   VMT=0x5F7B58
    ///         │   └ TFoxBossDungeonDynEnvir  VMT=0x5F9934
    ///         └ TArenaRoom          VMT=0x612C70
    ///
    /// VMT 槽位与四个触发点的对应（0x695B8E `A1 30 47 77 00 mov eax,[0x774730]` +
    /// `E8 60 EE 0D 00 call 0x7749F8` 证明普通地图是 TEnvironment）：
    ///
    /// | 槽 | TEnvironment | TDynEnvir | 触发 |
    /// |---|---|---|---|
    /// | +0x00 DeleteObject | 0x77A014 无派发 | 0x5FD574 | @OnLeave |
    /// | +0x04 AddObject    | 0x779F68 无派发 | 0x5FD534 | @OnEnter |
    /// | +0x08 ObjectDied   | 0x779F64 = 裸 `C3` | 0x5FD4D4 | @OnDie |
    /// | +0x10 AutoRelive   | 0x77BB38 **有派发** | 0x5FD384 | @OnReLive |
    ///
    /// ⇒ <c>@OnEnter</c> / <c>@OnLeave</c> / <c>@OnDie</c> **只在 TDynEnvir 动态地图上存在**，
    /// 普通地图的基类实现根本不碰派发器；<c>@OnReLive</c> 则在所有地图上都有。
    /// 判据是 <c>[map+0xD8]</c>（人数）落在 0xD4..0x108 区间，TEnvironment 的 212 字节实例
    /// 里没有这个字段。C# 侧用 <see cref="IsDynamicRoom"/> 作为 TDynEnvir 的类门。
    ///
    /// 四个派发器形状完全一致，例（@OnEnter，0x6468C8）：
    ///   006468CB  80 B8 95 05 00 00 00  cmp byte [npc+0x595],0   ; 该过程存在位
    ///   006468D2  74 0E                 je  0x6468E2             ; 不存在 -> 静默返回
    ///   006468D4  68 EC 68 64 00        push 0x6468EC            ; '@OnEnter'
    ///   006468D9  6A 00                 push 0                   ; 参数 = ''
    ///   006468DD  E8 B6 73 FF FF        call 0x63DC98            ; GotoLable
    /// 另三个：0x6468F8 `[npc+0x596]` '@OnLeave'、0x646928 `[npc+0x597]` '@OnDie'、
    /// 0x646954 `[npc+0x598]` '@OnReLive'。存在位由 TPsNpc.Initialize / ReInitialize
    /// 用 0x4EB054 按名查过程后 `cmp eax,-1 / setne al` 写入。
    ///
    /// <c>[map+0xA4]</c>（QuestNPC）的两条绑定路径：
    ///   ① 普通地图：MapInfo.txt 的 <c>CHECKQUEST(&lt;名&gt;)</c> 标记 —
    ///      0x776312 `B9 0A 00 00 00 mov ecx,0xA` / `BA 40 6C 77 00 mov edx,0x776C40`
    ///      （= "CHECKQUEST"）→ 0x776333 取括号内文本 → 0x77633D `call 0x77ADD4`
    ///      → 0x776342 `89 83 A4 00 00 00 mov [map+0xA4],eax`。
    ///      0x77ADD4 拼 <c>&lt;基目录&gt;PsMapQuest\&lt;名&gt;.pas</c>
    ///      （0x77AF40 'PsMapQuest\' + 0x77AF54 '.pas'），FileExists 通过才
    ///      `TPsNpc.Create`，并置 `[npc+0x45D]=2`。缺文件时打 0x77AF64 '[缺MapQuest脚本]:'。
    ///   ② 动态地图：0x5FE342 `A1 A8 CF 63 00 mov eax,[0x63CFA8]`（TPsNpc 类）→
    ///      0x5FE347 `call 0x63D848` → 0x5FE351 `89 98 A4 00 00 00 mov [map+0xA4],ebx`，
    ///      **无条件创建**，`[npc+0x45D]=1`，脚本路径取自房间定义 `[roomdef+0x18]`。
    /// </summary>
    public partial class Envirnoment
    {
        /// <summary>
        /// 四个派发器 0x6468C8 / 0x6468F8 / 0x646928 / 0x646954 的公共壳。
        ///
        /// 原生的「过程存在位」<c>[npc+0x595..0x598]</c> 在 C# 里没有独立存储，但语义等价：
        /// <see cref="NormNpc.GotoLable"/> 走 <c>PasEngine.TryCallNpcLabel</c>，过程不存在
        /// 时返回 false 且对非 @main 标签不发任何气泡，与 `je 0x6468E2` 的静默返回一致。
        /// </summary>
        private void DispatchMapQuestLabel(TBaseObject actor, string label)
        {
            if (QuestNPC is not NormNpc questNpc) return;
            try
            {
                // 0x6468DD `call 0x63DC98` 的实参：ecx=0、参数串为空、player = 触发者。
                questNpc.GotoLable(actor as TPlayObject, label, false);
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[Exception] TEnvirnoment::DispatchMapQuestLabel " + label + " " + ex.Message);
            }
        }

        /// <summary>
        /// <c>TDynEnvir.AddObject</c>（VMT+0x04）尾部的 <c>@OnEnter</c> 派发。
        ///
        ///   005FD541  E8 22 CA 17 00        call 0x779F68        ; inherited，先跑
        ///   005FD546  85 F6 / 74 25         test esi,esi / je    ; 对象非空
        ///   005FD550  80 B8 78 01 00 00 00  cmp byte [obj+0x178],0
        ///   005FD557  75 16                 jne -> 跳过          ; 只有 RC_PLAYOBJECT(0)
        ///   005FD559  FF 83 D8 00 00 00     inc dword [map+0xD8] ; 人数 +1，在派发【之前】
        ///   005FD55F  8B 93 A4 00 00 00     mov edx,[map+0xA4]
        ///   005FD565  85 D2 / 74 06         test edx,edx / je    ; 没绑 QuestNPC -> 跳过
        ///   005FD56A  E8 59 93 04 00        call 0x6468C8
        ///
        /// 所以脚本读到的在场人数**已经把自己算进去了**。
        /// 上层调用者：<c>TEnvironment.AddToMap</c>（VMT+0x28 = 0x7776EC）
        ///   00777AB1  8B 45 0C              mov eax,[ebp+0xC]    ; AddObj
        ///   00777AB4  8A 40 04              mov al,[obj+4]       ; 格子类型字节
        ///   00777AB7  FE C8 / 75 0E         dec al / jne         ; == OS_MOVINGOBJECT
        ///   00777AC6  FF 53 04              call [map_vmt+0x04]
        /// </summary>
        private void NativeDynEnvirAddObjectTrigger(TBaseObject actor)
        {
            // 类门：只有 TDynEnvir 家族重写了 VMT+0x04；TEnvironment 的 0x779F68 不派发。
            if (!IsDynamicRoom || actor == null) return;
            // 0x5FD550 `cmp byte [obj+0x178],0` —— [obj+0x178] 是 m_btRaceServer。
            if (actor.m_btRaceServer != Grobal2.RC_PLAYOBJECT) return;
            DispatchMapQuestLabel(actor, "@OnEnter");
        }

        /// <summary>
        /// <c>TDynEnvir.DeleteObject</c>（VMT+0x00）里的 <c>@OnLeave</c> 派发。
        ///
        ///   005FD57D  80 3E 01              cmp byte [node],1     ; OS_MOVINGOBJECT
        ///   005FD582  8B 46 04              mov eax,[node+4]      ; node.CellObj
        ///   005FD589  80 B8 78 01 00 00 00  cmp byte [obj+0x178],0
        ///   005FD590  75 3B                 jne -> 跳过
        ///   005FD592  FF 8B D8 00 00 00     dec dword [map+0xD8]  ; 人数 -1，在派发【之前】
        ///   005FD598  8B 93 A4 00 00 00     mov edx,[map+0xA4]
        ///   005FD5A3  E8 50 93 04 00        call 0x6468F8
        ///   005FD5A8  83 BB D8 00 00 00 00  cmp dword [map+0xD8],0
        ///   005FD5AF  7F 1C                 jg  -> 跳过收尾
        ///   005FD5D1  E8 3E CA 17 00        call 0x77A014         ; inherited，**在派发之后**
        ///
        /// 所以脚本读到的在场人数**已经把自己排除了**。这与 @OnEnter 是对称的。
        /// 注意 inherited 的位置与 @OnEnter 相反（那边先 inherited 后派发）；但基类
        /// <c>TEnvironment.DeleteObject</c>（0x77A014..0x77A172）**从不递减 [map+0xC4]**
        /// （逐字节核过：只摘哈希 0x49EEE4、摘 NPC 表、再做 [map+0xBE]/[map+0xC0] 等级复检），
        /// 所以两个计数器的相对顺序对脚本不可观测。
        /// 上层调用者：<c>TEnvironment.DeleteFromMap</c>（0x7794A8）
        ///   00779517  80 38 01              cmp byte [node],1
        ///   0077951F  3B 58 04              cmp ebx,[node+4]      ; 命中要摘的对象
        ///   00779546  FF 11                 call [map_vmt+0x00]
        /// </summary>
        private void NativeDynEnvirDeleteObjectTrigger(TBaseObject actor)
        {
            if (!IsDynamicRoom || actor == null) return;
            if (actor.m_btRaceServer != Grobal2.RC_PLAYOBJECT) return;
            DispatchMapQuestLabel(actor, "@OnLeave");
        }

        /// <summary>
        /// <c>TDynEnvir</c> VMT+0x08 的 <c>@OnDie</c> 派发。基类 <c>TEnvironment</c> 的同槽
        /// 实现 0x779F64 是**裸 `C3` ret**（后面 `8D 40 00` 是对齐填充），所以普通地图不触发。
        ///
        ///   005FD4E1  E8 7E CA 17 00        call 0x779F64          ; inherited = 空
        ///   005FD4E6  85 DB / 74 46         test ebx,ebx / je
        ///   005FD4EA  80 BB 78 01 00 00 00  cmp byte [obj+0x178],0
        ///   005FD4F1  75 1C                 jne -> 跳过            ; 只有玩家
        ///   005FD4F3  80 7B 73 00           cmp byte [obj+0x73],0
        ///   005FD4F7  75 16                 jne -> 跳过            ; ghost 位（不是死亡位）
        ///   005FD4F9  83 BE A4 00 00 00 00  cmp dword [map+0xA4],0 / je
        ///   005FD50A  E8 19 94 04 00        call 0x646928
        ///
        /// 上层调用者：<c>sub_76631C</c>（置死亡位的那个函数），紧接在写死亡位之后：
        ///   00766321  8B D8                 mov ebx,eax
        ///   00766323  C6 43 74 01           mov byte [obj+0x74],1  ; m_boDeath := TRUE
        ///   00766327  E8 14 20 CA FF        call 0x408340          ; GetTickCount
        ///   0076632C  89 83 30 03 00 00     mov [obj+0x330],eax    ; m_dwDeathTick
        ///   00766341  8B B3 28 01 00 00     mov esi,[obj+0x128]    ; PEnvir
        ///   00766349  74 09                 je  -> 无地图则跳过
        ///   00766351  FF 51 08              call [map_vmt+0x08]
        /// 即：**先置死亡位与死亡时间戳，再派发**，脚本里看到的是「已经死了」的状态。
        /// </summary>
        internal void NativeDynEnvirObjectDiedTrigger(TBaseObject actor)
        {
            if (!IsDynamicRoom || actor == null) return;
            if (actor.m_btRaceServer != Grobal2.RC_PLAYOBJECT) return;
            // 0x5FD4F3 `cmp byte [obj+0x73],0` —— +0x73 是 ghost 位（唯一写入点
            // 0x7680EF `C6 43 73 01`，在 MarkDelete 里），不是死亡位 +0x74。
            if (actor.m_boGhost) return;
            DispatchMapQuestLabel(actor, "@OnDie");
        }

        /// <summary>
        /// 地图 VMT+0x10：<c>TEnvironment</c> 的 0x77BB38 与 <c>TDynEnvir</c> 的 0x5FD384
        /// **字节级同构**，两者都派发 <c>@OnReLive</c>，所以这一个在**所有地图**上都有。
        /// 这就填上了 <c>TBaseObject.NativeRevive.cs</c> 里那条「Envir vtbl slot +0x10 未解析」
        /// 的 BLOCKED。
        ///
        ///   0077BB3C  8B CA / 33 D2         mov ecx,edx / xor edx,edx   ; 结果预置 false
        ///   0077BB40  85 C9 / 74 31         test ecx,ecx / je -> false
        ///   0077BB46  80 BB 78 01 00 00 00  cmp byte [obj+0x178],0
        ///   0077BB4D  75 26                 jne -> false                ; 只有玩家
        ///   0077BB4F  80 7B 73 00           cmp byte [obj+0x73],0
        ///   0077BB53  75 20                 jne -> false                ; ghost
        ///   0077BB55  83 B8 A4 00 00 00 00  cmp dword [map+0xA4],0
        ///   0077BB5C  74 17                 je  -> false                ; 没绑 QuestNPC
        ///   0077BB66  E8 E9 AD EC FF        call 0x646954               ; @OnReLive
        ///   0077BB6B  83 BB AC 02 00 00 00  cmp dword [obj+0x2AC],0     ; HP
        ///   0077BB72  0F 9F C2              setg dl                     ; 返回 HP > 0
        /// （TDynEnvir 版同址同形：0x5FD392 / 0x5FD39B / 0x5FD3A1 / 0x5FD3B2 / 0x5FD3B7。）
        ///
        /// 返回值语义：**脚本负责把血加回来，宿主只是回报「加成功了没有」**。
        /// 上层调用者 <c>sub_7436F8</c>（复活裁决，THumanKind VMT+0x08）：
        ///   00743921  84 DB / 75 23         test bl,bl / jne     ; 已经复活过就不走这条
        ///   00743925  8B 86 28 01 00 00     mov eax,[obj+0x128]  ; PEnvir
        ///   0074392B  80 78 7E 00           cmp byte [map+0x7E],0
        ///   0074392F  74 17                 je  -> 跳过          ; AUTORELIVE 地图标记
        ///   0074393B  FF 51 10              call [map_vmt+0x10]
        ///   0074393E  83 BE AC 02 00 00 00  cmp dword [obj+0x2AC],0
        ///   00743945  0F 9F C3              setg bl
        /// <c>[map+0x7E]</c> = AUTORELIVE，两个解析器都写它：
        /// 0x77567F `BA 78 5E 77 00`（GM 开关臂，0x775E78 "AUTORELIVE"）→ 0x775694
        /// `C6 43 7E 01`；0x7766EF `BA B8 6D 77 00`（MapInfo.txt 臂）→ 0x776700 `C6 43 7E 01`。
        /// </summary>
        /// <returns>原生 <c>dl</c>：派发之后该对象是否活着（HP &gt; 0）。</returns>
        internal bool NativeEnvirAutoReliveSlot(TBaseObject actor)
        {
            // 0x77BB40 test ecx,ecx / je -> 返回 false
            if (actor == null) return false;
            // 0x77BB46 只有 RC_PLAYOBJECT
            if (actor.m_btRaceServer != Grobal2.RC_PLAYOBJECT) return false;
            // 0x77BB4F ghost 位
            if (actor.m_boGhost) return false;
            // 0x77BB55 没绑 QuestNPC 就连 HP 都不看，直接 false
            if (QuestNPC == null) return false;

            DispatchMapQuestLabel(actor, "@OnReLive");

            // 0x77BB6B cmp dword [obj+0x2AC],0 / setg dl
            return actor.m_WAbil.HP > 0;
        }
    }
}

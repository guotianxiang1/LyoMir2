using SystemModule;

namespace GameSvr.Services
{
    // =======================================================================
    // 跨服禁言复制 —— OthGs ident 209 / 210。
    // 底本 flat_image.bin (ImageBase=0x400000, file_off = VA - 0x400000)。
    //
    // 两条 ident 都只是把本服的「禁言名单管理器」操作转发给他服:
    //   209 stub @0x65723D  mov ecx,[ebp+0x10] / mov edx,[ebp+0xC] / call 0x6580B8
    //       sub_6580B8: 0x6580D6 call 0x405708(_LStrFromPChar) 取裸角色名;
    //                   0x6580DE mov eax,[0x7D7104] / mov eax,[eax]  (管理器单例)
    //                   0x6580E5 mov ecx,esi        (= 帧头第三个 dword)
    //                   0x6580E7 call 0x621B14      (Add)
    //   210 stub @0x65724D  mov ecx,[ebp+8] / mov edx,[ebp+0xC] / call 0x657FF8
    //       sub_657FF8: 0x658013 call 0x405708 取裸角色名;
    //                   0x65801B mov eax,[0x7D7104] / mov eax,[eax]
    //                   0x658022 call 0x621CE4      (Delete)
    //       —— stub 送进 ecx 的是长度, 但 sub_657FF8 序言只有 0x657FFE `mov ebx,edx`,
    //          从不读 ecx, 所以 210 既无长度门也无第三参。
    //
    // [[0x7D7104]] 的身份本仓早已坐实并建模: Services/NativeGmDenyListCommands.cs
    // 的头注释把 off_7D7104 记为 BlockUsers.Dat 管理器单例, 并逐个列出
    // sub_621B14=Add / sub_621CE4=Delete / sub_622040=Tick / sub_622630=Save,
    // 连「sub_657110 ProcessOthGsMsg cases 209/210 -> cross-server Add/Delete
    // replication」这一行都已写明 —— 本文件即把那两条接线补上。
    //
    // sub_621B14(mgr, edx=name, cx=seconds) 逐条:
    //   0x621B4D call 0x40BCBC  = UpperCase(name)            (故比较大小写不敏感)
    //   0x621B5F/0x621B6A       遍历 [mgr+0x20] 链表, 逐节点 _LStrFromShortString
    //                           (0x405774) 后 _LStrCmp (0x40591C)
    //   命中: 0x621B71 movzx eax,word[ebp-6] / 0x621B77 add [node+0x14],eax
    //         —— **累加**剩余秒数, 且不写玩家标志、不落盘。
    //   未命中: 0x621B98 GetMem(0x20) / 0x621BAA 清零 /
    //           0x621BBD 0x4057AC + 0x621BCD 0x4039E4(cl=0x0F) 存 UpperCase 名
    //           (ShortString[15]) / 0x621BD6 [node+0x14]=movzx(cx) /
    //           0x621BE6 [node+0x18]=GetTickCount(0x408340) / 头插 /
    //           0x621BFA inc [mgr+0x24] /
    //           0x621C10 mov byte[player+0xB99],1 (在线才写) /
    //           0x621C19 call 0x622630 (Save)
    // sub_621CE4(mgr, edx=name) 逐条:
    //   0x621D14 UpperCase; 命中则摘链 + 0x621D6E FreeMem(node,0x20);
    //   0x621D88 mov byte[player+0xB99],0 (在线才写); 0x621D8F dec [mgr+0x24];
    //   0x621D94 call 0x622630 (Save)。未命中则**什么都不做**(连 Save 都没有)。
    //
    // 单位: 计数器是**秒**。清扫 sub_622040 @0x622085:
    //   eax = now - [node+0x18]; 0x62208A mov ecx,0x3E8; 0x622091 div ecx;
    //   0x622093 sub [node+0x14],eax   => 每 1000 ms 扣 1。
    // 第三个 dword 取的是**低 16 位**: 0x621B71 `movzx eax,word[ebp-6]`。
    //
    // C# 落点: 活的禁言存储是 M2Share.g_DenySayMsgList
    // (ConcurrentDictionary<角色名, 到期 tick(ms)>), 由 @OutSay / @ShifangSay /
    // @LookOutSay 与 GameServer.cs 的到期清扫共用。native 的 byte[player+0xB99]
    // 在 C# 不是独立字段而是由字典成员资格派生 —— 见
    // TPlayObject.NativeCorpsChat.cs 的 IsNativeChatMuted():
    //   m_boDisableSayMsg || M2Share.g_DenySayMsgList.ContainsKey(m_sCharName)
    // 所以增/删字典项即等价于 native 的置/清 [+0xB99], 不需要 (也不应) 另写字段。
    //
    // 已知差异 (均为本仓既有存储的性质, 非本次引入, 故不在此单方面改动):
    //   * g_DenySayMsgList 用默认序数比较器, native 比较大小写不敏感;
    //   * g_DenySayMsgList 不落盘, native 每次增删都写 BlockUsers.Dat
    //     (完整的落盘编解码已在 NativeGmBlockUserList 里建模, 但那套是 dormant 的)。
    // =======================================================================
    public static class NativeMirrorChatBan
    {
        /// <summary>
        /// 战神 sub_621B14 经 ident 209 的跨服腿: 给 <paramref name="name"/> 追加
        /// <paramref name="nParam"/> 秒禁言。已在名单上则累加剩余时长。
        /// </summary>
        public static void Add(string name, int nParam)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            var list = M2Share.g_DenySayMsgList;
            if (list == null)
            {
                return;
            }

            // 0x621B71 movzx eax,word[ebp-6] —— 只取第三个 dword 的低 16 位。
            long addMs = (nParam & 0xFFFF) * 1000L;

            HUtil32.EnterCriticalSection(list);
            try
            {
                // 命中: [node+0x14] += secs (累加剩余量)。本仓存的是绝对到期 tick,
                // 给到期时刻加同样的毫秒数即等价。
                // 未命中: 建新节点, [node+0x18]=GetTickCount 起算。
                if (list.TryGetValue(name, out var expireTick))
                {
                    list[name] = expireTick + addMs;
                }
                else
                {
                    list[name] = (long)HUtil32.GetTickCount() + addMs;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(list);
            }
        }

        /// <summary>
        /// 战神 sub_621CE4 经 ident 210 的跨服腿: 把 <paramref name="name"/> 从禁言
        /// 名单摘除。不在名单上时 native 什么都不做。
        /// </summary>
        public static void Remove(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            var list = M2Share.g_DenySayMsgList;
            if (list == null)
            {
                return;
            }

            HUtil32.EnterCriticalSection(list);
            try
            {
                list.TryRemove(name, out _);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(list);
            }
        }
    }
}

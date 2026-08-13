using System;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ===================================================================
        // 跨服 OthGs 师徒消息处理器 —— 忠实移植自 ProcessOthGsMsg (sub_657110)
        // 跳表。底本 flat_image.bin (ImageBase=0x400000, file_off=VA-0x400000)。
        //
        //   ident 217 -> 索引表 @0x657160[15]=0x0B -> 地址表 @0x657198[0x0B]
        //                -> stub @0x6572A4 (mov ecx,[ebp+8]; mov edx,[ebp+0xC];
        //                   call 0x657CF0) -> sub_657CF0
        //   ident 218 -> 索引表 @0x657160[16]=0x0C -> 地址表 @0x657198[0x0C]
        //                -> stub @0x6572B4 (mov ecx,[ebp+8]; mov edx,[ebp+0xC];
        //                   call 0x657AC0) -> sub_657AC0
        //
        // 入参: edx = body PChar ([ebp+0xC]). native 不校验 serverNum —— stub 虽
        // 装配 ecx=[ebp+8], 但 sub_657CF0/sub_657AC0 序言仅 `mov ebx,edx` 从不读
        // ecx (校准见 dispatcher 0x6572A4/0x6572B4)。body 为 "师父名/徒弟名":
        // native 用 0x405708 拷串 + 0x4C6AEC 按 '/'(0x2F) 拆为 head/tail
        // (head=[ebp-4]=师父名, tail=[ebp-8]=徒弟名)。C# 文本传输层保留 '/' 作
        // Ident/serverNum/Body 顶层分隔, 故 body 内子字段沿用反斜杠 (与
        // MsgGetWhisper 等所有同胞 handler 一致), 语义等价于 native '/'。
        //
        // 字段映射 (权威来源: TPlayObject.NativeLeaveMaster.cs /
        // TPlayObject.NativeSocialSlots.cs, 已按 LOAD/SAVE 逐字节坐实):
        //   obj+0xB95 = m_boStudent        obj+0xB97 = m_nStudentCount
        //   obj+0xB98 = 录 0xE1 (无 DTO)   obj+0xC58 = m_sMasterName / 录 0x660
        //   obj+0xC78 = m_sStudentNames[5] (stride 16) / 录 0x680
        // SysMsg 色: 两者均 `mov cx,0xFFDB; call [vmt+0xD4]`, 与已核验忠实的离婚
        // (sub_6579D8 同一指令) 对齐为 MsgColor.Red / MsgType.Hint。
        // ===================================================================

        /// <summary>
        /// 战神 sub_657CF0 (OthGs ident 217): 徒弟在他服自行离开师门, 更新本服的
        /// 师父。body="师父名/徒弟名"。在本服找到师父对象, 从其徒弟槽移除该徒弟、
        /// 递减徒弟数并通知师父。native 不发 DBServer 帧、不改师父的 0xB91/0xBF4
        /// (自行离开不是出师), 逐条对齐。
        /// </summary>
        internal static void NativeMirrorStudentLeftMaster(string masterName,
            string studentName)
        {
            // 0x657D29 cmp [ebp-4],0 / je ; 0x657D2F cmp [ebp-8],0 / je
            if (string.IsNullOrEmpty(masterName)
                || string.IsNullOrEmpty(studentName))
            {
                return;
            }

            // 0x657D35 mov eax,[0x7D6D50]/[eax]=UserEngine; 0x657D3F GetPlayObject
            // (0x652784); 0x657D46 test esi,esi / je —— 师父不在本服则静默返回。
            var master = M2Share.UserEngine?.GetPlayObject(masterName);
            if (master == null)
            {
                return;
            }

            // 0x657D52 call sub_6C614C(master, 徒弟名, out idx) / test al / je ;
            // 0x657D5B mov eax,idx / sub eax,5 / jae —— idx 须命中且 0..4。
            // FindNativeStudentSlot 已按 native (含 `cmp byte[+0xB97],0 / jbe` 的
            // 无符号计数门 + AnsiCompareText) 复刻, 命中即保证 count>0。
            var slot = FindNativeStudentSlot(master, studentName);
            if (slot < 0)
            {
                return;
            }

            // 0x657D63 dec byte[master+0xB97] (native 无 >0 守卫; 命中已保证 >0)
            master.m_nStudentCount--;
            // 0x657D69 mov eax,idx/add eax,eax; 0x657D6E mov byte[master+idx*16+0xC78],0
            master.ClearNativeStudentSlot(slot);
            if (master.m_sStudentNames != null
                && slot < master.m_sStudentNames.Length)
            {
                master.m_sStudentNames[slot] = string.Empty;
            }

            // 0x657D76 _LStrCatN(3): "你的徒弟 "(0x657DE0) + 徒弟名 + " 自行离开了
            // 师门！"(0x657DF4); 0x657D93 cx=0xFFDB call [vmt+0xD4] -> SysMsg。
            master.SysMsg("你的徒弟 " + studentName + " 自行离开了师门！",
                MsgColor.Red, MsgType.Hint);
        }

        /// <summary>
        /// 战神 sub_657AC0 (OthGs ident 218): 师父在他服将徒弟逐出师门, 更新本服的
        /// 徒弟。body="师父名/徒弟名"。在本服找到徒弟对象, 校验其存储的师父名与消息
        /// 中的师父名相等 (AnsiCompareText) 后清师门关系并通知徒弟。native 只清
        /// 0xB95/0xC58/0xB98 三处, 不动 0xB96(录0xDF) 与 0xCCC(m_MasterHuman)。
        /// </summary>
        internal static void NativeMirrorMasterExpelStudent(string masterName,
            string studentName)
        {
            // 0x657AF7 cmp [ebp-4],0 / je ; 0x657B01 cmp [ebp-8],0 / je
            if (string.IsNullOrEmpty(masterName)
                || string.IsNullOrEmpty(studentName))
            {
                return;
            }

            // 0x657B07 UserEngine.GetPlayObject(徒弟名=tail); 0x657B18 test/je
            var student = M2Share.UserEngine?.GetPlayObject(studentName);
            if (student == null)
            {
                return;
            }

            // 0x657B1C string(student+0xC58)=m_sMasterName; 0x657B30 sub_40BD78
            // (AnsiCompareText, 大小写不敏感); 0x657B35 test eax / jne —— 不等则退。
            if (!string.Equals(student.m_sMasterName ?? string.Empty, masterName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 0x657B39 mov byte[student+0xB95],0
            student.m_boStudent = false;
            // 0x657B40 push 0/push 9/push 0/push 0/push 师父名/push 0; cx=0x278E;
            // call sub_765E68 -> SendMsg(RM_MASTERRELATION, nParam1=9, 师父名)。
            // (与本地逐出 PasApiBridge 0,9 及离婚 0,7 同族; 9=逐出师门)
            student.SendMsg(student, Grobal2.RM_MASTERRELATION, 0, 9, 0, 0,
                masterName);
            // 0x657B5B mov byte[student+0xC58],0 (DTO + 录 0x660 双写, 同
            // NativeLeaveMaster 出师腿的清 master 槽)
            student.m_sMasterName = string.Empty;
            student.ClearNativeSocialSlotLengthByte(NativeMasterSlotOffset);
            // 0x657B62 mov byte[student+0xB98],0 (录 0xE1)
            student.ClearNativeStudentAuxRecordByte();
            // 0x657B69 call sub_7685E0 -> RefShowName
            student.RefShowName();
            // 0x657B70 cx=0xFFDB edx=0x657BB4 "你的师傅将你逐出了师门" call [vmt+0xD4]
            student.SysMsg("你的师傅将你逐出了师门", MsgColor.Red, MsgType.Hint);
        }

        /// <summary>
        /// 战神 sub_657AC0 的 `mov byte[ebx+0xB98],0` —— 清录 0xE1
        /// (NativeStudentAuxRecordOffset)。该字节无 DTO 成员, codec clone-carry,
        /// 须直改原始记录 blob (同 NativeLeaveMaster.ClearNativeStudentScalarRecord
        /// Bytes 的 0xE1 腿, 但 218 不清 0xB96/录0xDF)。
        /// </summary>
        private void ClearNativeStudentAuxRecordByte()
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length <= NativeStudentAuxRecordOffset)
            {
                return;
            }
            raw[NativeStudentAuxRecordOffset] = 0;
        }
    }
}

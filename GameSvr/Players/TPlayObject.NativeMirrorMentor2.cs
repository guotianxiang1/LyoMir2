using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ===================================================================
        // 跨服 OthGs 处理器补齐 —— ident 226 / 240。
        // 底本 flat_image.bin (ImageBase=0x400000, file_off = VA - 0x400000)。
        //
        // 派发器 TOtherGSMsg.ProcessOthGsMsg @0x657110 (0x6570D8 起是它的 Delphi
        // VMT 尾 + 类名短串 '\x0BTOtherGSMsg'@0x657104, 故 0x657110 是函数体首字节
        // 而非跳表)。真正的两级表在函数体内:
        //   0x657140 movzx edx,word[ebp-2]      ; ident
        //   0x657144 add   edx,0xFFFFFF36       ; edx = ident - 202
        //   0x65714A cmp   edx,0x37 / ja 0x6573A0
        //   0x657153 mov   dl,byte[edx+0x657160]   ; 56 字节索引表
        //   0x657159 jmp   dword[edx*4+0x657198]   ; 28 项地址表
        // => 基数 202, 跨度 202..257, 27 REAL / 29 SINK。
        //   ident 226 -> 索引表[24]=0x12 -> 地址表[0x12] -> stub @0x65731E
        //                (mov ecx,[ebp+8]; mov edx,[ebp+0xC]; call 0x657888)
        //   ident 240 -> 索引表[38]=0x15 -> 地址表[0x15] -> stub @0x65734F
        //                (mov ecx,[ebp+8]; mov edx,[ebp+0xC]; call 0x657F3C)
        //
        // 形参坐标 (由唯一调用点 0x712F3F..0x712F56 的压栈次序 + 上游
        // 0x713EC0..0x713EF0 的取值坐实):
        //   push [ebx+8] -> [ebp+0x10]   帧头第三个 dword
        //   push [ebp-4] -> [ebp+0xC]    payload 指针 (= 帧基址+0xC)
        //   push esi     -> [ebp+8]      payload 长度 (= [ebx+4]-0xC)
        // 上游 0x713EC0 `cmp [ebx+4],0xC / jl` 先卡总长 >= 12, 再 0x713EC6
        // `mov eax,[ebx+4]/sub eax,0xC` 算净荷长度、0x713ED5 `mov eax,[ebx]/
        // add eax,0xC` 算净荷指针, 0x713EE4/0x713EEA 分别压栈/送 ecx。
        // 注意 native 派发器**根本没有 serverIdx 形参** (C# 的 serverNum 是本仓
        // 传输层自加的); 226/240 的序言又都只有 `mov ebx,edx` (0x657897 /
        // 0x657F48), 连长度都不读, 故两者都无任何前置守卫。
        // payload 是字节缓冲, 但 226/240 都用 0x405708 (_LStrFromPChar, 扫 NUL
        // 定长) 取串, 即按**文本**解读 —— 与 222 用 0x405774 读 ShortString、
        // 247 直接取三个 dword 的二进制解读不同。
        //
        // body 内子字段 native 用 '/' (0x2F) 拆分 (0x4C6AEC: eax=源串,
        // edx=@首段, cl=分隔符, 压栈=@余段)。C# 传输层已把 '/' 占用为
        // Ident/serverNum/Body 的顶层分隔 (UsrEngn.SendServerGroupMsg 拼
        // nCode+"/"+nServerIdx+"/"+sMsg), 故 body 内沿用反斜杠, 与
        // MsgGetWhisper / 217 / 218 等所有同胞 handler 一致。
        //
        // SysMsg: 两者均 `mov cx,0xFCFF; call [vmt+0xD4]`。cx 打包为
        // FColor=cx&0xFF / BColor=cx>>8, 即 MsgColor.Blue —— 沿用
        // NativeLeaveMaster.cs 0x6C5FBA 处已坐实的同一映射。
        // ===================================================================

        /// <summary>
        /// 战神 sub_657888 (OthGs ident 226): 徒弟在他服出师, 更新本服的师父。
        /// body="师父名/徒弟名"。这是 sub_6C5EC8 出师腿 (mode=1) 的跨服镜像 ——
        /// 逐条写同一组字段, 只是作用在本服的师父对象上。
        /// </summary>
        internal static void NativeMirrorStudentGraduated(string masterName,
            string studentName)
        {
            // 0x6578CD cmp [ebp-4],0 / je ; 0x6578D3 cmp [ebp-8],0 / je
            if (string.IsNullOrEmpty(masterName)
                || string.IsNullOrEmpty(studentName))
            {
                return;
            }

            // 0x6578D9 mov eax,[0x7D6D50]/[eax]=UserEngine; 0x6578E3 GetPlayObject
            // (0x652784); 0x6578EA test ebx,ebx / je —— 师父不在本服则静默返回。
            var master = M2Share.UserEngine?.GetPlayObject(masterName);
            if (master == null)
            {
                return;
            }

            // 0x6578F6 call sub_6C614C(master, 徒弟名, out idx) / test al,al / je ;
            // 0x6578FF mov eax,idx / sub eax,5 / jae —— idx 须命中且 0..4。
            var slot = FindNativeStudentSlot(master, studentName);
            if (slot < 0)
            {
                return;
            }

            // 0x657907 mov byte[master+0xB91],1 —— 师父标记 (出师才置, 自行离开不置)
            master.m_boMaster = true;
            // 0x65790E dec byte[master+0xB97] (native 无 >0 守卫; 命中已保证 >0)
            master.m_nStudentCount--;
            // 0x657914 mov eax,idx/add eax,eax; 0x657919 mov byte[master+idx*16+0xC78],0
            master.ClearNativeStudentSlot(slot);
            if (master.m_sStudentNames != null
                && slot < master.m_sStudentNames.Length)
            {
                master.m_sStudentNames[slot] = string.Empty;
            }

            // 0x657921 _LStrCatN(3): "你的徒弟 "(0x657990, len 9) + 徒弟名 +
            // " 顺利出师！"(0x6579A4, len 11); 0x65793E cx=0xFCFF call [vmt+0xD4]
            master.SysMsg("你的徒弟 " + studentName + " 顺利出师！",
                MsgColor.Blue, MsgType.Hint);

            // 0x65794C inc dword[master+0xBF4] —— native 把这一步排在 SysMsg 之后
            // (sub_6C5EC8 出师腿排在之前); 照搬 226 自己的次序。
            master.BumpNativeApprenticeNum();
        }

        /// <summary>
        /// 战神 sub_657F3C (OthGs ident 240): 他服玩家邀请本服玩家加入宗派, 只向
        /// 被邀请人发一条提示。body="被邀请人名/邀请人名"。native 除这条 SysMsg
        /// 外无任何状态改动 —— 不建宗派关系、不回帧。
        /// </summary>
        internal static void NativeMirrorSectInvite(string recipientName,
            string inviterName)
        {
            // 0x657F73 cmp [ebp-4],0 / je —— native 先查余段(邀请人)再查首段。
            if (string.IsNullOrEmpty(inviterName))
            {
                return;
            }
            // 0x657F79 cmp [ebp-8],0 / je
            if (string.IsNullOrEmpty(recipientName))
            {
                return;
            }

            // 0x657F7F UserEngine.GetPlayObject(首段=被邀请人); 0x657F90 test/je
            var recipient = M2Share.UserEngine?.GetPlayObject(recipientName);
            if (recipient == null)
            {
                return;
            }

            // 0x657F94 _LStrCat3(dest=eax, s1=edx=余段, s2=ecx=0x657FE4
            // "邀请你加入他的宗派" len 18) —— 0x40581C 先 _LStrAsg(dest,s1) 再
            // _LStrCat(dest,s2), 故拼接次序是 邀请人名 在前、字面量在后。
            // 0x657FA7 cx=0xFCFF; 0x657FAF call [vmt+0xD4]
            recipient.SysMsg(inviterName + "邀请你加入他的宗派",
                MsgColor.Blue, MsgType.Hint);
        }
    }
}

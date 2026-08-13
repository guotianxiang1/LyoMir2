using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ===================================================================
        // 需要「帧头第三个 dword」的 OthGs handler —— ident 224。
        // 底本 flat_image.bin (ImageBase=0x400000, file_off = VA - 0x400000)。
        //
        // 跳表 stub @0x65730A 三个形参全给足:
        //   0065730A  8B 45 08        mov eax,[ebp+8]      ; 净荷长度
        //   0065730D  50              push eax
        //   0065730E  8B 4D 10        mov ecx,[ebp+0x10]   ; 帧头第三个 dword
        //   00657311  8B 55 0C        mov edx,[ebp+0xC]    ; 净荷指针
        //   00657314  E8 9B 01 00 00  call 0x6574B4
        //
        // sub_6574B4 逐条:
        //   006574C3  8B F1              mov esi,ecx          ; esi = 第三个 dword
        //   006574C5  8B DA              mov ebx,edx
        //   006574DA  E8 ..              call 0x405708        ; _LStrFromPChar(&s, body)
        //   006574E3  8D 55 F8           lea edx,[ebp-8]      ; @首段
        //   006574E6  B1 2F              mov cl,0x2F          ; 分隔符 '/'
        //   006574EB  E8 ..              call 0x4C6AEC        ; 拆分, 余段 -> [ebp-0xC]
        //   006574F6  E8 ..              call 0x405598        ; s := 余段
        //   006574FB  83 7D F8 00 / 74   cmp [ebp-8],0 / je   ; 首段(师父名)非空
        //   0065750B  E8 ..              call 0x652784        ; UserEngine.GetPlayObject(首段)
        //   00657512  85 DB / 74 47      test ebx,ebx / je    ; 师父须在本服
        //   00657516  85 F6 / 7E 43      test esi,esi / jle   ; **第三个 dword > 0**
        //   0065751A  01 B3 F0 04 00 00  add [ebx+0x4F0],esi  ; 声望 += n
        //   00657520..00657547            _LStrCatN(5 段)
        //   0065754F  66 B9 FF FC        mov cx,0xFCFF
        //   00657557  FF 93 D4 00 00 00  call [vmt+0xD4]      ; SysMsg
        //
        // 五段拼接 (0x405890, 先压者在左; 字面量都是 GBK, 长度前缀已核):
        //   0x657590 len 0x10 "恭喜：您的徒弟: "
        //   [ebp-4]           = 余段 = 徒弟名
        //   0x6575AC len 0x17 " 等级提升，给您带来了: "
        //   IntToStr(esi)     (0x40C89C)
        //   0x6575CC len 0x0B " 点声望增加"
        //
        // [+0x4F0] = 声望。字节依据: @ChgSwTo「调整玩家声望」(命令表 idx 107,
        // 处理器 0x0062513F -> sub_6C2148, 已记于 NativeGmPlayerAttrCommands.cs)
        // 在 0x6C21A3 `mov edx,[eax+0x4F0]` 读旧值、0x6C21AC
        // `mov [eax+0x4F0],ebx` 写新值。C# 的声望标量是 TPlayObject.m_nShengWan
        // (PasApiBridge 的 myshengwan / Give"声望" / SetShengWan / 存档
        // HumData.nShengWan), 故 [+0x4F0] <-> m_nShengWan。
        //
        // 注意 native 224 **不发 RM_ABILITY**: 0x65751A 之后直接拼串发 SysMsg,
        // 没有属性刷新虚调。所以这里必须直接加 m_nShengWan, 不能走
        // SetShengWan() —— 后者会多发一条 RM_ABILITY。
        //
        // 发送侧 (仅作对照, 本仓不新增): 0x6BDEC0 `mov dx,0xE0` 走 sub_713890,
        // body = [self+0xC58] + "/"(0x6BDF94 是长度 1 的 ShortString '/') +
        // [self+0x106], nParam = movzx byte[ebp-6]。即「徒弟(自己)升级 -> 给师父
        // 加声望」, 与本 handler 的首段=师父、余段=徒弟完全吻合。该发送侧最终落
        // 0x7138CC 空桩, 在本 build 上发不出去。
        // ===================================================================

        /// <summary>
        /// 战神 sub_6574B4 (OthGs ident 224): 他服的徒弟升级, 给本服的师父加声望
        /// 并提示。body="师父名/徒弟名", <paramref name="points"/> 来自帧头第三个
        /// dword。
        /// </summary>
        internal static void NativeMirrorMentorReputation(string masterName,
            string studentName, int points)
        {
            // 0x6574FB cmp [ebp-8],0 / je —— 只卡首段(师父名), native 不查余段。
            if (string.IsNullOrEmpty(masterName))
            {
                return;
            }

            // 0x657501 mov eax,[0x7D6D50]/[eax]=UserEngine; 0x65750B GetPlayObject;
            // 0x657512 test ebx,ebx / je —— 师父不在本服则静默返回。
            var master = M2Share.UserEngine?.GetPlayObject(masterName);
            if (master == null)
            {
                return;
            }

            // 0x657516 test esi,esi / jle —— 非正数直接退出, 连提示都没有。
            if (points <= 0)
            {
                return;
            }

            // 0x65751A add [master+0x4F0],esi
            master.m_nShengWan = unchecked(master.m_nShengWan + points);

            // 0x657520..0x657547 _LStrCatN(5); 0x65754F cx=0xFCFF; 0x657557 [vmt+0xD4]
            master.SysMsg("恭喜：您的徒弟: " + studentName
                + " 等级提升，给您带来了: " + points + " 点声望增加",
                MsgColor.Blue, MsgType.Hint);
        }
    }
}

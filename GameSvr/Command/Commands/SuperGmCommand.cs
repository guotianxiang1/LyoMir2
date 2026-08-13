using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表 0x007C0B14 `08 "supergm"`，+0x18 = 119，+0x1C = 4。
    // 帮助 ShortString「升级超级GM」。生产 FormGMCommand.ini `119=gm`。
    // jt[119] 槽 0x00622B1C+119*4 = 0x00622CF8 → case@0x00625253：
    //   00625253  8b 45 f8              mov eax,[ebp-8]        ; self
    //   00625256  e8 d1 25 0b 00        call 0x6D782C
    //   0062525B  e9 ec 63 00 00        jmp 0x62B64C           ; 静默出口
    //
    // sub_6D782C 整函数（0x006D782C..0x006D7869）：
    //   006D7833  6a 00 × 6              push 0                 ; SendMsg 六槽全 0
    //   006D783F  66 b9 b0 28            mov cx,0x28B0          ; RM_PASSWORD = 10416
    //   006D7843  8b d3                  mov edx,ebx
    //   006D7845  8b c3                  mov eax,ebx
    //   006D7847  e8 1c e6 08 00         call 0x765E68          ; 入队 RM_PASSWORD
    //   006D784C  66 b9 db ff            mov cx,0xFFDB          ; 绿
    //   006D7850  ba 74 78 6d 00         mov edx,0x6D7874       ; 长串 len=10「请输入密码」
    //   006D7859  ff 96 d4 00 00 00      call [esi+0xD4]        ; SysMsg
    //   006D785F  c6 83 74 06 00 00 01   mov byte [ebx+0x674],1
    //   006D7869  c3                     ret
    //
    // 0x006D1C20 不是这条命令：那是 idx 222 UpUserHeroLv 的核心
    // （0x0062615E call 0x6D1C20，字符串「正确格式 @UpUserHeroLv <人物名> <英雄新等级>」）。
    //
    // 权限上限是注册表 +0x1C = 4，原生从不写 m_btPermission=10。
    // 「超级GM模式已激活 / 权限等级: 10 / 无敌 / 隐身 / 管理员」全镜像 GBK 0 命中。
    [GameCommand("SuperGm", "升级超级GM", 4)]
    public class SuperGmCommand : BaseCommond
    {
        internal const string NativePasswordPrompt = "请输入密码";

        [DefaultCommand]
        public void SuperGm(TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;
            PlayObject.SendMsg(PlayObject, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
            PlayObject.SysMsg(NativePasswordPrompt, MsgColor.Green, MsgType.Hint);
            PlayObject.m_boWaitSuperGmPassword = true;
        }
    }
}

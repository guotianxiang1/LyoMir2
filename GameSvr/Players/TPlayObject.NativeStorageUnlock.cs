using System;
using System.Globalization;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>战神 obj+0xB77：非零时必须走 CD 卡页解锁。</summary>
        internal bool m_boNativeCdCardStorageGate;

        /// <summary>
        /// 仓库 CD 卡页解锁 <c>sub_6C8FB0</c>，由 SM 输入框 inputType=0x17 触发
        /// (<c>sub_6DD290</c> jump @0x6DD3C2)。
        /// </summary>
        internal void TryNativeStorageUnlockFromCdCard(string inputText)
        {
            const uint CoreEa = 0x006C8FB0;
            _ = CoreEa;

            if (string.IsNullOrWhiteSpace(inputText))
                return;

            // 0x6C8FD7 call 0x6C7D88(player,1) — equipment-secret lock absent here.
            // 0x6C8FE4 cmp [+0x683],0
            if (!m_boCanGetBackItem)
                return;

            // 0x6C8FF1 cmp [+0xB77],0 / jne CD-card redirect
            if (m_boNativeCdCardStorageGate)
            {
                SysMsg("错误: 请到CD卡页面操作仓库解锁", MsgColor.Red, MsgType.Hint);
                return;
            }

            // 0x6C900F Now - [+0x780]; 0x6C901A mul 86400; compare > 3 minutes.
            // 0x6C9014 fsub [+0x780]; 0x6C901A fmul 1440.0 — minutes since login base.
            var minutesOnline = (DateTime.Now - m_dLogonTime).TotalMinutes;
            if (minutesOnline <= 3.0)
            {
                SysMsg("[错误]: 登录游戏3分钟后,方可执行该操作", MsgColor.Red, MsgType.Hint);
                return;
            }

            if (!int.TryParse(inputText.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var cardPage) || cardPage <= 0)
                return;

            // 0x6C9056 call 0x6C5438(player, page, ident=0x11) — CD卡请求; async half BLOCKED.
            _nativePendingCdCardStoragePage = cardPage;

            SysMsg("系统已接受解锁申请，处理中...", MsgColor.Red, MsgType.Hint);
        }

        private int _nativePendingCdCardStoragePage;
    }
}

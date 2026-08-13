using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // Perm-0 player commands that SET (or XOR) native obj+0xB9C / +0xBA0 bits.
    // Native does not toggle 拒绝私聊/允许私聊; C# Chat.cs @AllowMsg toggle is a
    // leftover English GOM name and is not these registry keys.

    /// <summary>idx=1 perm=0 case@0x006236D8: or byte [eax+0xB9C],1; SysMsg 0x38FF "拒绝接收私聊信息".</summary>
    [GameCommand("拒绝私聊", "拒绝私聊", 0)]
    public class RefuseWhisperCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.m_dwChatShieldMask |= 0x01u;
            playObject.ApplyChatShieldMaskToAllowFlags();
            playObject.SysMsg("拒绝接收私聊信息", MsgColor.Red, MsgType.Hint);
        }
    }

    /// <summary>idx=2 perm=0 case@0x006236FB: and byte [eax+0xB9C],0xFE; SysMsg 0xFFDB "允许接收私聊信息".</summary>
    [GameCommand("允许私聊", "允许私聊", 0)]
    public class AllowWhisperCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.m_dwChatShieldMask &= ~0x01u;
            playObject.ApplyChatShieldMaskToAllowFlags();
            playObject.SysMsg("允许接收私聊信息", MsgColor.Green, MsgType.Hint);
        }
    }

    /// <summary>idx=6 perm=0 case@0x006237AC: or byte [eax+0xB9C],4; no SysMsg.</summary>
    [GameCommand("拒绝喊话", "拒绝喊话", 0)]
    public class RefuseShoutCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.m_dwChatShieldMask |= 0x04u;
            playObject.ApplyChatShieldMaskToAllowFlags();
        }
    }

    /// <summary>idx=7 perm=0 case@0x006237BB: and byte [eax+0xB9C],0xFB; no SysMsg.</summary>
    [GameCommand("接受喊话", "接受喊话", 0)]
    public class AcceptShoutCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.m_dwChatShieldMask &= ~0x04u;
            playObject.ApplyChatShieldMaskToAllowFlags();
        }
    }

    /// <summary>
    /// idx=8 perm=0 case@0x006237CA: xor byte [eax+0xBA0],1 then
    /// nonzero → 0xFFDB "[可以交易]"; zero → 0xFFDB "[拒绝交易]".
    /// </summary>
    [GameCommand("拒绝交易", "拒绝或允许交易", 0)]
    public class RefuseDealCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.m_boAllowDeal = !playObject.m_boAllowDeal;
            playObject.SysMsg(playObject.m_boAllowDeal ? "[可以交易]" : "[拒绝交易]",
                MsgColor.Green, MsgType.Hint);
        }
    }

    /// <summary>idx=9 perm=0 case@0x00623812: mov byte [eax+0xBA0],1; SysMsg 0xFFDB "[可以交易]".</summary>
    [GameCommand("允许交易", "允许交易", 0)]
    public class AllowDealCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.m_boAllowDeal = true;
            playObject.SysMsg("[可以交易]", MsgColor.Green, MsgType.Hint);
        }
    }

    /// <summary>idx=19 perm=0 case@0x00623972: and byte [eax+0xB9C],0xF7; no SysMsg.</summary>
    [GameCommand("允许行会聊天", "允许行会聊天", 0)]
    public class AllowGuildChatCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.m_dwChatShieldMask &= ~0x08u;
            playObject.ApplyChatShieldMaskToAllowFlags();
        }
    }

    /// <summary>idx=20 perm=0 case@0x00623981: or byte [eax+0xB9C],8; no SysMsg.</summary>
    [GameCommand("拒绝行会聊天", "拒绝行会聊天", 0)]
    public class RefuseGuildChatCommand : BaseCommond
    {
        [DefaultCommand]
        public void Execute(TPlayObject playObject)
        {
            playObject.m_dwChatShieldMask |= 0x08u;
            playObject.ApplyChatShieldMaskToAllowFlags();
        }
    }
}

namespace GameGate.Models;

/// <summary>Game protocol command IDs from 40-range dispatch table.</summary>
public enum GameCommand : ushort
{
    // Client → Server
    CM_LOGIN = 0x0001, CM_LOGOUT = 0x0002,
    CM_WALK = 0x0003, CM_RUN = 0x0004, CM_TURN = 0x0005,
    CM_ATTACK = 0x0006, CM_MAGIC = 0x0007, CM_CHAT = 0x0008,
    CM_PICKUP = 0x0009, CM_DROP = 0x000A,
    CM_USE_ITEM = 0x000B, CM_EQUIP = 0x000C, CM_UNEQUIP = 0x000D,
    CM_BUY = 0x000E, CM_SELL = 0x000F,
    CM_NPC_CLICK = 0x0010, CM_TRADE = 0x0011, CM_CAST = 0x0012,

    // Server → Client
    SM_LOGIN_OK = 0x0100, SM_MOVE = 0x0101, SM_ATTACK = 0x0102,
    SM_MAGIC = 0x0103, SM_CHAT = 0x0104, SM_ITEM_UPDATE = 0x0105,
    SM_STATUS = 0x0106, SM_NPC_DIALOG = 0x0107, SM_MAP_CHANGE = 0x0108,
    SM_SYS_MSG = 0x0109, SM_SPEED_WARN = 0x010A, SM_KICK = 0x010B,

    // Gateway → M2Server
    GM_GATE_HELLO = 0x0384, GM_GATE_DATA = 0x0385,
    GM_GATE_HEARTBEAT = 0x0386, GM_GATE_CLOSE = 0x0387,
    GM_CHECK_SERVER = 0x0388, GM_CHECK_CLIENT = 0x0389,
    GM_RECEIVE_OK = 0x038A,
    GM_SPEED_VIOLATION = 0x038B, GM_SPEED_WARNING = 0x038C,
}

public enum ActionType : byte
{
    WALK = 0, RUN = 1, CAST = 2, ATTACK = 3, TURN = 4,
    BUY = 5, CURE = 6, NPC = 7, TRADE = 8, CHAT = 9,
}

public enum SessionState : byte { FREE = 0, ACTIVE = 1, BANNED = 2, MUTED = 3, CLOSING = 4 }

public enum PenaltyLevel : byte { NONE = 0, WARNED = 1, OBSERVED = 2, MUTED = 3, BANNED = 4 }

public enum ChannelType : byte { UPSTREAM = 0, UP_DELAY = 1, DOWNSTREAM = 2, DOWN_DELAY = 3 }

using System.Collections.Generic;

namespace SystemModule
{
    /// <summary>手游客户端命令号 ↔ LyoMir2命令号 双向映射(仅登录/选服/选角阶段)。进游戏后命令号一致无需映射。</summary>
    public static class MobileCmdMap
    {
        private static readonly Dictionary<ushort, ushort> _toServer = new()
        {
            { 4002, 104 }, // CM_SELECT_SERVER     → CM_SELECTSERVER
            { 4012, 101 }, // CM_NEWCHR             → CM_NEWCHR
            { 4013, 102 }, // CM_DELCHR
            { 4014, 105 }, // CM_QUERYDELCHR
            { 4015, 106 }, // CM_RECOVERCHR
            { 4017, 103 }, // CM_SELCHR             → CM_SELCHR
        };

        private static readonly Dictionary<ushort, ushort> _toClient = new();

        static MobileCmdMap()
        {
            foreach (var kv in _toServer)
                _toClient[kv.Value] = kv.Key;

            _toClient[Grobal2.SM_QUERYCHR] = 4010;          // SM_CHR_LIST
            _toClient[Grobal2.SM_QUERYCHR_FAIL] = 4010;
            _toClient[Grobal2.SM_SELECTSERVER_OK] = 4002;
            _toClient[(ushort)4004] = 4004;                 // SM_LOGIN_AUTH (DBSvr直接发4004)
            _toClient[Grobal2.SM_NEWCHR_SUCCESS] = 4012;
            _toClient[Grobal2.SM_NEWCHR_FAIL] = 4012;
            _toClient[Grobal2.SM_DELCHR_SUCCESS] = 4013;
            _toClient[Grobal2.SM_DELCHR_FAIL] = 4013;
            _toClient[Grobal2.SM_QUERYDELCHR] = 4014;
            _toClient[Grobal2.SM_QUERYDELCHR_FAIL] = 4014;
            _toClient[Grobal2.SM_RESDELCHR_SUCCESS] = 4015;
            _toClient[Grobal2.SM_RESDELCHR_FAIL] = 4015;
            _toClient[536] = 4015;
            _toClient[537] = 4015;
            _toClient[Grobal2.SM_STARTPLAY] = 4017;
            _toClient[Grobal2.SM_STARTFAIL] = 4017;
            _toServer[4039] = 4039;  // CM_SELCHR_EXIT
            _toClient[4039] = 4039;  // SM_SELCHR_EXIT
        }

        /// <summary>客户端发来的命令号 → LyoMir2内部命令号。不需要映射时返回原值。</summary>
        public static ushort ToServer(ushort clientIdent)
            => _toServer.TryGetValue(clientIdent, out var v) ? v : clientIdent;

        /// <summary>LyoMir2内部命令号 → 客户端命令号。不需要映射时返回原值。</summary>
        public static ushort ToClient(ushort serverIdent)
            => _toClient.TryGetValue(serverIdent, out var v) ? v : serverIdent;

        /// <summary>是否需要映射(登录链)。</summary>
        public static bool NeedMap(ushort ident) => _toServer.ContainsKey(ident) || _toClient.ContainsKey(ident);
    }
}

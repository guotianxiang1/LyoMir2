namespace SystemModule
{
    public class TProcessMessage
    {
        public int wIdent;
        public int wParam;
        public int nParam1;
        public int nParam2;
        public int nParam3;
        public int dwDeliveryTime;
        public int BaseObject;
        public bool boLateDelivery;
        public string sMsg;
        public object Payload;

        /// <summary>
        /// Wire body length in bytes = total packet length minus the 12-byte header. This is the
        /// FOURTH parameter 战神 passes to its CM dispatcher (sub_6D7D68): the caller computes it
        /// at 0x6B1B11 `movzx esi,word [node+8]` / 0x6B1B15 `sub esi,0x0C` and pushes it at
        /// 0x6B1B2C, and the dispatcher keeps it in ESI plus a zero-extended copy in EDI
        /// (0x6D7DA8 `0F B7 FE movzx edi,si`). 39 handlers branch on it and ~25 more pass it on
        /// to their callee, so it has to survive the hop through the player message queue.
        /// 0 for messages that were not produced by a client packet.
        /// </summary>
        public int nBodyLen;
    }
}

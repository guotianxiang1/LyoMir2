namespace GameSvr
{
    public struct SendMessage
    {
        public int wIdent;
        public int wParam;
        public int nParam1;
        public int nParam2;
        public int nParam3;
        public int dwDeliveryTime;
        public TBaseObject BaseObject;
        public int ObjectId;
        public bool boLateDelivery;
        public string Buff;
        public object Payload;

        /// <summary>
        /// Wire body length (total packet length - 12) for messages that came off a client
        /// packet; 0 for everything the server generates itself. Carries 战神's fourth CM
        /// dispatcher parameter (sub_6D7D68 ESI/EDI, pushed at 0x6B1B2C) across the queue hop.
        /// </summary>
        public int nBodyLen;
    }

    
    
    
    public class TVisibleBaseObject
    {
        public TBaseObject BaseObject;
        public int nVisibleFlag;
    }

    public enum PlayGender : byte
    {
        Man = 0,
        WoMan = 1
    }
}

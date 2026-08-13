namespace GameSvr
{
    public class MonGenInfo
    {
        public string sMapName;
        public int nX;
        public int nY;
        public string sMonName;
        public int nRange;
        public int nCount;
        public int nActiveCount;
        public int dwZenTime;
        public int nMissionGenRate;
        public IList<TBaseObject> CertList;
        public int CertCount;
        public object Envir;
        public int nRace;
        public int dwStartTick;
        /// <summary>
        /// 战神 <c>dword[gen+0x38]</c>: consecutive factory failures for this generator.
        /// The worker bumps it when the factory returns nil (<c>0x67CAB8 inc dword
        /// [ebx+0x38]</c>) and zeroes it on any success (<c>0x67CAA7 xor eax,eax /
        /// 0x67CAA9 mov [ebx+0x38],eax</c>).  Its only reader turns it into the
        /// factory's fourth argument:
        /// <code>
        /// 67CA2B  83 7B 38 05  cmp dword [ebx+0x38],5
        /// 67CA2F  0F 9D C0     setge al
        /// 67CA32  50           push eax
        /// </code>
        /// which sub_679F8C forwards twice into sub_7782D0 (0x679FE9 / 0x679FED) and
        /// sub_7782D0 hands to CanWalk sub_777EF8 at 0x77834B, where
        /// <c>0x777F70 cmp byte [ebp+8],0 / jne</c> returns true without scanning the
        /// cell's object list.  So five failures in a row let the next attempt land on
        /// a tile another creature is standing on.
        /// </summary>
        public int nFailCount;
    }
}
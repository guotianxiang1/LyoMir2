using System;

namespace SystemModule
{
    public class IdWorker
    {
        
        private static long workerId;
        private static long twepoch = 687888001020L; 
        private static long sequence = 0L;
        private static int workerIdBits = 4; 
        public static long maxWorkerId = -1L ^ -1L << workerIdBits; 
        private static int sequenceBits = 10; 
        private static int workerIdShift = sequenceBits; 
        private static int timestampLeftShift = sequenceBits + workerIdBits; 
        public static long sequenceMask = -1L ^ -1L << sequenceBits; 
        private long lastTimestamp = -1L;

        
        
        
        
        public IdWorker(long workerId)
        {
            if (workerId > maxWorkerId || workerId < 0)
                throw new Exception(string.Format("worker Id can't be greater than {0} or less than 0 ", workerId));
            IdWorker.workerId = workerId;
        }

        public long nextId()
        {
            lock (this)
            {
                long timestamp = TimeGen();
                if (this.lastTimestamp == timestamp)
                {
                    
                    IdWorker.sequence = (IdWorker.sequence + 1) & IdWorker.sequenceMask; 
                    if (IdWorker.sequence == 0)
                    {
                        
                        timestamp = TillNextMillis(this.lastTimestamp);
                    }
                }
                else
                {
                    
                    IdWorker.sequence = 0; 
                }

                if (timestamp < lastTimestamp)
                {
                    
                    throw new Exception(string.Format(
                        "Clock moved backwards.  Refusing to generate id for {0} milliseconds",
                        this.lastTimestamp - timestamp));
                }

                this.lastTimestamp = timestamp; 
                long nextId = (timestamp - twepoch << timestampLeftShift) |
                              IdWorker.workerId << IdWorker.workerIdShift | IdWorker.sequence;
                return nextId;
            }
        }

        
        
        
        
        
        private long TillNextMillis(long lastTimestamp)
        {
            long timestamp = TimeGen();
            while (timestamp <= lastTimestamp)
            {
                timestamp = TimeGen();
            }
            return timestamp;
        }

        
        
        
        
        private long TimeGen()
        {
            return DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }
    }
}
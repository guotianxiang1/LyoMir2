using System;
using System.Threading;

namespace SystemModule
{
    
    
    
    public class TimestampID
    {
        private long _lastTimestamp;
        private long _sequence; 
        private readonly DateTime? _initialDateTime;
        private static TimestampID _timestampID;
        private const int MAX_END_NUMBER = 9999;

        private TimestampID(DateTime? initialDateTime)
        {
            _initialDateTime = initialDateTime;
        }

        
        
        
        
        
        public static TimestampID GetInstance(DateTime? initialDateTime = null)
        {
            if (_timestampID == null) Interlocked.CompareExchange(ref _timestampID, new TimestampID(initialDateTime), null);
            return _timestampID;
        }

        
        
        
        protected DateTime InitialDateTime
        {
            get
            {
                if (_initialDateTime == null || _initialDateTime.Value == DateTime.MinValue) return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return _initialDateTime.Value;
            }
        }

        
        
        
        
        public string GetID()
        {
            long temp;
            var timestamp = GetUniqueTimeStamp(_lastTimestamp, out temp);
            return string.Format("{0}{Fill({1})}", timestamp, temp);
        }

        
        
        
        
        public long GetUniqueTimeStamp(long lastTimeStamp, out long temp)
        {
            lock (this)
            {
                temp = 1;
                var timeStamp = GetTimestamp();
                if (timeStamp == _lastTimestamp)
                {
                    _sequence = _sequence + 1;
                    temp = _sequence;
                    if (temp >= MAX_END_NUMBER)
                    {
                        timeStamp = GetTimestamp();
                        _lastTimestamp = timeStamp;
                        temp = _sequence = 1;
                    }
                }
                else
                {
                    _sequence = 1;
                    _lastTimestamp = timeStamp;
                }
                return timeStamp;
            }
        }

        
        
        
        
        private long GetTimestamp()
        {
            if (InitialDateTime >= DateTime.Now) throw new Exception("最初时间比当前时间还大，不合理");
            var ts = DateTime.UtcNow - InitialDateTime;
            return (long)ts.TotalMilliseconds;
        }
    }
}

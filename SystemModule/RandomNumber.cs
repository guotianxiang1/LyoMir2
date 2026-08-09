using System;
using System.Collections.Generic;

namespace SystemModule
{
    
    
    
    public class RandomNumber
    {
        private static Random random = null;

        
        private static RandomNumber singleton;
        
        
        private static readonly object syncObject = new object();

        private RandomNumber() { }

        public static RandomNumber GetInstance()
        {
            if (singleton == null)
            {
                lock (syncObject)
                {
                    if (singleton == null)
                    {
                        random = new Random();
                        singleton = new RandomNumber();
                    }
                }
            }
            return singleton;
        }

        
        
        
        
        
        
        public IList<int> RandomSelect(IList<int> sourceList, int selectCount)
        {
            if (selectCount > sourceList.Count)
                throw new ArgumentOutOfRangeException("selectCount必需大于sourceList.Count");
            IList<int> resultList = new List<int>();
            for (int i = 0; i < selectCount; i++)
            {
                int nextIndex = GetRandomNumber(1, sourceList.Count);
                int nextNumber = sourceList[nextIndex - 1];
                sourceList.RemoveAt(nextIndex - 1);
                resultList.Add(nextNumber);
            }
            return resultList;
        }

        
        
        
        
        
        
        public int GetRandomNumber(int minValue, int maxValue)
        {
            int result = random.Next(minValue, maxValue + 1);
            if (RngTraceSink.Enabled) RngTraceSink.Record(RngTraceApi.GetRandomNumber, minValue, maxValue, result, 0, 0);
            return result;
        }

        
        
        
        
        
        public int Random()
        {
            int result = random.Next();
            if (RngTraceSink.Enabled) RngTraceSink.Record(RngTraceApi.ParamlessAdvance, 0, 0, result, 0, 0);
            return result;
        }

        
        
        
        
        
        public int Random(int Value)
        {
            int result = random.Next(Value);
            if (RngTraceSink.Enabled) RngTraceSink.Record(RngTraceApi.Random, Value, 0, result, 0, 0);
            return result;
        }

        
        
        
        
        
        public int Random(int minValue, int maxValue)
        {
            int result = random.Next(minValue, maxValue);
            if (RngTraceSink.Enabled) RngTraceSink.Record(RngTraceApi.RandomMinMax, minValue, maxValue, result, 0, 0);
            return result;
        }
    }
}
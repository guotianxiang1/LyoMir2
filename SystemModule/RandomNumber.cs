using System;
using System.Collections.Generic;

namespace SystemModule
{
    // POIS-26 — every gameplay draw goes through this facade, and it used to sit on
    // .NET System.Random. The original draws come from the Delphi bounded LCG
    // sub_403B4C @0x00403B4C (bytes verified against flat_image.bin):
    //
    //   00403B4C  53                        push ebx
    //   00403B4D  31 DB                     xor  ebx, ebx
    //   00403B4F  69 93 08 20 7A 00 05 84 08 08  imul edx,[ebx+0x7A2008],0x08088405
    //   00403B59  42                        inc  edx
    //   00403B5A  89 93 08 20 7A 00         mov  [ebx+0x7A2008], edx
    //   00403B60  F7 E2                     mul  edx                 ; eax = bound
    //   00403B62  89 D0                     mov  eax, edx            ; take the high 32
    //   00403B64  5B / C3                   pop  ebx / ret
    //
    // so result = high32((uint32)bound * (seed*0x08088405 + 1)). That distribution is
    // low-biased whenever bound does not divide 2^32, and every drop rate, poison
    // chance and critical roll in the original is tuned on top of that bias;
    // Random.Next is approximately uniform, so all of them shifted.
    //
    // Two semantic differences the .NET path had, both resolved by delegating:
    //  * Random(0): Delphi still advances the seed and returns 0; Next(0) returns 0
    //    without advancing, which desynchronised the sequence at every deliberate
    //    advance site (the three `_ = M2Share.RandomNumber.Random()` calls).
    //  * negative bound: Delphi participates as the UInt32 bit pattern; Next throws.
    //
    // DelphiRandom is process-global and lock-linearised, which also removes the
    // unsynchronised System.Random instance that the gameplay and AI threads shared.
    public class RandomNumber
    {
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

        /// <summary>Inclusive of max: min + Random(max - min + 1).</summary>
        public int GetRandomNumber(int minValue, int maxValue)
        {
            int result = DelphiRandomNumberFacade.GetRandomNumber(minValue, maxValue);
            if (RngTraceSink.Enabled) RngTraceSink.Record(RngTraceApi.GetRandomNumber, minValue, maxValue, result, 0, 0);
            return result;
        }

        /// <summary>
        /// Deliberate seed advance, the original Random(0): steps the seed once and
        /// yields 0. Every caller discards the value.
        /// </summary>
        public int Random()
        {
            int result = DelphiRandomNumberFacade.Advance();
            if (RngTraceSink.Enabled) RngTraceSink.Record(RngTraceApi.ParamlessAdvance, 0, 0, result, 0, 0);
            return result;
        }

        /// <summary>Bounded draw, sub_403B4C.</summary>
        public int Random(int Value)
        {
            int result = DelphiRandomNumberFacade.Random(Value);
            if (RngTraceSink.Enabled) RngTraceSink.Record(RngTraceApi.Random, Value, 0, result, 0, 0);
            return result;
        }

        /// <summary>Half-open [min, max): min + Random(max - min).</summary>
        public int Random(int minValue, int maxValue)
        {
            int result = DelphiRandomNumberFacade.Random(minValue, maxValue);
            if (RngTraceSink.Enabled) RngTraceSink.Record(RngTraceApi.RandomMinMax, minValue, maxValue, result, 0, 0);
            return result;
        }
    }
}

namespace SystemModule
{
    public class WeightedItem<T>
    {
        
        
        
        public int Weight;

        
        
        
        public readonly T Value;

        
        
        
        internal int CumulativeWeight;

        public WeightedItem(T value, int weight)
        {
            Value = value;
            Weight = weight;
            CumulativeWeight = 0;
        }
    }
}
namespace SystemModule
{
    public class SelectorOption
    {
        
        
        
        public bool AllowDuplicate { get; set; }

        
        
        
        public bool RemoveZeroWeightItems { get; set; }

        public SelectorOption()
        {
            AllowDuplicate = false;
            RemoveZeroWeightItems = true;
        }
    }
}
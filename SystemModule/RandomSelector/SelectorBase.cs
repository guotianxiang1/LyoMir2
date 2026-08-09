using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemModule
{
    internal abstract class SelectorBase<T>
    {
        protected readonly WeightedSelector<T> WeightedSelector;

        internal SelectorBase(WeightedSelector<T> weightedSelector)
        {
            WeightedSelector = weightedSelector;
        }

        
        
        
        internal WeightedItem<T> BinarySelect(List<WeightedItem<T>> items)
        {
            if (items.Count == 0)
            {
                throw new InvalidOperationException("没有元素可以筛选");
            }

            int index = Array.BinarySearch(WeightedSelector.CumulativeWeights, new Random().Next(1, items.Sum(i => i.Weight) + 1));
            
            if (index < 0)
            {
                index = -index - 1;
            }

            return items[index];
        }

        
        
        
        
        
        internal WeightedItem<T> LinearSelect(List<WeightedItem<T>> items)
        {
            
            if (!items.Any())
            {
                throw new InvalidOperationException("没有元素可以筛选");
            }

            var count = 0;
            var seed = new Random().Next(1, items.Sum(i => i.Weight) + 1);
            foreach (var item in items)
            {
                count += item.Weight;
                if (seed <= count)
                {
                    return item;
                }
            }

            return items.FirstOrDefault();
        }
    }
}
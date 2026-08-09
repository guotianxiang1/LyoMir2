using System;
using System.Collections;
using System.Collections.Generic;

namespace SystemModule
{
    
    
    
    
    public class WeightedSelector<T> : IEnumerable<T>
    {
        internal readonly List<WeightedItem<T>> Items = new List<WeightedItem<T>>();
        internal readonly SelectorOption Option;

        
        
        
        internal int[] CumulativeWeights;

        
        
        
        private bool _isAddedCumulativeWeights;

        public WeightedSelector(SelectorOption option = null)
        {
            Option = option ?? new SelectorOption();
        }

        public WeightedSelector(List<WeightedItem<T>> items, SelectorOption option = null) : this(option)
        {
            Add(items);
        }

        public WeightedSelector(IEnumerable<WeightedItem<T>> items, SelectorOption option = null) : this(option)
        {
            Add(items);
        }

        
        
        
        
        public void Add(WeightedItem<T> item)
        {
            if (item.Weight <= 0)
            {
                if (Option.RemoveZeroWeightItems)
                {
                    return;
                }

                throw new InvalidOperationException("权重值不能为0");
            }

            _isAddedCumulativeWeights = true;
            Items.Add(item);
        }

        
        
        
        
        public void Add(IEnumerable<WeightedItem<T>> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        
        
        
        
        
        public void Add(T item, int weight)
        {
            Add(new WeightedItem<T>(item, weight));
        }

        
        
        
        
        public void Remove(WeightedItem<T> item)
        {
            _isAddedCumulativeWeights = true;
            Items.Remove(item);
        }

        
        
        
        public T Select()
        {
            CalculateCumulativeWeights();
            var selector = new SingleSelector<T>(this);
            return selector.Select();
        }

        
        
        
        public List<T> SelectMultiple(int count)
        {
            CalculateCumulativeWeights();
            var selector = new MultipleSelector<T>(this);
            return selector.Select(count);
        }

        
        
        
        private void CalculateCumulativeWeights()
        {
            if (!_isAddedCumulativeWeights) 
            {
                return;
            }

            _isAddedCumulativeWeights = false;
            CumulativeWeights = GetCumulativeWeights(Items);
        }

        
        
        
        
        
        
        public static int[] GetCumulativeWeights(List<WeightedItem<T>> items)
        {
            int totalWeight = 0;
            int index = 0;
            var results = new int[items.Count + 1];

            foreach (var item in items)
            {
                totalWeight += item.Weight;
                results[index] = totalWeight;
                index++;
            }

            return results;
        }
        public IEnumerator<T> GetEnumerator()
        {
            return Items.GetEnumerator() as IEnumerator<T>;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
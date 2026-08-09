using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SystemModule.Common
{
    [Serializable]
    public class EventArgs<T> : System.EventArgs
    {
        public T Argument;

        public EventArgs() : this(default(T))
        {
        }

        public EventArgs(T argument)
        {
            Argument = argument;
        }
    }

    public class AsynQueue<T>
    {
        
        private int isProcessing;
        
        private const int Processing = 1;
        
        private const int UnProcessing = 0;
        
        private volatile bool enabled = true;
        private Task currentTask;
        public event Action<T> ProcessItemFunction;
        public event EventHandler<EventArgs<Exception>> ProcessException;
        private readonly ConcurrentQueue<T> queue;

        public AsynQueue()
        {
            queue = new ConcurrentQueue<T>();
        }

        public int Count
        {
            get
            {
                return queue.Count;
            }
        }

        public void Start()
        {
            Thread process_Thread = new Thread(PorcessItem)
            {
                IsBackground = true
            };
            process_Thread.Start();
        }

        public void Enqueue(T items)
        {
            if (items != null)
            {
                queue.Enqueue(items);
                DataAdded();
            }
            else
            {
                throw new ArgumentException("items");
            }
        }

        
        private void DataAdded()
        {
            if (enabled)
            {
                if (!IsProcessingItem())
                {
                    currentTask = Task.Factory.StartNew(ProcessItemLoop);
                }
            }
        }

        
        private bool IsProcessingItem()
        {
            return !(Interlocked.CompareExchange(ref isProcessing, Processing, UnProcessing) == 0);
        }

        private void ProcessItemLoop()
        {
            if (!enabled && queue.IsEmpty)
            {
                Interlocked.Exchange(ref isProcessing, 0);
                return;
            }
            
            
            
            T publishFrame;
            if (queue.TryDequeue(out publishFrame))
            {
                try
                {
                    ProcessItemFunction(publishFrame);
                }
                catch (Exception ex)
                {
                    OnProcessException(ex);
                }
            }

            if (enabled && !queue.IsEmpty)
            {
                currentTask = Task.Factory.StartNew(ProcessItemLoop);
            }
            else
            {
                Interlocked.Exchange(ref isProcessing, UnProcessing);
            }
        }

        
        
        
        
        private void PorcessItem(object state)
        {
            int sleepCount = 0;
            int sleepTime = 1000;
            while (enabled)
            {
                
                if (queue.IsEmpty)
                {
                    if (sleepCount == 0)
                    {
                        sleepTime = 1000;
                    }
                    else if (sleepCount <= 3)
                    {
                        sleepTime = 1000 * 3;
                    }
                    else
                    {
                        sleepTime = 1000 * 50;
                    }
                    sleepCount++;
                    Thread.Sleep(sleepTime);
                }
                else
                {
                    
                    if (enabled && Interlocked.CompareExchange(ref isProcessing, Processing, UnProcessing) == 0)
                    {
                        if (!queue.IsEmpty)
                        {
                            currentTask = Task.Factory.StartNew(ProcessItemLoop);
                        }
                        else
                        {
                            Interlocked.Exchange(ref isProcessing, 0);
                        }
                        sleepCount = 0;
                        sleepTime = 1000;
                    }
                }
            }
        }

        public void Flsuh()
        {
            Stop();

            if (currentTask != null)
            {
                currentTask.Wait();
            }

            while (!queue.IsEmpty)
            {
                try
                {
                    T publishFrame;
                    if (queue.TryDequeue(out publishFrame))
                    {
                        ProcessItemFunction(publishFrame);
                    }
                }
                catch (Exception ex)
                {
                    OnProcessException(ex);
                }
            }
            currentTask = null;
        }

        public void Stop()
        {
            this.enabled = false;
        }

        private void OnProcessException(System.Exception ex)
        {
            var tempException = ProcessException;
            Interlocked.CompareExchange(ref ProcessException, null, null);

            if (tempException != null)
            {
                ProcessException(ex, new EventArgs<Exception>(ex));
            }
        }
    }
}

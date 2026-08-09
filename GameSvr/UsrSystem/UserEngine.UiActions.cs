using System.Collections.Concurrent;

namespace GameSvr
{
    public partial class UserEngine
    {
        private readonly ConcurrentQueue<Action> _uiActions = new();

        public async Task<T> InvokeFromUiAsync<T>(Func<T> action, int timeoutMilliseconds = 5000)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!M2Share.boStartReady || _stopRequested)
                throw new InvalidOperationException("游戏处理引擎尚未运行");

            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var expired = 0;
            _uiActions.Enqueue(() =>
            {
                if (Volatile.Read(ref expired) != 0)
                {
                    completion.TrySetCanceled();
                    return;
                }
                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });

            try
            {
                return await completion.Task.WaitAsync(
                    TimeSpan.FromMilliseconds(Math.Max(250, timeoutMilliseconds)));
            }
            catch (TimeoutException)
            {
                Interlocked.Exchange(ref expired, 1);
                throw;
            }
        }

        private void ProcessUiActions()
        {
            const int maximumPerCycle = 32;
            for (var index = 0; index < maximumPerCycle && _uiActions.TryDequeue(out var action); index++)
                action();
        }
    }
}

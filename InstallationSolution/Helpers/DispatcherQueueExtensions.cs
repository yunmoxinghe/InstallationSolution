using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;

namespace InstallationSolution.Helpers
{
    /// <summary>
    /// DispatcherQueue 扩展方法
    /// </summary>
    public static class DispatcherQueueExtensions
    {
        /// <summary>
        /// 异步地在 UI 线程上执行操作，并等待完成
        /// </summary>
        public static Task EnqueueAsync(this DispatcherQueue dispatcher, Action action, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
        {
            var tcs = new TaskCompletionSource<bool>();

            bool enqueued = dispatcher.TryEnqueue(priority, () =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!enqueued)
            {
                tcs.SetException(new InvalidOperationException("无法将操作加入 UI 线程队列"));
            }

            return tcs.Task;
        }

        /// <summary>
        /// 异步地在 UI 线程上执行操作并返回结果
        /// </summary>
        public static Task<T> EnqueueAsync<T>(this DispatcherQueue dispatcher, Func<T> func, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
        {
            var tcs = new TaskCompletionSource<T>();

            bool enqueued = dispatcher.TryEnqueue(priority, () =>
            {
                try
                {
                    var result = func();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!enqueued)
            {
                tcs.SetException(new InvalidOperationException("无法将操作加入 UI 线程队列"));
            }

            return tcs.Task;
        }
    }
}

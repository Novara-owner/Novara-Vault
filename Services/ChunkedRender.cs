using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace Novara.Services;

/// <summary>



/// </summary>
public static class ChunkedRender
{
    /// <summary>
    
    
    /// </summary>
    public static Task RunAsync(int total, int batchSize, DispatcherQueue queue, Action<int, int> processBatch, CancellationToken token = default)
    {
        if (total <= 0)
            return Task.CompletedTask;
        if (token.IsCancellationRequested)
            return Task.FromCanceled(token);
        if (batchSize <= 0) batchSize = 1; 

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int index = 0;

        void Next()
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }
            if (index >= total)
            {
                tcs.TrySetResult();
                return;
            }
            int start = index;
            int end = Math.Min(index + batchSize, total);
            index = end;
            try
            {
                processBatch(start, end);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                return;
            }
            
            if (!queue.TryEnqueue(DispatcherQueuePriority.Low, Next))
                tcs.TrySetException(new InvalidOperationException("DispatcherQueue is shutting down"));
        }

        
        
        Next();
        return tcs.Task;
    }
}

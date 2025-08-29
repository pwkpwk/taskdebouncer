namespace TaskDebouncer.Implementation;

public class TaskResultHub<TArg, TResult>(
    TArg arg,
    TaskDebouncer<TArg, TResult> debouncer) where TArg : notnull
{
    private readonly LinkedList<Tracker> _trackers = new();
    private readonly ReaderWriterLockSlim _guard = new(LockRecursionPolicy.SupportsRecursion);
    
    public void Start(Func<TArg, Task<TResult>> taskFactory)
    {
        taskFactory(arg).ContinueWith(OnTaskFinished, TaskContinuationOptions.ExecuteSynchronously);
    }
    
    public Task<TResult> Add(CancellationToken cancellationToken)
    {
        LinkedListNode<Tracker> node = new(new());

        _guard.EnterWriteLock();
        _trackers.AddLast(node);
        _guard.ExitWriteLock();
        
        cancellationToken.Register(() =>
        {
            _guard.EnterWriteLock();
            _trackers.Remove(node);
            _guard.ExitWriteLock();
            node.Value.Cancel();
        });
        
        return node.Value.Task;
    }

    private void OnTaskFinished(Task<TResult> task)
    {
        debouncer.RemoveHub(arg);

        // The hub has been removed from the debouncer, but task cancellations still may happen,
        // hence the lock
        _guard.EnterReadLock();
        try
        {
            if (task.IsFaulted)
            {
                OnTaskFailed(task.Exception);
            }
            else if (task.IsCanceled)
            {
                OnTaskCancelled();
            }
            else
            {
                OnTaskSucceeded(task.Result);
            }
        }
        finally
        {
            _guard.ExitReadLock();
        }
    }

    private void OnTaskFailed(Exception exception)
    {
        foreach (var tracker in _trackers)
        {
            tracker.Fail(exception);
        }
    }

    private void OnTaskCancelled()
    {
        foreach (var tracker in _trackers)
        {
            tracker.Cancel();
        }
    }
    
    private void OnTaskSucceeded(TResult result)
    {
        foreach (var tracker in _trackers)
        {
            tracker.SetResult(result);
        }
    }

    private sealed class Tracker
    {
        private readonly TaskCompletionSource<TResult> _tcs = new();
        
        public Task<TResult> Task => _tcs.Task;

        public void SetResult(TResult result) => _tcs.TrySetResult(result);
        
        public void Cancel() => _tcs.TrySetCanceled();
        
        public void Fail(Exception exception) => _tcs.TrySetException(exception);
    }
}
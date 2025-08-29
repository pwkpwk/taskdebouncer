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
        Tracker tracker = new();
        LinkedListNode<Tracker> node = new(tracker);

        _guard.EnterWriteLock();
        _trackers.AddLast(node);
        _guard.ExitWriteLock();
        
        cancellationToken.Register(() =>
        {
            _guard.EnterWriteLock();
            _trackers.Remove(node);
            _guard.ExitWriteLock();
            tracker.Cancel();
        });
        
        return tracker.Task;
    }

    private void OnTaskFinished(Task<TResult> task)
    {
        Detach();
        
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

    private void Detach()
    {
        debouncer.RemoveHub(arg);
    }

    private void OnTaskFailed(Exception exception)
    {
        _guard.EnterReadLock();
        try
        {
            foreach (var tracker in _trackers)
            {
                tracker.Fail(exception);
            }
        }
        finally
        {
            _guard.ExitReadLock();
        }
    }

    private void OnTaskCancelled()
    {
        _guard.EnterReadLock();
        try
        {
            foreach (var tracker in _trackers)
            {
                tracker.Cancel();
            }
        }
        finally
        {
            _guard.ExitReadLock();
        }
    }
    
    private void OnTaskSucceeded(TResult result)
    {
        _guard.EnterReadLock();
        try
        {
            foreach (var tracker in _trackers)
            {
                tracker.SetResult(result);
            }
        }
        finally
        {
            _guard.ExitReadLock();
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
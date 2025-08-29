using TaskDebouncer.Implementation;

namespace TaskDebouncer;

/// <summary>
/// Debouncer of spawning new tasks with the same argument
/// </summary>
/// <param name="taskFactory">Factory of tasks that return the same result when called with the same argument</param>
/// <param name="initialCapacity">Initial capacity of the internal hash set of task groups keyed with the task argument;
/// the value should represent the cardinality of the task arguments</param>
/// <param name="argEquality">Equality comparer of the task arguments; if null, the framework shall create the default comparer
/// and use it in the hash set of argument values</param>
/// <typeparam name="TArg">Type of the task argument; the task factory function takes one argument, that can be a composite object
/// contaning all parameters of a new task</typeparam>
/// <typeparam name="TResult">Type of the result produced by tasks spawned by the task factory</typeparam>
public sealed class TaskDebouncer<TArg, TResult>(
    Func<TArg, Task<TResult>> taskFactory,
    int initialCapacity = 17,
    IEqualityComparer<TArg>? argEquality = null) : ITaskDebouncer<TArg, TResult>
    where TArg : notnull
{
    private readonly Dictionary<TArg, TaskResultHub<TArg, TResult>> _hubs = new(
        initialCapacity,
        argEquality ?? EqualityComparer<TArg>.Default);
    private readonly ReaderWriterLockSlim _guard = new(LockRecursionPolicy.SupportsRecursion);

    Task<TResult> ITaskDebouncer<TArg, TResult>.CallAsync(TArg arg, CancellationToken cancellationToken)
    {
        bool startHub = false;
        TaskResultHub<TArg, TResult> hub;
        Task<TResult> task;

        _guard.EnterUpgradeableReadLock();

        try
        {
            if (!_hubs.TryGetValue(arg, out hub))
            {
                _guard.EnterWriteLock();

                try
                {
                    if (!_hubs.ContainsKey(arg))
                    {
                        hub = new(arg, this);
                        _hubs.Add(arg, hub);
                        startHub = true;
                    }
                }
                finally
                {
                    _guard.ExitWriteLock();
                }
            }
            
            task = hub.Add(cancellationToken);
        }
        finally
        {
            _guard.ExitUpgradeableReadLock();
        }
        
        if (startHub)
        {
            // Call the task factory outside the lock; only one of the simultaneous callers may set startHub to true
            hub.Start(taskFactory);
        }
        
        return task;
    }

    internal void RemoveHub(TArg arg)
    {
        _guard.EnterWriteLock();
        try
        {
            _hubs.Remove(arg);
        }
        finally
        {
            _guard.ExitWriteLock();
        }
    }
}
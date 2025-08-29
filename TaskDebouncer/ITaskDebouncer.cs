namespace TaskDebouncer;

public interface ITaskDebouncer<in TArg, TResult> where TArg : notnull
{
    Task<TResult> CallAsync(TArg arg, CancellationToken cancellationToken);
}
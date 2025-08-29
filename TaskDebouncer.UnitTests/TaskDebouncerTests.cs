namespace TaskDebouncer.UnitTests;

[TestFixture]
public class TaskDebouncerTests
{
    private Dictionary<long, TaskCompletionSource<long>> _tasks;
    private ITaskDebouncer<long, long> _debouncer;

    [SetUp]
    public void Setup()
    {
        _tasks = new();
        _debouncer = new TaskDebouncer<long, long>(arg =>
        {
            if (!_tasks.TryGetValue(arg, out var tcs))
            {
                tcs = new();
                _tasks.Add(arg, tcs);
            }

            return tcs.Task;
        });
    }

    [Test]
    public async Task TaskFinishesAfterDebouncing_CorrectResults()
    {
        CancellationTokenSource cts = new();
        Task<long>[] tasks = new Task<long>[10];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = _debouncer.CallAsync(100, cts.Token);
            Assert.That(tasks[i].IsCompleted, Is.False);
        }

        _tasks[100].SetResult(10);
        var results = await Task.WhenAll(tasks);
        Assert.That(results.Length, Is.EqualTo(tasks.Length));

        foreach (var result in results)
        {
            Assert.That(result, Is.EqualTo(10));
        }
    }

    [Test]
    public async Task TaskFinishesBeforeDebouncing_CorrectResults()
    {
        ITaskDebouncer<long, long> debouncer = new TaskDebouncer<long, long>(arg => Task.FromResult(10L));
        CancellationTokenSource cts = new();
        Task<long>[] tasks = new Task<long>[10];
        
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = debouncer.CallAsync(100, cts.Token);
            Assert.That(tasks[i].IsCompleted, Is.True);
        }

        var results = await Task.WhenAll(tasks);
        Assert.That(results.Length, Is.EqualTo(tasks.Length));

        foreach (var result in results)
        {
            Assert.That(result, Is.EqualTo(10));
        }
    }

    [Test]
    public async Task CancelBeforeDebouncing_AllTasksCancelled()
    {
        CancellationTokenSource cts = new();
        await cts.CancelAsync();
        Task<long>[] tasks = new Task<long>[10];
        
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = _debouncer.CallAsync(100, cts.Token);
            Assert.That(tasks[i].IsCanceled);
        }
    }
}
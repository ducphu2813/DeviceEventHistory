namespace DeviceEventStatistics.Worker.Configuration;

public sealed class StartupReadinessBarrier
{
    private readonly TaskCompletionSource<bool> completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsOpen => completion.Task.IsCompletedSuccessfully;

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        completion.Task.WaitAsync(cancellationToken);

    public void Open() => completion.TrySetResult(true);

    public void Fail(Exception exception) => completion.TrySetException(exception);
}

public sealed class StartupReadinessState
{
    private readonly Lock gate = new();
    private string? failureCode;

    public bool IsReady { get; private set; }

    public bool IsDisabled { get; private set; }

    public string? FailureCode
    {
        get
        {
            lock (gate)
            {
                return failureCode;
            }
        }
    }

    public void MarkReady()
    {
        lock (gate)
        {
            IsReady = true;
            IsDisabled = false;
            failureCode = null;
        }
    }

    public void MarkDisabled()
    {
        lock (gate)
        {
            IsReady = true;
            IsDisabled = true;
            failureCode = null;
        }
    }

    public void MarkFailed(string code)
    {
        lock (gate)
        {
            IsReady = false;
            IsDisabled = false;
            failureCode = code;
        }
    }
}

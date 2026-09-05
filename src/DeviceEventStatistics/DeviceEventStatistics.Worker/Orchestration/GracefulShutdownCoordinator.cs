namespace DeviceEventStatistics.Worker.Orchestration;

public sealed class GracefulShutdownCoordinator
{
    private readonly Lock gate = new();
    private TaskCompletionSource<bool> drained = CreateCompletionSource();
    private int activeOperations;
    private bool isDraining;

    public bool IsDraining
    {
        get
        {
            lock (gate)
            {
                return isDraining;
            }
        }
    }

    public int ActiveOperations
    {
        get
        {
            lock (gate)
            {
                return activeOperations;
            }
        }
    }

    public bool TryBeginOperation(out IDisposable? operation)
    {
        lock (gate)
        {
            if (isDraining)
            {
                operation = null;
                return false;
            }

            activeOperations++;
            operation = new Operation(this);
            return true;
        }
    }

    public void BeginDrain()
    {
        lock (gate)
        {
            if (isDraining)
            {
                return;
            }

            isDraining = true;
            if (activeOperations == 0)
            {
                drained.TrySetResult(true);
            }
        }
    }

    public Task WaitForDrainAsync(CancellationToken cancellationToken = default) =>
        drained.Task.WaitAsync(cancellationToken);

    private void CompleteOperation()
    {
        lock (gate)
        {
            activeOperations = Math.Max(0, activeOperations - 1);
            if (isDraining && activeOperations == 0)
            {
                drained.TrySetResult(true);
            }
        }
    }

    private static TaskCompletionSource<bool> CreateCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Operation(GracefulShutdownCoordinator owner) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.CompleteOperation();
            }
        }
    }
}

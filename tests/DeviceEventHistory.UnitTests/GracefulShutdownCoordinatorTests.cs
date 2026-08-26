using DeviceEventHistory.Worker.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeviceEventHistory.UnitTests;

public sealed class GracefulShutdownCoordinatorTests
{
    [Fact]
    public async Task Scheduler_starts_before_polling_loop()
    {
        var schedulerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new GracefulShutdownCoordinator(
            NullLogger<GracefulShutdownCoordinator>.Instance);

        Task SchedulingLoop(CancellationToken _)
        {
            schedulerStarted.TrySetResult();
            return Task.CompletedTask;
        }

        async Task PollingLoop(CancellationToken _)
        {
            Assert.True(
                schedulerStarted.Task.IsCompleted,
                "The polling loop must start after scheduler consumers are ready.");
            await Task.CompletedTask;
        }

        await coordinator.RunAsync(
            PollingLoop,
            SchedulingLoop,
            CancellationToken.None);

        Assert.True(schedulerStarted.Task.IsCompletedSuccessfully);
    }
}

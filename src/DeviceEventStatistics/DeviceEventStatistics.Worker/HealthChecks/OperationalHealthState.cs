using DeviceEventStatistics.Application.Observability;

namespace DeviceEventStatistics.Worker.HealthChecks;

public sealed class OperationalHealthState
{
    private readonly Lock gate = new();
    private StatisticsHealthEvaluation? evaluation;
    private Exception? dependencyFailure;

    public StatisticsHealthEvaluation? Evaluation
    {
        get
        {
            lock (gate)
            {
                return evaluation;
            }
        }
    }

    public Exception? DependencyFailure
    {
        get
        {
            lock (gate)
            {
                return dependencyFailure;
            }
        }
    }

    public void Set(StatisticsHealthEvaluation value)
    {
        lock (gate)
        {
            evaluation = value;
            dependencyFailure = null;
        }
    }

    public void SetDependencyFailure(Exception exception)
    {
        lock (gate)
        {
            dependencyFailure = exception;
            evaluation = null;
        }
    }
}

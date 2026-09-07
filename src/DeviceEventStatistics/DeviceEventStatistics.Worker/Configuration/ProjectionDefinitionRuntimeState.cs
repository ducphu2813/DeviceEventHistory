using DeviceEventStatistics.Application.Projection;
using DeviceEventStatistics.Domain.Common;

namespace DeviceEventStatistics.Worker.Configuration;

public sealed class ProjectionDefinitionRuntimeState
{
    private ResolvedProjectionDefinition? definition;

    public void Set(ResolvedProjectionDefinition value) =>
        Interlocked.CompareExchange(ref definition, value, null);

    public ResolvedProjectionDefinition GetRequired()
    {
        return Volatile.Read(ref definition) ?? throw new InvalidOperationException(
            StatisticsContractConstants.Messages.MSG_PROJECTION_DEFINITION_NOT_READY);
    }
}

using DeviceEventStatistics.Application.Projection;

namespace DeviceEventStatistics.Application.History;

public sealed record PreparedAuditPage(
    PreparedProjectionPage ProjectionPage,
    bool IsComplete);

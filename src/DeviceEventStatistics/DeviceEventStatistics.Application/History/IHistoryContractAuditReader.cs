namespace DeviceEventStatistics.Application.History;

public interface IHistoryContractAuditReader
{
    Task<HistoryAuditResult> ReadAuditPageAsync(
        string? afterSourceDocumentId,
        int pageSize,
        CancellationToken cancellationToken = default);
}

namespace DeviceEventStatistics.Infrastructure.SqlServer;

internal static class StatisticsSqlObjectNames
{
    public const string TablePrefix = "DES.";

    public static string Table(string baseName) => $"{TablePrefix}{baseName}";

    public static string QualifiedTable(string schemaName, string baseName) =>
        $"{QuoteIdentifier(schemaName)}.{QuoteIdentifier(Table(baseName))}";

    public static string QualifiedTableName(string schemaName, string tableName) =>
        $"{QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)}";

    private static string QuoteIdentifier(string value) =>
        $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
}

using System.Globalization;
using DeviceEventHistory.Domain.Common;
using MongoDB.Bson;

namespace DeviceEventHistory.Infrastructure.MongoDb.Mapping;

internal static class MongoDocumentValue
{
    public static BsonValue DateTimeOffset(DateTimeOffset? value) =>
        value.HasValue
            ? new BsonDateTime(value.Value.UtcDateTime)
            : BsonNull.Value;

    public static BsonValue DateOnly(DateOnly value) =>
        value.ToString(AppConst.MongoDb.CheckpointDateFormat, CultureInfo.InvariantCulture);

    public static BsonValue String(string? value) =>
        value is null ? BsonNull.Value : new BsonString(value);

    public static BsonValue Int32(int? value) =>
        value.HasValue ? new BsonInt32(value.Value) : BsonNull.Value;

    public static BsonValue Int64(long? value) =>
        value.HasValue ? new BsonInt64(value.Value) : BsonNull.Value;

    public static BsonValue Double(double? value) =>
        value.HasValue ? new BsonDouble(value.Value) : BsonNull.Value;

    public static BsonArray StringArray(IEnumerable<string> values) =>
        new(values.Select(value => new BsonString(value)));

    public static BsonArray Int32Array(IEnumerable<int> values) =>
        new(values.Select(value => new BsonInt32(value)));
}

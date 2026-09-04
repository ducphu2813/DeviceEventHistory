using MongoDB.Bson;
using MongoDB.Driver;

namespace DeviceEventStatistics.Infrastructure.MongoDb.Reading;

internal static class MongoHistoryFieldProjection
{
    public static ProjectionDefinition<BsonDocument> Definition { get; } =
        Builders<BsonDocument>.Projection.Combine(
        [
            Builders<BsonDocument>.Projection.Include("_id"),
            Builders<BsonDocument>.Projection.Include("eventId"),
            Builders<BsonDocument>.Projection.Include("schemaVersion"),
            Builders<BsonDocument>.Projection.Include("companyId"),
            Builders<BsonDocument>.Projection.Include("category"),
            Builders<BsonDocument>.Projection.Include("sourceKind"),
            Builders<BsonDocument>.Projection.Include("occurredAtUtc"),
            Builders<BsonDocument>.Projection.Include("receivedAtUtc"),
            Builders<BsonDocument>.Projection.Include("persistedAtUtc"),
            Builders<BsonDocument>.Projection.Include("timelineAtUtc"),
            Builders<BsonDocument>.Projection.Include("timeBasis"),
            Builders<BsonDocument>.Projection.Include("source.sourceId"),
            Builders<BsonDocument>.Projection.Include("source.eventName"),
            Builders<BsonDocument>.Projection.Include("source.deliveryKind"),
            Builders<BsonDocument>.Projection.Include("device.id"),
            Builders<BsonDocument>.Projection.Include("device.gateId"),
            Builders<BsonDocument>.Projection.Include("device.type"),
            Builders<BsonDocument>.Projection.Include("device.code"),
            Builders<BsonDocument>.Projection.Include("device.name"),
            Builders<BsonDocument>.Projection.Include("device.gateCode"),
            Builders<BsonDocument>.Projection.Include("device.gateName"),
            Builders<BsonDocument>.Projection.Include("facts.tagRead"),
            Builders<BsonDocument>.Projection.Include("facts.businessEvent"),
            Builders<BsonDocument>.Projection.Include("facts.connection"),
            Builders<BsonDocument>.Projection.Include("facts.deviceOnline"),
            Builders<BsonDocument>.Projection.Include("facts.deviceControlState"),
            Builders<BsonDocument>.Projection.Include("facts.sensorState"),
            Builders<BsonDocument>.Projection.Include("facts.scanner"),
            Builders<BsonDocument>.Projection.Include("facts.deviceError"),
            Builders<BsonDocument>.Projection.Include("parse.status")
        ]);
}

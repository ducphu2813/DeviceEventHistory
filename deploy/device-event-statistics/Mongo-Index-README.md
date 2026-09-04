# Mongo history index for Statistics Worker

The Statistics Worker reads the immutable `device_event_history` collection by
`persistedAtUtc ASC, eventId ASC`. Apply the index with:

```powershell
.\Ensure-MongoHistoryStatisticsIndex.ps1 `
  -ConnectionString $env:DEVICE_EVENT_STATISTICS_MONGO_CONNECTION_STRING `
  -DatabaseName device_event_history `
  -CollectionName device_event_history
```

The script is idempotent and refuses an existing index with the same name but
different keys. Do not put credentials in the repository or command history.
The worker verifies the index at startup; it does not create or modify MongoDB
indexes at runtime.

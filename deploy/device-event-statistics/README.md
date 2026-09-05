# Device Event Statistics deployment

## SQL schema

Apply additive SQL migrations with a deployment identity before enabling the
Statistics Worker. The runtime identity only verifies the schema and latest
migration; it does not create or alter SQL objects.

```powershell
.\Apply-SqlMigrations.ps1 `
  -ConnectionString $env:DEVICE_EVENT_STATISTICS_SQL_CONNECTION_STRING `
  -DatabaseName UA-REPORTING-DB `
  -SchemaName dbo
```

## Mongo history retention

Preview the retention boundary before activation. The TTL is based only on
`persistedAtUtc`; `timelineAtUtc` is never used for deletion. The script does
not target `ingestion_checkpoints` and reports documents with an invalid or
missing persisted timestamp separately.

```powershell
.\Enable-HistoryRetention.ps1 `
  -ConnectionString $env:DEVICE_EVENT_STATISTICS_MONGO_CONNECTION_STRING `
  -DatabaseName device_event_history `
  -Preview

.\Enable-HistoryRetention.ps1 `
  -ConnectionString $env:DEVICE_EVENT_STATISTICS_MONGO_CONNECTION_STRING `
  -DatabaseName device_event_history
```

MongoDB removes TTL-eligible documents asynchronously; the script does not
promise deletion at an exact second. Apply this with a deployment-owned
identity after UAT confirms the TTL/index contract.

## One-shot modes

Manual modes require explicit date range and company/device scope. They use
the same lease, admission, exact replacement and durable `ProjectionRun`
contracts as the service. They do not start the incremental scheduler.

```powershell
.\Invoke-StatisticsMode.ps1 -Mode Bootstrap `
  -FromUtc '2026-09-01T00:00:00Z' -ToUtc '2026-09-04T00:00:00Z' `
  -CompanyId 1001 -DeviceId 2001

.\Invoke-StatisticsMode.ps1 -Mode Backfill `
  -FromUtc '2026-09-02T00:00:00Z' -ToUtc '2026-09-03T00:00:00Z' `
  -CompanyId 1001 -DeviceId 2001
```

`Rebuild` must use a new projection version configured through the normal
projection settings. Do not reset the raw History Worker checkpoint or purge
facts to force a rebuild. A range outside retained source coverage is stopped
with a durable coverage gap; existing SQL facts remain unchanged.

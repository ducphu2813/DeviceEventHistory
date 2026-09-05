# Device Event Statistics deployment

## SQL schema

The Statistics Worker uses the default `dbo` schema. Its table names use the
`DES.` convention, for example `[dbo].[DES.DeviceDailySnapshot]`.

For a direct fresh installation through SSMS or Azure Data Studio, open and execute
the standalone script below in `UA-REPORTING-DB`:

```text
src/DeviceEventStatistics/DeviceEventStatistics.Infrastructure/SqlServer/Migrations/009_CreateDeviceEventStatisticsSchema.sql
```

The script creates the complete Statistics schema in the currently selected
database. It does not contain `USE`, `DROP`, or rename statements, so it does
not depend on a database name and does not modify legacy tables. Do not run it
while another Statistics Worker instance is processing the database. The file
is a create-only bootstrap script; it is not an in-place schema upgrade script.

For an existing Statistics database bootstrapped by migration 009, apply the
versioned upgrade migrations through `Apply-SqlMigrations.ps1`. Migrations 010
and 011 add the durable audit checkpoint and scoped `ProcessedEvent`/TVP V2
contract without dropping or rewriting data. Migration 011 also adds the SQL
membership index used by exact device reconciliation.

The older `001–008` files remain versioned migration history. Automated
deployment may still use the migration runner below; the runtime identity only
verifies the schema and latest migration, and never creates or alters SQL
objects.

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

## Observability and shutdown

The worker registers startup and operational health checks with the host health
check service. The operational state reports idle/caught-up, backlog warning
at 12 hours, SLO breach at 24 hours, dependency failure, lease loss,
retention risk, unrecoverable coverage and graceful-drain state. These are
pipeline signals, not Sprint 4 device health scoring.

Metrics use the `DeviceEventStatistics.Worker` `System.Diagnostics.Metrics`
meter. Attach the deployment exporter or collector at the host boundary;
vendor-specific exporter dependencies are not embedded in the worker. Event,
device and run identities are excluded from metric labels.

Set `DeviceEventStatistics__ShutdownTimeout` to the approved drain budget. A
shutdown cancels only work that has not committed, preserves the checkpoint,
and releases the projection lease after the active bounded operation exits.

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

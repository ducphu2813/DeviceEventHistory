# Device Event Statistics - Sprint 3 Runbook

## Preflight

1. Confirm the Statistics SQL target is `UA-REPORTING-DB` with schema `dbo`.
2. If the database contains old Statistics objects, stop the Statistics Worker,
   select the target database in SSMS or Azure Data Studio, and execute
   `010_CleanupDeviceEventStatisticsSchema.sql`. This is a destructive,
   Statistics-owned cleanup only; it does not select a database and does not
   touch History, HangFire or ERP tables.
3. Select the target database and execute
   `009_CreateDeviceEventStatisticsSchema.sql`. It is the only create/bootstrap
   script and creates all current `DES.*` tables, types, indexes and metric seed
   under `dbo`. It is idempotent and does not modify legacy tables.
4. Verify the latest schema through the worker startup preflight.
5. Verify Mongo connectivity, the history collection, the unique event index,
   the global Statistics cursor index and the scoped cursor index.
6. Check the projection definition, checkpoint, audit cursor, pending reconciliation requests
   and coverage before changing a mode.
7. Keep the Statistics worker disabled while applying schema changes or
   enabling history TTL.

## Dependency and schema verification (Phase F7)

The Statistics and History infrastructure projects pin `MongoDB.Driver` to
the first version in the audited line that resolves both compression
dependencies without the known advisories: `3.9.0` resolves
`SharpCompress >= 0.48.1` and `Snappier >= 1.3.1`. Re-run
`dotnet list DeviceEventStatistics.sln package --include-transitive --vulnerable`
and the equivalent History command after every driver change. Do not suppress
NU1902/NU1903; a new warning requires a package upgrade or an approved risk
disposition before deployment.

For a fresh database, select the target database in SSMS and execute
`009_CreateDeviceEventStatisticsSchema.sql` twice. The second execution is
expected to be a no-op for existing objects and seed rows. For an intentional
Statistics reset, execute `010_CleanupDeviceEventStatisticsSchema.sql` once,
then run 009. The cleanup is the only destructive script and must never be
run against a database whose Statistics evidence must be retained.

The disposable Phase F7 smoke verification exercised 009 twice with a
sentinel `DeviceDimension` row. Evidence: 22 `DES.*` tables, 22 indexes, 14
metric rows, 4 durable audit columns, the scoped V2 type and index, and one
disabled `device_error` metric after recreation. The generated test database
was removed after the successful run; no development application database was
reset.

For a new projection definition, set `ResumeFromStoredDefinition=false` and
provide an explicit `CoverageStartAtUtc`. After the definition has been
created, restart/resume with `ResumeFromStoredDefinition=true` and set
`CoverageStartAtUtc=null`; startup loads the stored immutable contract and
fails fast if mapping, ownership, metric-set or timezone configuration does
not match it.

Device-scoped exact reconciliation replaces only device-grained facts,
snapshots, state and coverage. `IngestionQualityDaily` is company-grained, so
it is intentionally left unchanged by a device-scoped rebuild to prevent one
device from deleting another device's quality aggregate.

The worker never creates SQL schema or changes the History Worker checkpoint.
Connection strings and credentials must be supplied through local environment
configuration or deployment secrets.

When `HealthEndpointEnabled=true`, external monitors can probe
`http://<worker-host>:<HealthPort>/health/live` and `/health/ready`.
Liveness contains only the process check; readiness contains startup and
operational checks. Responses expose status and check names only, never
connection strings, exception details or tenant identifiers. Disabled/manual
completed modes do not require an active projection lease for operational
health.

## Phase 8 operational signals

The worker registers startup and operational health checks with the host health
check service. The operational check distinguishes idle/caught-up, degraded
lag or pending work, dependency/lease failure, retention risk and
unrecoverable coverage. The default lag thresholds are 12 hours for warning
and 24 hours for an SLO breach. These signals describe pipeline health only;
they are not Sprint 4 device health scoring.

Metrics are emitted through the `DeviceEventStatistics.Worker`
`System.Diagnostics.Metrics` meter. A deployment may attach its exporter at
the host boundary. Event, device and run identities are intentionally not
metric labels.

During shutdown the worker stops admitting new bounded operations, waits up to
`DeviceEventStatistics:ShutdownTimeout` for active SQL work, and releases the
projection lease afterwards. Cancelled work keeps its previous checkpoint and
remains recoverable after restart.

## Normal incremental operation

Use `Mode=Incremental`. The worker owns one SQL lease, advances the normal
checkpoint and bounded deep-discovery cursor only after the atomic SQL
transaction commits, refreshes open state durations, schedules rolling
reconciliation and audits Mongo by `_id` according to
`Projection:DeepDiscoveryInterval`. A restart resumes from the durable
checkpoint/request/audit state.

## Bootstrap a new projection version

Use `Invoke-StatisticsMode.ps1 -Mode Bootstrap` with an explicit retained date
range and company/device scope. Bootstrap creates a `building` projection
definition, admits source events through `ProcessedEvent`, computes facts with
the exact path and marks the definition `ready` only after publish succeeds.

Do not set a new version active until every required date has acceptable
coverage. Keep the previous version available for rollback.

## Same-version backfill

Use `Mode=Backfill` for a bounded range on an existing definition. Backfill
does not move the normal high-watermark cursor. Missing source events are
admitted idempotently and the target range is exact-replaced under the SQL
writer gate. Re-running the same command must not increase counts.

If the requested range is older than the retained Mongo source or membership
cannot be proven, stop and preserve the existing SQL result. The durable
coverage/recovery reason is the evidence for the operator decision.

## Rebuild and cutover

Use a new `ProjectionVersion` with `Mode=Rebuild`; never reset a live version's
checkpoint and add into old aggregates. Build the retained range, catch up the
tail, verify counts/state/quality/coverage, then switch consumers to the new
version for dates with complete coverage. Retain the old version for rollback
where the new version has a gap.

## Mongo history retention

Run `Enable-HistoryRetention.ps1 -Preview` first. The only deletion basis is
`persistedAtUtc`, with the initial contract of 604800 seconds. A late business
date with a recent persisted timestamp remains eligible for replay; a missing
or invalid `persistedAtUtc` is reported separately and is not silently treated
as retained. TTL deletion is asynchronous.

Do not apply this index to `ingestion_checkpoints`, and do not change History
Worker failure/checkpoint retention as part of Sprint 3.

## Recovery after interruption

- Restart the worker with the same projection version and configuration.
- Inspect processing reconciliation requests and their attempt/error state.
- Allow expired claims to be reclaimed; do not edit claims or checkpoints by hand.
- For a bounded source range still inside retention, rerun the explicit
  backfill command.
- For a new mapping/contract, build a new version and use rebuild/cutover.
- If the source range is outside retention, keep existing SQL facts and record
  the coverage gap; do not report zero missing events.

## Stop and rollback conditions

Stop a manual run when startup contract checks fail, membership is missing,
opening state evidence is unavailable for a required complete result, or the
SQL revision/lease becomes stale. A failed publish must leave the previous
facts intact. Rollback means disable the Statistics worker or route consumers
back to a version whose date coverage is valid; never reset the raw ingestion
checkpoint.

Sprint 3 does not provide full-history rebuild outside Mongo's retained source
window or long-term raw archive. Projection facts, coverage manifests,
state anchors and unresolved recovery evidence are not automatically purged.

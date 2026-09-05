# Device Event Statistics - Sprint 3 Runbook

## Preflight

1. Confirm the Statistics SQL target is `UA-REPORTING-DB` with schema `dbo`.
2. Apply SQL migrations with the deployment identity and verify the latest
   migration through the worker startup preflight.
3. Verify Mongo connectivity, the history collection, the unique event index
   and the Statistics cursor index.
4. Check the projection definition, checkpoint, pending reconciliation requests
   and coverage before changing a mode.
5. Keep the Statistics worker disabled while applying schema changes or
   enabling history TTL.

The worker never creates SQL schema or changes the History Worker checkpoint.
Connection strings and credentials must be supplied through local environment
configuration or deployment secrets.

## Normal incremental operation

Use `Mode=Incremental`. The worker owns one SQL lease, advances the normal
checkpoint only after the atomic SQL transaction commits, refreshes open state
durations and schedules rolling reconciliation. A restart resumes from the
durable checkpoint/request state.

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

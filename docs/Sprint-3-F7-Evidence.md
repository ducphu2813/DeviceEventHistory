# Sprint 3 - Phase F7 Evidence

## Dependency audit

Command:

```powershell
dotnet list DeviceEventStatistics.sln package --include-transitive --vulnerable
dotnet list DeviceEventHistory.sln package --include-transitive --vulnerable
```

Before the fix, `MongoDB.Driver 3.4.0` resolved `SharpCompress 0.30.1`
(`GHSA-6c8g-7p36-r338`) and `Snappier 1.0.0`
(`GHSA-pggp-6c3x-2xmx`). The package dependency metadata showed that
`MongoDB.Driver 3.9.0` is the smallest audited driver version resolving
`SharpCompress 0.48.1` and `Snappier 1.3.1`. The package is pinned at 3.9.0
in both infrastructure projects. The Statistics Worker no longer declares
framework-provided Hosting or HealthChecks packages, removing NU1510.

The post-change vulnerable-package audit reports no vulnerable packages for
the Statistics or History solutions. The resolved MongoDB driver was covered
by both solution builds and the existing Mongo compatibility test projects.

## SQL schema verification

The disposable SQL runner used the local Development SQL connection only to
create a generated database named `DeviceEventStatistics_F7_yyyyMMdd_HHmmss`.
It refused an existing database name and removed only that generated database
after a successful verification.

The verification performed these assertions against the consolidated 009
bootstrap:

1. Execute 009 on an empty database and inspect tables, indexes, types and
   metric seed.
2. Insert a sentinel `DeviceDimension` row and a legacy table, execute 010,
   and verify all Statistics tables/types are removed.
3. Execute 009 twice after cleanup and verify the complete schema, audit
   columns, scoped V2 type/index, metric seed and retry idempotency.

Result:

```text
F7 SQL verification passed: tables=22; indexes=22; metrics=14;
sentinelRows=1; checkpointRows=1; auditColumns=4; scopedType=1;
scopedIndex=1.
```

The 009 bootstrap contains the durable audit fields, scoped ProcessedEvent
columns, TVP V2 and the corrected V1 metric registry. Every standalone index
creation is guarded by `sys.indexes`. The separate 010 cleanup is explicitly
destructive, idempotent and limited to Statistics-owned objects.

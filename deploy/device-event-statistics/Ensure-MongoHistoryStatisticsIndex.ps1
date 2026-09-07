[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ConnectionString,

    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [string] $CollectionName = "device_event_history"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "ConnectionString must not be empty."
}

foreach ($identifier in @($DatabaseName, $CollectionName)) {
    if ($identifier -notmatch '^[A-Za-z0-9_.-]+$') {
        throw "Mongo identifier '$identifier' contains unsupported characters."
    }
}

$indexName = "ix_statistics_persisted_event_id"
$scopedIndexName = "ix_statistics_scope_persisted_event_id"
$javascript = @"
const collection = db.getCollection('$CollectionName');
const existing = collection.getIndexes().find(index => index.name === '$indexName');
if (!existing) {
  collection.createIndex({ persistedAtUtc: 1, eventId: 1 }, { name: '$indexName' });
} else {
  const keyNames = Object.keys(existing.key);
  if (keyNames.length !== 2 ||
      existing.key.persistedAtUtc !== 1 ||
      existing.key.eventId !== 1) {
    throw new Error('Existing cursor index has incompatible keys.');
  }
}
const scopedExisting = collection.getIndexes().find(index => index.name === '$scopedIndexName');
if (!scopedExisting) {
  collection.createIndex({ companyId: 1, 'device.id': 1, persistedAtUtc: 1, eventId: 1 }, { name: '$scopedIndexName' });
} else {
  const scopedKeyNames = Object.keys(scopedExisting.key);
  if (scopedKeyNames.length !== 4 ||
      scopedExisting.key.companyId !== 1 ||
      scopedExisting.key['device.id'] !== 1 ||
      scopedExisting.key.persistedAtUtc !== 1 ||
      scopedExisting.key.eventId !== 1) {
    throw new Error('Existing scoped cursor index has incompatible keys.');
  }
}
print('Statistics cursor index verified.');
"@

& mongosh $ConnectionString --quiet --eval "use('$DatabaseName'); $javascript"
if ($LASTEXITCODE -ne 0) {
    throw "mongosh failed with exit code $LASTEXITCODE."
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ConnectionString,

    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [string] $CollectionName = "device_event_history",

    [int] $RetentionSeconds = 604800,

    [switch] $Preview
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "ConnectionString must not be empty."
}

foreach ($identifier in @($DatabaseName, $CollectionName)) {
    if ($identifier -notmatch '^[A-Za-z0-9_.-]+$') {
        throw "Mongo identifier contains unsupported characters."
    }
}

if ($CollectionName -ieq "ingestion_checkpoints") {
    throw "History retention cannot target ingestion_checkpoints."
}

if ($RetentionSeconds -le 0) {
    throw "RetentionSeconds must be positive."
}

$previewFlag = if ($Preview) { "true" } else { "false" }
$javascript = @'
const collectionName = '__COLLECTION__';
const retentionSeconds = __RETENTION__;
const preview = __PREVIEW__;
const collection = db.getCollection(collectionName);
const indexes = collection.getIndexes();
const persistedIndex = indexes.find(index =>
  index.key && index.key.persistedAtUtc === 1);
const cutoff = new Date(Date.now() - retentionSeconds * 1000);
const missingPersistedAtUtc = collection.countDocuments({
  $or: [
    { persistedAtUtc: { $exists: false } },
    { persistedAtUtc: { $not: { $type: 'date' } } }
  ]
});
const expiredCandidates = collection.countDocuments({
  persistedAtUtc: { $type: 'date', $lt: cutoff }
});
printjson({
  database: '__DATABASE__',
  collection: collectionName,
  persistedAtUtcIndex: persistedIndex ? persistedIndex.name : null,
  cutoffUtc: cutoff.toISOString(),
  retentionSeconds: retentionSeconds,
  missingPersistedAtUtc: missingPersistedAtUtc,
  expiredCandidates: expiredCandidates,
  preview: preview
});
if (missingPersistedAtUtc > 0) {
  print('Documents without a valid persistedAtUtc remain outside TTL processing and require separate audited migration.');
}
if (!preview) {
  const ttlName = 'ttl_statistics_persisted_at_7d';
  const existing = indexes.find(index => index.name === ttlName);
  if (existing && (existing.key.persistedAtUtc !== 1 || existing.expireAfterSeconds !== retentionSeconds)) {
    throw new Error('Existing history TTL index has an incompatible contract.');
  }
  if (!existing) {
    collection.createIndex(
      { persistedAtUtc: 1 },
      { name: ttlName, expireAfterSeconds: retentionSeconds });
  }
  print('History TTL index verified. MongoDB removes eligible documents asynchronously.');
}
'@
$javascript = $javascript.Replace('__COLLECTION__', $CollectionName, [StringComparison]::Ordinal)
$javascript = $javascript.Replace('__DATABASE__', $DatabaseName, [StringComparison]::Ordinal)
$javascript = $javascript.Replace('__RETENTION__', $RetentionSeconds.ToString([Globalization.CultureInfo]::InvariantCulture), [StringComparison]::Ordinal)
$javascript = $javascript.Replace('__PREVIEW__', $previewFlag, [StringComparison]::Ordinal)

& mongosh $ConnectionString --quiet --eval "use('$DatabaseName'); $javascript"
if ($LASTEXITCODE -ne 0) {
    throw "mongosh failed with a non-zero exit code."
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [Parameter(Mandatory = $false)]
    [string]$SchemaName = "dbo",

    [Parameter(Mandatory = $false)]
    [string]$MigrationPath = (Join-Path $PSScriptRoot "..\..\src\DeviceEventStatistics\DeviceEventStatistics.Infrastructure\SqlServer\Migrations")
)

$ErrorActionPreference = "Stop"

if ($SchemaName -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
    throw "SchemaName must be a simple SQL identifier."
}

if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    throw "The SqlServer PowerShell module is required to apply migrations."
}

Import-Module SqlServer

function Invoke-StatisticsSql {
    param([Parameter(Mandatory = $true)][string]$Query)
    Invoke-Sqlcmd -ConnectionString $ConnectionString -Database $DatabaseName -Query $Query -QueryTimeout 120 -ErrorAction Stop
}

$target = Invoke-StatisticsSql "SELECT DB_NAME() AS DatabaseName;"
if (-not [string]::Equals([string]$target.DatabaseName, $DatabaseName, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolved SQL database does not match the requested target."
}

$migrationFiles = @(Get-ChildItem -LiteralPath $MigrationPath -Filter "*.sql" -File | Sort-Object Name)
if ($migrationFiles.Count -eq 0) {
    throw "No SQL migration files were found."
}

$legacyHistoryTable = "[$SchemaName].[SchemaMigration]"
$desHistoryTable = "[$SchemaName].[DES.SchemaMigration]"

function Test-DesBootstrapApplied {
    param([Parameter(Mandatory = $true)][string]$TableName)

    $result = Invoke-StatisticsSql @"
SELECT CASE WHEN OBJECT_ID(N'$TableName', N'U') IS NULL THEN 0 ELSE 1 END AS TableExists;
"@
    return [int]$result.TableExists -eq 1
}

$desBootstrapApplied = Test-DesBootstrapApplied $desHistoryTable

foreach ($file in $migrationFiles) {
    $migrationId = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    if ($migrationId -notmatch "^[0-9]{3}_[A-Za-z0-9_]+$") {
        throw "Invalid migration file name: $($file.Name)"
    }

    $migrationNumber = [int]$migrationId.Substring(0, 3)

    $rawScript = [IO.File]::ReadAllText($file.FullName)
    $script = $rawScript.Replace("__SCHEMA__", $SchemaName)
    $checksumBytes = [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($rawScript))
    $checksumHex = -join ($checksumBytes | ForEach-Object { $_.ToString("x2") })

    if ($desBootstrapApplied -and $migrationNumber -le 9) {
        Write-Host "Already applied by DES bootstrap: $migrationId"
        continue
    }

    $isDesBootstrap = $migrationId -eq "009_CreateDeviceEventStatisticsSchema"
    $historyTable = if ($migrationNumber -ge 10) { $desHistoryTable } else { $legacyHistoryTable }
    $historyExists = Invoke-StatisticsSql @"
SELECT CASE WHEN OBJECT_ID(N'$historyTable', N'U') IS NULL THEN 0 ELSE 1 END AS HistoryExists;
"@
    if ([int]$historyExists.HistoryExists -eq 0 -and $migrationId -ne "001_CreateStatisticsSchema") {
        throw "SchemaMigration is missing before migration $migrationId."
    }

    if ([int]$historyExists.HistoryExists -eq 1) {
        $existing = Invoke-StatisticsSql @"
SELECT CONVERT(varchar(64), [Checksum], 2) AS Checksum
FROM $historyTable
WHERE [MigrationId] = '$migrationId';
"@
        if ($null -ne $existing) {
            $existingChecksum = [string]$existing.Checksum
            if (-not [string]::Equals($existingChecksum, $checksumHex, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Migration checksum mismatch for $migrationId. The applied migration must not be edited."
            }

            Write-Host "Already applied: $migrationId"
            continue
        }
    }

    Write-Host "Applying: $migrationId"
    Invoke-StatisticsSql $script | Out-Null

    if ($isDesBootstrap) {
        $desBootstrapApplied = $true
        continue
    }

    $appliedBy = "device-event-statistics-migration"
    Invoke-StatisticsSql @"
INSERT INTO $historyTable ([MigrationId], [Checksum], [AppliedAtUtc], [AppliedBy])
VALUES ('$migrationId', 0x$checksumHex, SYSUTCDATETIME(), '$appliedBy');
"@ | Out-Null
}

Write-Host "SQL migrations completed for database '$DatabaseName' and schema '$SchemaName'."

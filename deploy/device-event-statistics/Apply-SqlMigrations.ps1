[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [Parameter(Mandatory = $false)]
    [string]$SchemaName = "dbo",

    [Parameter(Mandatory = $false)]
    [string]$MigrationPath = (Join-Path $PSScriptRoot "Migrations")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $MigrationPath -PathType Container)) {
    $MigrationPath = Join-Path $PSScriptRoot "..\..\src\DeviceEventStatistics\DeviceEventStatistics.Infrastructure\SqlServer\Migrations"
}

if ($SchemaName -ne "dbo") {
    throw "The standalone Statistics bootstrap uses the dbo schema by contract."
}

if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    throw "The SqlServer PowerShell module is required to apply the bootstrap."
}

$migrationFiles = @(
    "009_CreateDeviceEventStatisticsSchema.sql",
    "011_AddRetentionCleanupIndexes.sql"
)
foreach ($migrationFile in $migrationFiles) {
    $filePath = Join-Path $MigrationPath $migrationFile
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "The Statistics migration script was not found: $filePath"
    }
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

foreach ($migrationFile in $migrationFiles) {
    $filePath = Join-Path $MigrationPath $migrationFile
    $script = [IO.File]::ReadAllText($filePath).Replace("__SCHEMA__", $SchemaName)
    Write-Host "Applying Statistics migration: $migrationFile"
    Invoke-StatisticsSql $script | Out-Null
}
Write-Host "Statistics migrations completed for database '$DatabaseName' and schema '$SchemaName'."
Write-Host "Run 010_CleanupDeviceEventStatisticsSchema.sql manually before 009 only when a destructive reset is explicitly intended."

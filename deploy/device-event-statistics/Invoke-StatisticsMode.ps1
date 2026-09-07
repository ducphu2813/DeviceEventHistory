[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Bootstrap", "Backfill", "Rebuild")]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [DateTimeOffset] $FromUtc,

    [Parameter(Mandatory = $true)]
    [DateTimeOffset] $ToUtc,

    [Parameter(Mandatory = $true)]
    [int[]] $CompanyId,

    [Parameter(Mandatory = $true)]
    [int[]] $DeviceId,

    [string] $Project = (Join-Path $PSScriptRoot "..\..\src\DeviceEventStatistics\DeviceEventStatistics.Worker\DeviceEventStatistics.Worker.csproj")
)

$ErrorActionPreference = "Stop"
if ($FromUtc -ge $ToUtc) {
    throw "FromUtc must be earlier than ToUtc."
}
if ($CompanyId.Count -eq 0 -or $DeviceId.Count -eq 0) {
    throw "At least one company and device scope are required for manual mode."
}

$arguments = [System.Collections.Generic.List[string]]::new()
$arguments.AddRange(@(
    "run", "--project", $Project, "--no-build", "--",
    "--DeviceEventStatistics:Projection:Mode=$Mode",
    "--DeviceEventStatistics:Projection:ManualRange:FromUtc=$($FromUtc.ToUniversalTime().ToString('O'))",
    "--DeviceEventStatistics:Projection:ManualRange:ToUtc=$($ToUtc.ToUniversalTime().ToString('O'))"
))

for ($index = 0; $index -lt $CompanyId.Count; $index++) {
    $arguments.Add("--DeviceEventStatistics:Projection:Scope:CompanyIds:$index=$($CompanyId[$index])")
}
for ($index = 0; $index -lt $DeviceId.Count; $index++) {
    $arguments.Add("--DeviceEventStatistics:Projection:Scope:DeviceIds:$index=$($DeviceId[$index])")
}

$dotnetArguments = $arguments.ToArray()
& dotnet @dotnetArguments
if ($LASTEXITCODE -ne 0) {
    throw "Statistics manual mode exited with code $LASTEXITCODE."
}

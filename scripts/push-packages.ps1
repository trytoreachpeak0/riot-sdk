#Requires -Version 7
<#
.SYNOPSIS
    Pushes RIoT.Sdk.Core, RIoT.Sdk.Generated and RIoT.Sdk.Facade to a NuGet source.

.DESCRIPTION
    publish.yml runs this twice with the same command shape: in the check job
    against a local folder (a rehearsal that every run does, including a run
    that does not publish), and in the push job against GitHub Packages. The
    rehearsal proves, on the SDK this repository pins in global.json, that the
    arguments parse and that NuGet reads the throwaway nuget.config; run
    39006594529 failed on exactly that ("Unrecognized option '--configfile'")
    only once it tried to publish. The script copies global.json into
    -WorkDirectory, so both runs use the pinned SDK and not whatever is newest
    on the machine.

    The token never appears on a command line. The script writes a nuget.config
    that names the source "github" into -WorkDirectory and runs
    `dotnet nuget push <package> --source github` from there, without
    --configfile (SDK 8 has none) and without --api-key. NuGet takes the
    credential from the environment variable NuGetPackageSourceCredentials_github,
    which the caller sets.

    No --skip-duplicate: a version that already exists must fail. Core and
    Generated go first, then the Facade that needs them. The first failure
    stops the script with that exit code.

.EXAMPLE
    pwsh -File scripts/push-packages.ps1 -PackageDirectory $env:RUNNER_TEMP/packages -Source $env:RUNNER_TEMP/rehearsal-feed -WorkDirectory $env:RUNNER_TEMP/push-rehearsal
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    # A feed URL, or a local folder for the rehearsal.
    [Parameter(Mandatory)]
    [string] $Source,

    # Gets this repository's global.json and a throwaway nuget.config; publish.yml
    # uses folders under RUNNER_TEMP for both runs.
    [Parameter(Mandatory)]
    [string] $WorkDirectory
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..' 'global.json') -Destination (Join-Path $WorkDirectory 'global.json') -Force
$escaped = [System.Security.SecurityElement]::Escape($Source)
Set-Content -LiteralPath (Join-Path $WorkDirectory 'nuget.config') -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="github" value="$escaped" />
  </packageSources>
</configuration>
"@

Set-Location -LiteralPath $WorkDirectory
Write-Output "dotnet $(dotnet --version), pushing from $WorkDirectory to $Source"
foreach ($id in 'RIoT.Sdk.Core', 'RIoT.Sdk.Generated', 'RIoT.Sdk.Facade') {
    $package = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter "$id.*.nupkg")
    if ($package.Count -ne 1) {
        Write-Output "::error::Expected one $id package in $PackageDirectory, found $($package.Count)."
        exit 1
    }
    dotnet nuget push $package[0].FullName --source github
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

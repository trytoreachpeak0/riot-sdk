#Requires -Version 7
<#
.SYNOPSIS
    Checks the packed RIoT.Sdk packages before they are pushed to GitHub Packages.

.DESCRIPTION
    Run against the output folder of `dotnet pack`. publish.yml runs it before
    every push; run it locally to review a package before asking for a publish.

    It fails when:
    - the version is one the 8005 v2 line has used, or carries a -controlserver
      or -fp suffix;
    - the folder does not hold exactly RIoT.Sdk.Core, RIoT.Sdk.Generated and
      RIoT.Sdk.Facade at that version, as .nupkg;
    - a package holds a file outside the expected set (nuspec, the one DLL with
      its embedded PDB, README.md, and the OPC files NuGet adds);
    - the nuspec lacks the license expression, repository URL or commit, or the
      Facade does not depend on Core and Generated at the same version;
    - any file contains something shaped like a credential (a JWT, a private
      key, a bearer token, a password or key assignment) or an absolute local
      path (build with ContinuousIntegrationBuild=true);
    - with -CheckFeed, the GitHub Packages feed already holds that version, or
      cannot be asked. The feed needs a token with read access to packages in
      the environment variable NUGET_FEED_TOKEN (in publish.yml, GITHUB_TOKEN).
      This is a real NuGet API request to nuget.pkg.github.com, so it also
      proves the runner can reach the feed.

    It prints every package's file list, which is what goes into a publish
    request.

.EXAMPLE
    dotnet pack csharp/RIoT.Sdk.sln -c Release -o artifacts/packages -p:ContinuousIntegrationBuild=true
    pwsh -File scripts/check-packages.ps1 -PackageDirectory artifacts/packages
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [switch] $CheckFeed,

    [string] $FeedUrl = 'https://nuget.pkg.github.com/trytoreachpeak0/index.json'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$ids = 'RIoT.Sdk.Core', 'RIoT.Sdk.Generated', 'RIoT.Sdk.Facade'
$v2Versions = @(
    '0.1.0-controlserver.1', '0.1.0-controlserver.2',
    '0.2.0-fp.1', '0.2.0-fp.2', '0.2.0-fp.3', '0.2.0-fp.4',
    '0.2.0-fp.5', '0.2.0-fp.6', '0.2.0-fp.7'
)
$failures = [System.Collections.Generic.List[string]]::new()

$propsPath = Join-Path $PSScriptRoot '..' 'Directory.Build.props'
$props = [xml](Get-Content -LiteralPath $propsPath -Raw)
$version = @($props.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$license = @($props.Project.PropertyGroup.PackageLicenseExpression | Where-Object { $_ })[0]
if (-not $version) { throw "No <Version> in $propsPath." }
Write-Output "Version $version, license $license"

if ($version -in $v2Versions -or $version -match '-(controlserver|fp)(\.|$)') {
    $failures.Add("Version $version belongs to the 8005 v2 numbering; pick a new one.")
}

$expected = foreach ($id in $ids) { "$id.$version.nupkg" }
$present = @(Get-ChildItem -LiteralPath $PackageDirectory -File | Where-Object Extension -in '.nupkg', '.snupkg' | ForEach-Object Name)
foreach ($name in $expected) { if ($name -notin $present) { $failures.Add("Missing $name.") } }
foreach ($name in $present) { if ($name -notin $expected) { $failures.Add("Unexpected package $name.") } }

# Shaped like a credential, in UTF-8 text or in the UTF-16 string heap of a DLL.
$credentialPatterns = [ordered]@{
    'JWT'                 = 'eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}'
    'private key'         = '-----BEGIN [A-Z ]*PRIVATE KEY-----'
    'bearer token'        = '(?i)bearer\s+[A-Za-z0-9._~+/-]{16,}'
    'secret assignment'   = '(?i)(password|passwd|pwd|secret|api[_-]?key|callapikey|access[_-]?token)["'']?\s*[=:]\s*["'']?[A-Za-z0-9._~+/!@#$%^&*-]{8,}'
    'connection password' = '(?i)(password|pwd)=[^;"\s]{1,}'
}
$localPath = '(?i)[A-Z]:\\(Users|a|actions-runner|runner)\\'

foreach ($name in $expected | Where-Object { $_ -in $present }) {
    $path = Join-Path $PackageDirectory $name
    $id = $name.Substring(0, $name.Length - ".$version.nupkg".Length)
    $allowed = '_rels/.rels', '[Content_Types].xml', "$id.nuspec", "lib/net8.0/$id.dll", 'README.md'

    Write-Output ''
    Write-Output "== $name"
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        foreach ($entry in $zip.Entries) {
            Write-Output ("  {0,10:N0}  {1}" -f $entry.Length, $entry.FullName)
            if ($entry.FullName -notin $allowed -and $entry.FullName -notmatch '^package/services/metadata/core-properties/[0-9a-f]+\.psmdcp$') {
                $failures.Add("$name holds unexpected file $($entry.FullName).")
            }

            $stream = $entry.Open()
            $buffer = [System.IO.MemoryStream]::new()
            try { $stream.CopyTo($buffer) } finally { $stream.Dispose() }
            $bytes = $buffer.ToArray()
            foreach ($text in [System.Text.Encoding]::UTF8.GetString($bytes), [System.Text.Encoding]::Unicode.GetString($bytes)) {
                foreach ($pattern in $credentialPatterns.GetEnumerator()) {
                    foreach ($match in [regex]::Matches($text, $pattern.Value)) {
                        $failures.Add("$name/$($entry.FullName): looks like a $($pattern.Key) at offset $($match.Index) (value not shown).")
                    }
                }
                if ($text -match $localPath) {
                    $failures.Add("$name/$($entry.FullName): holds an absolute local path ($($Matches[0])); pack with ContinuousIntegrationBuild=true.")
                }
            }

            # The embedded PDB is compressed, so the text scan above cannot see its
            # source paths. Read them: a CI build maps every one under /_/.
            if ($entry.FullName -eq "lib/net8.0/$id.dll") {
                $pe = [System.Reflection.PortableExecutable.PEReader]::new([System.IO.MemoryStream]::new($bytes))
                try {
                    $embedded = @($pe.ReadDebugDirectory() | Where-Object Type -eq 'EmbeddedPortablePdb')
                    if (-not $embedded) {
                        $failures.Add("$name/$($entry.FullName) has no embedded PDB.")
                    }
                    else {
                        $provider = $pe.ReadEmbeddedPortablePdbDebugDirectoryData($embedded[0])
                        try {
                            $pdb = $provider.GetMetadataReader()
                            $documents = @(foreach ($handle in $pdb.Documents) { $pdb.GetString($pdb.GetDocument($handle).Name) })
                            $unmapped = @($documents | Where-Object { -not $_.StartsWith('/_/') })
                            Write-Output "  embedded PDB: $($documents.Count) source paths, e.g. $($documents | Select-Object -First 1)"
                            foreach ($document in $unmapped | Select-Object -First 3) {
                                $failures.Add("$name/$($entry.FullName): PDB source path '$document' is not mapped under /_/; pack with ContinuousIntegrationBuild=true.")
                            }
                        }
                        finally { $provider.Dispose() }
                    }
                }
                finally { $pe.Dispose() }
            }

            if ($entry.FullName -eq "$id.nuspec") {
                $nuspec = [xml][System.Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)
                $metadata = $nuspec.package.metadata
                if ($metadata.id -ne $id) { $failures.Add("$name nuspec id is '$($metadata.id)'.") }
                if ($metadata.version -ne $version) { $failures.Add("$name nuspec version is '$($metadata.version)'.") }
                if ($metadata.license.type -ne 'expression' -or $metadata.license.'#text' -ne $license) {
                    $failures.Add("$name license is '$($metadata.license.'#text')' ($($metadata.license.type)), expected expression '$license'.")
                }
                if ($metadata.repository.url -notmatch '^https://github\.com/trytoreachpeak0/riot-sdk(\.git)?$') {
                    $failures.Add("$name repository url is '$($metadata.repository.url)'.")
                }
                if ($metadata.repository.commit -notmatch '^[0-9a-f]{40}$') {
                    $failures.Add("$name has no repository commit.")
                }
                if (-not $metadata.description) { $failures.Add("$name has no description.") }
                if ($metadata.readme -ne 'README.md') { $failures.Add("$name nuspec names no README.md as its readme.") }
                Write-Output "  license $($metadata.license.'#text'), repository $($metadata.repository.url) @ $($metadata.repository.commit)"
                $dependencies = @($metadata.dependencies.group.dependency | Where-Object { $_ })
                foreach ($dependency in $dependencies) { Write-Output "  depends on $($dependency.id) $($dependency.version)" }
                if ($id -eq 'RIoT.Sdk.Facade') {
                    foreach ($sibling in 'RIoT.Sdk.Core', 'RIoT.Sdk.Generated') {
                        $dependency = $dependencies | Where-Object id -eq $sibling
                        if (-not $dependency -or $dependency.version -ne $version) {
                            $failures.Add("RIoT.Sdk.Facade does not depend on $sibling $version.")
                        }
                    }
                }
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

if ($CheckFeed) {
    Write-Output ''
    if (-not $env:NUGET_FEED_TOKEN) { throw 'NUGET_FEED_TOKEN is not set; -CheckFeed needs a token that can read packages.' }
    $basic = [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("token:$($env:NUGET_FEED_TOKEN)"))
    $headers = @{ Authorization = "Basic $basic" }
    # 45 seconds: the runner reaches GitHub through a proxy, where short timeouts
    # have failed requests that complete given time.
    $index = Invoke-RestMethod -Uri $FeedUrl -Headers $headers -TimeoutSec 45
    $base = @($index.resources | Where-Object { $_.'@type' -like 'PackageBaseAddress/3.0.0*' })[0].'@id'
    if (-not $base) { throw "The feed index at $FeedUrl lists no PackageBaseAddress." }
    Write-Output "Feed $FeedUrl answered; package base address $base"
    foreach ($id in $ids) {
        $uri = "$($base.TrimEnd('/'))/$($id.ToLowerInvariant())/index.json"
        $response = Invoke-WebRequest -Uri $uri -Headers $headers -TimeoutSec 45 -SkipHttpErrorCheck
        if ($response.StatusCode -eq 404) {
            Write-Output "Feed: $id not published yet."
        }
        elseif ($response.StatusCode -eq 200) {
            $published = @(($response.Content | ConvertFrom-Json).versions)
            Write-Output "Feed: $id has $($published -join ', ')"
            if ($version.ToLowerInvariant() -in $published) { $failures.Add("The feed already has $id $version.") }
        }
        else {
            $failures.Add("The feed answered $($response.StatusCode) for $id; cannot tell whether $version is free.")
        }
    }
}

Write-Output ''
if ($failures.Count) {
    $failures | ForEach-Object { Write-Output "FAIL: $_" }
    exit 1
}
Write-Output 'All package checks passed.'

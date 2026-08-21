<#
.SYNOPSIS
    Builds the solution and runs every test suite.

.PARAMETER Configuration
    Debug or Release. Release is what CI and the quality gate use.

.PARAMETER Coverage
    Also produce an OpenCover report that SonarQube can read.

.PARAMETER NoBuild
    Run the tests against whatever is already built.

.EXAMPLE
    .\build\Invoke-Tests.ps1
    .\build\Invoke-Tests.ps1 -Coverage
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Coverage,
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$root = Get-RepositoryRoot
$resultsDirectory = Join-Path $root 'artifacts\test-results'

if (-not $NoBuild)
{
    Write-Step "Building the solution ($Configuration|x64)"
    & (Get-MSBuildPath) (Join-Path $root 'TiaSclStudio.sln') -t:Restore -v:quiet -nologo
    Assert-LastExitCode 'NuGet restore'

    & (Get-MSBuildPath) (Join-Path $root 'TiaSclStudio.sln') `
        "-p:Configuration=$Configuration" -p:Platform=x64 -v:minimal -nologo -m
    Assert-LastExitCode 'Build'
}

if (Test-Path $resultsDirectory)
{
    Remove-Item $resultsDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null

$vstest = Get-VSTestPath
$assemblies = Get-TestAssemblyPaths -Configuration $Configuration

if (-not $Coverage)
{
    Write-Step "Running $($assemblies.Count) test assemblies"
    & $vstest $assemblies `
        /Platform:x64 `
        /Framework:".NETFramework,Version=v4.8" `
        "/ResultsDirectory:$resultsDirectory" `
        /Logger:"trx;LogFileName=results.trx" `
        /Logger:"console;verbosity=minimal"
    Assert-LastExitCode 'Test run'
    Write-Host "`nTest results: $resultsDirectory" -ForegroundColor Green
    return
}

# Coverage needs the binaries somewhere AltCover can address. This repository's
# own path contains '#', which the tool reads as the start of a comment, so
# every run is staged into a scratch directory that is guaranteed not to.
#
# Each suite is instrumented and run on its own, because AltCover derives the
# name of its visit file from the report path. Pointing several test processes
# at one staging directory makes the last one to finish overwrite everything
# the others recorded, which silently reports well covered code as untested.
# SonarQube accepts a list of OpenCover reports, so there is nothing to merge.
$altCoverRoot = Get-NuGetToolPackage -PackageId 'AltCover' -Version '9.0.1'
$altCover = Join-Path $altCoverRoot 'tools\net472\AltCover.exe'
if (-not (Test-Path $altCover))
{
    throw "AltCover was restored but '$altCover' is missing."
}

$reports = @()
$failedSuites = @()

foreach ($name in Get-TestProjectNames)
{
    Write-Step "Running $name under coverage"

    $staging = New-SafeWorkingDirectory -Prefix 'tiascl-coverage'
    try
    {
        Copy-Item (Join-Path $root "tests\$name\bin\x64\$Configuration\*") $staging -Recurse -Force

        $stagedReport = Join-Path $staging 'opencover.xml'
        & $altCover `
            "--inputDirectory=$staging" `
            "--report=$stagedReport" `
            --inplace `
            --save `
            '--assemblyFilter=xunit' `
            '--assemblyFilter=Microsoft' `
            '--assemblyFilter=NuGet' `
            '--assemblyFilter=Newtonsoft' `
            '--assemblyFilter=TestSupport' `
            '--assemblyFilter=SelfTest' `
            '--assemblyFilter=Tests$' | Out-Null
        Assert-LastExitCode "Instrumenting $name"

        & $vstest (Join-Path $staging "$name.dll") `
            /Platform:x64 `
            /Framework:".NETFramework,Version=v4.8" `
            "/ResultsDirectory:$resultsDirectory" `
            /Logger:"trx;LogFileName=$name.trx" `
            /Logger:"console;verbosity=minimal"
        if ($LASTEXITCODE -ne 0)
        {
            $failedSuites += $name
        }

        & $altCover runner "--recorderDirectory=$staging" --collect | Out-Null

        if (-not (Test-Path $stagedReport))
        {
            throw "AltCover produced no coverage report for $name."
        }

        $target = Join-Path $resultsDirectory "opencover-$name.xml"
        Copy-Item $stagedReport $target -Force
        $reports += $target
    }
    finally
    {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Per-module union across the reports. A sequence point covered by any suite is
# covered, which is exactly how SonarQube reads a list of reports. Summing the
# per-report percentages instead would understate every assembly that more than
# one suite touches.
$modules = @{}
foreach ($report in $reports)
{
    $document = [xml](Get-Content $report)

    # File ids are assigned per report, so the same source file gets a different
    # number in each one. Keying the union on the id would count one line as
    # several distinct lines and understate the result.
    $fileNames = @{}
    foreach ($file in $document.SelectNodes('//FileRef/..//File') + $document.SelectNodes('//Files/File'))
    {
        if ($file -and $file.uid)
        {
            $fileNames[$file.uid] = $file.fullPath
        }
    }

    foreach ($module in $document.CoverageSession.Modules.Module)
    {
        if (-not $modules.ContainsKey($module.ModuleName))
        {
            $modules[$module.ModuleName] = @{
                Visited = New-Object 'System.Collections.Generic.HashSet[string]'
                All     = New-Object 'System.Collections.Generic.HashSet[string]'
            }
        }

        # A module with no classes, a class with no methods and a method with no
        # sequence points are all normal in an OpenCover report, and under
        # StrictMode reaching through a missing element is an error rather than
        # a null, so every level is selected explicitly.
        $bucket = $modules[$module.ModuleName]
        foreach ($point in $module.SelectNodes('.//SequencePoint'))
        {
            $file = if ($fileNames.ContainsKey($point.fileid)) { $fileNames[$point.fileid] } else { $point.fileid }
            $key = "$file|$($point.sl):$($point.sc):$($point.el):$($point.ec)"
            [void]$bucket.All.Add($key)
            if ([int]$point.vc -gt 0)
            {
                [void]$bucket.Visited.Add($key)
            }
        }
    }
}

Write-Host ''
Write-Host 'Line coverage by assembly (union of every suite):'
foreach ($name in ($modules.Keys | Sort-Object))
{
    $bucket = $modules[$name]
    if ($bucket.All.Count -eq 0)
    {
        continue
    }

    $percent = [math]::Round(100.0 * $bucket.Visited.Count / $bucket.All.Count, 2)
    Write-Host ("  {0,-34} {1,6}%  ({2}/{3} points)" -f $name, $percent, $bucket.Visited.Count, $bucket.All.Count)
}

Write-Host ''
Write-Host "Coverage reports: $resultsDirectory\opencover-*.xml" -ForegroundColor Green

if ($failedSuites.Count -gt 0)
{
    throw "Failing suites: $($failedSuites -join ', ')."
}

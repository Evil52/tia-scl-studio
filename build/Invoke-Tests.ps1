<#
.SYNOPSIS
    Builds the solution and runs every test suite.

.PARAMETER Configuration
    Debug or Release. Release is what CI and the quality gate use.

.PARAMETER Coverage
    Also produce an OpenCover report that SonarQube can read.

.PARAMETER NoBuild
    Run the tests against whatever is already built.

.PARAMETER MinimumScopedLineCoverage
    Minimum line coverage for the Sonar production scope. The default is 100.

.EXAMPLE
    .\build\Invoke-Tests.ps1
    .\build\Invoke-Tests.ps1 -Coverage
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Coverage,
    [switch] $NoBuild,
    [ValidateRange(0.0, 100.0)]
    [double] $MinimumScopedLineCoverage = 100.0
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
$coverageGate = @{
    Visited = New-Object 'System.Collections.Generic.HashSet[string]'
    All     = New-Object 'System.Collections.Generic.HashSet[string]'
}

function Test-IsScopedProductionSource
{
    param(
        [string] $ModuleName,
        [string] $SourcePath
    )

    if ($ModuleName -notin @(
        'TiaSclStudio.Core',
        'TiaSclStudio.Diagram',
        'TiaSclStudio.Openness',
        'TiaSclStudio.App'))
    {
        return $false
    }

    $fullPath = [IO.Path]::GetFullPath($SourcePath)
    $rootPrefix = $root.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase))
    {
        return $false
    }

    $relative = $fullPath.Substring($rootPrefix.Length).Replace('/', '\')
    if ($relative -like '*\bin\*' -or
        $relative -like '*\obj\*' -or
        $relative -like '*\Properties\AssemblyInfo.cs')
    {
        return $false
    }

    # These files remain part of static analysis. Only runtime coverage is
    # excluded: they are WPF event wiring or boundaries that require a real
    # installed TIA Portal/Siemens runtime and are exercised on the TIA VM.
    if ($relative -like 'src\TiaSclStudio.App\MainWindow*.cs' -or
        $relative -like 'src\TiaSclStudio.App\*.xaml' -or
        $relative -like 'src\TiaSclStudio.App\*.xaml.cs' -or
        $relative -eq 'src\TiaSclStudio.App\TiaGatewayWorker.cs' -or
        $relative -eq 'src\TiaSclStudio.Openness\Discovery\OpennessInstallationLocator.cs' -or
        $relative -like 'src\TiaSclStudio.Openness.Legacy.V17\*')
    {
        return $false
    }

    return $true
}

# Tests that start the product as a child process cannot be measured in the
# same pass as the rest of their suite. The child loads the instrumented
# assemblies from the same staging directory and writes to the same AltCover
# visit file, so whichever process flushes last wins and the other's data is
# lost. That showed up as this assembly's coverage swinging by thirty points
# between identical runs. These tests still run, just afterwards and without
# instrumentation, where all they have to prove is that the shipped executable
# works.
$processLaunchingTests = @{
    'TiaSclStudio.EndToEnd.Tests' = 'SelfTestExecutableTests'
}

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

        $instrumentedArguments = @(
            (Join-Path $staging "$name.dll"),
            '/Platform:x64',
            '/Framework:.NETFramework,Version=v4.8',
            "/ResultsDirectory:$resultsDirectory",
            "/Logger:trx;LogFileName=$name.trx",
            '/Logger:console;verbosity=minimal'
        )

        if ($processLaunchingTests.ContainsKey($name))
        {
            # "!~" is vstest's does-not-contain operator; wrapping the positive
            # form in "!( )" is not valid filter syntax and silently selects
            # nothing.
            $instrumentedArguments += "/TestCaseFilter:FullyQualifiedName!~$($processLaunchingTests[$name])"
        }

        & $vstest @instrumentedArguments
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

    if ($processLaunchingTests.ContainsKey($name))
    {
        Write-Step "Running the process-launching tests of $name without instrumentation"
        & $vstest (Join-Path $root "tests\$name\bin\x64\$Configuration\$name.dll") `
            /Platform:x64 `
            /Framework:".NETFramework,Version=v4.8" `
            "/ResultsDirectory:$resultsDirectory" `
            "/Logger:trx;LogFileName=$name.subprocess.trx" `
            '/Logger:console;verbosity=minimal' `
            "/TestCaseFilter:FullyQualifiedName~$($processLaunchingTests[$name])"
        if ($LASTEXITCODE -ne 0)
        {
            $failedSuites += "$name (process tests)"
        }
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

            if (Test-IsScopedProductionSource ([string]$module.ModuleName) $file)
            {
                [void]$coverageGate.All.Add($key)
                if ([int]$point.vc -gt 0)
                {
                    [void]$coverageGate.Visited.Add($key)
                }
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

if ($coverageGate.All.Count -eq 0)
{
    $failedSuites += 'Scoped line coverage (no production sequence points found)'
}
else
{
    $scopedPercent = 100.0 * $coverageGate.Visited.Count / $coverageGate.All.Count
    $coverageColor = if ($scopedPercent + 0.0000001 -ge $MinimumScopedLineCoverage) { 'Green' } else { 'Red' }
    Write-Host ''
    Write-Host (
        'Sonar production-scope line coverage: {0}% ({1}/{2} points; required {3}%)' -f
        [math]::Round($scopedPercent, 2),
        $coverageGate.Visited.Count,
        $coverageGate.All.Count,
        $MinimumScopedLineCoverage) -ForegroundColor $coverageColor

    if ($scopedPercent + 0.0000001 -lt $MinimumScopedLineCoverage)
    {
        $failedSuites += 'Scoped line coverage'
    }
}

Write-Host ''
Write-Host "Coverage reports: $resultsDirectory\opencover-*.xml" -ForegroundColor Green

if ($failedSuites.Count -gt 0)
{
    throw "Failing suites: $($failedSuites -join ', ')."
}

<#
.SYNOPSIS
    Runs a full SonarQube analysis against a locally hosted server.

.DESCRIPTION
    The .NET analyser works by wrapping the build: the scanner's "begin" step
    installs itself into MSBuild, the solution is rebuilt so the analyser sees
    every compilation, the tests run to produce coverage, and "end" uploads the
    result. A build that is skipped or incremental produces an empty analysis,
    so this script always rebuilds.

    Prerequisites, none of which this script installs for you:
      * A Java 17+ runtime on PATH               (the scanner and the server are Java)
      * A SonarQube server, by default at http://localhost:9000
        - build\sonarqube-compose.yml starts one in Docker
      * A user token from that server            (My Account -> Security -> Generate)

.PARAMETER Token
    The SonarQube user token. Falls back to the SONAR_TOKEN environment variable.

.PARAMETER HostUrl
    The server to publish to. Defaults to the local one.

.PARAMETER SkipTests
    Analyse without running the tests. The result carries no coverage.

.EXAMPLE
    $env:SONAR_TOKEN = 'squ_...'
    .\build\Invoke-SonarQube.ps1

.EXAMPLE
    .\build\Invoke-SonarQube.ps1 -Token squ_xxx -HostUrl http://sonar.internal:9000
#>
[CmdletBinding()]
param(
    [string] $Token = $env:SONAR_TOKEN,
    [string] $HostUrl = 'http://localhost:9000',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $ScannerVersion = '11.2.1.137242',
    [switch] $SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$root = Get-RepositoryRoot
$projectKey = 'tia-scl-studio'
$projectName = 'TIA SCL Studio'
$projectVersion = '0.2.0'

# ---------------------------------------------------------------------------
# Prerequisites, checked up front so a missing one fails in a second rather
# than after a full rebuild.
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($Token))
{
    throw @'
No SonarQube token. Create one in the server under
  My Account -> Security -> Generate Tokens
then either pass -Token or set the SONAR_TOKEN environment variable.
'@
}

$java = Get-Command java -ErrorAction SilentlyContinue
if (-not $java)
{
    throw @'
No Java runtime on PATH. Both the SonarQube server and the scanner need
Java 17 or newer. Install a JDK (for example Temurin 17) and reopen the shell.
'@
}

Write-Step 'Checking the SonarQube server'
try
{
    $status = Invoke-RestMethod -Uri "$HostUrl/api/system/status" -TimeoutSec 15
    if ($status.status -ne 'UP')
    {
        throw "SonarQube at $HostUrl reports status '$($status.status)'. Wait for it to finish starting."
    }

    Write-Host "  $HostUrl is up (SonarQube $($status.version))"
}
catch [System.Net.WebException]
{
    throw @"
Cannot reach SonarQube at $HostUrl.

Start one locally with:
  docker compose -f build\sonarqube-compose.yml up -d

then wait for $HostUrl to answer, sign in as admin/admin, change the password
and generate a token.
"@
}

# ---------------------------------------------------------------------------
# The scanner
# ---------------------------------------------------------------------------

Write-Step 'Resolving SonarScanner for MSBuild'
$scanner = Get-SonarScannerPath -Version $ScannerVersion
Write-Host "  $scanner"

if (-not (Test-PathIsToolSafe $root))
{
    Write-Warning @"
The repository path contains a '#':
  $root
Several tools in this chain treat it as the start of a comment and silently
truncate the path. The coverage step already works around it, but if the
scanner behaves oddly, move the checkout to a path without '#'.
"@
}

# ---------------------------------------------------------------------------
# begin -> rebuild -> test -> end
# ---------------------------------------------------------------------------

$resultsDirectory = Join-Path $root 'artifacts\test-results'
$coveragePattern = Join-Path $resultsDirectory 'opencover-*.xml'
$trxPattern = Join-Path $resultsDirectory '*.trx'

Write-Step 'sonarscanner begin'
$beginArguments = @(
    'begin',
    "/k:$projectKey",
    "/n:$projectName",
    "/v:$projectVersion",
    "/d:sonar.host.url=$HostUrl",
    "/d:sonar.token=$Token",
    "/d:sonar.sourceEncoding=UTF-8",
    "/d:sonar.scanner.scanAll=false"
)

if (-not $SkipTests)
{
    $beginArguments += "/d:sonar.cs.opencover.reportsPaths=$coveragePattern"
    $beginArguments += "/d:sonar.cs.vstest.reportsPaths=$trxPattern"
}

& $scanner @beginArguments
Assert-LastExitCode 'sonarscanner begin'

try
{
    Write-Step 'Rebuilding the solution under the analyser'
    $msbuild = Get-MSBuildPath
    & $msbuild (Join-Path $root 'TiaSclStudio.sln') -t:Restore -v:quiet -nologo
    Assert-LastExitCode 'NuGet restore'

    # A full rebuild, not an incremental one: the analyser only sees files that
    # are actually compiled during the wrapped build.
    & $msbuild (Join-Path $root 'TiaSclStudio.sln') `
        -t:Rebuild "-p:Configuration=$Configuration" -p:Platform=x64 -v:minimal -nologo -m
    Assert-LastExitCode 'Build'

    if (-not $SkipTests)
    {
        Write-Step 'Running the tests with coverage'
        & (Join-Path $PSScriptRoot 'Invoke-Tests.ps1') -Configuration $Configuration -Coverage -NoBuild
        Assert-LastExitCode 'Tests'
    }
}
finally
{
    Write-Step 'sonarscanner end'
    & $scanner end "/d:sonar.token=$Token"
    Assert-LastExitCode 'sonarscanner end'
}

Write-Host ''
Write-Host "Analysis published: $HostUrl/dashboard?id=$projectKey" -ForegroundColor Green

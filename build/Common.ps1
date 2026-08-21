<#
.SYNOPSIS
    Tool discovery shared by every build script in this folder.

.DESCRIPTION
    The repository targets .NET Framework 4.8 with legacy project files, so the
    toolchain is MSBuild and vstest.console from a Visual Studio or Build Tools
    installation rather than the dotnet SDK. Nothing here assumes a particular
    install path: everything is located through vswhere.
#>

Set-StrictMode -Version Latest

$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Get-RepositoryRoot
{
    return $script:RepositoryRoot
}

function Get-VsWherePath
{
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere))
    {
        throw "vswhere.exe was not found at '$vswhere'. Install Visual Studio 2022 or the Visual Studio Build Tools."
    }

    return $vswhere
}

function Get-MSBuildPath
{
    $found = & (Get-VsWherePath) -latest -products * -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $found)
    {
        throw 'MSBuild was not found. Install the "MSBuild" component of Visual Studio 2022 Build Tools.'
    }

    return $found
}

function Get-VSTestPath
{
    $found = & (Get-VsWherePath) -latest -products * `
        -find 'Common7\IDE\Extensions\TestPlatform\vstest.console.exe' | Select-Object -First 1
    if (-not $found)
    {
        throw 'vstest.console.exe was not found. Install the "Testing tools core features" component.'
    }

    return $found
}

<#
.SYNOPSIS
    Restores a NuGet package through MSBuild and returns its extracted folder.

.DESCRIPTION
    There is no dotnet SDK on a machine that only has Build Tools, so a throwaway
    legacy project with a PackageReference is the most dependable way to pull a
    command-line tool down into the global package cache.
#>
function Get-NuGetToolPackage
{
    param(
        [Parameter(Mandatory = $true)][string] $PackageId,
        [Parameter(Mandatory = $true)][string] $Version
    )

    $cached = Join-Path $env:USERPROFILE ".nuget\packages\$($PackageId.ToLowerInvariant())\$Version"
    if (Test-Path $cached)
    {
        return $cached
    }

    $stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("tiascl-tool-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
    try
    {
        $projectPath = Join-Path $stagingRoot 'tool.csproj'
        @"
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="`$(MSBuildToolsPath)\Microsoft.Common.props" Condition="Exists('`$(MSBuildToolsPath)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '`$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '`$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{33333333-2222-3333-4444-555555555555}</ProjectGuid>
    <OutputType>Library</OutputType>
    <AssemblyName>Tool</AssemblyName>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <RestoreProjectStyle>PackageReference</RestoreProjectStyle>
    <OutputPath>bin\</OutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="$PackageId" Version="$Version" />
  </ItemGroup>
  <Import Project="`$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
"@ | Set-Content -Path $projectPath -Encoding utf8

        & (Get-MSBuildPath) $projectPath -t:Restore -v:quiet -nologo | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "Restoring $PackageId $Version failed."
        }
    }
    finally
    {
        Remove-Item $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path $cached))
    {
        throw "$PackageId $Version was restored but is not in the expected cache folder '$cached'."
    }

    return $cached
}

<#
.SYNOPSIS
    A scratch directory guaranteed to contain no '#' character.

.DESCRIPTION
    This repository's own folder name contains '#'. Several command-line tools,
    AltCover among them, treat it as a comment delimiter and silently truncate
    the path. Anything that has to hand a directory to such a tool stages it
    here first.
#>
function New-SafeWorkingDirectory
{
    param([string] $Prefix = 'tiascl')

    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("$Prefix-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    return $path
}

function Test-PathIsToolSafe
{
    param([Parameter(Mandatory = $true)][string] $Path)

    return -not $Path.Contains('#')
}

function Get-TestProjectNames
{
    return @(
        'TiaSclStudio.Core.Tests',
        'TiaSclStudio.Diagram.Tests',
        'TiaSclStudio.Openness.Tests',
        'TiaSclStudio.Integration.Tests',
        'TiaSclStudio.EndToEnd.Tests'
    )
}

function Get-TestAssemblyPaths
{
    param([string] $Configuration = 'Release')

    $root = Get-RepositoryRoot
    $paths = @()
    foreach ($name in Get-TestProjectNames)
    {
        $assembly = Join-Path $root "tests\$name\bin\x64\$Configuration\$name.dll"
        if (-not (Test-Path $assembly))
        {
            throw "Test assembly '$assembly' was not found. Build the solution first."
        }

        $paths += $assembly
    }

    return $paths
}

<#
.SYNOPSIS
    The product assemblies that coverage and quality gates apply to.
#>
function Get-ProductAssemblyNames
{
    return @(
        'TiaSclStudio.Core',
        'TiaSclStudio.Diagram',
        'TiaSclStudio.Openness',
        'TiaSclStudio.Openness.Legacy.V17',
        'TiaSclStudio.App'
    )
}

<#
.SYNOPSIS
    Returns the path to SonarScanner.MSBuild.exe, downloading it if necessary.

.DESCRIPTION
    The .NET Framework flavour of the scanner is published as a zip on GitHub
    rather than on NuGet: the 'dotnet-sonarscanner' package is a dotnet tool
    that needs the SDK, which a Build Tools machine does not have. An existing
    scanner on PATH always wins, so a machine with a managed install is left
    alone.
#>
function Get-SonarScannerPath
{
    param([string] $Version = '11.2.1.137242')

    $onPath = Get-Command 'SonarScanner.MSBuild.exe' -ErrorAction SilentlyContinue
    if ($onPath)
    {
        return $onPath.Source
    }

    $toolsRoot = Join-Path (Get-RepositoryRoot) "artifacts\tools\sonar-scanner-$Version"
    $scanner = Join-Path $toolsRoot 'SonarScanner.MSBuild.exe'
    if (Test-Path $scanner)
    {
        return $scanner
    }

    $archiveName = "sonar-scanner-$Version-net-framework.zip"
    $url = "https://github.com/SonarSource/sonar-scanner-msbuild/releases/download/$Version/$archiveName"
    $downloadPath = Join-Path ([System.IO.Path]::GetTempPath()) $archiveName

    Write-Host "  downloading $archiveName"
    try
    {
        # The GitHub release endpoint refuses the default .NET user agent and
        # older TLS versions.
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $downloadPath -UseBasicParsing -TimeoutSec 300
    }
    catch
    {
        throw @"
Could not download the SonarScanner for MSBuild from
  $url

Download '$archiveName' by hand, extract it to
  $toolsRoot
and run this script again. Alternatively put SonarScanner.MSBuild.exe on PATH.

Underlying error: $($_.Exception.Message)
"@
    }

    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
    Expand-Archive -Path $downloadPath -DestinationPath $toolsRoot -Force
    Remove-Item $downloadPath -Force -ErrorAction SilentlyContinue

    if (-not (Test-Path $scanner))
    {
        # Some releases nest everything one level down.
        $scanner = Get-ChildItem $toolsRoot -Recurse -Filter 'SonarScanner.MSBuild.exe' |
            Select-Object -First 1 -ExpandProperty FullName
    }

    if (-not $scanner -or -not (Test-Path $scanner))
    {
        throw "The scanner archive was extracted to '$toolsRoot' but SonarScanner.MSBuild.exe is not in it."
    }

    return $scanner
}

function Write-Step
{
    param([Parameter(Mandatory = $true)][string] $Message)

    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-LastExitCode
{
    param([Parameter(Mandatory = $true)][string] $Activity)

    if ($LASTEXITCODE -ne 0)
    {
        throw "$Activity failed with exit code $LASTEXITCODE."
    }
}

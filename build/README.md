# Build, test and analysis

Everything here runs from the repository root in Windows PowerShell.

```powershell
.\build\Invoke-Tests.ps1                 # build + run all five suites
.\build\Invoke-Tests.ps1 -Coverage       # the same, plus OpenCover reports
.\build\Invoke-SonarQube.ps1             # full analysis into a local SonarQube
```

## What you need installed

| Tool | Needed for | Notes |
|---|---|---|
| Visual Studio 2022 **Build Tools** | build, test | Components: *MSBuild*, *.NET Framework 4.8 SDK*, *Testing tools core features* |
| Java 17+ | SonarQube analysis | Both the scanner and the server are Java |
| Docker *or* a SonarQube zip | the SonarQube server | See below |

Not needed: the .NET SDK. The projects are legacy MSBuild files targeting
.NET Framework 4.8, and every tool the scripts use either ships with Build Tools
or is downloaded on first run into `artifacts\tools\`.

## Running the tests

`Invoke-Tests.ps1` restores, builds `Release|x64`, and runs the five suites
through `vstest.console.exe`:

| Suite | What it covers |
|---|---|
| `TiaSclStudio.Core.Tests` | identifiers, validation, SCL generation, source writing |
| `TiaSclStudio.Diagram.Tests` | graph analysis, diagram validation, storage, undo history |
| `TiaSclStudio.Openness.Tests` | gateway contracts, discovery, ownership stamping |
| `TiaSclStudio.Integration.Tests` | sheet to SCL, whole-project compilation, the bundle on disk |
| `TiaSclStudio.EndToEnd.Tests` | the real WPF window, the self-test executable, a full user journey |

Results land in `artifacts\test-results\`.

## Coverage

`-Coverage` instruments the product assemblies with
[AltCover](https://github.com/SteveGilham/altcover) and writes one OpenCover
report per suite:

```
artifacts\test-results\opencover-TiaSclStudio.Core.Tests.xml
artifacts\test-results\opencover-TiaSclStudio.Diagram.Tests.xml
...
```

Two things about that arrangement are deliberate:

* **One report per suite.** AltCover names its visit file after the report path.
  Pointing several test processes at one staging directory makes the last one to
  finish overwrite what the others recorded, which reports well covered code as
  untested. SonarQube reads a list of reports and unions them, so there is
  nothing to merge.
* **Staged into a scratch directory.** This repository's own folder name
  contains `#`, which AltCover reads as the start of a comment and silently
  truncates. Everything that hands a directory to such a tool copies it to a
  `#`-free temporary path first. If you hit odd tool behaviour elsewhere, that
  character is the first thing to suspect.
* **Tests that launch the product run in a separate uninstrumented pass.**
  `SelfTestExecutableTests` starts `TiaSclStudio.SelfTest.exe`, which loads the
  same instrumented assemblies and writes to the same visit file as its own test
  host. Whichever process flushed last won, and `TiaSclStudio.App` coverage
  swung between 21% and 53% across identical runs. Those tests still run, just
  afterwards and without instrumentation — all they have to prove is that the
  shipped executable works. Coverage is now byte-identical between runs.

The script prints both the raw per-assembly union and the coverage-gated Sonar
production scope. The latter must be **100%** and is enforced locally and in CI.
It contains every model-only production class. WPF event wiring and the narrow
TIA runtime boundaries are still statically analysed, but their coverage needs
the Windows/TIA VM and is therefore excluded from this offline line gate.

## SonarQube

### Starting a local server

With Docker:

```powershell
docker compose -f build\sonarqube-compose.yml up -d
# wait for http://localhost:9000 to answer (the first start takes a minute or two)
```

Without Docker, download the SonarQube Community zip, extract it and run
`bin\windows-x86-64\StartSonar.bat`. Java is already a prerequisite, so no
further setup is needed for a single-user local instance.

Then sign in as `admin` / `admin`, change the password when prompted, and
generate a token under **My Account → Security → Generate Tokens**.

### Running the analysis

```powershell
$env:SONAR_TOKEN = 'squ_...'
.\build\Invoke-SonarQube.ps1
```

or

```powershell
.\build\Invoke-SonarQube.ps1 -Token squ_xxx -HostUrl http://sonar.internal:9000
```

The script downloads the .NET Framework build of SonarScanner for MSBuild on
first use, then does `begin` → full rebuild → tests with coverage → `end`. The
rebuild is not optional: the analyser only sees files that are compiled inside
the wrapped build, so an incremental build produces an empty analysis.

After the server finishes processing, the script reads the published
`line_coverage` metric back through the Sonar API and fails unless it is 100%.
This makes the target independent of a server whose default quality gate may be
less strict. Use `-MinimumLineCoverage` only for an intentional policy change.

There is deliberately **no `sonar-project.properties`** in this repository. That
file belongs to the generic CLI scanner; the SonarScanner for .NET discovers
sources through MSBuild instead and aborts with
*"sonar-project.properties files are not understood by the SonarScanner for
.NET"* if it finds one. Every analysis setting is a `/d:` argument in
`Invoke-SonarQube.ps1`.

### Suggested quality gate

The defaults measure the whole codebase. For a project with an existing body of
code, gating on *new* code is what actually changes behaviour:

* line coverage on new code = **100%**
* duplicated lines on new code ≤ 3%
* zero new blocker or critical issues
* maintainability, reliability and security rating on new code = A

`Invoke-SonarQube.ps1` waits for the server-side quality gate, so a failed gate
fails the CI job. The coverage exclusions in that script exactly mirror the
local 100% production-scope line gate. `TiaSclStudio.Openness.Legacy.V17`, the
installation locator, WPF event wiring and the gateway worker need a real TIA
Portal/desktop session; they are excluded from coverage only, not from static
analysis. Their integration checks run on the dedicated TIA runner.

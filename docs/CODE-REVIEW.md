# Code review — TIA SCL Studio

Two passes. The first was at `0.1.0`, ~28 000 lines. The second, recorded in
[the second-pass section](#second-pass-at-76555e4), was at commit `76555e4`
after the codebase had grown to ~49 000 lines with the UDT library, SCL import,
PLC addressing and auto-layout.

## The short version

The architecture is sound and unusually disciplined for a project this age. The
layering is real (`Core` and `Diagram` genuinely know nothing about WPF or the
Siemens API), every persisted entity has a stable `Guid`, references use ids
rather than mutable names, and the Openness export is gated behind a dry run and
a one-time confirmation token. The existing self-test is a serious piece of
work — roughly fifty scenarios asserting on exact generated SCL.

What was missing was not care, it was *reach*. The self-test is one
all-or-nothing executable: the first failure aborts the remaining forty-nine
scenarios, nothing measures coverage, and no CI runs any of it. Underneath that
blind spot sat ten defects, several of which end with a lost project file or a
dead process rather than an error message.

Nine of the ten are fixed here, each with a test that fails without the fix.

---

## Defects found and fixed

### 1. A long call chain kills the process

`TopologicalSorter` detected cycles with a recursive Tarjan walk — one CLR frame
per edge of the deepest path. A sheet with a few thousand chained calls exhausts
the thread stack.

This is worse than an exception. On Windows a stack overflow cannot be caught:
the process is torn down. The failure mode observed while reproducing it was a
modal system dialog, **"Cannot create a new guard page for the stack"**, with the
process wedged behind it and unsaved work gone.

It was also *intermittent*, which is the worst property a crash can have. Tarjan
starts from whichever key the dictionary yields first, and node ids are random
`Guid`s, so how deep the recursion goes depends on where in the chain the walk
happens to begin. The same sheet crashes on some opens and not others.

**Fixed** in [TopologicalSorter.cs](../src/TiaSclStudio.Diagram/Validation/TopologicalSorter.cs):
the walk runs on an explicit frame stack. A 50 000-node chain and a 50 000-node
cycle are both covered by tests.

### 2. A newline in a data type rewrites the generated block

Data types, initial values, result targets, binding expressions and FC return
types are copied verbatim into the middle of one line of SCL. Nothing checked
them for line breaks, so this initial value:

```
TRUE;
END_VAR
VAR_TEMP
Injected : Bool
```

closed the declaration, closed the section, and had everything after it read as
a new one. The generated block still looks plausible and TIA still compiles it.

**Fixed**: validation reports `CONTROL_CHARACTER`
([ProjectValidator.cs](../src/TiaSclStudio.Core/Validation/ProjectValidator.cs))
and the generator folds these values onto one line as defence in depth for its
public entry points that do not validate
([SclText.cs](../src/TiaSclStudio.Core/Validation/SclText.cs)).

Binding expressions remain raw SCL by design — an engineer must be able to write
any expression TIA accepts — but they can no longer contain a line break, which
can never be necessary and always corrupts the emitted call.

### 3. A block named `CON` loses its source silently

Generated file names come from block names. Windows keeps `CON`, `PRN`, `AUX`,
`NUL`, `COM1`–`COM9` and `LPT1`–`LPT9` reserved *even with an extension*, so
`CON.scl` opens a device rather than a file.

Measured behaviour: `CON` and `NUL` threw `NotSupportedException` from inside
`FileStream` — an error no caller was catching. The others depend on whether
the device exists on the machine, and where it does the write **succeeds** while
producing no file, shipping an import bundle one source short.

**Fixed** in [ProjectStorage.cs](../src/TiaSclStudio.Core/Storage/ProjectStorage.cs).
Trailing dots and spaces are rejected too, since Windows strips them and the
file then differs from the name that was asked for.

### 4. Unsafe file names surfaced as the wrong error

`ValidateFileName` called `Path.GetFileName` before checking for invalid
characters. On `<`, `>`, `"` and `|`, `Path.GetFileName` throws its own
`ArgumentException` about "illegal characters in path" — so a model problem
escaped as a path-parsing error the caller could not attribute to anything.

**Fixed**: character validation runs first, and every unsafe name now produces
the same `InvalidOperationException` naming the file.

### 5. A hole in the model was reported as valid, then dereferenced

`ProjectValidator` filtered null entries out of every collection through a
`Safe()` helper. A project file with a missing `<Blocks>` element or a null entry
was therefore reported **valid** — and `DiagramSclGenerator.Generate` then did
`project.Blocks.GroupBy(block => block.Id)` and threw `NullReferenceException`.

Validation exists to stop exactly this. A model with a hole cannot be generated
from, so it is not valid.

**Fixed**: `MISSING_COLLECTION` and `NULL_MODEL_ITEM` errors, applied to every
collection down to a unit's bindings.

### 6. A damaged call sheet crashed the editor on every keystroke

`DiagramValidator` guarded `sheet.Groups` against null but not `sheet.Nodes`,
`sheet.Wires` or `node.Pins`. Validation runs on every edit, so a project file
missing one of those collections made the editor throw continuously.

**Fixed**: a shape check runs first and reports `DGM060`–`DGM065`.

### 7. `NaN` geometry silently disabled the bounds checks

Group bounds are validated against the sheet size. Every comparison against
`NaN` is false, so a corrupt sheet width did not fail the check — it *passed*
it, and disabled it for every group on the sheet.

**Fixed**: `DGM066`–`DGM068` require finite, positive sheet dimensions, a finite
positive zoom, and finite node positions.

### 8. Concurrent saves littered the project folder and failed

Two saves racing inside `File.Replace` — two windows on one project, or an
autosave landing on a manual save — left Windows' own `~RF*.TMP` backup files
next to the project and failed roughly a quarter of the attempts. The staging
file the code creates itself was cleaned up correctly; the ones `ReplaceFile`
creates internally were not, because nothing knew about them.

**Fixed** in [DiagramProjectStorage.cs](../src/TiaSclStudio.Diagram/Storage/DiagramProjectStorage.cs):
saving is serialised across the process, with a short retry for the transient
sharing violation a virus scanner or backup agent causes. Content atomicity was
already correct and is now pinned by a test.

### 9. A project could become unsaveable with no way to find out why

Free text — a block description, a comment, a body — carrying a character XML
cannot represent (a `NUL` pasted from a log, a lone surrogate) makes the whole
project impossible to save. The failure arrived as an exception from deep inside
`XmlSerializer` naming no field, leaving the user stranded with work they cannot
write to disk.

**Fixed**: `NON_PERSISTABLE_TEXT` names the field. The existing atomic-save
behaviour means the previous version on disk survives, which is now tested.

### 10. Dead defensive code

```csharp
private static readonly XmlSerializer Serializer = typeof(DiagramProject)
    .GetConstructor(Type.EmptyTypes) == null ? null : new XmlSerializer(...);
```

`DiagramProject` has a parameterless constructor, so the null branch is
unreachable — and had it ever been taken, every method would have thrown
`NullReferenceException` instead of saying what was wrong. **Removed.**

---

## Findings not changed

These are judgement calls that belong to the maintainer, not defects to fix
unilaterally.

**`AreExactlyCompatible(null, null)` returns true.** Two pins that both lost
their data type are considered compatible. That is safe today only because every
pin is matched against a validated interface member and `Core` rejects an empty
data type. It is a real coupling between two files that do not reference each
other, so it now has a test that says so out loud.

**`Normalize` strips *all* whitespace**, which is what makes
`Array [0..9] of Bool` match `Array[0..9]ofBool` — but it also makes `I nt`
match `Int`. Harmless in practice, worth knowing.

**`OpennessInstallationLocator` always scans the registry.** The constructor
accepts fallback roots "for deterministic tests", but registry discovery runs
regardless, so results depend on what is installed on the machine. The tests work
around it by scoping every assertion to a throwaway directory. A seam that
disables registry scanning would make them simpler and more honest.

**Two files are very large.** `MainWindow.xaml.cs` is 2 280 lines across seven
partials, and `LegacyV17Gateway.cs` is 2 919 lines. Both are coherent, but the
window is the reason `TiaSclStudio.App` sits at 22% coverage: the logic worth
testing is interleaved with WPF plumbing that is not. The `*EditingLogic` classes
show the pattern that works — continuing to pull decisions out of the window into
those would raise both testability and clarity.

**`.editorconfig` asks for CRLF; the working tree is LF.** Every editor that
honours one of them rewrites whole files. A `.gitattributes` is added here to
settle it, but the existing files still need one normalising commit.

---

## Test suite

605 tests, all green, in about 30 seconds.

| Suite | Tests | Scope |
|---|---:|---|
| `TiaSclStudio.Core.Tests` | 217 | identifiers, validation, SCL generation, source writing |
| `TiaSclStudio.Diagram.Tests` | 189 | graph analysis, diagram validation, storage, undo |
| `TiaSclStudio.Openness.Tests` | 103 | gateway contracts, discovery, ownership stamping |
| `TiaSclStudio.Integration.Tests` | 61 | sheet → SCL, whole-project compilation, bundle on disk |
| `TiaSclStudio.EndToEnd.Tests` | 35 | the real WPF window, the self-test executable, a user journey |

Line coverage, union across suites:

| Assembly | Coverage |
|---|---:|
| `TiaSclStudio.Core` | 97.8% |
| `TiaSclStudio.Diagram` | 95.2% |
| `TiaSclStudio.Openness` | 73.6% |
| `TiaSclStudio.App` | 21.7% |
| `TiaSclStudio.Openness.Legacy.V17` | 9.4% |

The last two are honest rather than disappointing. `App` is a WPF window; the
end-to-end suite covers its startup, palette commands, undo/redo and rendering,
and the rest is layout. `Openness.Legacy.V17` compiles against the Siemens API
and cannot be exercised without a machine that has TIA Portal installed — only
`SclSourceInspector`, which is pure text manipulation, is reachable offline.
Raising that number needs a VM in CI, not more tests.

### What the tests actually try to catch

The instruction was to find bugs, not to turn a check green, so the suites are
built around ways this product can be wrong:

* **Deformed models.** [`Deformations.cs`](../tests/TiaSclStudio.TestSupport/Deformations.cs)
  reproduces states a crashed session or a hand-edited file really produces: a
  duplicate id from an aborted rename, a wire that outlived its node, a pin list
  pasted without re-keying, a collection that came back null. The rule every
  suite enforces is that a deformed model produces a *diagnostic* — never an
  exception, and never silently wrong SCL.
* **Text that escapes its construct.** Newlines and control characters in every
  value that reaches one line of generated SCL.
* **Hostile file names.** Directory separators, `..`, absolute paths, UNC paths,
  invalid characters, trailing dots, Windows device names.
* **Scale.** 50 000-node chains and cycles, which is what found defect 1.
* **Locale.** Reserved-word lookup under `tr-TR`, where `I` lower-cases to a
  dotless letter and a culture-sensitive comparison would let `IF` through as an
  identifier on a Turkish machine only.
* **Concurrency.** Four threads saving one project, which is what found
  defect 8.
* **Determinism.** The same project compiled twice, saved and reopened, and
  exported twice must produce byte-identical output — otherwise every export
  looks like a change to whoever reviews the TIA project.
* **Atomicity.** A failed save must leave the previous file untouched, and a
  failed compile must write nothing at all.

---

## Production readiness

Added:

* **CI** ([ci.yml](../.github/workflows/ci.yml)) — build, all five suites with
  coverage, the offline self-test executable, the WPF smoke script, and an
  explicitly named `offline-stub` x64 artifact. GitHub-hosted runners do not
  contain Siemens TIA Portal and cannot produce a deployable Online adapter.
* **CodeQL** ([codeql.yml](../.github/workflows/codeql.yml)) — `security-extended`
  and `security-and-quality` on every push, pull request and weekly. No query
  filters: nothing has been triaged yet, and suppressing a rule before seeing
  what it reports would hide the findings it was set up to find.
* **SonarQube, self-hosted** — [`build/Invoke-SonarQube.ps1`](../build/Invoke-SonarQube.ps1)
  runs `begin` → full rebuild → tests with coverage → `end` against a local
  server, and [`build/sonarqube-compose.yml`](../build/sonarqube-compose.yml)
  starts one. The scanner downloads itself on first use.
* **Coverage** — AltCover in OpenCover format, one report per suite.
* **Warnings as errors** for the product assemblies via `Directory.Build.props`,
  plus deterministic builds. The codebase already compiles clean under it.
* Dependabot, `SECURITY.md`, `CONTRIBUTING.md`, `CHANGELOG.md`, `.gitattributes`,
  issue and pull-request templates.
* **Production CD** ([release.yml](../.github/workflows/release.yml)) — a trusted
  self-hosted TIA runner builds and verifies the real Legacy V17 adapter, runs
  the full checks, optionally signs the binaries, publishes a versioned ZIP and
  SHA-256, and rejects any package containing a Siemens runtime DLL.
* Assembly versions unified at `0.2.0` — `Core` had been left at `1.0.0.0` while
  everything else was `0.1.0.0`.

### One environment limitation to remember

**The checkout path contains `#`.** Several tools in this chain — AltCover
demonstrably, and anything else using a comment-aware argument parser — treat it
as the start of a comment and silently truncate the path. The coverage script
works around it by staging into a temporary directory, but moving the checkout
to a path without `#` would remove a whole class of confusing failures.

### Worth doing next

1. Register and harden the dedicated self-hosted TIA runner described in
   [CI-CD.md](CI-CD.md). Hosted CI deliberately compiles the offline stub;
   production tags wait for the licensed runner.
2. Structured logging. There is currently no diagnostic trail from a failed
   export other than what the window shows, which is gone once it closes.
3. Configure the Authenticode certificate secrets. The CD workflow already
   creates a versioned ZIP and checksum, but publishes it unsigned until those
   secrets are present.

---

# Second pass, at `76555e4`

The codebase had grown from ~28 000 to ~49 000 lines: a UDT library with
dependency ordering, declaration-only SCL import, a strict PLC type catalogue,
direct I/Q/M addressing, hardware-channel discovery, auto-layout and a
read-only TIA project inspector. The test suites grew with it, from 605 to
1 128 tests, and the new modules arrived with their own suites rather than
getting them afterwards. That is the right habit and it shows.

This pass found one defect of the kind that reaches a plant, three robustness
gaps, and got SonarQube running against a local server.

## Defect: overlapping tag addresses were never checked

Every tag address was validated on its own — the operand parses, the data type
fits the area, the offset is inside the CPU range. Nothing compared tags against
each other.

Two tags can each be perfectly valid and still be the same memory:

| Tag | Occupies |
|---|---|
| `Status_Word : Int @ %MW10` | bytes 10-11 |
| `Status_Bit : Bool @ %M10.0` | bit 0 of byte 10 |

Writing the word silently changes the bit. The same holds for `%MW10` against
`%MW11`, for `%MD20` against `%MW22`, and — less obviously — for `%IW0` against
`%I1.3`, because the digital and analog input operands address one process
image.

Nothing downstream can catch this. The SCL is well formed, TIA imports it, the
PLC compiles it, and the plant then misbehaves in a way that looks like a wiring
fault. The editor was the only place it could have been caught, and it said
nothing.

**Fixed.** `PlcAddressSpec` now exposes the address space and the absolute bit
range an operand occupies, and `ProjectValidator` sweeps for intersections
(`TAG_ADDRESS_OVERLAP`).

It is a **warning, not an error**, and that is deliberate: overlaying a status
word with individually named bits is idiomatic Siemens practice. Refusing to
generate would make the tool unusable for a common, correct layout. The point is
that the engineer is told, not that they are stopped.

Nineteen tests cover it, including the layouts that must stay silent — two bits
in one byte, adjacent words, the same offset in different areas — and a
2 000-tag table packed densely but correctly.

## Gap: regular expressions had no timeout

Seventeen patterns run over content this process did not write: imported `.scl`
sources, `.tiasclproj` files that arrive by e-mail, type names read back from a
TIA project. None had a match timeout, so input chosen to make one backtrack
freezes the editor with no way out — and, in the export path, does so
mid-transaction against a live project.

**Fixed** with a shared two-second bound (`SclRegex.MatchTimeout`, and
`SclRegexTimeouts.MatchTimeout` in the adapter, which cannot reference Core).
That is far longer than any legitimate match here needs, so a slow machine never
trips it.

## Gap: no global exception handler

There was no `DispatcherUnhandledException` handler anywhere in the application.
Any exception escaping a UI event handler ended the process through Windows
Error Reporting: the editor vanished, the open diagram went with it, and the
engineer had nothing to report but "it closed".

**Fixed** in `App.OnStartup`. Failures are appended to
`%LOCALAPPDATA%\TiaSclStudio\crash.log`, shown to the user, and — for dispatcher
exceptions, where the UI thread is still usable — handled so the window survives
and the work can be saved. Background and unobserved-task failures are logged; a
terminating one cannot be recovered from, so a record is all that is left.

The dialog deliberately says to save to a **new** file. Writing over the
known-good project from a state that has just thrown is how a crash becomes data
loss.

## Gap: `Safe<T>` was not constrained

`Safe<T>(IEnumerable<T>)` filters nulls, but `T` was unconstrained, so for a
value-type sequence the filter is silently a no-op. Every current caller passes
reference types, so nothing was broken — but the guarantee the helper appears to
offer was not one the compiler was enforcing. Now `where T : class`.

## SonarQube

Running against a local server started by `build\sonarqube-compose.yml`,
analysing the whole solution with coverage from all five suites.

Three things had to be fixed before it produced meaningful output.

**`sonar-project.properties` made the scanner refuse to run.** That file belongs
to the generic CLI scanner; the SonarScanner for .NET discovers sources through
MSBuild and aborts when it finds one. It had been in the repository since the
first pass and had never been exercised. Removed — every setting is now a `/d:`
argument in `Invoke-SonarQube.ps1`.

**The self-test was analysed as production code.** 5 900 lines of assertions
counted in every metric, and its exact float comparisons, which are correct in a
test, were reported as product defects. Marking it `SonarQubeTestProject`
dropped NCLOC from 43 726 to 38 403 and removed 28 false bugs.

**Coverage was not reproducible.** `TiaSclStudio.App` swung between 21% and 53%
across identical runs. The end-to-end tests launch `TiaSclStudio.SelfTest.exe`,
which loads the same instrumented assemblies and writes to the same AltCover
visit file as its own test host; whichever process flushed last won. Those tests
now run in a separate uninstrumented pass, and two consecutive runs are
byte-identical.

### Result

| Metric | Before | After |
|---|---:|---:|
| Bugs | 43 | **0** |
| Vulnerabilities | 17 | **0** |
| Security hotspots | 0 | **0** |
| Reliability rating | C | **A** |
| Security rating | B | **A** |
| Maintainability rating | A | A |
| Code smells | 374 | 349 |
| Duplication | 1.0% | 1.2% |
| NCLOC | 43 726 | 38 403 |

Of the original 60 bugs and vulnerabilities, 18 were real (the regex timeouts
and the unconstrained generics), 28 were the self-test being misclassified, and
14 were correct-by-intent code now suppressed **at the site, with the reason
written next to it**, rather than by a project-wide rule exclusion:

* `HasChanges` in `SheetAutoLayoutLogic`, and the resize test in
  `GroupEditingLogic`, compare doubles exactly on purpose. They decide whether
  anything actually moved, and therefore whether an undo entry is recorded. A
  tolerance would swallow a genuine sub-pixel nudge and leave the canvas and the
  undo stack disagreeing about what happened.
* `EditNodeProperties` is `async void` because WPF can only invoke a
  double-click continuation that way, and its single awaited call reports every
  failure through its own catch.

### What the remaining 349 smells say

Not noise, but not urgent either. The largest group by far is **96 methods over
the cognitive-complexity threshold**, concentrated in the window partials, the
SCL parser and the auto-layout. That is the same signal the file sizes give:
`LegacyV17Gateway.cs` is now 7 750 lines and `MainWindow.xaml.cs` 2 396. The
`*EditingLogic` classes show the pattern that works — pulling decisions out of
the window into testable classes — and `TiaSclStudio.App` reaching 53% coverage
is the direct result of having done that. Continuing it is the highest-value
maintainability work available.

### Coverage

| Assembly | Line coverage |
|---|---:|
| `TiaSclStudio.Core` | **100%** |
| `TiaSclStudio.Diagram` | **100%** |
| `TiaSclStudio.Openness` | 85.6% raw |
| `TiaSclStudio.App` | 60.1% raw |
| `TiaSclStudio.Openness.Legacy.V17` | 16.4% |

The Sonar production scope is **100% line-covered (7062/7062 sequence points)**.
It includes every model-only production class. The lower raw assembly numbers
above include WPF event wiring, process launch code, the installation probe and
the Siemens adapter. Those boundaries remain statically analysed, but require
the Windows/TIA VM for meaningful runtime coverage. The local test script and
the post-analysis Sonar API check both fail below 100%, so the number is enforced
rather than documented aspirationally.

## Still open

* **A CI job on a VM with TIA Portal.** Unchanged from the first pass and still
  the biggest hole: the assembly that can change a customer's plant program is
  the least covered one.
* **`#` in the checkout path.** Now warned about by the analysis script and
  worked around by the coverage script, and still worth removing.
* **Structured logging.** The crash log added here records failures; there is
  still no trace of what a successful export actually did.
* **A stray file named `; done`** sits untracked in the repository root, left by
  a shell command that lost its quoting. Harmless, but delete it.

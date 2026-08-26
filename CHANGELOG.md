# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.1] - 2026-08-26

### Fixed

- The production release workflow now uses Windows PowerShell 5.1, matching the
  documented Windows/TIA runner prerequisites without requiring PowerShell 7.

## [0.3.0] - 2026-08-26

### Fixed

- **Overlapping PLC tag addresses are now reported.** Two tags could each carry
  a valid address and still occupy the same bits: `%MW10` contains `%M10.0`, and
  `%IW0` contains `%I1.3`, so writing one silently changed the other. Nothing
  downstream could catch it — the SCL is well formed and the PLC compiles it.
  Reported as `TAG_ADDRESS_OVERLAP`, a warning rather than an error, because
  overlaying a status word with named bits is standard Siemens practice.
- **Regular expressions that run over untrusted input now have a timeout.**
  Patterns matching imported SCL, project files and TIA type names had no match
  timeout, so input chosen to make one backtrack froze the editor with no way
  out. All seventeen sites now use a two-second bound.
- **An unhandled exception no longer takes the editor down silently.** There was
  no `DispatcherUnhandledException` handler, so anything escaping a UI event
  handler ended the process through Windows Error Reporting and the open diagram
  was lost. Failures are now logged to `%LOCALAPPDATA%\TiaSclStudio\crash.log`,
  reported to the user, and the window stays alive so the work can be saved.
- `Safe<T>` and `WalkGroupTree<TGroup, TItem>` are constrained to reference
  types. Their null filters were silently meaningless for a value-type argument.

- **A long call chain no longer takes the process down.** Cycle detection walked
  the graph recursively, so a sheet with a few thousand chained calls exhausted
  the thread stack. On Windows that is not a catchable exception: the process
  dies with a modal system error and the user loses unsaved work. The walk now
  runs on an explicit stack and handles a 50,000-node chain.
- **A line break in a data type, initial value, result target or binding
  expression can no longer rewrite the generated block.** These values are
  copied verbatim into the middle of a declaration or a call, so a newline ended
  the construct early and turned everything after it into free-standing source
  text. They are now rejected by validation and folded onto one line by the
  generator.
- **A block named after a Windows device no longer loses its source.** A block
  called `CON`, `NUL`, `COM1` and so on produced a file name that opens a device
  instead of a file. Depending on the device the export either threw from inside
  `FileStream` or reported success while writing the SCL nowhere at all, leaving
  the import bundle one source short.
- **A model with a hole in it is reported instead of being passed on.** A null
  entry in a collection was filtered out silently, so validation called the
  model valid and the generator then dereferenced it. Missing collections and
  empty entries are now `MISSING_COLLECTION` and `NULL_MODEL_ITEM` errors.
- **A damaged call sheet is diagnosed rather than crashing the editor.** A sheet
  with a missing `Nodes`, `Wires` or pin collection threw a
  `NullReferenceException` from inside validation, which runs on every edit.
  These are now `DGM060`–`DGM065`.
- **A non-finite sheet size or node position is rejected.** `NaN` makes every
  comparison false, so a corrupt value silently disabled the group bounds check
  instead of failing anywhere visible (`DGM066`–`DGM068`).
- **Concurrent saves no longer litter the project folder.** Two saves racing
  inside `File.Replace` left Windows' own `~RF*.TMP` backup files next to the
  project and failed one of the saves. Saving is now serialised across the
  process, with a short retry for a transient sharing violation from a scanner
  or backup agent.
- **A project that cannot be saved is now diagnosed before the user is stranded
  with it.** Free text carrying a character XML cannot represent made the whole
  project unsaveable, and the failure surfaced as an exception from deep inside
  the serializer (`NON_PERSISTABLE_TEXT`).
- `ProjectStorage` now reports an unsafe generated file name consistently.
  Characters such as `<`, `>`, `"` and `|` escaped as an `ArgumentException`
  from path parsing rather than the intended diagnostic.

### Added

- TIA library readback now imports LAD/FBD FB and FC blocks as interface-only
  declarations. Their pin names, types and sections are available in the local
  library, while executable LAD/FBD logic is never copied or converted to SCL.
- UDT library editor with create/edit/delete/reorder operations, nested UDT references,
  dependency ordering, safe rename propagation and referenced-type deletion protection.
- A shared strict PLC type catalogue for FB/FC interfaces, tags and UDT members. Block and
  UDT editors now use non-editable type selectors with TIA basic types and project UDTs;
  unknown arbitrary type strings are rejected before SCL generation.
- Declaration-only import of FB, FC and UDT interfaces from external `.scl` sources. Import
  has a conflict preview, defaults to preserving existing objects, validates the complete
  future project, applies atomically as one Undo/Redo operation and never copies or executes
  an imported block body.
- PLC-tag address editor with `DI`/`DO`/`AI`/`AO`/`M` categories, compatible
  `Bool`/`Int`/`Word`/`Real` choices, canonical Siemens addresses and offline
  S7-1200/S7-1500 range checks. While connected, hardware I/O is selected only
  from exact channels read from the current TIA hardware configuration and is
  revalidated before the atomic model commit.
- Project format 3 persists the CPU family; formats 1 and 2 remain readable and
  are upgraded only after a successful save.
- Auto-layout v1 for the active call sheet: deterministic source/logic/block/sink
  layers, barycentric crossing reduction, dense wrapping, separate cyclic and
  disconnected zones, nested-group bounds, automatic sheet growth, one-step
  undo/redo and automatic Fit All. Stable IDs, wire endpoints and generated SCL
  are preserved.
- Five test suites, more than 600 tests: unit tests per assembly, integration tests across
  assemblies and the filesystem, and end-to-end tests that drive the real WPF
  window and run the shipped self-test executable as a process.
- `build\Invoke-Tests.ps1` and `build\Invoke-SonarQube.ps1`, plus
  `build\sonarqube-compose.yml` for a local SonarQube server.
- Code coverage through AltCover in OpenCover format, one report per suite.
- GitHub Actions: build and test, the offline self-test, CodeQL
  (`security-extended`) and an optional SonarQube analysis.
- Tag-driven production CD on a trusted self-hosted TIA/Openness runner: strict
  build and tests, optional Authenticode signing, a versioned ZIP, SHA-256 and a
  GitHub Release. Hosted artifacts are explicitly marked `offline-stub`.
- Dependabot for NuGet and GitHub Actions.
- `SECURITY.md`, `CONTRIBUTING.md`, `.gitattributes`, a pull request template
  and this changelog.
- `Directory.Build.props`: deterministic builds and warnings as errors for the
  product assemblies.
- Test-only MSBuild defaults moved to `Directory.Build.targets`, where legacy
  projects have already declared `IsTestProject` and the conditions take effect.
- A working local SonarQube analysis: `build\Invoke-SonarQube.ps1` now runs end
  to end against the server started by `build\sonarqube-compose.yml`.

### Changed

- `sonar-project.properties` removed. The SonarScanner for .NET refuses to run
  when that file is present — it belongs to the generic CLI scanner — so every
  analysis setting moved into `Invoke-SonarQube.ps1` as a `/d:` argument.
- `TiaSclStudio.SelfTest` is marked `SonarQubeTestProject`. Its 5 900 lines of
  assertions were being measured as production code, inflating every metric and
  reporting its deliberate exact float comparisons as product defects.
- Coverage measurement is reproducible. The end-to-end tests that launch
  `TiaSclStudio.SelfTest.exe` shared one AltCover visit file with their own test
  host, so whichever process flushed last won and this assembly's reported
  coverage swung between 21% and 53% across identical runs. Those tests now run
  in a separate uninstrumented pass.

### Changed

- Product assemblies now share one version number (`0.2.0`).
  `TiaSclStudio.Core` had been left at `1.0.0.0` while everything else was
  `0.1.0.0`.

## [0.1.0]

- First visual MVP: block library, interface editor, call-sheet editor,
  validation, SCL generation, and the legacy Openness V17 adapter with a
  mandatory dry run before any change to a TIA project.

# Contributing

## Getting a build

You need Visual Studio 2022 or the Visual Studio 2022 **Build Tools** with the
*MSBuild*, *.NET Framework 4.8 SDK* and *Testing tools core features*
components. You do not need the .NET SDK.

```powershell
.\build\Invoke-Tests.ps1
```

That restores, builds `Release|x64` and runs all five suites. See
[build/README.md](build/README.md) for coverage and SonarQube.

## What the layers are allowed to know

```
TiaSclStudio.App (WPF, x64)
├── TiaSclStudio.Core                    model, validation, SCL generation
├── TiaSclStudio.Diagram ──→ Core        sheets, nodes, wires, graph analysis
├── TiaSclStudio.Openness                version-neutral gateway contracts
└── TiaSclStudio.Openness.Legacy.V17     the real V17–V20 adapter
```

* `Core` and `Diagram` know nothing about WPF or the Siemens API. Keep it that
  way: it is what makes them testable without TIA Portal installed.
* Siemens types never cross the `Openness` public contract.
* Every persisted entity has a stable `Guid`. References use the id, never a
  mutable name. A name is a display fallback for legacy files.

## Writing tests

The suites are split by what they can prove, not by folder:

| Suite | Runs against |
|---|---|
| `*.Tests` per assembly | one class, in isolation |
| `Integration.Tests` | several assemblies together, and the filesystem |
| `EndToEnd.Tests` | the real WPF window and the shipped self-test executable |

A test earns its place by describing a way the product can be wrong, not by
touching a line. Concretely:

* **Name the failure, not the method.** `RefusesAnInitialValueThatWouldBreakOutOfItsDeclaration`
  says what goes wrong. `TestGenerateBlock2` says nothing.
* **Say why in a comment when the reason is not obvious from the assertion.**
  The interesting tests here are the ones where a reader would otherwise ask
  "why would anyone do that?" — a `#` in a path, a Turkish locale, `CON.scl`,
  a 50,000-node chain.
* **Deform a valid model in exactly one way.** `TestSupport/Deformations.cs`
  holds the ones that reproduce real corruption: a duplicate id from an aborted
  rename, a wire that outlived its node, a collection that came back null from
  a half-written file.
* **A deformed model must produce a diagnostic, never an exception and never
  silently wrong SCL.** That is the single most important property in this
  codebase, because wrong SCL compiles and then misbehaves on a real plant.

Do not add a test that only asserts the code does what it currently does. If it
would still pass after the behaviour was broken in a plausible way, it is not
paying for its maintenance.

## Style

`.editorconfig` is authoritative: four spaces, CRLF, UTF-8, a final newline.

The codebase targets `LangVersion 7.3`, so no nullable reference types, no
`switch` expressions, no target-typed `new`. Match the surrounding code rather
than modernising a file you happened to touch.

Warnings are errors in `Release`. If a warning is wrong, suppress it at the
narrowest scope with a comment explaining why, not with a project-wide `NoWarn`.

## Before opening a pull request

1. `.\build\Invoke-Tests.ps1` is green.
2. `.\tools\selftest\bin\Release\TiaSclStudio.SelfTest.exe` exits 0.
3. Any change to generated SCL is reflected in the self-test's expected output,
   and you have said in the description why the output changed.
4. New public behaviour has a test that would fail without it.
5. [`CHANGELOG.md`](CHANGELOG.md) has an entry if the change is visible to a user.

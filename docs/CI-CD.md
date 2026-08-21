# CI/CD

## Hosted CI

`.github/workflows/ci.yml` runs on `windows-2022` for pushes and pull requests to
`main`, `master` and `develop`. It restores and rebuilds the .NET Framework 4.8
x64 solution, runs all five test assemblies with coverage, executes the offline
self-test and the hidden WPF smoke test, and publishes test results.

GitHub-hosted runners do not contain Siemens TIA Portal. Their application
artifact is therefore named `TiaSclStudio-offline-stub-x64`: it deliberately
cannot connect to TIA. It is useful for UI/offline validation only and must not
be promoted as a production TIA Online build.

CodeQL and Dependabot are configured separately. SonarQube runs only when the
repository variable `SONAR_HOST_URL` exists; `SONAR_TOKEN` must then be stored as
a repository secret.

## Production release runner

`.github/workflows/release.yml` builds a deployable V17–V20 adapter only on a
self-hosted runner carrying all four labels:

```text
self-hosted
Windows
X64
tia-openness-v17
```

The runner needs:

1. Windows x64, Visual Studio 2022 Build Tools with MSBuild, WPF and the .NET
   Framework 4.8 Targeting Pack.
2. TIA Portal V17, V18, V19 or V20 with the V17 PublicAPI facade installed.
3. A dedicated non-interactive service account. Do not run untrusted fork pull
   requests on this machine because it contains licensed engineering software.
4. Network access to GitHub Actions and Releases.

The workflow locates `Siemens.Engineering.dll` only as a compile/runtime
dependency, verifies that the real `LegacyV17Gateway` was built, and fails if a
Siemens DLL reaches the package. The resulting ZIP contains only this project's
assemblies, a build manifest and SHA-256 checksum.

Push a semantic version tag to publish:

```powershell
git tag -a v0.2.0 -m "TIA SCL Studio v0.2.0"
git push origin v0.2.0
```

The workflow can also be started manually with `workflow_dispatch` and a tag.

## Optional signing

When both repository secrets below exist, the release job Authenticode-signs
the executable and project DLLs before packaging:

- `WINDOWS_SIGNING_CERT_BASE64` — the Base64-encoded PFX file;
- `WINDOWS_SIGNING_CERT_PASSWORD` — its password.

Without them the release remains reproducible but unsigned and the workflow
adds an explicit notice to its log.

## Branch protection

After the first successful hosted run, protect `main` and require these checks:

- `CI / Build and test`;
- `CI / Offline self-test`;
- `CodeQL / Analyze C#` when CodeQL is available for the repository plan.

Do not make the self-hosted release job a pull-request requirement; it is a tag
deployment gate and should execute only for trusted commits.

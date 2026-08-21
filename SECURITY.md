# Security

## Reporting a vulnerability

Do not open a public issue. Report privately through GitHub's **Security →
Report a vulnerability** form, or by e-mail to the maintainer listed in the
repository profile. Expect an acknowledgement within five working days.

Include the version, the operating system, whether TIA Portal was connected,
and the smallest `.tiasclproj` file that reproduces the problem.

## What this tool can reach

TIA SCL Studio writes SCL into a customer's PLC project. The risk is not data
theft, it is a plant program that differs from what the engineer approved. The
boundaries that exist to prevent that:

* **Offline by default.** `OfflineGateway` is what runs unless the user
  explicitly connects. It never opens a project, reads a source file, starts
  Portal or writes anything, whatever it is asked to do.
* **Two-phase export.** A read-only dry run issues a one-time confirmation
  token. An export without a valid token is rejected. The token is checked
  again under exclusive access before anything is committed.
* **Ownership marker.** Blocks this tool created carry a `FAMILY` header. A
  block without it is never overwritten unless the user allows it for that
  export.
* **No automatic version upgrade.** The `.apNN` extension must match the Portal
  version; the tool refuses rather than letting Portal migrate a project.

## Handling untrusted input

A `.tiasclproj` file arrives by e-mail as often as it is created locally, so it
is treated as untrusted:

* DTD processing is disabled when a project is read, so an external entity
  cannot turn opening a diagram into a file read or an outbound request.
* Generated file names are constrained to a safe leaf name. Directory
  separators, invalid characters, trailing dots and Windows reserved device
  names (`CON`, `NUL`, `COM1` …) are all rejected before anything is opened.
* Values that are copied verbatim into one line of SCL — data types, initial
  values, result targets, binding expressions — are rejected if they contain a
  line break or other control character. Without that check a value can close
  its own declaration and have the rest of the file read as something else.
* A model with a missing collection or an empty entry is reported as invalid
  rather than silently skipped, so it cannot reach the generator.

Binding expressions are raw SCL by design: an engineer can write any expression
that TIA will compile. That is an intentional escape hatch, not an oversight,
and it is why the export path requires an explicit dry run and confirmation.

## Supported versions

Only the latest release receives fixes. TIA Portal V17–V20 are supported
through the legacy Openness API; V21 and newer need the modular adapter that is
not finished yet.

## Automated analysis

Every push and pull request runs CodeQL (`security-extended`) and the test
suites. A local SonarQube analysis is available through
`build\Invoke-SonarQube.ps1`.

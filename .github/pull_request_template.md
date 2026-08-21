## What changed and why

<!-- The behaviour, not the diff. If it fixes a defect, say how it showed up. -->

## Effect on generated SCL

- [ ] No change to generated SCL
- [ ] Generated SCL changes, and the self-test's expected output is updated to match

<!-- If the output changed, paste the before/after of one affected block. This
     is the part a reviewer cannot reconstruct from the code. -->

## Checks

- [ ] `.\build\Invoke-Tests.ps1` is green
- [ ] `TiaSclStudio.SelfTest.exe` exits 0
- [ ] New behaviour has a test that fails without the change
- [ ] `CHANGELOG.md` updated, or the change is invisible to users

## Risk to a live plant project

<!-- Anything touching Openness export, ownership stamping, the confirmation
     token or file writing needs a sentence here. Write "none" otherwise. -->

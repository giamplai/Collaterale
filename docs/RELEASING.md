# Releasing

1. Make sure `main` is green: `scripts/check.sh`.
2. Bump `<Version>` in `Directory.Build.props` (semantic versioning) and describe the changes in `CHANGELOG.md`.
3. Commit: `git commit -am "chore: release 0.2.0"`.
4. Tag and push: `git tag v0.2.0 && git push origin main v0.2.0`.
5. The **Release** workflow (`.github/workflows/release.yml`) checks that the tag matches the version, runs the Core tests, builds every package and attaches them to a GitHub release together with `SHA256SUMS`.

## Building packages locally
```bash
scripts/package.sh                 # everything → artifacts/packages
scripts/package.sh linux-x64       # one target
```
| Target | Output |
|---|---|
| linux-x64, linux-arm64 | `.tar.gz` (with `install.sh`, desktop entry, icon) and `.deb` |
| win-x64 | `.zip` (portable single-file `Collaterale.exe`) and `Collaterale-<version>-win-x64-setup.exe` (Inno Setup, built by the `windows-installer` job of `release.yml`) |
| osx-arm64, osx-x64 | `Collaterale.app` in a `.zip`, **ad-hoc signed** (Apple silicon refuses to run unsigned code); built on a macOS runner by the `macos-app` job of `release.yml` |

Linux packages and the Windows zip are cross-built from Linux; the macOS app (signing) and the Windows setup program (Inno Setup) need their own OS, so `release.yml` builds them on macOS and Windows runners. Running `scripts/package.sh osx-*` on Linux still works, but the app is then unsigned and may not launch on Apple silicon.

### What is verified automatically
* **Linux:** the `.deb`/`.tar.gz` are built and `install.sh` was run into a clean home folder (`--version`, `--select-left`).
* **Windows, locally (`scripts/test-windows-installer.sh`, Docker + Wine, no Windows needed):** compiles the Inno Setup script into the real setup program, installs it silently, runs the installed `Collaterale.exe` (`--version`, `--diff`), checks the four Explorer registry entries and verifies that uninstalling removes the folder and the entries. All 9 checks pass.
* **Windows (CI job `windows-installers`, and again in the release job):** the Inno Setup script compiles; the setup program installs silently, registers the menu and uninstalls cleanly.
* **macOS (CI job `macos-app`, and in the release job):** the app is built, ad-hoc signed, `codesign --verify` passes and the executable runs `--version`.
These jobs run on every push, so a broken installer fails the build before a tag is ever pushed.

## Known gaps
* macOS and Windows builds are **not signed or notarized**: users see Gatekeeper / SmartScreen warnings (see the HowTo, "Installing").
* No MSI/MSIX, dmg or AppImage yet; those need signing infrastructure or platform tooling.
* Regenerate the icon with `python3 scripts/make-icon.py` (needs Pillow).

# Contributing to Collaterale

Thanks for helping! Bug reports, ideas, docs and code are all welcome.

## Reporting bugs and proposing features
Open an [issue](https://github.com/giamplai/Collaterale/issues) using the templates. For bugs include your OS, Collaterale version (`collaterale --version`), steps to reproduce, and if possible a small sample of the files involved. For larger features, open an issue first so we can agree on the approach before you invest time.

## Development setup
1. Install the **.NET 10 SDK**.
2. Fork and clone the repository, then enable the repo hooks: `git config core.hooksPath scripts/hooks`.
3. Run `scripts/check.sh` — it builds (warnings are errors) and runs every test. It must pass before you open a pull request.

## Project layout
* `src/Collaterale.Core` – UI-free engine (diff, merge, folders, text, scripting, settings).
* `src/Collaterale.App` – Avalonia UI (`ViewModels/`, `Views/`, `Controls/`).
* `tests/` – xUnit tests; UI tests render headless and can export screenshots.
* `docs/` – user guide, architecture, feature map. `scripts/` – check, package, docs tools.

## Coding guidelines
* Logic belongs in the Core or in ViewModels; keep code-behind to dialogs and pickers. Use compiled bindings (`x:DataType`).
* Core changes are **test-first**; prefer oracle/property tests where possible.
* Stay cross-platform: no Windows-only APIs in shared code, use `Path.Combine`, never hardcode `\`.
* Keep the app fast: virtualized views, background work for large inputs, cost caps in algorithms.
* Match the surrounding style; the analyzers enforce the rest.

## Documentation
Every change that alters user-visible behavior must update `docs/HOWTO.md` and, for UI changes, the screenshots (`scripts/update-docs.sh`). Add a line to `CHANGELOG.md` under *Unreleased*.

## Commits and pull requests
* Small, focused conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `chore:`.
* One topic per pull request; describe what changed and why, and how you tested it.
* Do not submit code, text, icons or assets copied from other projects unless their license allows it and you state the source. Contributions must be your own work or compatible with the MIT license.

## License of contributions
By submitting a contribution you agree that it is licensed under the project's [MIT License](LICENSE.md), and that the original author credit remains in place.

## Conduct
Be respectful. See the [Code of Conduct](CODE_OF_CONDUCT.md).

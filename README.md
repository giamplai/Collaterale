# Collaterale

**Compare and merge, side by side.** A fast, modern, cross-platform tool for **Linux, macOS and Windows**:
text files, folders, three-way merges, hex, tables and images in one window with tabs.

[![CI](https://github.com/giamplai/Collaterale/actions/workflows/ci.yml/badge.svg)](https://github.com/giamplai/Collaterale/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)

![Text compare](docs/images/text-compare.png)

## Features

* **Text compare** – aligned side-by-side view, word-level highlights, in-place editing, find & replace (regex), moved-block detection, and smart rules: ignore whitespace, line endings, case, comments (per language), timestamps or your own patterns; compare JSON/XML by structure.
* **Folder compare** – two-sided color-coded tree, quick and content tests (size, date, checksum, byte, rules), name/size/date filters with presets, rename detection, archives as folders, copy and **synchronize with a preview**.
* **Three-way merge** – base / left / right / result layout with per-section actions, display filters, automatic merge and conflict review.
* **Hex, table and image compare** – with swipe/overlay modes for images.
* **Sessions, workspaces and scripts** – save and reopen comparisons, automate with a small script language, export/import settings.
* **Command line** – `collaterale left right`, `/qc`, `/automerge`, `@script`, meaningful exit codes; works as a **git difftool / mergetool**.
* **Safe by default** – optional backups before overwriting, confirmations, read-only and lock handling.

More screenshots and the full guide: **[docs/HOWTO.md](docs/HOWTO.md)**.

## Install

Download the package for your system from the [Releases](https://github.com/giamplai/Collaterale/releases) page:

| System | Package |
|---|---|
| Linux (x64, arm64) | `.deb` or `.tar.gz` |
| Windows (x64) | `-setup.exe` installer or portable `.zip` |
| macOS (Apple silicon, Intel) | `.app` in a `.zip` |

Windows and macOS builds are not code-signed yet, so SmartScreen / Gatekeeper may warn on first start (see the guide, "Installing"). Verify downloads with `SHA256SUMS`.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Collaterale.App -- left.txt right.txt   # run
scripts/check.sh                                              # build (warnings are errors) + all tests
scripts/package.sh linux-x64                                  # build release packages → artifacts/packages
```

## Usage

```bash
collaterale                          # home screen
collaterale left.txt right.txt       # compare two files
collaterale folderA folderB          # compare two folders
collaterale mine.txt theirs.txt base.txt   # three-way merge
collaterale /qc a.txt b.txt          # quick compare, exit code only
```

All switches and exit codes are in the [guide](docs/HOWTO.md#1c-command-line-and-git-integration).

## Documentation

[User guide](docs/HOWTO.md) · [Feature map](docs/FEATURES.md) · [Architecture](docs/ARCHITECTURE.md) · [Behavior corpus](docs/BEHAVIOR.md) · [Releasing](docs/RELEASING.md) · [Changelog](CHANGELOG.md)

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md). Report security issues as described in [SECURITY.md](SECURITY.md).

## License and credits

Released under the [MIT License](LICENSE.md): free to use, copy, modify and distribute, provided the copyright notice is kept.
Created by **Giampiero Lai** ([@giamplai](https://github.com/giamplai)). See [NOTICE.md](NOTICE.md) for attribution guidance and third-party components.

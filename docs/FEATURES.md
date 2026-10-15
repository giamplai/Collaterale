# Feature map

Status of each area of the tool. "Done" means implemented, tested and documented.

| Area | Feature | Status | Notes |
|---|---|---|---|
| Engine | Myers diff (minimal) with cost cap | Done | oracle-tested vs brute-force LCS |
| Engine | Patience diff | Done | |
| Engine | Histogram diff | Done | skips re-scanning matched runs; fast on repetitive files |
| Engine | Line normalization options (whitespace, case, ignored patterns) | Done | |
| Engine | Unimportant differences: line endings, whitespace, case, comments by language (13 syntaxes), timestamps, blank lines, custom regex rules | Done | comments not applied to 3-way merge on purpose |
| Engine | Structural compare for JSON and XML (canonical layout, sorted keys/attributes) | Done | HTML not normalized |
| Engine | Row alignment (pair modified lines by similarity) | Done | |
| Engine | Moved-block detection | Done | linear-time hash windows; min 3 lines and 24 characters |
| Engine | Distinctiveness-weighted line pairing + behavior corpus (20 cases, goldens, `--rows`) | Done | see docs/BEHAVIOR.md |
| Engine | Intra-line (word) diff | Done | |
| Text | Encoding/EOL detection and preservation | Done | |
| Text | Side-by-side compare view, gutter copy buttons, overview map, sync scroll | Done | read-only panes |
| Text | Edit in place (live re-compare, caret kept), undo/redo, save | Done | |
| Text | Syntax highlighting (TextMate grammars, light/dark) | Done | |
| Text | Next/previous difference, copy block left/right, undo/redo, save | Done | |
| Text | Collapse unchanged regions (context lines, click to expand) | Done | read-only while collapsed |
| Text | Find and replace panel (regex, case, whole words, sides, wrap, history, highlight), go to line (Ctrl+G) | Done | bookmarks pending |
| Text | Visible whitespace, word wrap, over-under layout, text/hex details of the current line | Done | wrapped panes scroll together only approximately |
| Text | Next/previous individual difference (changed word or line), wrap-around and message preferences | Done | |
| Text | Files changed on disk: ask or auto-reload (never over unsaved edits) | Done | |
| Text | Auto indent, backspace unindents, context lines, column line, crosshatch, dim inactive pane, orphan color | Done | |
| Folder | Folder compare (size/time/content), tree view, filters, expand/collapse | Done | |
| Folder | Filters/masks (include, skip files, skip folders) | Done | |
| Folder | Copy / delete / synchronize (update, mirror, two-way) with preview | Done | |
| Folder | Rename/move detection (same content, different path) and rename-aware mirror | Done | content-identical only |
| Folder | Open a differing file in a text compare tab | Done | |
| Folder | Color scheme (older/same/orphan/newer/filtered), color spot, folder colors, legend, configurable colors | Done | |
| Folder | Sortable and hideable columns, "why it differs" column, message log, free space, Swap, Stop, Peek | Done | |
| Folder | Session settings window (spec, comparison, handling, name filters, other filters, alignment, colors) | Done | defaults can be saved |
| Folder | Quick tests: size, time (tolerance, DST, time zone), name case, attributes, Unix permissions, versions | Done | no owner/group IDs |
| Folder | Content tests: byte, CRC-32, text with rules; quick-then-content with skip/override | Done | |
| Folder | Name filters (include/exclude files and folders) with presets; other filters (size, date, attributes, OS files) with one-sided "teal" rule | Done | |
| Folder | Alignment: by hand, different extensions, Unicode forms; per-side paths | Done | |
| Folder | Archives as files / folders once opened / folders always; symbolic links as entries or followed | Done | |
| Folder | Disable editing (protects children too), automatic refresh, expand-on-load modes | Done | |
| Merge | Three-way merge engine | Done | validated against `git merge-file` |
| Merge | Merge UI (left, base, right, editable result), resolve, save, mergetool exit code | Done | |
| Merge | Side colors (left teal, right magenta, conflict red), display filters, Take left/center/right/both, per-section buttons, Merge to Left/Right/Other | Done | |
| Integration | Command line + git difftool/mergetool | Done | |
| Integration | Full command-line reference: switches with `-x`, `--x`, `/x`; 3 and 4 files as a merge; `/automerge`, `/qc`, `/fv`, scripts, packages, patches, stdin; exit codes | Done | |
| App | Options window: startup, tabs, text editing, next difference, backup, file operations, archive types, commands and shortcuts, open-with, tweaks, log, metrics, filter presets, scripts, shared sessions | Done | no FTP, no tray, no extra line spacing |
| App | Sessions, workspaces (auto-saved at exit), shared sessions file, settings packages (`.cpkg`) | Done | |
| App | Backups before overwrite (suffix, numbered, time-stamped, folder) with pruning | Done | off by default |
| App | Script language (`collaterale @file`): load, filter, criteria, select, copy, delete, sync, report, diff, include | Done | whole script validated first |
| App | Text edit view, patch view, standard input, clipboard compare (history of ten) | Done | no tray icon |
| App | Update check against a JSON feed | Done | off unless an address is set |
| App | Several windows (new sessions and child sessions in a tab or a window) | Done | |
| Integration | Headless unified diff (`--diff`, exit codes, folders) | Done | validated by applying patches with git and a strict reference applier |
| Integration | Shell integration (context menu) | Done | Dolphin, Nautilus scripts, Explorer; macOS via terminal command |
| Other | Hex compare (by position, inspector, copy/undo/save, 512 MB) | Done | alignment of inserted bytes pending |
| Other | Table (CSV/TSV) compare (key/content rows, header columns, tolerance) | Done | read-only |
| Other | Picture compare (pixel diff, regions, overlay/swipe/difference views, tolerance) | Done | |
| Other | Archive browsing and comparison (zip, tar, tar.gz) | Done | 7z/rar not supported |
| Other | Recent sessions, tabs, drag and drop | Done | |
| Other | Automatic view selection by file type (picture / table / hex / text) | Done | CLI, drop, folder double-click, recents |
| Distribution | deb, tar.gz, zip, macOS app + release workflow | Done | unsigned; no MSI/dmg/AppImage |

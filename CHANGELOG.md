# Changelog

## Unreleased
* **Carry selected lines to the other file:** selecting lines shows a filled arrow in the gutter beside them; clicking it copies exactly those lines over what faces them on the other side (one undo step).
* **Isolations survive edits:** typing or copying lines no longer removes every isolation; the ones after the edit move along with the text and only those overlapping the edit are dropped.
* **Calmer colors:** row and gutter tints are lighter, the changed-word highlight is softer, and syntax colors are off on difference lines by default (Options ▸ Editor turns them on); that option now really works, it used to be overridden by the syntax colorer.
* **Clearer, more compact toolbars:** the emoji on buttons (save, comments, dates, find, peek) are replaced by small line icons that look the same on every system and follow the theme; toolbar buttons are smaller, so the text compare toolbar fits in two rows instead of three.
* **Clear a manual alignment, line by line:** right-click ▸ **Clear alignment on this line** removes only the alignment (Align with… / Isolate) that holds that line; **Clear all manual alignments** removes them all. Rules also accept regex options such as `(?i)` for rule-by-rule case handling (documented).

## 1.2.0 — 2026-10-22
* **Align with…, whole stretches:** select several lines before choosing it and the whole stretch is aligned opposite the line you click (and the same number of lines below it).

## 1.1.0 — 2026-10-22
* **Text compare, Align with…:** choose it on a line (toolbar, Ctrl+Alt+A or right-click), then click the line of the other pane to put opposite it; Esc cancels.
* **Text details, easier to read:** the two lines now touch, are tinted like the pane rows and have the changed words marked (spaces, tabs and line ends too with **· Whitespace**); still editable with Enter.

## 1.0.0 — 2026-10-22
* **Text compare, select whole lines:** click the line-number margin (the pointer becomes a mirrored arrow) to select a line, drag along it to select several.

## 0.4.0 — 2026-10-22
* **Fix, text details panel:** it now shows the two lines that face each other on the cursor's row, whichever pane the cursor is in (before, each box followed its own pane, so with a long wrapped line the two boxes could show unrelated lines), and the two lines are stacked one above the other so differences line up.
* **Your own file formats:** **Options ▸ File formats** describes a language Collaterale does not know (extensions, line and block comments, string quotes) so that **💬 Comments** and **❝ Strings** work for it; it shows in the language list and is chosen from the file name, and wins over a built-in definition for the same extension.

## 0.3.0 — 2026-10-21
* **Folder compare, stronger checksums:** the content test can now compare **SHA-1** or **SHA-256** hashes (besides CRC-32 and byte by byte), also as `/qc=sha1` and `/qc=sha256` on the command line.
* **Choose which syntax elements matter:** the new **❝ Strings** switch makes what a string literal says unimportant (quotes, code and comments still count); with **💬 Comments** also on, only the code is important. Also in the folder compare's content rules and as `--ignore-strings` on the command line.
* **Merge, faster to work through:** **✓ Accept** marks an automatic change as reviewed (with an "n of m accepted" count), **↶ Undo choice** takes back one section's choice, **Resolve all ▾** settles every open conflict left / right / both / center, accepts every automatic change or undoes every choice, and a **Text details** panel shows the current line of each version and of the result in full (remembered).
* **Remembered choices:** the text compare and the three-way merge reopen the way you left them: whitespace and case decisions (shared by both), algorithm, moves, collapse, ignore switches, whitespace symbols, wrap, over-under, the text/hex details panel, the merge's Show filter and Center pane.
* **Text compare, Isolate:** select lines and press **⇋ Isolate** (Ctrl+Alt+I, or the context menu) to realign them on their own: lines on one side stand alone with padding opposite, lines on both sides are compared only with each other. **✕ Isolations** goes back to the automatic alignment.

## 0.2.0 — 2026-10-21
* **Maintenance:** CI actions and test packages updated (setup-dotnet 6, checkout 7, codeql-action 4, action-gh-release 3, coverlet 10, Test.Sdk 18, CommunityToolkit.Mvvm 8.4.2); flaky tests fixed. xunit.v3 4.x is deferred until Avalonia.Headless.XUnit supports it.
* **Fix, text compare:** the overview map (right-edge scroll bar) did nothing and the two panes could drift apart; dragging or clicking the map and every jump now scroll both panes together; fast scrolling no longer makes the panes fight and drift apart.
* **Options window and workspaces:** startup, tabs, text editing, next difference, backups, file-operation confirmations, archive types, command shortcuts, open-with, tweaks, log, metrics, name-filter presets, scripts and shared sessions; named sessions and workspaces (also auto-saved at exit); settings packages; a script language (`collaterale @file`); text edit, patch and clipboard views; checks for files changed on disk; an optional update feed; sessions and child sessions in tabs or windows.
* **Text compare:** a find panel (regular expressions, case, whole words, sides, wrap, history), replace and replace all, match highlighting; visible whitespace, word wrap, over-under layout, and text/hex details of the current line.
* **Three-way merge, redesigned:** left changes teal, right changes magenta, conflicts red; display filters; *Take center*; ◀ ▶ buttons beside each section of the result; *Merge to* Left / Right / Other; file details in the headers and a richer status bar.
* **Command line:** the full set of switches (`/automerge`, `/force`, `/qc`, `/fv`, `/ro`, `/title1`…, `/vcs1`…, `/savetarget`, `/filters`, `/expandall`, `/sync`, `/edit`), three or four files as a merge, and the exit codes 0, 1, 2, 11–14, 100, 101, 105–107; switches may be written `-x`, `--x` or `/x`.
* **Look:** a softer theme (calmer palette, rounder corners); the folder compare shows the two folders in two panes side by side, with the state between them and an empty slot for a missing side.
* **Smart rules:** ignore comments (13 language families, string- and multi-line-aware), dates and times, blank lines, and custom regular expressions; line endings never count. Unimportant lines are shown in faint grey and take no part in matching.
* **Structure compare** for JSON and XML (canonical layout, sorted keys/attributes) in a new tab.
* **Folder compare, much extended:** a color scheme with spots, legend and configurable colors; sortable/hideable columns, "why it differs", message log, free space, Swap/Stop/Peek; a session settings window; quick tests (time tolerance, daylight saving, time zone, name case, attributes, permissions, versions) and content tests (byte, CRC-32, text with rules); include/exclude folder and file masks with presets and size/date/attribute filters; alignment by hand, by extension and by Unicode form; archives as folders; symbolic-link handling; Disable editing; automatic refresh.
* **Folder compare:** rename/move detection with rename-aware mirror sync; zip/tar/tar.gz archives compare as folders.
* **Integration:** Dolphin, Nautilus and Windows Explorer context-menu entries (`--select-left` / `--compare-selected`); Windows setup program; macOS app is ad-hoc signed in CI.
* **Performance:** lines unique to one side are removed before the exact diff (a fully changed 200k-line file: 5 s → 74 ms); performance regression tests.
* **CLI:** `--ignore-comments`, `--lang`, `--ignore-timestamps`, `--ignore-blank-lines`, `--structure`.

## 0.1.0 — first release
* **Text compare:** aligned side-by-side view with filler rows, word-level highlights, gutter copy buttons, overview map, undo/redo, whitespace/case/algorithm options, encoding- and line-ending-preserving save.
* **Folder compare:** tree view with state filters and counts, size/date/content criteria, file masks, copy and delete with confirmation, and synchronization (update, mirror, two-way) with a full preview.
* **Three-way merge:** aligned left/base/right panes, editable result, per-change resolution, safe saving, and the `git mergetool` exit-code contract.
* **Workspace:** tabs, home screen, recent sessions, drag and drop, light/dark themes, command line (`collaterale left right`, `--merge`, `--help`, `--version`).
* **Engine:** Myers (minimal) and patience diff with a cost cap, similarity-based line pairing, three-way merge validated against `git merge-file`.
* **Packages:** Linux (.deb, .tar.gz), Windows (.zip), macOS (.app zip).

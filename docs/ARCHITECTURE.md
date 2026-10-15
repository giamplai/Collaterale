# Architecture

```
Collaterale.App (Avalonia, MVVM)  ──►  Collaterale.Core (no UI)
 Home · Text · Folder · Merge       Diff · Merge · Folders · Text I/O
```
* **Everything is built on `SequenceDiff`**: lines (or tokens) are mapped to integers, then diffed with Myers' linear-space O(ND) algorithm (exact, minimal) with a cost cap so unrelated huge files stay fast, or a patience variant that anchors on unique lines.
* **Line pipeline**: decode → split lines → normalize per options (whitespace, case, ignored patterns) → ids → blocks → aligned rows (pairing changed lines by similarity) → intra-line token diff for highlights.
* **Merge** uses two diffs against the common ancestor and combines them (diff3 semantics); overlapping, differing changes are conflicts.
* **UI**: AvaloniaEdit panes with custom background renderers for diff colors, a connector control between panes, and synchronized scrolling.
* Platform-specific features (shell integration, registry compare) sit behind interfaces.


## Performance notes

* **Reduction before the exact search.** For inputs of 10,000 lines or more, `SequenceDiff` first removes every line that does not occur on the other side (it can never be part of a common subsequence), diffs what is left and maps the result back. A file that was re-formatted or regenerated, where almost every line changed, used to be the slowest case (the exact search walked up to its cost cap over the whole file); it now takes milliseconds. The result is validated to be as short as the plain search by a randomized test.
* **Normalized keys are cached per raw line**, so a line that appears on both sides (the usual case) is normalized once; the timestamp patterns are skipped for lines without a digit.
* `PerformanceTests` keeps order-of-magnitude bounds for 200k-line inputs (scattered edits with each algorithm, every line changed, all smart rules on, three-way merge, repetitive lines, JSON normalization).

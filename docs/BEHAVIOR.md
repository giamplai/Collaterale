# Behavior corpus

Comparison quality is mostly about *judgment*: which lines should face each other, what counts as one change, when a block was
moved. To make that judgment visible and protected, Collaterale ships a corpus of tricky cases, each with a note describing what a
human would expect, and a reviewed expected alignment.

* Cases live in `tests/corpus/cases/<NN-name>/`: `left.txt`, `right.txt`, `why.txt` (the human expectation), `expected.rows` (the
  reviewed alignment) and optionally `args` (extra options such as `-w`).
* `CorpusTests` compares the engine's current alignment with every `expected.rows`, checks that every line of both files appears
  exactly once and in order, and asserts the structural expectations written in the `why.txt` notes.
* An intentional change to the engine: review the new alignment (`scripts/run-corpus.sh`), then refresh the goldens with
  `COLLATERALE_UPDATE_CORPUS=1 dotnet test tests/Collaterale.Core.Tests --filter CorpusTests`. The diff of the `expected.rows` files is the
  review.

## The rows format

`collaterale --diff --rows left right [options]` prints the side-by-side alignment as text, one row per line:

```
=     1    1 | header
+     ·    2 | // new comment
*     2    3 | int total = price * 2;
             | int total = price * 3;
=     3    4 | footer
```

| Symbol | Meaning |
|---|---|
| `=` | Same on both sides. |
| `~` | Same under the ignore options (for example only whitespace differs). |
| `*` | A line on both sides whose content differs; the right-hand version is printed on the next line. |
| `-` / `+` | A line only on the left / only on the right. |
| `m-` / `m+` | Like `-` / `+`, but part of a block that was **moved** elsewhere in the file. |

The two numbers are the line numbers on the left and right (`·` when that side has no line).

## Comparing with another tool

The rows format is deliberately simple so the same inputs can be run through any other comparison tool and the *alignment
decisions* compared by eye or with a script. Run `scripts/run-corpus.sh`: it writes every case (inputs, notes and Collaterale's
`collaterale.rows`) to `artifacts/corpus/`. Open the same `left.txt` / `right.txt` in the other tool and note where it disagrees with
the `why.txt` expectation and with Collaterale. Where another tool's result is better than the reviewed one, add the case (or
correct the golden file) so the improvement is locked in.

## Cases

| Case | What it probes |
|---|---|
| 01 single-edit | The baseline: one changed line. |
| 02 insert-above-edit | An inserted line next to an edited one must not steal its partner. |
| 03 delete-near-edit | A deleted line next to an edited one. |
| 04 repeated-braces | A whole function inserted among look-alike code with repeated braces and blank lines: one contiguous insertion. |
| 05 moved-function | A block that changes position. |
| 06 whitespace-only (`-w`) | Indentation changes are not differences under the ignore options. |
| 07 case-only (`-i`) | Case changes are not differences under the ignore options. |
| 08 final-newline | A missing final newline never makes lines differ. |
| 09 crlf-vs-lf | Line-ending style never makes lines differ. |
| 10 blank-lines | Blank lines added, removed and shifted. |
| 11 swap-adjacent-lines | Two neighbouring lines swapped. |
| 12 block-replace | Rewritten lines that all start with the same prefix ("step N:"): pairs must follow the *distinctive* words. |
| 13 duplicate-lines | One more copy of a repeated line. |
| 14 unicode | Accents, CJK and an emoji. |
| 15 tabs-vs-spaces (`-b`) | A tab replaced by spaces. |
| 16 json-reformat | A one-line document pretty-printed. |
| 17 comment-edit | Only a trailing comment changes. |
| 18 version-bumps | The same number edited on several lines. |
| 19 appended-log | Lines appended at the end. |
| 20 prepended-header | A header prepended at the top. |

## Known limitations found by the corpus

* **05 moved-function:** the body of the moved method is flagged as moved, but its closing brace and the blank line after it end up
  as a separate two-line delete and insert, because runs shorter than three lines are not treated as moves. The result is correct
  but not as tidy as a human would draw it.
* **16 json-reformat:** when nothing resembles anything, the first old line is paired with the first new line (as a changed row)
  rather than shown purely as "removed" plus "added". Both readings are valid; the pairing keeps the row counts balanced.

## History of improvements driven by the corpus

* Case 12 originally paired `parse the body` with `validate the checksum` because every line starts with "step N:" and so looks
  similar to every other. Similarity is now **weighted by how distinctive each word is within the changed block**, so the shared
  prefix counts for little and shared content words decide.

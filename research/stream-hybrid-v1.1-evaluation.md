# Stream Hybrid v1.1: evidence-based evaluation and boundary repair

Date: 2026-10-09. Parent: `experiment/stream-hybrid-v1`.
Branch: `experiment/stream-hybrid-v1.1`.
This is a **research prototype** and has not been validated as a Windows-wide TSF IME.

## Real Windows evidence motivating the change

The user provided one local research export running the original v1 engine,
`algorithmVersion=iblab-stream-hybrid-v1`.
Only short, anonymized fragments are listed here; the complete raw typing
sample and large JSON history are **not** committed to the repository.

Observed properties:
- 128 keypress frames for a 128-character input;
- one cumulative count of **7,570** expanded path transitions;
- `mozcAvailable=false` in all frames and **0** Mozc probes;
- 46 raw characters were irreversibly committed at an explicit space;
- exported UTF-8 research JSON was approximately **5.3 MB**;
- the output retained known English `branch` and adjacent Japanese successfully;
- incorrect `toomoimasu -> とをも今す` and `meltypega -> meltyぺが`;
- the user's sentence also exposed `meltypenimo -> めltyぺにも` after Japanese context.

These are observations, **not accuracy percentages**. The user's intended
gold transcriptions for an independent held-out corpus have not been
verified. A better-looking local example does not establish general accuracy.

## Root causes and changes

### 1. Erroneous morpheme fragmentation, not a Mozc conversion issue

The old score could prefer `to | o | mo | ima | su` with multiple short
dictionary matches, even though the continuous phonetic hypothesis
`toomoimasu -> とおもいます` already existed.

**Fix:** treat the one-letter `o` particle as weak when immediately followed
by another ASCII romanization letter. The conventional two-letter `wo`
particle is unchanged. This is a **contextual edge-score adjustment**, not
adding `omoimasu` or other user phrases to the dictionary.

Potential downside: users deliberately spelling を as isolated `o`
without an explicit separator may see a different interpretation.
It remains a research hypothesis rather than a universal grammatical rule.

### 2. English terminal kana swallowed by Japanese

The original unknown-Latin detector found the unpronounceable middle
`lty` but stopped before the kana-readable terminal `pe` of `meltype`.
In mixed text, it could also let Japanese phonetic reading absorb the
`me` onset. This is a **two-sided boundary-generation defect**.

**Fixes:**
1. Extend a pending-consonant English island rightward by at most one
   kana-readable syllable when the following character position is an
   explicit boundary or a plausible Japanese particle start;
2. keep both the English span with its readable prefix and an alternative
   without the prefix;
3. create a **Japanese phonetic cut just before** a kana-readable prefix
   followed by two unreadable romanization consonants. This makes
   `...ni | meltype | ni...` reachable without forcing English;
4. preserve Japanese-phonetic and literal alternative hypotheses,
   and continue to use the bounded beam rather than new unbounded
   fallbacks.

This still does **not** recognize arbitrary unknown English tokens reliably.
The suffix decision relies on a heuristic particle list. It should be tested
against false positives, typos and non-English words.

## Automated regression observations

GitHub Actions `stream-hybrid` job executes **offline**, with the
unmodified public research source and new minimal user-derived snippets.

| Minimal input | v1 observed problem | v1.1 observed result |
| --- | --- | --- |
| `toomoimasu` | `とをも今す` within actual sequence | `とおもいます` |
| `saiyousitemoiitoomoimasu ` | incorrect internal o/ima split | `さいようしてもいいとおもいます ` |
| `meltype` | not isolated in this comparison | `meltype` |
| `meltypega` | `meltyぺが` | `meltypeが` |
| `meltypenimo` | `めltyぺにも` within context | `meltypeにも` |
| `konobranchwomotonimeltypenimo` | `めltyぺにも` within context | `このbranchをもとにmeltypeにも` |
| `meltypegakitinntohannnousite` | `meltyぺが...` | `meltypeがきちんとはんのうして` |

The existing examples `oreha`, `de-ta`, `de-taganaidesune`,
`githubdeissue`, `kyouhacommitsimasita`, `node.js`, and the
`english` mixed sentence remain covered by the same regression job.
No changes were made to the Mozc service or native TSF DLL.

### Performance interpretation

The prior user trace reports 7,570 transitions for 128 characters
**on the original v1**, but an identical 128-character Windows retest
with v1.1 has not yet been collected.

The existing 57-character synthetic/real-inspired comparison in CI
measures legacy v0.8 vs **v1.1**: earlier v0.8 generated 21,054
transitions, while v1.1 records around 481. This is a **work proxy**,
not end-user input latency. Single-shot GitHub runner stopwatch timings
are noisy; report p50/p95 on repeat actual Windows keypresses before
declaring meaningful performance improvements.

The v1.1 amendment adds only bounded lookahead around unknown islands
and optional single-kana extension edges. Beam width and trace caps
remain unchanged (6 candidate states/offset and 80 diagnostic edges
per frame). Memory and accuracy on arbitrarily long, unseparated
inputs are still not fully measured.

## Explicit limitations / next research tasks

1. **Mozc was unavailable** in the real user's export, so these changes
   improve ROMAJI INTERPRETATION and language ownership, not final
   katakana/kanji conversion. Real Mozc integration remains an independent task.
2. The four-category phonetic certainty remains a diagnostic analysis
   of competing paths, not yet a calibrated probability fed back into
   decision making.
3. v1.1 still reruns decoding over the uncommitted suffix each new
   keystroke. For long unpunctuated text, consider incremental sparse
   chart maintenance with invalidation windows, profile CPU/allocations.
4. The complete research JSON can still be megabytes in size.
   Consider a compact optional trace mode that stores only changed
   candidates and immutable suffix deltas, while keeping complete
   diagnostic mode available.
5. The current `BOUNDARYLAB_STREAM_MOZC=1` path issues synchronous
   probes and may block the GUI if the bridge stalls. Do not enable
   casually; use an asynchronous generation-cancellable conversion
   worker as the next integration layer.
6. Validate both directions with unseen data, not just these new
   passing examples: protected English vs wrongly preserved Japanese,
   `o`/ `wo` particle variations, camelCase, email and URLs,
   punctuation ambiguity, backspacing and app-specific TSF behavior.

## Windows CMD launch

```bat
git fetch origin
git switch experiment/stream-hybrid-v1.1
git pull --ff-only
set "BOUNDARYLAB_ENGINE=v1"
set "BOUNDARYLAB_STREAM_MOZC="
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

Title: `Stream Hybrid v1.1 (boundary refinement)`.
Exported JSON should identify
`algorithmVersion=iblab-stream-hybrid-v1.1` and
`parameters.engineId=stream-hybrid-v1.1`. Only compare exports
when `mozcAvailable` and test input conditions are equivalent.

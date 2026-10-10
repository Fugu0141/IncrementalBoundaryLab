# Stream Hybrid v1.3 — reversible Latin morphology candidates

Date: 2026-10-09. Parent: `experiment/stream-hybrid-v1.2`.
Status: research candidate / uncalibrated; not a native Windows IME.

## Problem and hypothesis

The v1.2 decoder already recognizes dictionary English, unknown *phonetically
unreadable* islands and structural code (such as `node.js`). But an
out-of-dictionary Latin word can be *entirely readable as Japanese romaji*.
For example:

```text
japaneseno
= ja + pa + ne + se + no  -> じゃぱねせの (phonetic)
= japanese + no          -> japaneseの (Latin island + Japanese)
```

A romaji projector cannot choose intent from this evidence alone. v1.3
tests whether **weak English morphological word-shape evidence** can make
both candidate interpretations accessible to the same decoder.

## Narrow v1.3 change

1. Generate provisional Latin edges when an ASCII run of 7–24 characters
   ends in a relatively distinctive English suffix (`-ese`, `-tion`,
   `-sion`, `-ment`, `-ness`, `-able`, `-ible`, `-ology`).
2. Require token end, punctuation, or a plausible multi-character Japanese
   particle immediately after the proposed Latin edge.
3. Give these edges a **length-saturating** heuristic score rather than a
   linear-in-length advantage. This prevents an arbitrary longer literal
   `orehajapanese` from automatically swallowing the Japanese `oreha`.
4. Generate dictionary, code, morphology and unknown-Latin candidates
   *before* kana runs. Their proposed starting offsets become **optional**
   Japanese phonetic cut points, surviving ordinary four-cut pruning.
5. Keep Japanese phonetic and punctuation alternatives. Do not declare a
   morphological candidate “confirmed”, or make irreversible commits without
   explicit hard delimiters.

The fixed word endings are *evidence patterns*, not a replacement for a
language model and not a claim to recognize arbitrary English.

## Regression coverage

New exploratory expected-output cases:
- `japanese -> japanese`
- `japaneseno -> japaneseの`
- `orehajapaneseno -> おれはjapaneseの`
- `japanesedesu -> japaneseです`

And Japanese-only negative controls: `nihongodesu`, `oreha`,
`toomoimasu`. Existing code, English, Japanese, punctuation and long
trace cases also remain in the StreamTests suite.

Passing such examples would establish *only these regressions*, not
improvement in general text. Generated hypotheses may over-predict English
on Japanese strings, especially names or uncommon romaji.

## Remaining hard ambiguity

`tokyo` can mean the literal place name `Tokyo` or an in-progress
Japanese romaji reading. `ime` might be an acronym or normal kana.
No deterministic separator can infer the writer's intent from identical
keystrokes with certainty.

The next version should accept explicit intent signals (e.g., user
Latin-lock/temporary mode, case-preserving acronyms, correction history)
while keeping the **default Japanese**, using a local reranker for mixed
ownership and a reversible preview. Original casing must not be discarded
before that evidence is extracted; `InputSyntax.Normalize` currently does.

## Planned staged research

- **Candidate recall:** per-gold boundary whether *any* lattice path can
  represent the correct Japanese/Literal segmentation. Record missing
  candidates separately from wrong ranking.
- **Ownership ranking:** score candidates using calibrated mora grammar,
  English letter language model, morphology, local Japanese particle
  transitions, code grammar, and personal term history. Do not treat the
  same source repeated across beam paths as independent votes.
- **Intent/commit:** retain undecidable alternatives, permit explicit
  Latin mode, and commit only with sufficiently stable evidence or explicit
  separators. Measure reversals, corrections and time-to-stable-output.
- **Performance:** cap added morphology candidates/edges, profile CPU/
  allocations and real keypress p50/p95/p99 rather than equating transition
  count with latency.
- **Validation:** freeze a separate human-labeled holdout covering
  proper nouns, camelCase, all-lowercase unknown English, romaji-only
  Japanese, suffix collisions, emails/URLs, and editing/undo.

No corpus-level accuracy, boundary F1, latency or TSF compatibility
conclusion is made by this change.

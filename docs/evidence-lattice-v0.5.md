# Evidence Lattice + Delayed Commit v0.5

v0.5 replaces the reactive **choose → freeze → detect contradiction → thaw**
loop as the primary decoder.

The old phonetic-first/RCR implementation remains in the repository as a comparison
baseline.

## Core idea

The active input is represented as a sparse lattice of competing edges.

Candidate edge sources:

- Japanese phonetic spans,
- Japanese lexical spans,
- English lexical spans,
- structural Latin spans around punctuation such as `node.js`, `foo_bar`, `C#`,
- symbols,
- unknown fallback.

The decoder keeps multiple complete paths through the active window. No individual
segment is considered final merely because its local confidence is high.

## Why

A locally plausible analysis can be globally wrong.

For example `he` is a valid Japanese particle, but in `hennkan...` committing it as
`へ` too early prevents the recognizer from later treating the larger span as one
Japanese phonetic sequence.

Likewise a period is strong evidence for a Latin identifier, but greedily extending a
single Latin token to the whole letter run can swallow preceding Japanese romaji.

The lattice keeps both interpretations alive until more context is available.

## Scoring

Each edge receives independent evidence:

- phonetic convertibility,
- optional exact lexicon evidence,
- a small character n-gram language profile,
- orthographic structure.

Path scoring adds lightweight transition constraints:

- Japanese particles are strongly discouraged at beginning of input,
- Japanese/English switching has a small cost,
- English followed by a Japanese particle is explicitly plausible,
- hard boundaries reset transition pressure.

The n-gram profiles are seeded from the existing transparent lexicon plus a small set of
general examples. Exact dictionary hits are evidence, not the only way to produce a
candidate.

## Delayed commit

The decoder keeps the best N paths. A normal commit occurs only when competing paths
within a score window share the same leading edge for multiple consecutive keystrokes
and enough lookahead exists.

A hard boundary such as a space or sentence punctuation can commit the completed region
immediately.

This is closer to a streaming recognizer: unstable partial hypotheses may change, while
only a stable prefix becomes irreversible.

## Research data

The v0.5 JSON exports:

- all lattice edges,
- Japanese/English profile scores,
- edge evidence and local score,
- top path hypotheses,
- path score margin,
- current common prefix,
- commit stability count,
- commit events,
- phonetic preview,
- expanded-edge workload per keystroke.

This makes candidate-generation failures distinguishable from scoring failures and
commit-policy failures.

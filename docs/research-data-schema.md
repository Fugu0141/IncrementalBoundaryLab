# Research JSON v2

The WinForms app exports one UTF-8 JSON file containing the complete observation trace.

Top-level fields:

- `formatVersion` — `incremental-boundary-lab/research-v2`.
- `algorithmVersion` — recognizer build/algorithm identifier.
- `generatedAtUtc` — export timestamp.
- `input` — normalized ASCII-letter input.
- `converted` — best final conversion.
- `parameters` — beam width, thresholds, vote requirements and stability window.
- `finalSegments` — final ensemble-selected segmentation.
- `finalBoundaries` — all possible cut positions with independent evidence.
- `frames` — one frame for every typed character.

## Boundary evidence

Every boundary contains:

- `probability` — combined consensus.
- `beamProbability` — left-to-right beam posterior.
- `bidirectionalProbability` — independent structural forward/backward support.
- `lexicalProbability` — local dictionary/romaji-alias support.
- `stabilityProbability` — persistence across previous frames, or -1 when there is not
  yet enough history.
- `independentSupport` — number of views that independently exceeded the vote threshold.
- `confirmed` — true only when the consensus and vote-count rules both pass.

## Segment interpretation evidence

Every selected segment contains:

- boundary consensus,
- beam interpretation confidence,
- lexical interpretation confidence,
- temporal interpretation confidence,
- combined interpretation confidence,
- independent interpretation support count,
- the four-state certainty class.

## Hypotheses

Top hypotheses now preserve both raw beam information and ensemble reranking data:

- raw score,
- beam probability,
- ensemble score,
- boundary agreement,
- lexical agreement,
- segmentation and converted text,
- per-segment evidence and lexical score.

The file therefore contains enough information to compare the old single-system
confidence with the new multi-view consensus and to investigate where the systems
disagree.

# Incremental recognition experiment

## v0.2: multi-view consensus

The first prototype kept many beam hypotheses, but they were still produced by one
scoring system. That can create false confidence: thirty-two similar hypotheses are not
thirty-two independent opinions.

v0.2 therefore separates **candidate competition** from **confirmation**.

For every possible boundary the recognizer now collects up to four different signals:

1. **Beam posterior** — probability mass of left-to-right beam hypotheses that contain
   the boundary.
2. **Bidirectional structural support** — a separate forward/backward dynamic program
   measures how competitive a complete segmentation through that position is without
   using the beam transition score.
3. **Local lexical support** — independently checks whether strong dictionary words or
   known romaji variants end and begin around the position.
4. **Temporal stability** — after enough characters have arrived, checks whether the
   same boundary continued to receive support in previous input frames.

A boundary is not confirmed merely because the beam gives it 99%. At least
`MinimumIndependentSupport` views must independently exceed the vote threshold and the
combined consensus must exceed the boundary threshold.

## Interpretation confirmation

Language/conversion interpretation is checked separately using:

- beam probability mass for the exact span + language + converted text,
- lexical/evidence quality,
- temporal persistence of the same interpretation.

This keeps the original four-state model:

| Boundary | Interpretation | Meaning |
| --- | --- | --- |
| clear | clear | multi-view confirmation succeeded |
| clear | ambiguous | split is supported, reading/language is not |
| ambiguous | clear | interpretation is supported, endpoint is not |
| ambiguous | ambiguous | keep observing input |

## Ensemble reranking

The displayed segmentation is no longer selected from raw beam score alone. Surviving
hypotheses are reranked using:

- beam probability,
- consensus strength at their internal boundaries,
- lexical/evidence quality,
- a penalty for unknown-character segments.

The raw beam probability is still preserved in the research export so the reranking is
auditable.

## Romaji spelling variants

The research lexicon now treats common Japanese keyboard spellings such as
`ashita/asita`, `watashi/watasi`, and `sushi/susi` as alternative evidence for the
same Japanese interpretation. These are explicit aliases rather than silent text
rewrites, so exported evidence shows when an alias affected a decision.

## Why this is useful for research

A final result now exposes *why* it was trusted. For each boundary the JSON contains:

- consensus probability,
- beam probability,
- bidirectional probability,
- lexical probability,
- temporal stability probability (when enough history exists),
- independent vote count.

For each recognized segment it similarly stores the separate interpretation signals.
This makes overconfidence measurable instead of hidden behind one number.

## Current scope

This remains a proof-of-concept. The lexicon is intentionally small and transparent.
The immediate research question is whether independent confirmation produces better
calibration and boundary stability before a production-size dictionary is introduced.

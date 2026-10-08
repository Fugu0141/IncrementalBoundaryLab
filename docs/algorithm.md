# Incremental recognition experiment

## Hypothesis

Instead of deciding the language split from the complete input at once, the recognizer
processes every prefix produced while the user types. At each prefix it keeps multiple
segmentation hypotheses alive.

For example:

```text
k
ky
kyo
kyou
kyouh
kyouha
kyouhac
...
kyouhacommitsimasita
```

The implementation uses a small beam search. Dictionary-backed Japanese and English
segments compete with low-confidence romaji and unknown fallbacks. The scores are
converted with softmax into hypothesis probabilities.

## Two independent confidence axes

For the best current segmentation, two values are calculated separately:

1. **Boundary confidence** — total probability mass of hypotheses that place a boundary
   at the same character position.
2. **Interpretation confidence** — total probability mass of hypotheses that agree on
   the same span, language and converted text.

This yields the four states used by the UI:

| Boundary | Interpretation | Meaning |
| --- | --- | --- |
| clear | clear | likely safe to confirm |
| clear | ambiguous | split seems known, reading/language is not |
| ambiguous | clear | interpretation seems known, endpoint is not |
| ambiguous | ambiguous | keep observing input |

The final input position is deliberately never treated as a confirmed boundary because
another character may still arrive.

## Why keep full incremental history?

A final correct answer can hide bad real-time behavior. The research export stores every
prefix, not only the last result, so we can measure:

- premature boundaries,
- boundary revisions,
- decision latency,
- hypothesis entropy,
- English/Japanese interpretation stability,
- the exact step at which a segment became confirmable.

## Current scope

This is a proof-of-concept, not a production IME. Its built-in lexicon is intentionally
small and easy to inspect. The experiment should first establish whether the incremental
boundary model is useful. A larger dictionary can then be plugged in without changing
the confidence model.

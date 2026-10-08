# Research JSON

The WinForms app exports one UTF-8 JSON file containing the complete observation trace.

Top-level fields:

- `formatVersion` — export schema identifier.
- `algorithmVersion` — recognizer build/algorithm identifier.
- `generatedAtUtc` — export timestamp.
- `input` — normalized ASCII-letter input.
- `converted` — best final conversion.
- `parameters` — thresholds, beam width and softmax temperature.
- `finalSegments` — best final segmentation with boundary/interpretation confidence.
- `finalBoundaries` — probability for every possible cut position.
- `frames` — one frame for every typed character.

Each frame stores:

- current prefix,
- best segmentation and conversion,
- best-hypothesis probability,
- entropy in bits,
- segment confidence values and four-state classification,
- every boundary probability,
- the top beam hypotheses including score, probability, evidence and lexical score.

This makes a single export sufficient to reconstruct not just the final result but how
the recognizer changed its mind while the input grew.

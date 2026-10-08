# IncrementalBoundaryLab

A public C# / WinForms research prototype for testing **incremental word-boundary
recognition** in space-less mixed Japanese-romaji + English input.

The project intentionally does not reuse Meltype's existing language segmentation
algorithm. The question here is different:

> If we observe input from left to right, preserve multiple interpretations, and estimate
> boundary confidence separately from language/conversion confidence, can mixed input be
> segmented more reliably?

## Example

Input is restricted to ASCII letters:

```text
kyouhacommitsimasita
```

Expected current best interpretation:

```text
kyou | ha | commit | simasita
今日 | は | commit | しました

今日はcommitしました
```

The UI shows:

- the current inferred segment groups,
- the converted string,
- English / Japanese / unknown classification,
- confirmed vs provisional segments,
- boundary confidence,
- interpretation confidence,
- the four certainty classes,
- probability for every candidate boundary,
- the complete character-by-character recognition timeline,
- top competing hypotheses.

## Run

Windows with .NET 8 SDK:

```powershell
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

Try:

```text
kyouhacommitsimasita
commitsuru
githubdeissue
networkmiru
thennado
```

## Research export

Press **研究データをJSONに書き出す**.

One JSON file contains the final answer **and every intermediate frame**. This includes
all top hypotheses, scores/probabilities, boundary probability at every position,
segment evidence, confidence, entropy and algorithm parameters.

See [docs/research-data-schema.md](docs/research-data-schema.md).

## Algorithm

The prototype uses a beam of competing segmentations for every input prefix. It then
normalizes their scores into probabilities and derives two independent confidence axes:

1. boundary confidence,
2. interpretation confidence.

Those axes produce four states: clear/clear, clear/ambiguous, ambiguous/clear and
ambiguous/ambiguous.

The input-end boundary is never considered confirmed simply because typing stopped at
that instant.

See [docs/algorithm.md](docs/algorithm.md).

## Tests

No third-party test framework is required:

```powershell
dotnet run --project .\tests\BoundaryLab.Core.SelfTests\BoundaryLab.Core.SelfTests.csproj -c Release
```

GitHub Actions builds the core on Linux, runs the self-tests, and separately builds the
WinForms prototype on Windows.

## Status

Research prototype. The built-in lexicon is intentionally small so that behavior remains
auditable while the boundary model is evaluated.

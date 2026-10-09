# IncrementalBoundaryLab

A public C# / WinForms research project for mixed Japanese-romaji + English input.

## Current experiment: phonetic-first v0.3

The current UI uses a new two-stage architecture:

1. **Stage 1: phonetic projection** — first try to read the active ASCII input as
   Japanese romaji and produce hiragana, while explicitly marking unreadable or
   incomplete ranges.
2. **Stage 2: boundary/language reinterpretation** — use those phonetic anomalies plus
   lexical evidence to decide which ranges should stay Japanese and which are English.

After enough lookahead, high-confidence leading segments are **frozen**. Append-only
typing never re-analyzes the frozen prefix, so work stays concentrated in a small active
window.

The earlier multi-view consensus v0.2 implementation remains in
`IncrementalRecognizer.cs` as a baseline for comparison.

See [docs/phonetic-first.md](docs/phonetic-first.md).

## Run

Windows with .NET 8 SDK:

```powershell
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

Useful research inputs:

```text
kyouhacommitsimasita
seidohasugokuiikannzininattakaramethodtositehakonnnakannzideiikamo
kyouhacommitasitanimotikosunogamenndoudattakarakousitayo
githubdeissue
networkmiru
thennado
```

## What the UI exposes

- frozen raw/output prefix,
- current Active Window,
- Stage 1 hiragana/pending preview,
- Stage 1 units and confidence,
- Stage 2 selected groups,
- all Stage 2 candidates and evidence,
- final output,
- per-keystroke active-window workload,
- cumulative analyzed-character count.

## Research export

Press **phonetic-first研究JSONを書き出す**.

The single UTF-8 JSON contains every incremental Stage 1 trace, Stage 2 candidate,
freeze event and workload counter, so both accuracy and efficiency can be analyzed from
one file.

## Tests

```powershell
dotnet run --project .\tests\BoundaryLab.Core.SelfTests\BoundaryLab.Core.SelfTests.csproj -c Release
```

GitHub Actions builds the core on Linux, runs the self-tests, and builds the WinForms
prototype on Windows.

## Status

Research prototype. The lexicon is intentionally small and transparent while the
architecture is evaluated.


## CI validation

Every push runs focused regressions, the Windows WinForms build, and a deterministic
large-scale stress/property suite with generated mixed-language cases and 15,000 random
inputs. See [docs/ci-validation.md](docs/ci-validation.md).


## Experimental v0.5 — Evidence Lattice + Delayed Commit

The active experiment on branch `experiment/evidence-lattice-v0.5` replaces reactive
RCR as the primary decoder with a sparse multi-hypothesis lattice. Japanese phonetic,
Japanese lexical, English lexical, structural Latin, symbol and unknown interpretations
compete in parallel. Only a prefix shared by multiple competitive paths is committed.

See [docs/evidence-lattice-v0.5.md](docs/evidence-lattice-v0.5.md).


## v0.6 — Mozc Responsibility IME

The current experimental branch is `experiment/mozc-responsibility-ime-v0.6`.

The decoder now treats the problem as **conversion responsibility routing** rather than
Japanese word segmentation:

- Japanese spans are evaluated by Mozc,
- literal/English spans remain raw,
- incomplete prefixes remain `OpenPrefix`,
- separators close unresolved tokens explicitly.

Two concrete v0.5 failures are addressed structurally:

- `c / co / commi` can no longer be committed merely because wrong paths agree on an
  Unknown prefix.
- `-` is no longer assumed to be Latin; candidates such as `de-ta` compete against
  a Mozc-backed Japanese interpretation.

See [docs/mozc-integration.md](docs/mozc-integration.md).

### Build the Mozc bridge

```powershell
.\tools\setup_mozc_bridge.ps1 -UpdateDependencies
$env:BOUNDARYLAB_MOZC_BRIDGE="$PWD\artifacts\mozc\boundary_mozc_bridge.exe"
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

The repository also contains a manual GitHub Actions workflow,
`build-mozc-bridge`, which builds the native bridge against the pinned Mozc checkout.

## Research evaluation and evidence (2026-10-09)

The current build and regression tests do **not** establish a measured accuracy
improvement over v0.5 or standard Mozc. An evidence audit, experiment protocol,
unverified pilot inputs, and a Python metrics evaluator are available in
[research/README.md](research/README.md).

Run the metric evaluator self-tests with:
```bash
python3 -m unittest discover -s research -p "test_*.py"
```

Do not report a CER improvement, boundary F1 gain, or confidence interval until
human-confirmed gold and paired observations have been collected.

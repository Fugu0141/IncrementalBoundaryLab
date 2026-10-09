# Stream Hybrid v1.2 — shared code/phonetic boundary evidence

Date: 2026-10-09. Parent branch: `experiment/stream-hybrid-v1.1`.
Status: offline C# research engine, not a production Windows TSF IME.

## Reproduction from a real Windows research export

The user reported that `node.js` was no longer displayed correctly in a
longer Japanese/English sentence. The provided v1.1 JSON recorded:
- 70 input characters, 70 incremental frames;
- `mozcAvailable=false` and zero Mozc probes throughout;
- 602 cumulative path expansions;
- 0 committed raw characters (all output remained reversible);
- a mixed-language candidate for `node.js` **was present** from step 27,
  as a `LatinStructural` edge with `localScore=12.85` at raw
  offset `20..27`. The selected path nevertheless read the preceding
  `konoyouninode` as one Japanese phonetic span and then `.` plus
  `js` separately.

The decisive observation is **candidate connectivity**, not missing
vocabulary. `node.js` existed but the phonetic candidate generation
had no corresponding Japanese cut at the start of `node` (raw offset 20).
An isolated `node.js` regression passed before the change, concealing the
failure in a continuous Japanese prefix.

A short independently testable minimal reproduction is
`konoyouninode.jsnado` (desired:
`このようにnode.jsなど`).

The whole original user-typed sentence/JSON is not added to this repo.

## Fix: cross-component boundary hints, not additional global scores

The new decoder already has a structural code recognizer that proposes
`node.js` as one ASCII edge. Rather than boosting that edge score (which
does not help if its start cannot be reached), v1.2:

1. Generates structural code edges **before** phonetic kana runs.
2. Collects source positions where those edges begin.
3. Passes these positions into the kana-run builder as **optional cuts**.
4. Preserves such code-backed kana cuts even when the usual
   `TakeLast(4)` pruning would discard them.
5. Leaves the uninterrupted kana path and literal punctuation path in
   the graph as alternatives; the ranked beam makes the ownership choice.

This is a small bidirectional evidence exchange: English/code grammar
can suggest a boundary to the left-side Japanese parser. The reverse
direction (phonetic uncertainty informing unknown English span proposals)
is handled by v1.1's unknown-Latin island rule.

In the reproduction, the graph can now express:

```
  konoyouni | node.js | nado
  このように | node.js | など
```

rather than only:

```
  konoyouninode | . | js | nado
  このようにので | . | js | など
```

The code root/extension recognizer is intentionally limited. The rule
does not mean every dot in text belongs to an English identifier.
For example `nihongo.node.js` is intended to render
`日本語.node.js`, with the first period retained as punctuation.

## Verified offline regressions

The GitHub Actions `stream-hybrid` test includes expected-output and
selected-path-kind assertions for:
- `konoyouninode.jsnado`,
- `konnnitiha.konoyouninode.jsnado`,
- `node.jsnado`,
- `konoyouninode.js`,
- `node.jswotukau`,
- `nihongo.node.js`,
- `node.jsonwotukau`.

It also preserves v1.1 tests for unknown `meltype`, Japanese
`toomoimasu`, standalone English `commit`, `theory`, and
Japanese/English mixed historical examples.

This is test-derived evidence, **not a held-out accuracy study**.
A Windows real-keypress retest of the full 70-character input and actual
Mozc conversion is still necessary.

## Performance and limitations

The change does not add new beam levels or increase the 12-edge-per-start
candidate cap. It may add phonetic boundary alternatives near code
identifiers, so transitions can increase slightly. Compare per-step
`expandedEdgesThisStep` and `totalExpandedEdges`, plus real Windows
p50/p95/p99 keystroke latency, before claiming a performance win.

- A standalone `node.js` was already correct in v1.1, but continuous
  `...ni + node.js...` exposed the missing left boundary.
- The code root set and extension list remain limited. Unknown names,
  email addresses, `foo.bar`, and sentence periods adjacent to letters
  can still be ambiguous.
- `mozcAvailable=false` means this fix concerns **language ownership
  and cuts**, not Japanese kanji/katakana conversion.
- The GUI and four-category diagnostics remain experimental. No native
  TSF registration or broad editor compatibility is asserted.
- The four-state grouping reports model hypotheses but is not yet a
  calibrated probability or automatic commit controller.

## Run in Windows CMD

```bat
git fetch origin
git switch experiment/stream-hybrid-v1.2
git pull --ff-only
set "BOUNDARYLAB_ENGINE=v1"
set "BOUNDARYLAB_STREAM_MOZC="
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

The title should say **Stream Hybrid v1.2 (shared code/phonetic boundaries)**.
Research JSON should identify
`algorithmVersion=iblab-stream-hybrid-v1.2` and
`parameters.engineId=stream-hybrid-v1.2`.

Always compare with the **same input**, Mozc-connected state and protocol,
and inspect whether structural candidate edges actually appear in the top path.

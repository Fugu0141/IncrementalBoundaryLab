# Four-state phonetic structure v0.8: confirmed/tentative spans and cuts

- Date: 2026-10-09
- Branch: `experiment/phonetic-four-state-v0.8`
- Parent: `experiment/phonetic-hybrid-v0.7`
- Status: **research instrumentation**, NOT an asserted improvement in CER or TSF usability.

## Motivation / evidence

Three Windows v0.7 research exports were inspected (all `mozcAvailable=false`,
`mozcProbesThisStep=0`, `usePhoneticFirstHybrid=true`):

| Raw | Observed output | What it shows |
| --- | --- | --- |
| `oreha` | `おれは` | phonetic candidate improved; Japanese-vs-English ownership remains a separate decision |
| `de-taganaidesune` | `でーたがないですね` | long-vowel hypothesis is now reachable; output is still kana without actual Mozc conversion |
| `tatoebathetoiukotobaha,englishwomanabunihadaizinakotodesu` | `たとえばtheということばは,えんglいshをまなぶにはだいじなことです` | unknown English is fragmented into kana + individual unknown letters |

The user also reported unexpected conversions around a **period** (`.`).
These three attached traces do **not** reproduce that exact period bug:
it is treated as a hypothesis to test, not a measured fact from these files.
The current `InputSyntax.IsBindingSymbol('.')` and
`AddStructuralLatinEdges` allow Latin structural candidates around `.`
without proving that the token is a code identifier rather than a sentence boundary.

### Why adding another global score is insufficient

The old `PhoneticProjector.Project(raw)` performs left-to-right kana projection,
but `KanaCoverage` is not a probability that a word is Japanese.
A kana-readable input can be English (`issue`, `or`), and an unreadable input
may be an unknown English term (`english`).
A strong language score must not imply an irreversible commit.
Therefore, track **groups** and **boundaries**, with independent evidence states.

## Four categories, not four exclusive per-character labels

| Kind | Meaning | Allowed consequences |
| --- | --- | --- |
| Confirmed group / 確定したまとまり | the **phonetic/structural reading** is supported | display as a stable *reading hypothesis*; it does NOT confirm Japanese-vs-English |
| Tentative group / 曖昧なまとまり | the grouping may need revision after context arrives | keep candidate variants; mark for review |
| Confirmed cut / 確定した切れ目 | a literal hard delimiter such as comma/space directly separates input | represent as a structural cut, **not** an automatic lexical/sentence semantic assertion |
| Tentative cut / 曖昧な切れ目 | plausible split from lexicon, phonetic transitions or binding symbols | do not force segmentation or irreversible commit |

A group covers an interval of **source raw positions**; a cut occurs **between**
source characters and can even fall inside a kana-token projection. For
`oreha`, the English `or` ends at raw position 2, although the left-to-right
kana tokenizer sees `o | re | ha`. Both interpretations must remain available.
For a reported `.`, always mark the cut tentative until additional evidence
distinguishes `node.js` from a sentence period or mixed text.

**Confirmed** in this analysis refers only to a reading/structural observation.
It is deliberately NOT the same state as `CommittedSegments`,
`LatticeCommitEvent`, `HardFrozen` or the final IME input commitment.
A group may be structurally confirmed but have ambiguous language ownership.

## Bilateral context review

Unknown/pending phonetic units, English lexical overlaps in kana-readable
material, and binding-symbol ambiguity trigger a **review window**:
`[max(0, start - 6), min(raw.Length, end + 6))`.
Overlapping review windows are merged, and reasons are retained.

For `englishwomanabu` (unknown English dictionary item), the unresolved
letters inside `english` trigger a window covering the preceding `en`
and following `wo...`, so these nearby kana hypotheses are explicitly
tentative. This is *diagnostic context*, not an automatic English detector.
The hard delimiter `,` is a confirmed cut, while `.` and `-` remain tentative.

The constant radius is provisional. A later experiment must compare it with
the original `PhoneticFirstSession`'s ripple parameters and explore adaptive
windows, without over-expanding every edit.

## What the implementation actually does

- `src/BoundaryLab.Core/PhoneticStructureAnalyzer.cs` derives
  `Groups`, `Boundaries`, and `ReviewWindows` from source raw,
  `PhoneticProjector` and the lexicon. It provides an independent
  four-category evidence layer.
- When `BOUNDARYLAB_PHONETIC_HYBRID=1`, the current v0.7-style
  `EvidenceLatticeSession` attaches `phoneticStructure` to each frame.
  There is a **Phonetic four-state** GUI tab and a summary line.
- `algorithmVersion` in JSON is `iblab-phonetic-four-state-v0.8`.
- This experimental step intentionally **does not alter** ranking,
  commit eligibility, or TSF behavior. The v0.7 hybrid decoder remains
  the reference algorithm while classification is assessed.
- The baseline mode without the hybrid flag emits `phoneticStructure: null`.
- The `Confirmation` labels are generated using heuristics and may be wrong.
  No gold set or inter-annotator agreement is available yet.

## Run on Windows

```powershell
git fetch origin
git switch experiment/phonetic-four-state-v0.8
git pull --ff-only
$env:BOUNDARYLAB_PHONETIC_HYBRID = "1"
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

Open the **Phonetic four-state** tab, then type the three raw examples,
`node.js`, `nihongo.`, `github.comde`, and the period example
that actually failed in the user's environment.
Export the research JSON for each test: inspect
`frames[last].phoneticStructure.groups`,
`frames[last].phoneticStructure.boundaries` and
`frames[last].phoneticStructure.reviewWindows`.

The user's original logs are not committed to this public branch.

## Next implementation/evaluation stage

Before using the four-state annotations to *choose* a path:

1. Gold-label **phonetic structure** separately from **language ownership**
   and irreversible commit status. Multiple annotators should label contested
   boundaries, especially unknown English followed by Japanese particles.
2. Measure coverage: proportion of gold cuts/groups represented among
   tentative and confirmed items. Measure confirmed-label errors, false
   positives around `.` and `-`, and window recall for known failures.
3. Implement a separate opt-in decoder mode to **delay commit** whenever a
   plausible path crosses a tentative cut/near-anomaly review window, while
   leaving the original beam and alternative candidates available.
4. Evaluate with a full held-out corpus and real Mozc before claiming
   a CER/latency improvement. Include the potential extra buffering delay
   and app-editing behavior in the cost.
5. Compare baseline v0.6, v0.7 additive score, and four-state feedback
   (ablation). Do not tune only to the three failing examples.

### Open problem: unknown-English vs readable Japanese

`english` is not a known English lexeme in this experiment, and the
offline v0.7 path can devolve into Kana + Unknown tokens. The four states
make the *unresolved island* visible but **cannot infer the whole English
word** by themselves. An unknown-Latin-span candidate and boundary-aware
backtracking are independent future work. Likewise, `.` classification
is only the first step toward reducing mistaken Latin binding scores.

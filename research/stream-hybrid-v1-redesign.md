# Stream Hybrid v1 — Zero-based redesign of the research decoder

Date: 2026-10-09 · Branch: `experiment/stream-hybrid-v1`

**Status: experimental Windows research GUI / C# core, not a validated Windows TSF IME and not a proof of superior CER.** The v0.6, v0.7 and v0.8 engines remain available in their original branches for A/B research.

## Why a fresh engine?

The user reported that the additive v0.7/v0.8 hybrid provided weak generalization and lower performance, especially around `.` and unknown embedded English.

Actual Windows v0.7 sample (offline, `mozcAvailable=false`):
- `oreha` -> `おれは` (improved from v0.6 `orえは`)
- `de-taganaidesune` -> `でーたがないですね` (reading available, not kanji)
- `tatoebathetoiukotobaha,englishwomanabunihadaizinakotodesu`
  -> `たとえばtheということばは,えんglいshをまなぶにはだいじなことです`
- For the 57-character mixed sentence, the old JSON recorded **21,054** expanded path transitions and a roughly **4.6 MB** exported JSON file. Its `candidateEdges` at final position contained 250 items.
- The user also reported a period error; those three cited logs do not actually contain a period. Its exact trigger needs independent reproduction.

## Architecture

```text
Incremental ASCII input (raw source offsets are stable)
          |
   one phonetic pass (existing RomajiConverter/PhoneticProjector)
          |
   +------+---------+----------+------------------+
   |      |         |          |                  |
 exact JP/EN   Kana runs   unknown Latin    punctuation and
 dictionary                  islands         code identifiers
   |      |         |          |                  |
   +------+---------+----------+------------------+
          |
 bounded candidate graph (<=12 edges per start)
          |
  bounded linked-node beam (6 states/offset, up to 4 paths exported)
          |
  soft group + boundary analysis from *actual competing paths*
          |
  optional single selected Mozc probe (OFF by default)
          |
   reversible preview / hard delimiters only commit
          |
    compact frame JSON and WinForms research display
```

**Design choice:** The phonetic branch generates possible readings; it never independently claims Japanese ownership. The English branch generates *literal islands*, including unknown words with consonant sequences unrecognized by romanization. The punctuation branch distinguishes:
- `node.js`: compatible with explicit code-extension grammar; keep together.
- `nihongo.`: `.` remains a literal punctuation symbol rather than automatically forcing an English structural token.
- `de-ta`: a competing kana-long-vowel hypothesis can win; literal `-` remains a valid alternate.
- `englishwomanabu`: unreadable `gl...sh` causes one Latin hypothesis to include the ambiguous `en` left prefix while leaving `wo...` for Japanese conversion.

A recognized English word such as `the` is separate from an *unknown English island*; isolated short 2-letter forms (`or`) are penalized when inside continuous romaji. Candidate generation and ranking are separate; the scoring coefficients are provisional and are **not calibrated probabilities**.

### Four categories, now coupled to the actual hypothesis graph

The v0.8 phonetic 4-state tracker computed a separate large analysis every keystroke, without influencing the decoder. v1 avoids duplicating that work: `StreamHybridStructure` derives groups, possible cuts, and `reviewWindows` from the actual retained hypotheses and their unknown/literal areas, including before/after a punctuation symbol.

| Category | Observation |
| --- | --- |
| Confirmed group | relatively unchallenged known reading **only**; not final language/IME commitment |
| Tentative group | lexical/phonetic ownership overlaps or a review window touches the group |
| Confirmed cut | explicit user-supplied hard delimiter (such as comma/space) |
| Tentative cut | alternative path boundary, period, hyphen, or unresolved island edge |

A boundary can fall inside a kana unit; `oreha` still retains `or | e | ha` as a hypothesis, without automatically accepting that split.

**Important limitation:** the four-state annotation is derived *after* decoding for diagnostics. The v1 decoder integrates the underlying kana/unknown/orthographic evidence directly into its candidate graph and uses a cut penalty, but it does **not yet** train a boundary confidence model or feed those derived four-state labels back into ranking. The latter requires genuinely annotated data, not hand-tuned claims.

### Performance and memory

Legacy repeatedly generated long edges and many beam combinations, then exported full graphs for each keystroke. v1:
- caps 12 candidate edges **per raw start offset**
- retains <=6 linked beam states per offset, instead of copying full edge lists on each transition
- shows at most 4 hypotheses and at most 80 *diagnostic* candidate edges per frame
- caps retained trace frames to the most recent 256 keypresses, while keeping the cumulative expanded count
- is append-incremental between hard separators, retaining a reversible active segment
- does not query the synchronous Mozc bridge unless explicitly opted in

The 57-character example yielded **477 transitions** versus **21,054** in a GitHub Actions core regression run. On the same CI runner, a single cold representative test logged 4.5 ms for v1 versus 107.8 ms for v0.8; further runs differed (6.7 ms vs 129.1 ms). **Do not generalize these one-shot timings** to production performance or different systems. The current decoder still redecodes the uncommitted suffix on every keypress, so very long inputs without a hard separator can degrade.

### Mozc integration

Default v1 does **no speculative Mozc probing** even if the bridge executable is found. To test a connected candidate conversion deliberately, set `BOUNDARYLAB_STREAM_MOZC=1`. When enabled, the v1 decoder probes at most one newly selected Japanese-phonetic span per step, caches by raw spelling, and can display its top candidate if the bridge returns success with heuristic quality >=0.85. This is **only output preview**, not a calibrated choice between all competing owner hypotheses.

The bridge remains synchronous and has a 3-second timeout: **keep it disabled for normal typing research**. Future work: background queue with generation/cancellation tokens, per-session cache, candidate ranking update after response, and bounded latency. No native TSF installation or end-to-end app integration is asserted.

## Run and compare on Windows

In CMD, inside the repo:

```bat
git fetch origin
git switch experiment/stream-hybrid-v1
git pull --ff-only
set "BOUNDARYLAB_ENGINE=v1"
set "BOUNDARYLAB_STREAM_MOZC="
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

Title: **Stream Hybrid v1 (new engine)**. The `Phonetic four-state` tab displays categories. Export the research JSON; it must include:
- `algorithmVersion: "iblab-stream-hybrid-v1"`
- `parameters.engineId: "stream-hybrid-v1"`
- `frames[*].phoneticStructure` (groups, boundaries, review windows)
- `frames[*].expandedEdgesThisStep`, `totalExpandedEdges`, and `mozcProbesThisStep`.

To compare, close the application and run:

```bat
set "BOUNDARYLAB_ENGINE="
set "BOUNDARYLAB_PHONETIC_HYBRID=1"
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

No edits to the legacy branch are required. Export separate JSON files, keep original inputs private, verify mode/connected status, and use verified gold data before computing error rates.

## Regression tests and open problems

Tests: `tests/BoundaryLab.Core.StreamTests` (new CI `stream-hybrid` job).
Known pilot success cases include `oreha`, `de-ta`, `de-taganaidesune`,
`nihongo`, `node.js`, `githubdeissue`, `networkmiru`,
`kyouhacommitsimasita`, and the unknown-English mixed sentence.
`nihongo.` is a separate period regression.

This suite is deliberately **not a held-out corpus** and does not prove statistically better conversion. Future evaluation must add:
1. broad, pre-registered human-verified Japanese/English and code-identifer corpus, including adversarial `.` and `-` cases
2. unknown English, short English, hiragana-visible but semantically incorrect readings, typos, Japanese punctuation, undo/backspace
3. boundary candidate recall/precision, language ownership mistakes, CER, latency p50/p95/p99 and peak memory
4. measured irrevocable commit errors, and same dataset under actually connected Mozc
5. ablation: no unknown island, no kana, no punctuation-aware code evidence, and no path-boundary cut penalty
6. native TSF implementation as a **separate** Windows engineering project after the research core is validated

The research GUI lowercases ASCII. Correct uppercase, punctuation-as-sentence-ending, `foo.bar`, email addresses and real-time cross-application input remain unproven and must not be implied to work.

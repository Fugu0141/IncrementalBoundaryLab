# Phonetic-first × Evidence Lattice hybrid v0.7 — pilot

## Why this experiment exists

The user collected 10 genuine Windows research JSON exports on 2026-10-09.
**All 10 exports reported `mozcAvailable: false` and zero Mozc probes**:
these are **offline v0.6** observations, not measurements of a connected Mozc IME.
The original exports are not added to the public repository. The following
small observations are derived from them, without user-specific metadata.

| Input | v0.6 offline observed output | Observation |
| --- | --- | --- |
| `nihongo` | `日本語` | output matched pilot expectation |
| `commit` | `commit` | output matched |
| `theory` | `theory` | output matched, but split as `the | or | y` |
| `node.js` | `node.js` | output matched |
| `de-ta` | `de-tあ` | wrong Roman/japanese boundary |
| `kyouhacommitsimasita` | `今日はcommitしました` | output matched |
| `githubdeissue` | `githubでissue` | output matched |
| `networkmiru` | `networkみる` | plausible kana-only Japanese part |
| `oreha` | `orえは` | short English word `or` stole Japanese prefix |
| `commitsitade-tawogithubnipushsitekudasai` | `commitしたで-たをgithubにpushしてください` | hyphenated Japanese word broken |

The observed output is not automatically a gold label. In particular,
`de-ta` might be `データ` only after **real Mozc conversion**,
and `oreha` can become `俺は` only after a conversion stage / lexicon.

## Mechanism (feature flag: opt-in)

Set `BOUNDARYLAB_PHONETIC_HYBRID=1` in the WinForms research environment.
The same branch without that flag preserves the v0.6 selection algorithm.

1. Compute **left-to-right phonetic projection** with
   `PhoneticProjector.Project(raw)`. This does **not** commit anything.
2. When a longer Japanese candidate is completely readable according to
   that projection, add a modest evidence bonus to its phonetic lattice edge.
   No bonus before a binding symbol such as the period in `node.js`.
3. If a hyphen has complete, kana-readable romaji on **both** sides,
   add a competing `JapanesePhonetic` edge with the kana long vowel mark.
   Do not replace the original Latin-structural edge or force Japanese ownership.
4. Continue to use the same beam search, path consensus, and delayed commits.
   Only evidence generation differs. An `OpenPrefix` cannot be committed normally.
5. For final Japanese kanji/katakana word selection, Mozc remains responsible.
   This experimental layer produces **kana**, not correct kanji by itself.

### Expected scope and limitations

- `oreha` can be interpreted as `おれは` rather than `orえは`.
- `de-ta` can be interpreted as `でーた` rather than `de-tあ`.
  **This is not equivalent to `データ`, and must not be claimed as perfect CER.**
- An explicit English token such as `no-de` might also become a
  Japanese-looking sequence. This is a known false-positive risk.
- A kana-readable English word can be misclassified as Japanese;
  the hybrid has no universal solution for semantic ambiguity.
- The research GUI lowercases ASCII, so capital-letter handling requires
  separate native verification.
- Native Windows TSF currently uses its own C++ responsibility logic.
  **This C# opt-in switch does not modify, install, or validate the native TSF DLL.**

## Run: compare two modes without mixing experiment conditions

```powershell
git fetch origin
git switch experiment/phonetic-hybrid-v0.7

# Baseline: unchanged v0.6 decision policy
Remove-Item Env:BOUNDARYLAB_PHONETIC_HYBRID -ErrorAction SilentlyContinue
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release

# Close the application, then run the opt-in hybrid
$env:BOUNDARYLAB_PHONETIC_HYBRID = "1"
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

The title and `Mode` summary identify which mode is active. Export research
JSON in both modes and verify the `algorithmVersion` and
`parameters.usePhoneticFirstHybrid` fields to avoid confusing conditions.

CI exercises the original Mozc-contract tests and the additional offline
hybrid regressions, but the **real user reproducibility and external valid
conversion tests are still outstanding**. Improvements must be evaluated on
an independent, previously unseen hold-out set. Do not tune the metric after
seeing the hold-out.

## Follow-up research questions

1. Does the phonetic-first evidence reduce pilot CER against pre-registered
   gold, and by how much on a held-out corpus?
2. What is the false Japanese-ownership rate for readable English and coding
   identifiers? Compare `node-core`, `no-de`, `theory`, and unseen terms.
3. Does it increase irreversible early commits or P95 keypress latency?
4. How does the method interact with actual Mozc candidate generation?
5. After proof on the C# research UI, how should the **same evidence**
   be integrated in the native responsibility runtime without changing the
   input method's compatibility or causing extra synchronous queries?

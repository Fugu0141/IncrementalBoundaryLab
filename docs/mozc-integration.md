# Mozc Responsibility IME v0.6

## Why Mozc is now part of the experiment

The previous lattice tried to solve two separate problems itself:

1. decide which span is Japanese vs literal/English,
2. convert Japanese readings into useful words.

v0.6 narrows BoundaryLab's responsibility. The lattice decides **who owns a span**:

- Japanese -> Mozc,
- Literal -> keep raw text,
- Open -> keep observing,
- Boundary -> punctuation / separator.

Japanese word segmentation and kanji vocabulary are deliberately delegated to Mozc.

## Mozc version

The bridge is pinned to upstream Mozc commit:

`921b8cc99904c8d31b771e395513da0a5d55182a`

This was the head of `google/mozc:master` when the integration was created.

Mozc is distributed under a BSD-style license. This repository does not vendor the
Mozc source tree. `tools/setup_mozc_bridge.ps1` clones the upstream repository at the
pinned commit and copies the small BoundaryLab bridge target into that checkout.

## Architecture

```text
keyboard/raw text
       |
       v
Responsibility lattice
  |       |       |
  |       |       +-- Open: never commit yet
  |       +---------- Literal: preserve raw
  +------------------ Japanese candidate
                         |
                         v
                   Mozc bridge
                         |
                  mozc::client::Client
                         |
                         v
                     mozc_server
                         |
                  candidates / preedit
                         |
                         v
        score is fed back into the lattice
```

Mozc therefore acts both as the eventual conversion engine and as a **Japanese
convertibility oracle** during boundary detection.

## Two structural fixes from v0.5

### Open prefixes are not Unknown

`c`, `co`, `comm`, `commi` can be prefixes of `commit`. v0.5 could commit
`c` merely because all surviving wrong paths shared the same Unknown first edge.

v0.6 introduces `OpenPrefix`. OpenPrefix and Unknown edges can never be committed by
normal multi-path consensus.

A hard separator may close an unresolved token as literal text, because the user has
explicitly supplied a boundary.

### Hyphen is not automatically Latin

`-` is ambiguous:

- `node-core` may be a literal identifier,
- `de-ta` can be Japanese keyboard input for データ.

Structural Latin edges containing a hyphen are penalized when the surrounding character
profiles are Japanese-like. When the Mozc bridge is available, spans containing a hyphen
are also emitted as `JapaneseMozc` candidates. Mozc's actual conversion quality then
decides whether they become competitive.

## Bridge protocol

`boundary_mozc_bridge.exe --stdio` reads one raw romaji query per line and emits one
JSON object per line:

```json
{"ok":true,"raw":"de-ta","preedit":"でーた","candidates":["データ","でーた"],"error":""}
```

The bridge uses Mozc's own `client::ClientFactory::NewClient()`, turns the session on
in HIRAGANA mode, sends the raw key sequence, sends Space to request conversion, reads
the preedit/candidate window, then reverts the temporary session.

## Build the bridge on Windows

Mozc's current Windows build requires the normal Mozc build prerequisites (Visual
Studio C++ toolchain, Python, Bazelisk, etc.).

From this repository:

```powershell
.\tools\setup_mozc_bridge.ps1 -UpdateDependencies
```

The result is copied to:

```text
artifacts/mozc/boundary_mozc_bridge.exe
```

The WinForms research UI auto-discovers that location. You can also set:

```powershell
$env:BOUNDARYLAB_MOZC_BRIDGE="C:\path\to\boundary_mozc_bridge.exe"
```

The bridge expects a compatible Mozc server to be available. For the first real Windows
IME prototype, build/install Mozc from the same pinned checkout so the client/server
protocol versions match.

## Experimental TSF key router status

This branch also has a **native, patched Mozc TSF prototype**, not only the
standalone bridge. The `tools/apply_mozc_tip_responsibility_patch.ps1` and
`tools/apply_mozc_tip_key_router_patch.ps1` scripts add responsibility state
and route eligible ASCII keys before the normal Mozc TSF handler. CI builds
`mozc_tip64.dll` and runs native decoder, output, and runtime unit tests.

**A successful CI build is not proof that the DLL can be safely installed or
used in real editors.** There is no completed installer/registration workflow
for an isolated BoundaryLab IME. Do not overwrite an existing Mozc installation
with the experimental DLL. Test composition, candidate navigation, Escape,
Backspace, mode changes, and non-ASCII input in a disposable Windows
environment before considering integration into Meltype.

## Known accuracy and responsiveness limitations

- English recognition currently uses a small static lexicon. Unknown
  **lowercase** English tokens may still be flushed prematurely to Japanese;
  this is not solved by the capital-letter regression fix.
- An unfamiliar token starting with an **uppercase ASCII letter** is now held
  as a literal candidate rather than speculatively flushed as Japanese.
  This favors English names/identifiers but can misclassify intentionally
  capitalized Japanese romaji. Known English lexemes still allow their
  established Japanese-suffix splits.
- A mixed-language token can be inherently ambiguous without more context;
  dictionary word recognition is not sufficient for universal boundary
  inference. Native unit tests currently rely on a fake Mozc oracle; they do
  not demonstrate end-to-end conversion quality with a running Mozc server.
- The C# research bridge enforces a three-second read timeout and disables
  the child process if it stops replying. This avoids *indefinite* UI hangs;
  the synchronous probing path can still cause short pauses and requires a
  proper asynchronous implementation before production use.
- No latency benchmark or comprehensive real-app TSF compatibility suite
  has been completed.

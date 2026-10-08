# Phonetic-first incremental segmentation

## Research hypothesis

v0.3 deliberately separates two questions that earlier prototypes tried to solve at
the same time.

### Stage 1 — phonetic projection

Read the active ASCII input from left to right as if it were Japanese romaji.

Every consumed range becomes one of:

- **Kana** — a normal romaji token was converted to hiragana.
- **Ambiguous** — the current tail could still become valid romaji after more input.
- **Pending** — the characters are not readable as Japanese romaji at this position.

No English/Japanese language decision is made here.

Examples:

```text
seido  -> せいど
method -> め{t?}ほ{d…}
commit -> {c?}おっみ{t…}
```

The exact trace, including raw ranges and confidence, is exported for research.

## Stage 2 — reinterpret only where needed

The resolver then uses Stage 1 as evidence.

An exact English dictionary span is promoted to an English anchor when its phonetic
readability is low. The remaining ranges are interpreted as Japanese using explicit
Japanese lexicon entries first and plain phonetic conversion second.

This means English is not searched for by making every English/Japanese split compete in
one global beam. Instead, Japanese pronunciation is attempted first and anomalous
regions are reconsidered.

Kana-readable strings such as `anime` intentionally remain ambiguous/Japanese by
default. That ambiguity is useful research data rather than something hidden by a hard
rule.

## Frozen prefix

The live session owns three logical regions:

```text
[ frozen / committed prefix ][ active window ][ unfinished tail ]
```

A segment can move into the frozen prefix only when:

- it has enough confidence,
- it is not Unknown,
- at least the configured lookahead has already arrived.

On ordinary append-only typing the frozen prefix is never analyzed again. Only the active
window is sent through Stage 1 and Stage 2.

Deleting text or editing the middle of the input invalidates the incremental assumption,
so the session safely resets and replays the edited input.

## Why this differs from v0.2

v0.2 used a 64-wide beam plus several confirmation views. It was useful for calibration
research but expensive and still let one shared candidate-generation model dominate the
decision.

v0.3 is a different architecture:

1. phonetic projection,
2. local language reinterpretation,
3. irreversible freezing after lookahead.

The old v0.2 code remains in the repository as a comparison baseline.

## Research JSON

The `phonetic-first-v1` export stores:

- every Stage 1 phonetic unit,
- phonetic preview and unresolved ratio,
- every Stage 2 candidate and why it existed,
- selected active segments,
- the exact segments frozen on each keystroke,
- committed raw length,
- active-window length,
- characters analyzed during that step,
- cumulative analyzed characters,
- whether a frame was rebuilt after an edit.

This allows accuracy and computational work to be studied together.

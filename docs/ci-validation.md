# Large-scale CI validation

The RCR branch is validated at three levels on every push and pull request.

## Focused regressions

The existing self-test suite checks named failures and research examples such as mixed
Japanese/English input, `node.js`, `mawasu`, orthographic binding tokens, ripple/thaw
events, punctuation and workload.

## Stress/property suite

`BoundaryLab.Core.StressTests` adds deterministic large-scale coverage:

- a fixed regression corpus,
- generated Japanese + English + Japanese combinations,
- generated Latin binding-token forms using `.`, `_`, `-`, `@`, `/`, and `:`,
- postfix forms such as `C#` and `C++`,
- an explicit low-confidence ripple case,
- 15,000 deterministic random inputs up to 48 characters,
- 500 duplicate runs to verify deterministic results,
- structural invariants for every result,
- a performance guard against full-prefix reanalysis.

For every generated case the suite checks that input is reconstructed exactly from the
segments, ranges are contiguous, output is reconstructable, committed/active freeze
states are coherent, HardFrozen never thaws, and the incremental frame count matches the
input length.

The random generator has a fixed seed so a CI failure is reproducible.

## Why not assert linguistic correctness for random text?

Random strings usually do not have a meaningful "correct translation". Fuzz tests
therefore check safety, determinism and state-machine invariants. Linguistic correctness
is covered by the fixed and combinatorial corpora, where an expected output is known.

This split lets the project add tens of thousands of cases without pretending that
arbitrary random text has a gold-standard language interpretation.

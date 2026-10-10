# H7 — Japanese-romaji / English mixed-input stress evaluation

2026-10-10. **Scope: offline Python candidate rankers H5, H6 and exploratory H7; NOT the C# StreamHybridDecoder or an installed IME.**

## Experimental setup and reproducibility

- **5,686 systematic cases × five seeds = 28,430 model evaluations**, 13 categories, 5,030 distinct raw strings in each seed. The same 54 OOV English word types recur across seeds; these are NOT 28,430 independent natural-language examples.
- **100,000 structural property/fuzz checks** on gold offset construction; zero observed inconsistencies. These check the test generator, **not model conversion accuracy**.
- Python 3.13, cmudict 1.1.3, seeds 20261010–20261014; H5/H6 development-only 5% candidate false-positive budgets and top four candidates. H7 is an exploratory asymmetric-context correction motivated by failures observed in H6, not an independently validated improvement.
- H4 6 tests, H5 8 tests, H6 8 tests, H7 11 unit tests passed. Two repeat runs produced identical JSON files (SHA256 summary `525a7bc17e02231b2e4c73d222bf2fd29806bf7dba29fab122a2b5e45223c8f5`, full `5894202445994c937a7120abdc2dddafe8c45ce3b202fa6b060cc0bfd14eb6ab`). No .NET/Windows/Mozc/TSF or interactive input run.

## Critical finding: H6 overfits English surrounded by Japanese

Numbers below are **top-ranked English-span exact matches summed across five seeds** (not C# conversion rates).

| Category | H5 | H6 | Exploratory H7 |
| --- | ---: | ---: | ---: |
| English at start | 1,797/3,240 (55.5%) | **96/3,240 (3.0%)** | 1,243/3,240 (38.4%) |
| English in middle | 1,878/3,780 (49.7%) | **2,659/3,780 (70.3%)** | 2,659/3,780 (70.3%) |
| English at end | 1,563/3,240 (48.2%) | **318/3,240 (9.8%)** | 1,531/3,240 (47.3%) |
| English only | 136/270 (50.4%) | 21/270 (7.8%) | 37/270 (13.7%) |
| Alternative Japanese romaji pair contexts | 1,504/3,240 (46.4%) | 2,190/3,240 (67.6%) | 2,190/3,240 (67.6%) |
| Short acronyms/words | 0/1,440 | 0/1,440 | 0/1,440 |
| Upper/initial uppercase Latin | 1/1,080 | 19/1,080 | 19/1,080 |
| English followed by punctuation/code symbol | 0/1,620 | 0/1,620 | 0/1,620 |
| Japanese mora interrupted by English (intentionally unnatural) | 0/1,080 | 0/1,080 | 0/1,080 |
| Nonsense strings intentionally treated as literal | 102/1,440 | 74/1,440 | 88/1,440 |
| Two separated English islands | 0/1,620 | 0/1,620 | 0/1,620 |
| Two adjacent English islands | 0/1,620 | 0/1,620 | 0/1,620 |
| Japanese-only negative inputs **with any false English** | 51/4,760 (1.07%) | 38/4,760 (0.80%) | 38/4,760 (0.80%) |

**Interpretation cautions:** H5/H6 only rank a single English span, so two-island full-span exactness is impossible by construction, not a proof that the C# lattice cannot parse them. The Python candidate generator only considers 7–24-letter words and restricted cuts; its 0% for short words, symbols and mid-mora interruptions is not a measure of the C# decoder's separate lexical, code or Mozc handling. Uppercase is also handled differently by the C# code. English-only input has no language-switch boundary, so do not use language-switch F1 alone for that category.

### Romanization invariance

C# token table supports pairs such as `shi/si`, `chi/ti`, `tsu/tu`, `fu/hu`, `sha/sya`. 324 paired contexts per seed, **1,620 repeat evaluation pairs**, not independent underlying English words. Raw typed character indices determine gold positions, with English unchanged. **Both variations exactly correct**: H5 719/1,620 (44.4%); H6/H7 1,074/1,620 (66.3%). Unknown input intent for `ime` or `tokyo` remains an unsolved ambiguity. The C# table still lacks the common `ji` single-syllable spelling.

### Candidate survival / boundaries

Full results separately record candidate Top4 gold containment, rank1 exact English spans, start/end absolute distance, within-1-character matches, false English on Japanese-only input, and raw-character Japanese↔English transition boundary micro-F1. Adjacent English pieces do NOT count as a language transition. Boundary positions at offset 0 or end-of-input likewise are not treated as switches.

The primary cause of the edge-position regression is H6 `score_boundary`: **if either side of the proposed English candidate is empty it returns -0.6**. H7 experimentally removes the missing-side penalty and calibrates its score on Japanese development data. H7 partially recovers sentence-start/end accuracy but remains behind H5 on sentence-start and pure-English inputs, so **do not deploy H7 as a solved replacement**.

## Next questions

1. Profile the actual C# v1.3 lattice, including structural code/short lexemes, before inferring production failure.
2. Build explicit **multispan** ground truth and ranked global paths, not merely one English span. Separate language-switch boundaries from Japanese internal morphological boundaries.
3. Test mode/intent preservation and mixed casing, punctuation, ambiguous words, in-progress keystrokes, correction/backspace, user-level holdouts, and latency p50/p95/p99.
4. For a future H8, explore evidence-based gating for English at start/middle/end/alone, with a pre-registered false-positive budget and unseen natural-text gold.

## Reproduction

Use the exact reproducibility package provided with this study, containing the Python evaluator, 11 harness tests, all five-seed JSON outputs and representative failures. Place the evaluator and self-test in `research/` beside the H4/H5/H6 scripts and run:

```bash
python -m pip install cmudict==1.1.3
PYTHONPATH=research python research/h7_mixed_stream_stress.py --root . --output-dir h7-results --seeds 20261010 20261011 20261012 20261013 20261014 --structural-fuzz 100000
python research/h7_mixed_stream_selftest.py
```

**Important:** these generated examples are exploratory and use synthetic intended-language labels rather than blinded human annotations. A finite generator cannot enumerate every possible input or prove universally correct segmentation.
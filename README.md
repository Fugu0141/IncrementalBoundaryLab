# IncrementalBoundaryLab

## Research log / 研究履歴（累積）

研究内容は日付順に**追記**し、詳細な根拠・失敗例・再現コードは `research/` に保存します。
研究用ブランチの結果を `main` の実装性能と取り違えないように記録します。

| 日付 | 研究内容 | 確認できたこと / 留保 | 資料 |
| --- | --- | --- | --- |
| 2026-10-10 | **H8: 複数英語区間の全体経路探索** | H7と同じ13分類×5分割＝28,430モデル評価。離れた英語2島の全体一致0→20.4%、文頭38.4→53.2%、純英語13.7→54.4%。ただし文中70.3→40.8%、別表記67.6→34.5%、日本語誤候補38→83件と悪化。**オフラインPython実験・IMEには未統合** | [H8レポート](research/2026-10-10-h8-global-multispan-study.md) / [再現コード](research/h8_global_path_experiment.py) |
| 2026-10-10 | **H7: 大規模な日英混在・区切り位置ストレス検証** | 13分類×5,686ケース×5分割＝28,430モデル評価、Gold座標ランダム検証100,000件。H6は文中英語70.3%に改善する一方、文頭3.0%・文末9.8%に大幅悪化。H7片側文脈補正も未解決。**Pythonオフライン候補モデルの実験でありC# IMEではない** | [検証レポート](research/2026-10-10-h7-mixed-stream-stress.md) / [再現コード](research/h7_mixed_stream_stress.py) |
| 2026-10-10 | **H6: 境界F1＋複数ローマ字入力方式** | H5比較・5分割の同一54語に対し、別表記ペアでの言語境界F1平均69.42%→79.83%、ペア双方の完全一致平均25.6→35.8/54。ただし±1文字以内や日本語誤検出は一部悪化。**Python候補再順位付けのみ** | [研究レポート](research/2026-10-10-h6-boundary-alias-study.md) / [再現コード](research/h6_boundary_alias_experiment.py) |
| 2026-10-10 | **H5: 音韻＋境界による候補検出** | 同じ54未知英語の5分割でH4の平均68.9%→H5の80.7%。日本語誤候補は5分割合計28→29と改善せず。別の合成文脈でも検出力は向上。**Python候補実験でありIME未統合** | [H5レポート](research/2026-10-10-h5-phonotactic-context-study.md) / [コード](research/h5_phonotactic_context_experiment.py) |
| 2026-10-10 | **H4: 誤候補予算付き英語候補生成** | 開発誤候補予算5%で閾値と上位候補を調整。5分割中4分割で日本語誤候補入力が減少する一方、英語候補検出は4分割で低下。未見テストで5%目標を超える場合あり。**Python候補実験でありIME本体には未適用** | [H4研究結果](research/2026-10-10-h4-error-budget-study.md) / [再現コード](research/h4_error_budget_experiment.py) |
| 2026-10-10 | **Codexによるv1.3独立検証** | C#既存5スイートとPython5テスト成功、RCR中心のストレス16,164件。H1の数値を再現。`customer`等は英語候補生成が不足。H2/H3は未証明、Claude 263件は未再現 | [実験ブランチの検証レポート](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/experiment/stream-hybrid-v1.3-latin-morphology/research/2026-10-10-codex-validation-report.md) |
| 2026-10-10 | **H1: 未知英語の文字統計による候補生成** | 英語54語で固定語尾のみ5語、文字モデル32語、候補併用34語。候補生成の測定でありIMEの最終変換精度は未測定。5種類のデータ分割で感度確認 | [研究ノート](research/2026-10-10-oov-character-candidates.md) / [再現コード](research/oov_character_model_experiment.py) |
| 2026-10-09 | **v1.3 Claude評価ハーネス（添付研究ブランチ）** | 263合成ケースの報告で未知英語の弱点を発見。ただしC#独立再実行前・gold境界ラベル誤り1件確認。**mainには未統合** | [検証と留保](research/2026-10-10-oov-character-candidates.md) |
| 2026-10-09 | **Stream Hybrid v1.1–v1.3** | 候補境界の接続・未知Latin形態候補・可逆的な経路保持を検討。各方式は別ブランチ | [v1.3実験ブランチ](https://github.com/Fugu0141/IncrementalBoundaryLab/tree/experiment/stream-hybrid-v1.3-latin-morphology) |
| 2026-10-08 | **Evidence Lattice / v0.5** | 複数の解釈を保持し、確定を遅らせるデコーダの探索 | [設計資料](docs/evidence-lattice-v0.5.md) |

実験の追加時は**日付・対象コミット・比較条件・限界・実行方法**を残し、旧結果は削除せず追記します。
[研究の目次とルール](research/README.md)

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

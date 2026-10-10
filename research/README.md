# Research archive / 研究履歴

このディレクトリには実験を時系列で累積します。新しい成果は個別の日付付きノートにし、トップレベルの [README研究ログ](../README.md) に1行追加してください。

## 2026-10-10 — H9 follow-up: shifted contexts, failure stages, long inputs

- [結果と原因分析（日本語）](2026-10-10-h9-additional-evaluation.md)：12分類1,094種類×5seed＝5,470回。候補生成・閾値/上位K採用・経路選択を入力単位で別々に集計
- [再現用Pythonコード](h9_additional_validation.py) / [持ち運び可能な12件のテスト](test_h9_additional_validation.py) / [全分割の集計JSON](h9-additional-summary.json)
- 短英語/記号/4英語区間の構造制約、1200文字のPython再帰エラー、path選択単体の処理時間も記録。C# / Mozc / TSF は未実行
- 検証プロトコルは [研究検証標準v1.1（ドキュメントPR #6）](https://github.com/Fugu0141/IncrementalBoundaryLab/pull/6) に拡張。研究コード本体をmainに適用しない

## 2026-10-10 — H9 joint Japanese-phonetic / English lattice study

- [H9研究レポート](2026-10-10-h9-joint-lattice-study.md) — H7・H8と同一13分類/5分割で、全英語span完全一致・言語切替境界F1・日本語誤検出・別表記精度を比較
- [H9再現コード](h9_joint_lattice_experiment.py) / [14件の単体テスト](h9_joint_lattice_selftest.py) / [集計JSON](h9-2026-10-10-summary.json)
- 28,430回の合成入力モデル評価。H8に比べ、文頭/中/末・2英語島・micro F1は改善。ただしH7より文中・別表記や日本語誤候補で回帰が残る。候補生成スキャナの短英語・記号制約も未解決
- H8 PR #7を親とする研究PR。H9は日本語ローマ字音韻を用いた**Python上の簡易ラティス**であり、C#本体・Mozc/TSF・毎打鍵レイテンシは未検証

## 2026-10-10 — H8 global multi-English span path study

- [詳細レポート](2026-10-10-h8-global-multispan-study.md) — 英語位置別、2つの英語区間、別表記、日本語誤候補の正負双方の結果を保存
- [再現コード](h8_global_path_experiment.py) / [13件のテスト](h8_global_path_selftest.py) / [5分割の集計JSON](h8-2026-10-10-summary.json)
- H7の同じ合成コーパスで28,430モデル評価、完全な結果は研究ZIPにも保存。全体経路探索で複数区間の表現は可能になったが、文中精度・日本語誤候補・別表記精度に回帰があり、実用アルゴリズムへ統合しない
- H7 PR #5に依存する独立研究ブランチ。C#/.NET・実IMEは今回未検証

## 2026-10-10 — H7 large-scale mixed input stress study

- [H7レポート](2026-10-10-h7-mixed-stream-stress.md) — 文頭/文中/文末/純英語/複数英語/コード記号/小文字大文字/ローマ字別表記など13分類の測定結果と重大な回帰
- [H7実験コード](h7_mixed_stream_stress.py) / [11件のテスト](h7_mixed_stream_selftest.py) / [集計JSON](h7-mixed-input-2026-10-10-summary.json)
- 5分割・合計28,430件の**Python英語候補モデル評価**と、100,000件のGold座標生成プロパティ検証。混在率だけでなく、英語の位置別完全一致、Top4、言語境界F1、表記揺れ、誤英語候補を分離して測定
- **H6は文中では有利でも、文頭・文末ではH5より大幅に悪い。H7の片側補正も十分ではない。** C#の本体ラティス・逐次操作は別途検証が必要。H6研究PRに依存

## 2026-10-10 — H6 exact boundary + alternative romaji study

- [区切り位置・表記揺れの研究レポート](2026-10-10-h6-boundary-alias-study.md) — 生入力座標での日本語⇔英語境界F1、±1文字許容、誤候補、aliasペア正解率の比較
- [H6再現コード](h6_boundary_alias_experiment.py) / [8件の単体テスト](h6_boundary_alias_selftest.py) / [集計JSON](h6-boundary-alias-2026-10-10-summary.json)
- 音韻表にshi/si等の両方式が存在することを検証し、ji/zi対応漏れも記録。H5より境界完全一致は改善する一方、日本語誤候補・±1文字以内の指標には回帰がある。C# IME未統合。H5研究PRに依存

## 2026-10-10 — H5 phonotactic + boundary candidate study

- [研究ノート（日本語）](2026-10-10-h5-phonotactic-context-study.md)
- [再現コード](h5_phonotactic_context_experiment.py) / [8件のテスト](h5_phonotactic_context_selftest.py) / [集計JSON](h5-phonotactic-context-2026-10-10-summary.json)
- 5種類のデータ分割、4条件のアブレーション、異なる日本語接頭・後続文脈の合成ストレステストを記録
- H5は候補検出率を改善したが、誤候補は一貫して減らない。IMEの最終精度・性能は未検証。H4研究PRに依存

## 2026-10-10 — H4 false-candidate-budget study

- [H4の独立実験と留保事項](2026-10-10-h4-error-budget-study.md) — 5seedで候補再現率と日本語の誤候補入力数を比較。制約は開発データの経験的な上限であり、未使用データでの保証ではない
- [再現用Pythonスクリプト](h4_error_budget_experiment.py) / [独立テスト6件](h4_error_budget_selftest.py) / [機械可読の集計](h4-error-budget-2026-10-10-summary.json)
- H1と同じ54未知英語を比較。候補数制限は探索量を削るが、正解候補も削る。C# / 実IMEへの効果は未検証

## 2026-10-10 — Out-of-vocabulary English candidate study (H1/H2)

- [詳細評価・仮説・制約](2026-10-10-oov-character-candidates.md)
- [再現用Pythonコード](oov_character_model_experiment.py) — 実行前に `python -m pip install cmudict`。mainにも存在するLexicon.csを使用。英語評価語54語・日本語音韻語19語は添付Claude実験の固定スナップショットを使用。
- 原因分析: 英単語内部の助詞衝突、候補の枝刈り、入力の意図が文字列だけでは決定できない問題。
- 結果: 固定語尾5/54、文字モデル32/54、併用34/54（seed=20261010、**候補発見率のみ**）。負例や5種のデータ分割の変動も詳細ノートに記載。

## 管理ルール

1. 新しい研究ごとに `README.md` の研究ログへ行を追記し、研究詳細は `research/YYYY-MM-DD-*.md` で残す。
2. 実験結果にはデータ出典・評価語数・乱数seed・失敗例・未測定事項・再現コマンドを必ず記載。
3. 第三者の報告値、独立再実行結果、仮説を明示的に分ける。候補検出率と最終IME出力の品質を混同しない。
4. 実験ブランチのコードとmainの状態を区別し、誤ったgoldラベルを修正した場合は数値も再測定する。
5. 利用者の私的入力、鍵情報、第三者のライセンス未確認の辞書データを公開しない。

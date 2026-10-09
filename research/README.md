# 研究評価の入り口 / Research evaluation

**現段階では、IME が既存手法より高精度になったとは実証していません。**
このディレクトリは、実測結果を他者が再現・監査できるようにするための土台です。

- [2026-10-09 予備監査・測定計画](research-evaluation-report-2026-10-09.md): 研究仮説、数式、CIの根拠、未検証事項、比較設計
- [Windows / Linux 実験者向け手順](manual-evaluation-protocol.md): 今回実際に触って記録する内容、起動コマンド、注意点
- [パイロット入力20例](pilot-corpus.jsonl): **未確認の暫定正解案**（`gold_status=unconfirmed`）
- [記録例](observations.example.jsonl): JSON出力のパスを紐付ける書式（測定済みデータではない）
- [評価スクリプト](evaluate.py): Python標準ライブラリのみでCER・完全一致・任意の境界F1・ペア付きbootstrap信頼区間を算出
- [評価スクリプトの単体テスト](test_evaluate.py)

## 実験結果を入れる前のルール

1. コーパスの `draft_expected_output` は参考値。**予測結果を見ない状態で**文ごとに `gold_output` を決め、`gold_status` を `verified` にする。正式な境界ラベルがない場合は `gold_boundaries: null` のままでよい（F1は算出されない）。
2. 各方式で同一入力を1文字ずつ試す。研究 GUI の「Mozc責務研究JSONを書き出す」を使い、例: `research/exports/M01-v0.6_offline.json` に保存する。
3. `observations.example.jsonl` を参考に、新規の `research/observations.jsonl` を作る。観測した試行の `status` は `measured` にし、`case_id`, `condition`, `research_json` を正確に指定する。`research_json` の相対パスは **リポジトリのルートから**記述する。
4. リポジトリのルートで以下を実行する。**goldが未確定、もしくは測定データなしのときは数値を出さず終了コード2**となる。

```bash
python3 research/evaluate.py \
  --corpus research/pilot-corpus.jsonl \
  --observations research/observations.jsonl \
  --baseline v0.6_offline --candidate v0.6_mozc
```

5. 2方式に同じ case ID の観測が存在すれば `delta_cer` と paired bootstrap 95%信頼区間を出す。
6. 先に単体テストを行うには `python3 -m unittest discover -s research -p "test_*.py"` を実行する。

### 観測JSONLの例（架空の結果は含めない）

```json
{"case_id":"M01","condition":"v0.6_offline","status":"measured","research_json":"research/exports/M01-v0.6_offline.json"}
```

CSV やスクリーンショットのみを原記録にせず、可能な限り研究用 JSON も保存する。
非公開の文章やパスワードを含む原データは公開リポジトリへ置かないこと。

### 解釈上の制限

- `MozcQuality` は校正済みの正解確率ではない。
- `boundary_micro_f1` は **手動で指定した gold / predicted boundaries のあるケースだけ**で集計する。研究JSON中の仮説候補を自動的に正解境界とみなさない。
- このスクリプトは現状 **変換時間・メモリ使用量・誤確定イベントの良否判定を測らない**。実測計測器・アノテーション手順は追加課題。
- 結果が良いケースのみ残すことを禁止する。失敗やタイムアウトも別途記録する。

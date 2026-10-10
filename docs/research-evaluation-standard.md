# IncrementalBoundaryLab 研究検証標準（Research Evaluation Standard）

> **Version:** 1.0 — 2026-10-10  
> **Status:** 研究・レビュー手順の規範。過去のH1〜H7の性能値を再測定したドキュメントではない。  
> **Scope:** 日本語ローマ字・英語・コードの混在境界推定、IME向け変換、逐次入力、再現性・性能。  
> **Report template:** [研究レポート雛形](../research/templates/evaluation-report-template.md)

この文書は、これまでのClaude評価報告、CodexによるC#追試、H1〜H7のPython比較実験、大量の境界データ生成テスト、および失敗例を整理し、**次回以降の研究を同じ基準で比較するための手順**として定める。テストが実装済みかどうか、現在のブランチにファイルがあるかどうかは、各回に実際に確認する。

## 0. 最重要ルール：異なる評価対象を混同しない

| 層 | 何を試験するか | この層から主張してよいこと | 主張してはならないこと |
| --- | --- | --- | --- |
| **A：候補生成（Python）** | 文字統計・音韻・境界スコア、候補の正解包含、誤候補 | 特定の合成入力で正解範囲が候補に存在した割合 | C#デコーダの最終変換精度、実際のIME性能 |
| **B：C# Core / ラティス** | 候補生成、接続、枝刈り、順位、最終出力、StreamSession | 指定コミットのオフラインデコーダで観測した事実 | Mozc/TSF/実アプリ互換性 |
| **C：逐次入力・状態管理** | 打鍵ごとの出力、保留、確定、Backspace、編集、候補安定性 | 特定セッションの確定と修正の正しさ | 単発文字列の正解率だけによる逐次挙動の推測 |
| **D：実IME・ユーザー入力** | 実Windows、Mozc bridge、TSF、各アプリ、遅延・操作性 | 環境・アプリを指定した実動作 | Coreビルド成功だけから実入力対応を断定 |

すべての結果に **層(A/B/C/D)**、**入力の種類（人手gold / 合成gold / 未確認）**、**ブランチとコミットSHA**、**実行済/未実行/失敗**を付す。過去報告の転記は「引用・未再実行」と明示する。

## 1. H1〜H7とこれまでに行ったテストの種類

| 検証 | 方法・規模 | 主な指標 | 発見した落とし穴 / 今後の必須確認 |
| --- | --- | --- | --- |
| **Claude v1.3評価** | 263合成ケースのC# EvalHarnessという報告 | 完全一致、CER、未知英語含有ケース | この263件の原本はCodexが公開参照から取得できず**独立再現していない**。Gold境界の誤記の指摘あり。未確認値を基準値にしない |
| **Codex C#追試** | 既存Self/Lattice/Mozc-contract/Stream/Stress、Python5単体テスト、WinFormsビルド、17入力の比較プローブ。Stressは16,164ケース・379,378文字・決定性再試行500回 | ビルド、回帰、出力、言語境界、セッション、コスト | Stressは主に**旧RCR**の性質確認で、v1.3の精度を測った件数ではない。Windowsビルドと実IMEは別 |
| **H1** | 英語CMUdict文字n-gram、未知英語54語・日本語語種のholdout、語尾方式と比較。5 seed | 未知英語候補検出率、誤候補、混在中gold span候補包含 | 候補生成は最終選択ではない。英単語単体と未分割混在入力は別課題 |
| **H4** | 開発の日本語連結165例で経験的な誤候補予算0/1/5/10%、閾値校正・Top K制限。テスト日本語229例、英語54例、5 seed | candidate recall、negative case FPR、候補数、閾値感度 | 開発5%は**未見データでの5%保証ではない**。候補上限で正解も落ちる |
| **H5** | 文字統計のみ／音韻のみ追加／境界のみ追加／両者追加の4条件、文脈変更ストレス、5 seed | 候補正解包含、誤検出、文脈変更への耐性 | 正しい候補が増えても日本語誤検出は一貫して減らない。音韻近似はC# converterと非同一 |
| **H6** | 読みが同じローマ字別表記ペア（例 `shi/si`、`chi/ti`）で、英語54語×2表記、5 seed | 境界P/R/F1、Rank1完全一致、±1文字、開始/終了誤差、両表記成功率 | 完全一致が改善しても±1文字以内の割合や誤英語候補が**悪化する**ことがある。英語の文字列は正規化しない |
| **H7** | 文頭・文中・文末・純英語など13分類の5,686合成ケース×5 seed＝**28,430回のモデル評価**。別途Gold座標生成の**100,000回プロパティ検証** | 位置別Rank1完全一致、Top4、境界F1、表記揺れ、負例、構造不変条件 | H6は文中で改善するが文頭・文末で大幅悪化。100,000回は**正解生成器の検証**でモデル精度ではない。単一英語span選択器で2島は表現不能 |

重要な履歴資料： [H1の独立検証](../research/2026-10-10-oov-character-candidates.md) ／ [Codex追試レポート（v1.3ブランチ）](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/experiment/stream-hybrid-v1.3-latin-morphology/research/2026-10-10-codex-validation-report.md) ／ [H4 PR #2](https://github.com/Fugu0141/IncrementalBoundaryLab/pull/2) ／ [H5 PR #3](https://github.com/Fugu0141/IncrementalBoundaryLab/pull/3) ／ [H6 PR #4](https://github.com/Fugu0141/IncrementalBoundaryLab/pull/4) ／ [H7 PR #5](https://github.com/Fugu0141/IncrementalBoundaryLab/pull/5)。

**注意：H4〜H7は別の積み重ね型研究ブランチにある。mainにファイルがないときはPRのブランチを参照し、無理にmainへ存在すると想定しない。**

## 2. 毎回の標準ワークフロー（必須）

### Stage 0：実験計画を凍結（データを見る前）

1. 仮説を**反証可能な一文**で記載し、主要評価指標を決める。例：「日本語のみの誤英語候補が5%以下という*テスト上の目標*の下で、未知英語のgold span包含率を改善する」。
2. ベースラインと変更案を同じ入力・同じ辞書・同じ実行環境・同じ予算で比較する。変更点を1要因ずつ切り替える**ablation**を追加する。
3. 合否・許容悪化幅（特に日本語誤変換、文頭/末尾の性能、IME遅延）、反証条件、実行規模、seed、統計手法を**事前に記録**する。
4. 環境（OS/CPU/メモリ/SDK/.NET/Python/辞書版）、Git remote、比較対象SHA、ワークツリー状態を記録。ユーザー変更は破壊せず別worktreeを使う。
5. Pythonの研究方式とC#本体を区別する。Core実装にない特徴（例えばPython文字モデル）をC#改善と呼ばない。

### Stage 1：Gold・データの検証

- 正例の生入力を **`[start,end)` という半開区間・0始まり**で注釈し、英語 / 日本語 / コード / 記号を区別する。ASCII打鍵文字列ならPythonとC#のraw offsetを一致させる。非ASCIIを含む場合はUnicodeコードポイントと.NET UTF-16コードユニットを区別して明記・変換する。
- **言語切替境界**と**日本語内部の単語・助詞境界**は別のgold集合を持つ。言語切替のない英語のみ入力を「正しい境界0件」として正例評価から落とさず、**span完全一致**も測る。
- 例：`shigotodecustomerga` → 英語`customer`は `[9,17)`。日本語接頭を`sigotode`に変えると `[8,16)`。出力のかな文字数ではなく、**原打鍵文字列の長さ**で計算する。
- goldはパーツ連結から自動算出し、`raw[start:end] == expected_span`、重複・逆順・範囲外・境界重複・空span・未接続spanを検査する。人工コーパスの意図ラベルと**実際の入力意図**は別物。
- 既知の誤ラベル：`myunknownsite.comniarimasu` の英語終了位置は **17**（過去の20は誤り）。正解を直すなら過去の結果を上書きせず、新gold版で再計測。
- 曖昧な入力（`tokyo`, `ime`, `japanese`, `node`等）は入力者の意図を明示するか、**許容解釈の集合**を事前登録する。「正解が一意でない」を誤認識扱いしない。Caseと意図・大小文字を保持する。
- 表記揺れペア（`shi/si`, `chi/ti`, `tsu/tu`, `fu/hu`, `sha/sya`など）は同一の**意図・出力**を期待するが、入力側goldオフセットが異なる。未対応の`ji/zi`、`n/nn`、促音・長音・拗音・未確定入力は別の負例/保留ケースも作る。
- train/dev/test分割は**語種、文章テンプレート、話題、可能なら入力者・時期単位**で行う。テスト入力の語、文脈、誤り例を閾値調整へ戻さない。既存の54英語テスト語を何回並べ替えても独立した語種は54。
- Gold種別 `synthetic`、`human_verified`、`observational_unverified` を区別。人手確認済み例は匿名化・プライバシー・利用権限を確認する。

### Stage 2：テストケース網羅（H7の13層＋必須追加）

毎回、まずH7の13分類を固定セットで実施する。**カテゴリ別の分母と失敗例を必ず報告**する（総合平均だけで判断しない）。

| ID | 入力の種類 | 必須検証 |
| --- | --- | --- |
| S01 | 英語が文頭 | 後続の日本語へ切替。前側が空でも不当減点しない |
| S02 | 英語が文中 | 左右の日本語との境界を正確に決定 |
| S03 | 英語が文末 | 後側が空でも不当減点しない |
| S04 | 英語のみ | literal保持、全体span一致。言語切替F1だけでは測れない |
| S05 | 日本語–英語–日本語–英語など英語2島以上 | **全span / 全経路**を評価。単一英語spanモデルでは不可能な条件と区別 |
| S06 | 英語が隣接（2単語間に日本語なし） | 語彙境界は言語切替境界ではない。区間結合/分割の妥当性 |
| S07 | ローマ字別表記（同じかな） | ペア双方成功率、raw-offset変化、英語綴り不変 |
| S08 | 短い英語・略語（IME/API/UI/CPU/JS等） | 7文字以上に限定したPython候補生成器の構造的制約を明記 |
| S09 | 記号・URL・メール・コード | `node.js`, `myunknownsite.com`、`/`, `@`, `-`等。記号の区切りとliteral保存 |
| S10 | 大文字・混在Case | `IME`/`ime`, `CUSTOMER`, CamelCase。Normalizeの情報損失を調査 |
| S11 | 日本語のみの連結入力 | 誤英語候補、誤切替、長い日本語ローマ字への過剰適合 |
| S12 | 意図的な無意味・OOV文字列 | 候補なし時の保留、Unknown扱い、過剰日本語化 |
| S13 | 日本語音節の途中に英語が入る不自然ケース | 理論上の候補連結性と無理な解釈の抑制 |

さらに**追加必須**：日本語単独/英語単独の自然文章、固有名詞、英和同形語、同じ文字列に異なる意図を与えたケース、数字やEmoji/非ASCII、N/NN・促音・長音・拗音、多連続記号、空入力・極短入力、最大長近辺・上限超過、連打・削除・途中カーソル編集。自然文と人工的な破壊ケースは異なるラベルで集計する。

網羅は数学的な「全入力列挙」ではない。**カテゴリをカバーする系統的列挙、特徴の組み合わせ（少なくともpairwise）、固定回帰セット、乱数プロパティ、自然文holdout**の組み合わせで保証範囲を示す。

### Stage 3：正確性の測定（最低限セット）

**共通の結果テーブル**はモデルごと×カテゴリごと×seedごと×入力形式ごとに次を保存する。

| 指標 | 定義・注意 |
| --- | --- |
| **Candidate Recall / Gold Span@K** | 生成した全候補、枝刈り後TopK、最終選択前の各段階で `gold[start,end)` が残った割合。単一区間ではなく**全英語span**を分母にする |
| **Path existence / connectivity** | Goldに一致する候補群が**文頭から文末まで接続した経路**を作れるか。辺が単独であるだけでは不十分 |
| **Rank1完全一致 / global exact** | 最優先経路のラベル付き全spanがGoldと完全一致する割合。複数英語島の全体正解を別に測定 |
| **言語切替境界 P/R/F1（micro＋macro）** | 日本語→英語/英語→日本語など**ラベルが変わる内部位置**でTP/FP/FNを数える。文頭0・末尾lenは切替位置ではない。記号・Unknownを含む場合は評価ポリシーを固定する |
| **日本語内部の語彙・助詞境界** | 言語切替とは別のGoldとF1を使う。現在のH1〜H7結果で「未評価」なら未評価と書く |
| **開始・終了位置誤差** | Goldとのraw offset距離、MAE、P95/最大ずれ、±1文字以内の割合。候補無し・スパン数不一致も別列で損失を計上し、MAEだけで隠さない |
| **False English Candidate Cases** | 日本語だけを意図した**入力ケース**のうち誤英語候補が1件以上ある割合。同時に誤候補span数/入力も記録。**実変換の誤り率とは別** |
| **別表記の頑健性** | aliasペアの両方が正解、片方だけ成功、双方失敗、意図された出力の一致、raw offsetの一致検証（文字数差を反映） |
| **最終出力 Exact / CER** | C#実デコーダが返す出力とgold出力の比較。曖昧入力は登録済み許容解釈の中で評価。Python候補結果に代用しない |
| **不当確定・編集再現・遅延** | 逐次入力の確定済み領域の破壊、プレビュー揺れ回数、訂正数、再入力回数、各打鍵のp50/p95/p99、割当・CPU・メモリ |

境界F1は `P=TP/(TP+FP), R=TP/(TP+FN), F1=2PR/(P+R)`。境界のないカテゴリは**N/Aを許容**し、spanやliteralの正しさで別途評価する。分母0、予測0、Gold0の場合の扱いは事前に定義し、恣意的に100%または0%へ埋めない。微視/巨視平均、非独立ケースの重みを必ず開示する。

評価単位は以下を別々にカウント：
**モデル実行回数 / 異なるraw入力数 / 英語・日本語の異なる語種数 / 異なる自然文数 / 人手でgold確認済み件数 / グループ単位の独立サンプル数**。

### Stage 4：原因帰属とアブレーション

誤りを、**(1)生の候補生成 → (2)直前/直後の接続可能性 → (3)上限・枝刈り → (4)スコア順位 → (5)確定・セッション → (6)IME統合**に分類する。

Gold辺がない場合、スコア調整だけでは直らない。辺はあるが前後へ繋がらない場合は接続境界、接続経路はあるが落ちる場合は枝刈り、残っているのに順位が違う場合はスコアが疑われる。現行の公開診断が生成直後の全辺を出力しない場合、「候補生成の欠落」と「観測前に枝刈りされた」を断定的に区別しない。

**アブレーションの例：** 文字のみ／＋音韻／＋境界、閾値固定／再校正、候補K=1/2/4/無制限、beam固定／変更、文頭/中/末ごとの減点有無、Romanization table有無。入力・辞書・計算資源・seedは揃える。

毎回、改善ケース上位だけでなく**最悪ケース／±1文字の悪化／日本語誤候補／文頭・末尾の性能低下**も優先して報告する。

### Stage 5：逐次入力とIME互換性

最終文字列だけでなく、各打鍵の`raw`、候補、暫定表示、確定済み長さ、変更幅、入力意図情報をトレースし、打鍵→Backspace→修正→再入力を実行する。例：`japan` → `japanese` → `japaneseno` → Backspace → 再入力。未確定のプレビュー変更と、誤った不可逆確定は別の失敗種類とする。

実Windows検証ではTSF/Mozc bridgeを必要に応じ明示有効化し、**権限・ビルド・起動・入力・選択・確定・再変換・フォーカス移動**を分ける。アプリ互換性（WinForms、ブラウザ、チャット、Unity等）は試したアプリ・バージョン・結果を個別に記載。Core/Mozc契約テスト成功をTSF入力成功と呼ばない。

### Stage 6：性能・信頼性・再現性

- 軽量チェック：単体/既存回帰/構文/Gold構築テスト。コード修正ごと。
- 通常の研究チェック：固定holdout × 位置別13分類 × 5seed × ablation。新しい試験方式も従来方式と並列比較。
- 大規模チェック：H7型数千例/seed、語種・文脈・入力者グループholdout、Gold座標の10万件級ランダムプロパティチェック、長文・大量削除/編集。**Fuzz成功は精度ではない**。
- 性能計測：入力長別（例20/40/80/160/320）、一括入力と**毎打鍵**を分離。ウォームアップ・繰返し・OS/CPU/GC条件を揃え、p50/p95/p99・最悪値・CPU/alloc/peak memory・展開辺数を保存する。単発57文字比較や3反復中央値を実IMEレイテンシと呼ばない。
- 再現：同じ環境で2回以上実施、入力JSONと結果JSONを保存してSHA-256照合。辞書は版をpinする（例：過去のCodexは`cmudict==1.1.1`、H4〜H7は`1.1.3`）。ハッシュは**どのファイルに対する値か**を明記。
- 統計：seed間のばらつきを出す。英語54語×5seedを270個の独立した語と扱わない。必要に応じ語種・入力者・テンプレートでクラスタ化した信頼区間とペア差を示し、合成データから自然入力への一般化を主張しない。

## 3. 標準のテスト実行コマンド

コマンドは**リポジトリの該当ブランチにソースが存在することを確認してから**使う。単体テストが成功しても、評価基盤の対象が古い方式でないか記録する。

### C# Core / Windows UI（.NET SDKがある環境）

```powershell
dotnet --info
dotnet build src/BoundaryLab.Core/BoundaryLab.Core.csproj -c Release
dotnet run --project tests/BoundaryLab.Core.SelfTests/BoundaryLab.Core.SelfTests.csproj -c Release
dotnet run --project tests/BoundaryLab.Core.LatticeTests/BoundaryLab.Core.LatticeTests.csproj -c Release
dotnet run --project tests/BoundaryLab.Core.MozcTests/BoundaryLab.Core.MozcTests.csproj -c Release
dotnet run --project tests/BoundaryLab.Core.StreamTests/BoundaryLab.Core.StreamTests.csproj -c Release
dotnet run --project tests/BoundaryLab.Core.StressTests/BoundaryLab.Core.StressTests.csproj -c Release
dotnet build src/BoundaryLab.WinForms/BoundaryLab.WinForms.csproj -c Release
```

**備考**：各プロジェクトが対象SHAに存在するか確認する。WinFormsのビルドはWindows環境で行う。既存GitHub Actionsのチェックと、手動でしか行えないIMEの実入力は区別する。表示された件数を転記し、過去の16,164件を今回の実行件数として流用しない。

### Python研究系（H1〜H7の適切な研究ブランチ）

```bash
python -m pip install cmudict==1.1.3
python -m unittest discover -s research -p 'test_*.py'
python research/h4_error_budget_selftest.py
python research/h5_phonotactic_context_selftest.py
python research/h6_boundary_alias_selftest.py
python research/h7_mixed_stream_selftest.py
python research/h7_mixed_stream_stress.py --root . --output-dir h7-results --seeds 20261010 20261011 20261012 20261013 20261014 --structural-fuzz 100000
```

**備考**：H4/H5/H6/H7のファイルは、執筆時点では研究ブランチ依存。上記を「すべてmainでそのまま実行可能」とは解釈しない。H1追試は`python research/oov_character_model_experiment.py --root . --seed 20261010 --output h1.json`。Pythonの単体テストの命名規則が`*_selftest.py`のとき、`test_*.py`だけのdiscoverには含まれないため**明示実行**する。

実験のrun manifestと標準レポートには**コマンド・exit code・stdout/stderr保存先・入力コーパス版・対象コミット**を残す。実行に失敗したら環境要因／コード回帰／評価データ不備を分ける。

## 4. 今後の報告書のフォーマットと判断基準

毎回、[レポート雛形](../research/templates/evaluation-report-template.md)から新しい`research/YYYY-MM-DD-<hypothesis>-evaluation.md`を作る。最低限の出力成果物：

1. 研究レポート（日本語Markdown）：仮説/反証条件・環境とSHA・実行したテストと未実行・層A〜Dの区別・カテゴリ別の全結果・失敗例・悪化指標・次の実験。
2. 機械可読`manifest.json`：dataset hash、git SHA、依存版、seed、閾値、予算、K、beam、実行コマンド、リソース・環境を記録。
3. `cases.jsonl`：case ID、raw、分割グループ、intended language spans、gold/許容解釈、ラベルの出典。秘密・個人データは含めない。
4. `results.jsonl`：case ID、モデル/コミット、選択経路、候補TopK、Gold spanの段階別生存、編集/確定イベント、latency、failed stage。長いログは別ファイル。
5. サマリーJSON／CSV：**seed別・カテゴリ別・自然文/合成別**の分子分母、平均とばらつき、回帰項目。再実行時のハッシュ。

**研究段階の成功**は「スクリプトが通った」こととは異なる。合意した主要指標と**安全側の副指標**が独立テストでも達成された場合にのみ「改善」とする。例えばH6/H7は文中改善があるが文頭/末尾に強い回帰があり、**本体への導入を推奨できない**。問題が残れば「部分的支持」「未確認」「棄却」とし、数値を残したまま次の仮説へ進む。

結果公開の運用：研究用ブランチで実験→再現性の確認→PRにレポートとコード・失敗例→レビュー→READMEの研究履歴に1行追記。ブランチ同士の依存とPR順序を記載し、研究プロトタイプのコードを自動的に製品へ混ぜない。

## 5. 実験ケースの最低限の注釈例

次は **書式例** であり、人手確認済み自然文コーパスではない。

```json
{
  "case_id": "alias-shi-si-001",
  "gold_kind": "synthetic",
  "raw": "shigotodecustomerga",
  "intended_spans": [
    {"start": 0, "end": 9, "language": "Japanese"},
    {"start": 9, "end": 17, "language": "English"},
    {"start": 17, "end": 19, "language": "Japanese"}
  ],
  "romanization_pair_id": "work-customer-ga",
  "source": "generated",
  "acceptable_outputs": ["しごとでcustomerが"],
  "split_group": "heldout-template-A"
}
```

別表記`sigotodecustomerga`は同じペアID・意図のまま英語spanだけ`[8,16)`へ変更する。厳密な期待出力は採用する変換モード（ひらがな・漢字・literal優先）に応じて事前定義する。自動生成の`acceptable_outputs`はテスト仕様であり、意味上の一意の正解を主張しない。

## 6. 現時点で未解決・次回の優先事項

1. **C# v1.3の生ラティス内部計測**：枝刈り前の全候補、接続、beam生存、Rank1選択をGoldと対応づける。H1〜H7 Python実験の利得がC#でも得られるかは未確認。
2. **言語境界＋日本語内部境界の二階層gold**：既存のH6/H7は主として言語切替のみ。日本語の単語・助詞区切りは人手確認Goldが不足。
3. **配置非依存・複数英語島の経路探索**：H6の文頭/末尾での強い回帰、H7の片側補正の不足を解消する。局所の英語span一つだけでは多区間を表現できない。
4. **本当のIME検証とユーザー意図**：`IME`のCase保持、`ime`/`tokyo`の意図の曖昧性、`ji`などローマ字対応の拡張、Mozc/TSF、逐次打鍵・Backspace・実アプリ別動作。
5. **外部自然文holdoutと利用者入力**：合成ケースの件数を増やすだけでは十分でない。日本語・英語の専門語、固有名詞、記号、自然な入力頻度を別の語種・文脈・入力者で人手注釈する。

**この検証標準の変更自体も、版番号・理由・影響する指標と過去結果との互換性を記録してPRレビューする。**

# IncrementalBoundaryLab — 総合テスト・研究結果の再現性検証

検証日: 2026-10-10（JST）

主対象: `experiment/stream-hybrid-v1.3-latin-morphology` / `ae5d027f6651b118f76e31d97212a9b2973185c9`

比較対象: `experiment/stream-hybrid-v1.2` / `fc5e7a2`、研究スクリプトは `main` / `8d4d91e`

## 1. エグゼクティブサマリー

- 公開 `main` の OOV 候補実験を `cmudict==1.1.1` で再実行し、seed `20261010` の **固定語尾5/54、文字モデル32/54、併用34/54** を再現した。ただしこれは*候補検出*であり、C# の最終変換精度ではない。5 seed では文字モデルが32〜52/54、日本語のみの混在34件への誤候補が0〜13件と変動した。
- 既存の C# 5実行スイート、Python 5単体テスト、Core/WinForms ビルドは成功。StressTests は16,164ケースを実行した。Claude が報告した **263ケースの評価ハーネスは公開参照にもローカル Git オブジェクトにも見つからず、未検証**。
- v1.3 は v1.2 より `japanese` を含む3入力で改善した。一方、指定された未知語 `customer`・`monitor`・`strategy` に全体を覆う英語候補がなく、`myunknownsite.comniarimasu` では構造的な英語範囲を途中からしか捉えなかった。
- 最も有効な設計は、英語・日本語・コードの候補を共通座標のラティスに置き、明示的区切りまで不可逆確定を遅らせる点。最大の限界は、候補生成が未知語一般に届かないことと、未確定全体の毎打鍵再デコードである。
- H1 は**限定的に支持**。H2・H3 は改善の因果実験が不足し**判断保留**。本報告は実 IME、実 Mozc、TSF、盲検の人手正解コーパスでの優位性を主張しない。

## 2. 検証環境と対象の確認

| 項目 | 記録 |
|---|---|
| OS | Windows NT 10.0.26300.0、x64。CIM から詳細表示名・CPU は権限不足で取得不可 |
| .NET | SDK 10.0.203、対象 `net8.0`、.NET 8 runtime 8.0.31 を含む環境 |
| Python | 3.14.0 |
| 辞書 | PyPI `cmudict` 1.1.1、`cmudict.words()` 135,166件。`cmudict.dict` SHA-256 `81917843c7f44ce2b094ac63873c2c7a4cf802040792c455ba3ca406891c3d22`。配布元: [cmudict 1.1.1](https://pypi.org/project/cmudict/1.1.1/) |
| 開始時の Git | v1.3 ブランチ、上記 HEAD、作業ツリーはクリーン |
| スクリプト | `main:research/oov_character_model_experiment.py` の blob `e6a4c2108b8f2e4b6ce15829620adcf9319d6275` を無変更で [検証用コピー](validation-logs/oov_character_model_experiment.py) に保存 |
| Lexicon | `main` と対象 HEAD の `Lexicon.cs` blob は同一 `03b89b68275f18c55c48523710402f1ae2337c16` |

README の研究履歴には v0.5ラティス、v0.6責務ルーティング、v0.7/0.8音韻、Stream Hybrid v1〜v1.3 がある。ただし README 冒頭の「新実験」は v1.2 までで、現在の HEAD は v1.3。`research/README.md` は既存の人手確認済み gold と観測記録が不足し、CER 改善を主張しないよう明記する。`research/pilot-corpus.jsonl` は未確認の暫定ラベルである。CI は [build.yml](../.github/workflows/build.yml) に Core、Self、Lattice、Mozc contract、Stream、Stress、WinForms、Python evaluator を定義する。

v1.2 はアーカイブを一時展開して同一診断コードで比較した。元の未コミット変更はなく、元コードにアルゴリズム変更は加えていない。

## 3. テスト実行一覧

| コマンド・対象 | 結果 | 件数・証拠 |
|---|---|---|
| `dotnet build src/BoundaryLab.Core/BoundaryLab.Core.csproj -c Release --no-restore` | 成功 | 警告0、エラー0。[ログ](validation-logs/BoundaryLab.Core.log) |
| `dotnet run --project tests/BoundaryLab.Core.SelfTests/BoundaryLab.Core.SelfTests.csproj -c Release` | 成功 | コンソール自己検査1スイート。個別 assertion 総数は出力されない。[ログ](validation-logs/BoundaryLab.Core.SelfTests.log) |
| 同 `LatticeTests` | 成功 | 1スイート、個別件数は出力されない。[ログ](validation-logs/BoundaryLab.Core.LatticeTests.log) |
| 同 `MozcTests` | 成功 | 1スイート、契約テスト。実 Mozc サーバの品質検証ではない。[ログ](validation-logs/BoundaryLab.Core.MozcTests.log) |
| 同 `StreamTests` | 成功 | 1スイート。v1.3回帰、逐次更新、明示区切り、270文字、57文字の比較を含む。[ログ](validation-logs/BoundaryLab.Core.StreamTests.log) |
| 同 `StressTests` | 成功 | **16,164ケース、379,378入力文字、決定性再試行500回**。主に旧 RCR の構造不変条件。[ログ](validation-logs/BoundaryLab.Core.StressTests.log) |
| `dotnet build src/BoundaryLab.WinForms/BoundaryLab.WinForms.csproj -c Release --no-restore` | 成功 | 警告0、エラー0。Windows GUI の起動・操作までは未実施。[ログ](validation-logs/BoundaryLab.WinForms.log) |
| `python -m unittest discover -s research -p 'test_*.py'` | 成功 | **5テスト**。[ログ](validation-logs/python-unittest.log) |
| `python research/validation-logs/oov_character_model_experiment.py --root . --seed SEED --output ...` | 成功 | 5 seed × 各54英語正例・34日本語単語負例・54混在正例・34混在負例。詳細は4・5節と [JSON](validation-logs/oov-seed-20261010.json) |
| `dotnet run --project research/validation_probe/BoundaryLab.ValidationProbe.csproj -c Release -- ...` | 成功 | v1.3とv1.2で**各17入力**、編集系列6状態、逐次 prefix 3系列、長さ別各3反復。[v1.3](validation-logs/probe.json)、[v1.2](validation-logs/probe-v12.json)、[コード](validation_probe/Program.cs) |
| Claude報告の263ケース評価ハーネス | **未実行・未検証** | `tests/BoundaryLab.Core.EvalHarness` と関連文書は公開 `main`/実験ブランチに存在せず、報告上の `23e1f6b` もローカル Git で解決不能。基準値は [過去の研究記事](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/research/2026-10-10-oov-character-candidates.md) の引用のみ |
| 実 Mozc ネイティブ bridge / TSF 全アプリ入力 | 未実行 | bridge セットアップ・実サービス・ホストアプリ観測がない。Windows 環境だが Core の offline 契約テストと分離した |

新規診断の最初の restore は NuGet 脆弱性データへの接続に失敗し `NU1900` が出たが、ビルド・実行の終了コードは0。[診断ログ](validation-logs/probe-run.log)。既存スイートの成功とは分けて記録した。
公開用ログの機械固有の絶対パスは `<workspace>` に置換した。StreamTests ログ1行の末尾空白は可視記号 `␠` に置換した。結果・件数・警告本文は変更していない。

## 4. 再現性の評価・データ漏洩監査

元スクリプトは日本語辞書85語を seed で55学習・15開発・15テストへ分割し、辞書外の日本語音韻19語を追加する。英語辞書はフィルタ後116,732学習・450開発。評価用英語54語は公開 `main` にある固定スナップショットで、元の未公開ハーネスとの同一性は現物がないため監査不能。英語54語、日本語音韻19語、Lexicon の日本語85語は英語学習・開発集合から単語単位で除外され、5 seed とも交差0件。日本語学習・開発・テスト間の交差も0件。[監査コード](validation-logs/check_oov_leakage.py) / [結果](validation-logs/leakage.log)。54語は現在の C# 英語辞書との完全一致も0件。**単語レベルの漏洩なし**は確認したが、スナップショット選定過程の先見や文字部分列の共有まで排除した意味ではない。

| seed | 固定語尾 /54 | 文字モデル /54 | 併用 /54 | 日本語単語の文字モデル誤検出 /34 | 混在中の正しい英語範囲 /54 | 日本語のみ混在で何らかの英語誤候補 /34 | 閾値 |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 20261010 | 5 | **32** | **34** | 0 | 30 | 0 | 0.96139 |
| 20261011 | 5 | 52 | 52 | 0 | 47 | 4 | 0.47539 |
| 20261012 | 5 | 40 | 41 | 1 | 38 | 3 | 0.76954 |
| 20261013 | 5 | 52 | 52 | 2 | 47 | **13** | 0.13998 |
| 20261014 | 5 | 39 | 40 | 1 | 37 | 1 | 0.77546 |

元報告の 5/54・32/54・34/54 は、今回の pinned 辞書と seed で**数値として再現**した。保存コピーを原本 Git blob とバイト単位で一致させた後に seed 20261010 を再実行し、先の JSON と完全一致した。seed 変更の結果も過去の記事の記載と一致する。閾値は15日本語開発語で偽陽性0を許す条件から選ぶため、分割に敏感。各 seed は同じ54英語評価語であり、5回を270個の独立事例とは数えない。Claudeの263ケースの「全体完全一致56.7%」「未知英語含有126件の完全一致10.3%」は**引用であって今回の測定値ではない**。

## 5. 定量的な性能評価

seed 20261010 の隔離された単語判定では、固定語尾5/54 = 9.3%（Wilson 95%: 4.0–19.9%）、文字モデル32/54 = 59.3%（46.0–71.3%）、併用34/54 = 63.0%（49.6–74.6%）。文字モデルと固定語尾の対比較は、文字モデルのみ29語・固定語尾のみ2語、exact McNemar 二側 `p=4.63×10^-7`（スクリプト出力）。ただし事前に無作為抽出した母集団ではないため、p値も区間も一般の IME 入力へ外挿できない。

同じ seed の日本語単語誤候補は3方式とも0/34だが、0/34 の Wilson 95%上限は**10.2%**。混在54件では正しい範囲の候補が固定語尾5/54、文字モデル30/54、併用32/54。正例54件で提案された範囲総数は順に53、166、207で、候補増加には探索負荷と誤選択の余地が伴う。日本語だけを連結した100例では既定 seed の誤候補0、seed 20261013 は1/100。別の日本語のみ混在34例では同 seed が13/34。いずれも合成入力で、人手ラベル付き文章の誤変換率ではない。

新規 C# 診断は、指定された15小例のうち、明示した文字列に完全一致が9/15、`tokyo`・`ime` の日本語読みを許容すると11/15。これは探索的・非盲検の例であり、推定精度として使わない。混在7例の**言語遷移境界**は6例で正確。結合した各部の長さから gold 位置を計算した。全7例の gold 境界8個は全て現れたが、`myunknownsite.comniarimasu` に余分な位置3の切替が1個あり、micro P=8/9、R=8/8、F1=16/17=0.941。これは正しい*語全体の候補*があることを意味しない。`githubdeissue` の `git|hub` のような同一言語内の分割は、この境界指標に含めない。[全ケース JSON](validation-logs/probe.json)。

処理負荷の小規模試験: 未区切り `a` の長さ40/80/160を新規 session に一括 `Update`、ウォームアップ後各3回。中央値は **9.66/44.96/136.66 ms**、当該スレッドの累積割当は約 **6.67/34.31/155.90 MB**、累積展開辺は **56/176/616**。時間は3回だけでばらつきが大きく（160字 92.64–217.92 ms）、入力応答の p95 やピーク常駐メモリではない。新規診断と旧 v1.2 の同条件の割当・展開はほぼ同じで、この入力では形態語尾追加の負荷を評価できない。[v1.2ログ](validation-logs/probe-v12.json)。`StreamTests` の57字比較は旧 v0.8 が21,054展開・95.0ms、v1.3 が713展開・5.2msだが、単発の内部 work proxy であり、実 IME の高速化率ではない。

## 6. 失敗例と発生段階

| 入力 | 期待する解釈 / gold境界 | v1.3 実測 | 根拠と発生段階 |
|---|---|---|---|
| `customer` | `customer` | `cusとめr` | 英語全体の辺0件、最終上位4経路にもなし。英語候補生成が先に失敗。`cus` と日本語 `tome` が接続された。 |
| `monitor` | `monitor` | `もにとr` | 英語全体の辺0件。音韻候補が内部に入り、末尾 `r` は Unknown。候補生成の限界。 |
| `strategy` | `strategy` | `stらてgy` | 英語全体の辺0件。短い Latin と日本語 `rate` の混在。候補生成の限界。 |
| `myunknownsite.comniarimasu` | `myunknownsite.com|niarimasu`、位置17、`myunknownsite.comにあります` | `みゅnknownsite.comにあります` | 完全な英語構造辺0件。選択は `myu|nk|nownsite.com|niarimasu`、位置3に偽の言語切替。`AddStructuralCodes` は未登録 stem の長さ>8を拒否する [コード](../src/BoundaryLab.Core/StreamHybridDecoder.cs#L525)。候補生成。 |
| `tokyo` / `ime` | literal と日本語読みの両方を許容 | `ときょ` / `いめ` | 日本語側は妥当だが literal を望む意図は観測不能。英語全体の候補0件。曖昧性と候補不足を分ける。 |
| `CUSTOMER` / `IME` | 大文字を保持した literal | `cusとめr` / `いめ` | `InputSyntax.Normalize` で大文字情報を先に破棄。[コード](../src/BoundaryLab.Core/InputSyntax.cs#L31)。入力情報の欠落。 |
| `githubdeissue` / `networkmiru` | `github|de|issue` / `network|miru` | 出力は期待通り | 完全な英語辺は存在し上位経路にも残るが、最高得点は `git|hub` / `net|work`。候補選択は語単位 gold と異なるが、表示と**言語遷移境界は正しい**。スコア課題の兆候であり、変換失敗とは数えない。 |

`japanese`、`japaneseno`、`orehajapaneseno` は v1.2 で英語全体の辺がなく、それぞれ `じゃぱねせ`、`じゃぱねせの`、`おれはじゃぱねせの`。v1.3 では英語辺ができ、期待出力に変わった。[対照ログ](validation-logs/probe-v12.json)。生成・日本語辺との接続・得点が同時に変更された比較なので、どれか一つだけを因果要因と断定しない。`japaneseno` の最終上位2経路は `japanese|no`（10.59点）と日本語全体（8.50点）で、代替候補が残ることを確認した。

現行17例で「必要な辺はあるが接続不能」と確定できる例はない。過去の `node.js` の接続問題は [v1.2研究記録](stream-hybrid-v1.2-code-boundaries.md) と StreamTests の回帰で扱われている。現行コードには始点あたり12辺、位置あたり beam 6、診断出力80辺の上限がある。[枝刈りコード](../src/BoundaryLab.Core/StreamHybridDecoder.cs#L98)。ただし今回の誤出力4例は診断辺が10〜40件で、完全な必要辺自体が見つからない。枝刈りとスコアリングに失敗原因を転嫁しない。内部生成直後の全候補は公開 API に出ず、12辺の選別前にあったかどうかを厳密に区別するには計測フックが要る。

逐次入力では `japan`→`japanese`→`japaneseno` が `じゃぱん`→`japanese`→`japaneseの` と表示変動したが、明示区切り前の確定長は全て0。削除して `japanesen` に戻すと再構築され `じゃぱねせん`、再入力で `japaneseの` に戻った。末尾`,`で初めて確定長11。*不当な不可逆確定*はこの系列では観測されず、プレビューの揺れは残る。[編集記録](validation-logs/probe.json)。

## 7. アルゴリズムの優れている点

1. **日本語音韻を候補化**: `PhoneticProjector.Project` と `AddKanaRuns` がローマ字からかなへの連続範囲を作る。[生成順](../src/BoundaryLab.Core/StreamHybridDecoder.cs#L60)。未知英語の未解決子音を別の英語島候補にも使うため、`meltypega` を `meltype|ga` と解ける。
2. **共通座標の候補ラティス**: 辞書、コード、形態語尾、未知 Latin、かなを同じ `Start/End` に積み、接続可能な候補始点をかなの切れ目へ伝える。[`latinStarts` と探索](../src/BoundaryLab.Core/StreamHybridDecoder.cs#L74)。v1.3 の `orehajapaneseno` は候補が繋がった実例。
3. **生成と順位付けの分離**: 生成された辺を `LocalScore`・切断費用・言語連続ボーナスで比較する。候補存在と最終選択を診断上分けられる。[スコア式](../src/BoundaryLab.Core/StreamHybridDecoder.cs#L119)。ただし文字統計モデルは**現行 StreamHybridDecoder で呼ばれない**。同モデルは旧 `EvidenceLatticeDecoder` で使用される。Python H1 の利点を C# v1.3 の性能として扱わない。
4. **遅延確定**: 現行 `StreamHybridSession` は `,;!?`・空白等の明示 hard boundary でだけ確定し、`.`・`-` は文脈記号として保留する。[確定条件](../src/BoundaryLab.Core/StreamHybridSession.cs#L76)。短い系列の削除・再入力は再構築された。

## 8. 課題と技術的限界

**構造上:** 同一の `tokyo`・`ime` 打鍵列は意図情報なしに一意に決められない。候補生成が英語全体を作らない場合、後段のビーム・得点を改善しても救えない。確信度・ユーザー意図は現行では独立状態として管理されず、四状態音韻分析は表示用診断である。`Process` は未確定 suffix 全体を毎打鍵 `Decode` し、長い未区切り入力の累積仕事と割当が増える。[再デコード](../src/BoundaryLab.Core/StreamHybridSession.cs#L70)。一方、256フレームの保存上限と80診断辺上限はトレース爆発を抑えるが、完全な候補監査を難しくする。

**実装上:** 固定8語尾・7〜24字は `customer` 等を覆わず、コード stem の8字上限は `myunknownsite.com` を途中から認識する。大文字を `Normalize` が失う。`IsJapaneseParticle` の短い助詞は英語内部との競合を起こしうる。候補グラフの12辺制限と beam 6が真の経路を落とす可能性はあるが、今回の失敗例での実証はない。Mozc は既定で同期 probe が無効、明示的有効化時の実サービス遅延は未測定。GUI の WinForms ビルド成功は TSF IME 実装可能性・アプリ互換性の証明ではない。

## 9. H1・H2・H3 の判定

| 仮説 | 判定と支持範囲 | 反証可能な次の条件 |
|---|---|---|
| **H1** 文字統計で固定語尾より未知英語候補生成率を改善 | **限定的支持**。同じ54語で32/54対5/54、5 seed とも文字モデルが上回った。日本語誤候補は seed と入力構造に敏感。最終 IME 変換改善は未検証。 | 独立に凍結した未知英語・日本語混在の人手 gold で、同一の許容誤候補率（例、負例5%以下）に揃えたとき候補 recall 増分が0以下、または信頼区間が実用的な効果を否定する。 |
| **H2** 正解候補を探索中に保持すれば最終変換精度を改善しうる | **判断保留**。`japaneseno` は日本語代替を保ちながら正しい英語経路を選択、v1.2→v1.3 の3例改善あり。ただし生成・接続・得点が同時に変わり、保持だけの因果効果は測れていない。 | 同一候補集合・同一得点で保持幅のみを変える ablation を行い、正解経路の生存率が上がっても盲検の最終精度が上がらない、あるいは誤候補増加が利益を上回る。 |
| **H3** 確信度と入力意図を分離すると逐次入力の誤確定を減らせる | **判断保留**。現行は hard boundary 前の自動不可逆確定が0で、今回の6状態でも誤確定0。比較すべき確定イベントがない。一方プレビュー揺れと大文字消失は確認。 | 意図状態あり/なしで同一の逐次・削除・修正コーパスを比較し、誤確定・訂正回数・安定までの打鍵数が改善しない、または待ち時間が許容値を超える。 |

## 10. 新しい研究仮説

1. **誤候補予算付き H1:** 文字モデル候補を固定語尾候補へ足す際、開発データで日本語混在の誤候補率を上限管理すると、未知英語範囲 recall の利得を保ちつつ seed 依存を減らせる。凍結した日本語長文・未知英語の対照で候補率と最終 CER を別々に測る。
2. **接続可能性 H2:** 英語候補の開始点を前段かな候補へ伝えることは、beam 幅を広げることより正解経路生存率に効く。生成直後・12辺制限後・beam 後の各段階で同一 gold 経路の有無を計測し ablation する。
3. **意図保持 H3:** 原文大文字・明示 Latin lock・直近修正履歴を別状態として保持すると、`IME`/`ime` などの曖昧性で誤変換や訂正を減らせる。小文字日本語入力への悪影響、入力遅延、不可逆確定を合わせて評価する。

## 11. 今後の改善優先順位

1. **評価基盤:** 263件ハーネスの公開可能な原本、誤ったと報告された gold 境界（`myunknownsite.com` の長さ17と過去記録20）の修正履歴を確保し、人手確認済み・非学習の holdout を作る。旧数値は上書きしない。
2. **段階別診断:** 候補生成直後、12辺制限後、接続後、beam 後、最終選択を別カウンタで出し、失敗の帰属を測定可能にする。正しい候補範囲と最終出力を別列にする。
3. **候補モデルの比較:** H1 の文字統計を C# へ導入する前に、誤候補率を固定した offline ablation と辞書版固定・ライセンス確認を行う。`customer` 等を正例、長い日本語ローマ字を負例に含める。
4. **意図・性能:** 大文字保存、明示意図、訂正履歴を試し、差分再デコード・候補数制限をプロファイルする。実 Windows キー入力の p50/p95/p99・ピークメモリ・Mozc bridge timeout を別途測る。

## 12. 再現手順と実験の限界

```powershell
# このブランチの既存テスト
dotnet run --project tests/BoundaryLab.Core.StreamTests/BoundaryLab.Core.StreamTests.csproj -c Release
dotnet run --project tests/BoundaryLab.Core.StressTests/BoundaryLab.Core.StressTests.csproj -c Release
python -m unittest discover -s research -p 'test_*.py'

# main の原本スクリプトと同一の保存コピー。辞書を別ターゲットへ導入してから実行
python -m pip install cmudict==1.1.1 --target research/.validation_deps
$env:PYTHONPATH=(Resolve-Path research/.validation_deps).Path
python research/validation-logs/oov_character_model_experiment.py --root . --seed 20261010
python research/validation-logs/check_oov_leakage.py

# C# 小規模診断。gold 境界は Program.cs の Part.Raw の長さから計算
dotnet run --project research/validation_probe/BoundaryLab.ValidationProbe.csproj -c Release -- research/validation-logs/probe.json

# v1.2 を現在の作業ツリーを切り替えず比較する場合
git archive --format=zip --output=research/validation-logs/v12-source.zip experiment/stream-hybrid-v1.2
Expand-Archive research/validation-logs/v12-source.zip research/.validation_v12 -Force
New-Item -ItemType Directory -Force research/.validation_v12/research/validation_probe | Out-Null
Copy-Item research/validation_probe/*.cs* research/.validation_v12/research/validation_probe/
dotnet run --project research/.validation_v12/research/validation_probe/BoundaryLab.ValidationProbe.csproj -c Release -- research/validation-logs/probe-v12.json
```

実験の英語54語と日本語34語は少数かつ固定・合成的で、特に閾値選択に使う日本語15語が小さい。Wilson 区間はこの選定バイアスを補正しない。CMU辞書を別版に替えた結果、元ハーネスの263件、実 Mozc/TSF、実ユーザーの訂正行動、候補が12辺制限**前**に存在したか、実打鍵の遅延とピークメモリは今回の再現範囲外。報告した失敗段階は、公開された診断辺とコードから確定できる範囲に限定した。

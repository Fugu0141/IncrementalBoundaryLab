# 実機評価手順（Windows優先、Linux補助）

この文書は [research-evaluation-report-2026-10-09.md](research-evaluation-report-2026-10-09.md) に対応する実験者向け手順書。
**Windows 用 TSF DLL を既存 Mozc に上書きしないこと。** 現在は独立インストーラー / TSF 登録の安全性が未確認。

## 0. 今回お願いしたい成果物

最初の実験では以下だけで十分:

1. Windows の OS バージョン、.NET SDK バージョン、`git rev-parse HEAD` の値と CPU/RAM（秘密情報は不要）
2. GUI で `Mozc bridge: unavailable/offline` の状態から、指定入力 **最低10件**の研究 JSON を保存
3. 実 Mozc と互換のブリッジがある場合のみ、`connected` 条件で同じ入力を記録（無理に導入しない）
4. 各試行の「期待される表示」「実際の表示」「途中の異常（誤確定・操作不能・遅延など）」をメモ
5. **失敗ケースも消さずに**元の研究 JSON を残す

**重要:** `connected` は単にブリッジのプロセスを検出している可能性があり、
「実際の Mozc サーバーで適切な変換候補が返った」ことを証明しない。
研究 JSON の `mozcAvailable`、`mozcProbesThisStep`、`mozcTopCandidate` と
実際の出力を併せて確認する。接続成功だけで日本語変換の品質を保証しない。

## 1. 研究用 GUI の起動（安全に始められるルート）

Windows / PowerShell、Git、.NET 8 SDK:

```powershell
git clone -b experiment/mozc-responsibility-ime-v0.6 https://github.com/Fugu0141/IncrementalBoundaryLab.git
cd IncrementalBoundaryLab
git rev-parse HEAD
New-Item -ItemType Directory -Force research\exports | Out-Null
dotnet --version
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

同名フォルダーが既にあるなら clone の代わりにそのリポジトリで
`git fetch origin` → `git switch experiment/mozc-responsibility-ime-v0.6` を使用する。
手元に未コミット変更があれば先にバックアップし、上書きしない。

研究対象入力を **入力欄へ1文字ずつ**打ち、入力途中の表示を観察する。
1ケースごとに入力欄を空にして開始し、最後に
「**Mozc責務研究JSONを書き出す**」で `exports/<case_id>-<condition>.json` として保存。
ファイル名と対応条件を記録する。コピー＆ペーストでの実行は別条件として区別する。

研究用 GUI では入力を小文字化するため、大文字維持・シフトキーの実機試験には使えない。
また、これは正式な TSF IME ではない。

### Mozc 接続あり条件

互換性が取れている **テスト専用の Mozc サーバー環境**が準備できた場合のみ以下を使用。
上流 Mozc ソースの pin は `921b8cc99904c8d31b771e395513da0a5d55182a`。

```powershell
.\tools\setup_mozc_bridge.ps1 -UpdateDependencies
$env:BOUNDARYLAB_MOZC_BRIDGE = "$PWD\artifacts\mozc\boundary_mozc_bridge.exe"
dotnet run --project .\src\BoundaryLab.WinForms\BoundaryLab.WinForms.csproj -c Release
```

ネイティブビルドには Visual Studio C++/Python/Bazelisk 等が必要。
接続あり/なしは同じ作業ディレクトリの自動検出にも影響されるため、
単に環境変数を削除しても必ず offline になるとは限らない。
GUI表示と出力ログで条件を確認し、未確認なら条件を `unknown` と記録して比較から外す。

## 2. 最初に試すケース

試験入力は [pilot-corpus.jsonl](pilot-corpus.jsonl) に保存。
以下は例であり、期待出力は原則 **事前に人手確定した gold** と照合する。

| 種類 | raw（入力欄に打つ内容） | 主な観察ポイント |
| --- | --- | --- |
| 日本語 | `nihongo` | 単純な日本語変換 |
| 日本語 | `hennkannnikannsiteha` | 長いローマ字列 |
| 英語 | `commit` | 英語が勝手に日本語化されないか |
| 英語 | `theory` | 短い英語語彙の誤切断 |
| 識別子 | `node.js` | ピリオドを含む literal |
| 識別子 | `foo_bar` | 未知の構造的 literal |
| 連結 | `kyouhacommitsimasita` | 日本語→英語→日本語 |
| 連結 | `githubdeissue` | 英語→助詞→英語 |
| 記号 | `de-ta` | 長音とハイフンの競合 |
| 連結 | `networkmiru` | 英語から日本語への境界 |
| 曖昧 | `oreha` | 短い英単語との衝突 |
| 長文 | `commitsitade-tawogithubnipushsitekudasai` | 複数の境界とMozc |

さらに各ケースで、途中入力 `c` → `co` → `comm` → `commi` →
`commit` の段階で **取り消せない誤確定**が出ていないか記録する。

## 3. 操作と例外系（別枠の探索的実験）

研究 GUI で確認可能なこと:
- 末尾 Backspace で削除・再入力する
- 文字列の途中にカーソルを置いて編集する
- 一度入力を消して別ケースを実行する
- 長文入力でUIが停止するか、`offline/connected` が変化するか観察する

IME としての実機動作は **独立したインストール環境が整ってから** Windows VM 等で確認する:
- メモ帳 / ブラウザー / Unity / LINE 等で入力、カーソル移動、候補選択、Esc、Backspace、Enter
- 半角/全角、IME On/Off、英大文字、フォーカス切替、アプリ終了・再起動
- 同じ変換・編集動作を反復してクラッシュ、二重入力、文字消失、残留 preedit を検査する

この TSF 動作を GUI の出力だけから「成功」とは記録しない。

## 4. 1試行につき必ず残す情報

- `case_id`, `condition`, `artifact_path`（研究JSONへのパス）
- 実際の出力文字列・操作で得た最終出力
- 逐次入力/貼り付けの区別、最初からやり直したか
- 応答遅延、誤確定、文字欠落、候補異常、クラッシュ、タイムアウト
- Git SHA、Mozc サーバー版、辞書版、OS、入力方法、日時
- 試行を除外した場合の理由（都合の悪い結果を削除しない）

`pilot-corpus.jsonl` に入っている `draft_expected_output` は参考値であり
**人手で妥当性を確定する前に精度の「正解」として使わない**。
本評価前に、正式な gold と保留ケース、許容される表記差を決定する。

## 5. Ubuntu / Linux でできる事前確認

.NET 8 SDK があれば GUI なしの回帰テストを実行できる:

```bash
dotnet run --project tests/BoundaryLab.Core.MozcTests -c Release
dotnet run --project tests/BoundaryLab.Core.LatticeTests -c Release
dotnet run --project tests/BoundaryLab.Core.StressTests -c Release
```

Linux の標準テストは **ネイティブLinux版Mozc IMEの動作実験ではない**。
テストの pass 件数を正解率に換算しない。

## 6. データの提出とプライバシー

実験結果は、case ID 付き JSON と事前記録した gold を対応付けて共有する。
公開リポジトリには、実際のチャット文、個人名、APIキー、パスワード、
入力履歴を含むログをアップロードしない。
匿名化したテスト文章だけを使う。研究者は元の失敗例を保持してから修正する。

# ClaudeUsageBar

Claude のプラン使用量をタスクバー上に表示する Windows 常駐アプリ（完成済み）。使い方と仕組みは `README.md`、仕様は `PROMPT.md`。

## 決定事項
- 取得方法は非公式の使用量エンドポイントのみ（ステータスライン方式は不採用）
- 規約：OAuth トークンの利用は規約に抵触するおそれが高いと確認（2026-11-25、根拠は README）。方式は変えず、文書に明記する（同日ユーザー決定）
- .NET 8 WinForms、追加ライブラリなし、ランタイム別の単一 exe
- 設定の保存先は `%APPDATA%\ClaudeUsageBar\state.json`
- GitHub：mickey-miki/ClaudeUsageBar（**公開**）

## ビルド
- 起動中は上書きできないので、先にアプリを終了する：`dotnet publish src -c Release -o publish`

## 守ること
- トークンは使用量の取得以外に使わず、更新もしない。決まったエンドポイント以外に送らない
- Claude Code の settings.json を変えるときは、先に変更内容を見せて確認を取る
- 仕様を変えたら `README.md` と `PROMPT.md` も直す

## 未確認
- 5分ごとの更新の長時間動作、429、スリープ復帰、高DPI
- 失敗理由のツールチップ、ログオン時の自動起動
- PROMPT.md から別環境で作れるか

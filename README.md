# Proc

Windows のアクティブウィンドウを自動追跡し、作業時間を一覧・分析するデスクトップアプリ。

## 概要

フォアグラウンドウィンドウを 1 秒ごとにサンプリングし、1 分ごとに「その 1 分間で最も長く使っていたウィンドウ」のプロセス名とタイトルを日別 CSV に記録します。常時最前面の半透明オーバーレイで今日の使用時間を確認でき、分析画面で日・週・月単位の集計を見られます。

## スクリーンショット

<!-- スクリーンショットをここに追加 -->

## 機能

- **自動記録** — 1 秒ごとのサンプリングを 1 分単位に集約して記録（途中でアプリを切り替えても、長く使った方に計上）
- **アイドル検出** — 1 分以上操作がないと `Idle` として記録し、5 分以上ではそもそも記録しない。画面ロック中も記録しない
- **オーバーレイ** — 最前面・半透明・ドラッグで移動・タスクバー非表示。ダブルクリックでタイトル表示を切り替え
- **アイドル時の表示** — 設定で有効にすると、離席から戻ったときだけオーバーレイを自動で表示
- **分析画面**
  - 日 / 週 / 月の切り替え、カレンダーで日付ジャンプ、← → キーで前後移動
  - 日表示: タイムライン（ドラッグで時間帯を選択、時刻ラベルのクリックでその 1 時間を選択、Esc で解除）
  - 週・月表示: 日別の積み上げグラフ（クリックでその日を開く）
  - 表示単位の切り替え: **App**（アプリ別）/ **Category**（ユーザー定義カテゴリ別）/ **Site**（ブラウザ時間をサイト別）
  - アプリフィルター（非表示にしたアプリは保存される）
  - **Export** — 表示中の集計を CSV / Markdown で出力
- **カテゴリ** — 設定画面で 1 行に 1 ルールを書く: `Work = devenv, code, title:Jira`（プロセス名の完全一致、または `title:` でタイトルの部分一致。上の行が優先）
- **プライバシー** — 指定したアプリのウィンドウタイトルを記録しない／指定日数より古いログを自動削除
- **CSV インポート** — `Name, Start, End, Duration, Process` 形式の CSV を取り込み（すでに記録がある分は既存データを優先）
- **自動更新** — 起動時に GitHub Releases から新しいバージョンを取得（SHA256 で検証）。起動中は 12 時間ごとに確認し、更新するかを尋ねる
- **スタートアップ登録** — 「Run as administrator」と併用し、Proc を管理者として起動しているときは、タスクスケジューラで UAC プロンプトなしに起動するよう登録

## データの保存場所

| パス | 内容 |
|------|------|
| `%LOCALAPPDATA%\Proc\logs\YYYY-MM-DD.csv` | 日別ログ (`HH:mm:ss,プロセス名,タイトル[,idle]`) |
| `%LOCALAPPDATA%\Proc\settings.json` | 設定 |
| `%LOCALAPPDATA%\Proc\exepaths.txt` | アイコン取得用の exe パスキャッシュ |
| `%LOCALAPPDATA%\Proc\error.log` | エラーログ（1 MB でローテーション） |

## 動作要件

- Windows 10 / 11
- .NET 9

## ビルド・実行・テスト

```bash
dotnet build
dotnet run
dotnet test Proc.Tests
```

## 操作方法

| 操作 | 動作 |
|------|------|
| ドラッグ | ウィンドウ移動 |
| ダブルクリック | タイトル表示の切替 |
| 右クリック | コンテキストメニュー (Show Title / Analysis / Settings / Import CSV / Exit) |
| トレイアイコン左クリック | ウィンドウの表示/非表示切替 |
| トレイアイコン右クリック | メニュー (Show/Hide / Analysis / Settings / Import CSV / Exit) |

## プロジェクト構成

```
Proc/
├── App.xaml / .cs                 # エントリポイント (単一起動、管理者で再起動、更新チェック)
├── MainWindow.xaml / .cs          # オーバーレイ UI・トレイアイコン
├── AnalysisWindow.xaml / .cs      # 分析画面 (タイムライン・日別グラフの描画)
├── AnalysisViewModel.cs           # 分析画面のロジック (期間・範囲・フィルター・グループ化)
├── SettingsWindow.xaml / .cs      # 設定画面
├── ActivityLogger.cs              # ウィンドウのサンプリング・CSV への記録
├── ActivityRecord.cs              # レコードモデル・CSV シリアライズ
├── LogAnalyzer.cs                 # ログの読み込み・集計・タイムライン生成
├── CategoryRules.cs               # カテゴリルールの解析とマッチング
├── SiteExtractor.cs               # ブラウザのタイトルからサイト名を抽出
├── ReportExporter.cs              # CSV / Markdown エクスポート
├── ImportHelper.cs                # 外部 CSV のインポート
├── CsvUtil.cs                     # CSV のエスケープ・パース
├── AppSettings.cs                 # 設定の読み書き
├── StartupManager.cs              # スタートアップ登録 (Run キー / タスクスケジューラ)
├── AutoUpdater.cs                 # GitHub Releases からの自動更新
├── ErrorLog.cs                    # エラーログ
├── IconHelper.cs                  # プロセスアイコン取得
├── ColorTheme.cs                  # テーマカラー
└── Proc.Tests/                    # xUnit テスト
```

## 技術スタック

- **.NET 9** / **WPF**
- **Win32 P/Invoke** — `GetForegroundWindow`, `GetWindowText`, `GetWindowThreadProcessId`, `SetWinEventHook`, `GetLastInputInfo`
- **Hardcodet.NotifyIcon.Wpf** — システムトレイアイコン
- **xUnit** — テスト

## リリース

`v*` タグを push すると GitHub Actions が単一ファイルの `Proc.exe` と `Proc.exe.sha256` をビルドしてリリースに添付します。自動更新は `.sha256` が添付されていないリリースはインストールしません。

## ライセンス

<!-- ライセンスをここに記載 -->

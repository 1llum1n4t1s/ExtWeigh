# 変更履歴

ExtWeighの利用者に影響する変更を記録します。

## 1.0.4 - 2026-07-22

### 追加

- Cloudflare R2へ署名済み配布物を安全な順序で公開するローカルリリース経路
- `extweigh.nephilim.jp` でランディングページと配布物を共存させるCloudflare Worker設定
- Microsoft Store申請用の日本語・英語掲載文、申請チェックリスト、プライバシーポリシー
- CI、CodeQL、Dependabot、ランディングページ公開のGitHub Actions

### 変更

- ランディングページとREADMEの入手先をCloudflare配信URLへ統一
- Store申請用に、内容が変わらないバージョン付き署名済みSetup.exeをリリース成果物へ追加

### 修正

- Windowsシェル上でインストール版を一貫して識別できるようAppUserModelIDを設定

## 1.0.3 - 2026-07-20

### 追加

- アプリ、ウィンドウ、インストーラーで共通利用するExtWeighアイコン
- 署名済みSetup.exe、Portable ZIP、Velopack packageの実配布検証

## 1.0.2 - 2026-07-19

### 追加

- Certum SimplySignによるAuthenticode署名付きWindows配布
- Velopackの起動フックとローカルリリーススクリプト

## 1.0.1 - 2026-07-18

### 追加

- Chrome拡張機能の単体ON/OFF差分計測
- 複数拡張の全OFF・全ON・1つ抜き計測
- V8 CPU、Long Tasks、JavaScriptヒープ、hot functionsの解析
- アプリ内結果表示と自己完結HTMLレポート
- manifestからの代表URLシナリオ生成と計測手順UI
- Windowsデスクトップアプリとランディングページの初期実装

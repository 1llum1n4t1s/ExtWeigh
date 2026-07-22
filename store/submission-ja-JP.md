# ExtWeigh Microsoft Store申請情報（ja-JP）

## 基本情報

- 製品名: `ExtWeigh`
- 言語: `日本語 (ja-JP)`
- カテゴリ候補: `Developer tools` または `Utilities & tools`
- ライセンス: `MIT`
- 価格候補: `無料`

## 掲載文

- 短い説明と詳細説明: `store/listing-ja-JP.md`
- サポートURL: `https://github.com/1llum1n4t1s/ExtWeigh/issues`
- プライバシーポリシー: `https://extweigh.nephilim.jp/privacy`
- 適用ライセンス条項: `https://github.com/1llum1n4t1s/ExtWeigh/blob/main/LICENSE`
- 1:1 Store logo: `icon/app_icon.png`

## Package

- App type: `EXE`
- Architecture: `x64`
- Installer parameters: `--silent`
- Package URL: `https://extweigh.nephilim.jp/ExtWeigh-<VERSION>-win-x64-Setup.exe`
- Google Chrome: 外部依存（自動検出、Chrome for Testingのパス指定も可能）
- 非Microsoft製driver / NT service: `なし`
- Bundleware: `なし`

## データ利用

- 利用者アカウント: `なし`
- テレメトリ / 解析SDK: `なし`
- 広告: `なし`
- 個人情報の制作者への送信: `なし`
- 計測データ: `ローカル保存のみ`
- 配布サイト: Cloudflareインフラの標準通信ログが処理される場合あり

## Certification notes案

本アプリは、利用者が選択した展開済みManifest V3拡張フォルダをChromeへ読み込み、指定URLでCPU・Long Tasks・JavaScriptヒープのON/OFF差分を計測します。Google Chromeが見つからない場合は、設定タブでChromeまたはChrome for Testingの実行ファイルを指定してください。ログイン済みセッションを必要とするページは現バージョンの対象外です。インストーラーは `--silent` でUIを表示せずユーザー単位にインストールできます。

## 申請前チェック

- [ ] Partner Centerで予約した製品名・Publisher情報を反映
- [ ] `<VERSION>` を申請する版へ置換
- [ ] versioned package URLがHTTPS 200で、提出後に上書きされないことを確認
- [ ] Setup.exeとインストールされるPEのAuthenticode署名を確認
- [ ] `--silent` の新規インストールとアンインストールをクリーン環境で確認
- [ ] 実画面スクリーンショットを1枚以上、推奨4枚以上登録
- [ ] `icon/app_icon.png` を1:1 Store logoとして登録
- [ ] プライバシーポリシーURLとサポートURLを公開
- [ ] 年齢区分、カテゴリ、価格、公開市場をPartner Centerで確定

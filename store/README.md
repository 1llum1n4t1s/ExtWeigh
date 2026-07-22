# Microsoft Store申請パック

ExtWeighをWin32 MSI/EXEアプリとしてPartner Centerへ申請するための下書きです。Partner Center固有の値は推測せず、申請時にアカウント画面で入力します。

## Package

| 項目 | 入力値 |
|---|---|
| App type | EXE |
| Architecture | x64 |
| Language | Japanese (Japan) |
| Installer parameters | `--silent` |
| Versioned package URL | `https://extweigh.nephilim.jp/ExtWeigh-<VERSION>-win-x64-Setup.exe` |
| Support URL | `https://github.com/1llum1n4t1s/ExtWeigh/issues` |
| Privacy policy URL | `https://extweigh.nephilim.jp/privacy` |
| License | MIT |
| Applicable license terms | `https://github.com/1llum1n4t1s/ExtWeigh/blob/main/LICENSE` |

`<VERSION>` は申請対象の `Directory.Build.props` と一致させます。URLのEXEは提出後に上書きしません。

## Listing files

- `listing-ja-JP.md`: 日本語の掲載文
- `listing-en-US.md`: 英語の掲載文
- `submission-ja-JP.md`: 日本語申請の入力・確認事項
- `submission-en-US.md`: 英語申請の入力・確認事項

## Image assets

最低1枚のアプリ画面スクリーンショットが必要です。Desktop向けは4枚以上を推奨します。画像は実際のExtWeigh画面を使用し、未実装機能や誤解を招く数値を合成しません。

想定ファイル:

- `images/01-measurement.png`: 計測タブと拡張選択
- `images/02-scenario.png`: 自動生成されたシナリオ編集
- `images/03-results.png`: 拡張別の差分結果
- `images/04-report.png`: 自己完結HTMLレポート

1:1 Store logoには、既存の `icon/app_icon.png`（1254×1254）を使用します。Partner Centerへ直接アップロードできるため、同じ画像の複製はリポジトリへ追加しません。

## Partner Centerでのみ確定できる項目

- 予約済み製品名とProduct ID
- Publisher display name / Publisher ID
- サポートメールアドレス
- 価格と公開市場
- 年齢区分アンケート
- 正式なカテゴリ

これらはアカウント固有のため、このリポジトリには仮値を記録しません。

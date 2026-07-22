# ExtWeigh Store listing（ja-JP）

## 製品名

ExtWeigh

## 短い説明

全ON・全OFF・1つ抜きの差分から、重いChrome拡張機能をCPU・Long Tasks・ヒープで特定します。

## 説明

Chromeが重い。その原因はサイトなのか、拡張機能なのか。

ExtWeighは、Chrome拡張機能を有効・無効にした条件を同じシナリオで順番に計測し、体感では分からない差を数値にします。

拡張が1つならONとOFFを比較。複数なら全OFF、全ON、さらに1つずつ外した条件を比較します。「全ON − BだけOFF」から、いつもの拡張構成の中でBが増やした負荷を確認できます。

主な機能:

- V8 CPU時間、Long Tasks、JavaScriptヒープの差分計測
- 複数拡張の全OFF・全ON・1つ抜き比較
- content script、Service Worker、Offscreen Documentのhot functions表示
- manifest.jsonから代表URLのシナリオを自動生成
- 反復結果の中央値・標準偏差とSIGNIF / NOISE判定
- アプリ内結果と、共有しやすい自己完結HTMLレポート
- 使い捨てChromeプロファイルと直列実行による条件統一

計測データとレポートはローカルPCに保存されます。テレメトリ、広告、利用状況の追跡はありません。

必要環境:

- Windows 10 / 11（x64）
- Google ChromeまたはChrome for Testing
- Manifest V3の展開済み拡張機能フォルダ

現在、ログイン必須ページの計測には対応していません。

ExtWeighはGoogle LLCおよびGoogle Chromeの公式製品ではなく、提携・承認を受けたものではありません。

## 検索語候補

Chrome拡張, パフォーマンス, プロファイラー, CPU, 開発者ツール, ブラウザ, ベンチマーク

# AGENTS.md — ExtWeigh 開発ガイド（LLM / 開発者向け）

Chrome 拡張機能のオーバーヘッドを ON/OFF 差分で計測する Avalonia デスクトップアプリ。cxcx スキル（Playwright + CDP による拡張計測）のパイプラインを、Playwright に依存しない自前 CDP クライアントでアプリ化したもの。

## アーキテクチャ（3 層構成）

```
src/
├─ ExtWeigh.Core/    … UI 非依存の計測エンジン（このアプリの本体）
│  ├─ Cdp/           … CdpClient（ClientWebSocket ベースの CDP JSON-RPC）/ CdpSession（flatten sessionId ルーティング）
│  ├─ Chrome/        … ChromeLocator（chrome.exe 検出）/ ChromeLauncher（--remote-debugging-port=0 起動 + DevToolsActivePort 待機）
│  ├─ Manifest/      … ManifestAnalyzer（MV3 検証 + matches → 代表 URL シナリオのヒューリスティック生成）
│  ├─ Measurement/   … MeasurementRunner（単体 ON/OFF、複数は全 OFF/全 ON/1 つ抜き × 反復の直列実行）/ KeyboardShortcut
│  ├─ Analysis/      … CpuProfile(.cpuprofile パーサ) / CpuProfileAnalyzer(Self・Total・Callers・Children) / Statistics(中央値・σ・SIGNIF/NOISE) / RunAnalyzer(analysis.json 生成)
│  ├─ Report/        … HtmlReportGenerator（自己完結 report.html）
│  └─ Logging/       … LoggerService（SuperLightLogger ラッパー、%APPDATA%\ExtWeigh\logs）
├─ ExtWeigh.UI/      … Avalonia 12 + FluentTheme(Dark) + CommunityToolkit.Mvvm + Microsoft.Extensions.DependencyInjection
└─ ExtWeigh.Tests/   … MSTest（命名: Xxx.test.cs、統合テストは Xxx.Integration.test.cs + TestCategory("Integration")）
```

## 計測フロー（MeasurementRunner.RunSingleAsync が心臓部）

1. 使い捨て user-data-dir で Chrome 起動（`--remote-debugging-port=0` → `DevToolsActivePort` ファイルからポート取得）
2. ブラウザレベル WebSocket に接続し `Target.setDiscoverTargets` → 拡張由来ターゲット（SW / Offscreen）は `targetCreated` イベント → Channel 経由で逐次 attach + Profiler 開始
3. ページターゲットに attach → `Profiler.start` + `Tracing.start`（ReturnAsStream）→ `Page.navigate` → シナリオステップ実行（Idle / Scroll / WaitSelector / Keyboard）
4. `Profiler.stop` で cpuprofile 保存、Long Tasks（注入した PerformanceObserver）+ `Performance.getMetrics` 収集、trace を `IO.read` で回収
5. `RunAnalyzer.Analyze` が metrics.json 群と cpuprofile 群から全体差分・拡張別の条件付き寄与・拡張由来 hot functions を集計 → `analysis.json` → `HtmlReportGenerator`

## 設計上の重要な前提

- **ON/OFF 差分が本質**。拡張単独のプロファイルはサイト本来の重さと区別できないため、必ず OFF → ON の対で計測する（環境差を抑えるため同一反復内で連続実行）
- **複数拡張は leave-one-out**。全 ON と各拡張を 1 つだけ OFF にした条件との差を、その拡張の条件付き寄与として扱う（相互作用を含むため寄与の単純合計は全体差と一致するとは限らない）
- **計測は直列実行する**。並列化すると Chrome 同士が CPU を奪い合い計測精度が壊れる
- **ブラウザ操作は CDP 直叩きで実装する**（Playwright / Puppeteer は製品機能に組み込まない方針。CDP は Chrome の公式プロトコルなのでこの方針に適合する）
- **画面外実行が既定**（`--window-position=-32000,0` + Background Throttling 無効化フラグ）。ユーザーのフォーカスを奪わず、かつ計測精度を保つ
- 反復 2 回以上のとき `|Δ| > σon+σoff` → SIGNIF、`|Δ| < (σon+σoff)/2` → NOISE。反復 1 回はバッジ "-"（判定不能）

## コーディング規約（Lhamiel / RealTimeTranslator 準拠）

- UI 文言・コメント・コミットメッセージは **日本語**
- 非同期メソッドは `Async` サフィックス + `CancellationToken` 伝播、ライブラリ層は `ConfigureAwait(false)`
- ViewModel は CommunityToolkit.Mvvm の **partial property 構文**（`[ObservableProperty] public partial T Prop { get; set; }`）+ `[RelayCommand]`
- ダイアログ表示など View 依存処理は **`Func<Task<string?>>` デリゲート注入**で VM から分離（NumericUpDown にバインドする数値プロパティは `decimal?`）
- 設定は `%APPDATA%\ExtWeigh\settings.json`（SettingsService、変更即保存）
- バージョンは `Directory.Build.props` の `<Version>` に一元化。**更新は /vava 指示時のみ**

## ビルド / テスト

```powershell
dotnet build ExtWeigh.slnx -c Debug
dotnet test src/ExtWeigh.Tests -c Debug --filter "TestCategory!=Integration"   # ユニットのみ（CI 用）
dotnet test src/ExtWeigh.Tests -c Debug --filter "TestCategory=Integration"    # 実 Chrome を起動する統合テスト
```

- lockfile 運用（`RestorePackagesWithLockFile`）: CI では `RestoreLockedMode` が自動で有効化される。ローカルでパッケージを追加したら lockfile 差分もコミットに含める

## 既知の制約 / 今後の課題

- ログイン必須ページの計測は未対応（cxcx の session 機能相当は将来課題。実装するなら「専用プロファイルに一度手動ログイン → コピーして使い回す」方式）
- Chrome 137+ の branded ビルドで `--load-extension` が無視される環境がある。その場合は設定タブで Chrome for Testing を指定してもらう（README のトラブルシュート参照）
- flamegraph の埋め込み（speedscope vendor 同梱）は未実装。`.cpuprofile` を DevTools / speedscope.app で開く運用
- Velopack の起動フックと Certum SimplySign によるローカルコード署名リリースを導入済み。`scripts/release-local.ps1` と `vava.config.json` が `win-x64` の署名付き Setup.exe / Portable ZIP を生成する。自動更新の配信先は未設定

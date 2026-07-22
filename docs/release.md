# 公開手順（Velopack + Cloudflare R2）

ExtWeighのWindows配布物はローカルで署名し、Cloudflare R2から配信します。ランディングページは、同じホスト名の前段に置くCloudflare Workerが返します。

## 配信構成

| 用途 | 値 |
|---|---|
| R2 bucket | `extweigh-updates` |
| 公開URL | `https://extweigh.nephilim.jp` |
| Velopack channel | `win-x64` |
| package ID | `ExtWeigh` |
| Landing Worker | `extweigh-landing` |
| Worker Route | `extweigh.nephilim.jp/*` |

`website/worker.js` は `/`、`/privacy`、CSS、JavaScript、robots、sitemapだけを返します。それ以外のリクエストは同一ホスト名のR2 Custom Domainへ委譲するため、Setup.exe、Portable ZIP、nupkg、Velopack manifestのRange・キャッシュ・Content-TypeをR2側に維持できます。

## 外部側の準備

次のCloudflare資源はリポジトリの設定だけでは作成されません。

1. `extweigh-updates` bucketを作成する。
2. bucketのCustom Domainに `extweigh.nephilim.jp` を接続する。
3. GitHub Actions用に `CLOUDFLARE_API_TOKEN` と `CLOUDFLARE_ACCOUNT_ID` を登録する。
4. `CLOUDFLARE_API_TOKEN` へWorkers Scriptsの編集権限を付与する。
5. ローカルリリース用の `cloudflare.api_token` へR2編集、zone参照、キャッシュパージに必要な最小権限を付与する。

DNS/Custom Domainを準備してからLanding Workerをデプロイします。同一ホスト名ではWorker RouteがR2 Custom Domainより先に実行され、Worker内の `fetch(request)` がR2側へ委譲します。

## ランディングページ

`website/**` をmainへpushすると `.github/workflows/deploy-landing.yml` がWorkerを検証してデプロイします。手動実行も可能です。このworkflowはアプリのリリースとは独立しています。

## アプリのリリース

バージョン更新は `/vava` でのみ行います。通常のローカルリリースは次を実行します。

```powershell
pwsh scripts/release-local.ps1
```

公開せず、build・package・署名だけを確認する場合:

```powershell
pwsh scripts/release-local.ps1 -SkipUpload
```

スクリプトは次の順序を守ります。

1. 署名証明書、R2 bucket、zone、DNS、認証権限をプリフライト確認
2. `win-x64`をlocked restore・publish
3. Velopack packageを生成し、Setup、nupkg内EXE、Portable内EXEの署名を検証
4. packageと固定成果物をR2へuploadしてHTTPS到達を確認
5. `releases.win-x64.json`を最後にupload
6. 固定URLのCloudflareキャッシュをpurgeし、全成果物を再確認

manifest外の古いnupkgは通常リリースでは削除しません。全channelのmanifestを生成したうえで削除を明示する場合だけ、次を使います。

```powershell
pwsh scripts/release-local.ps1 -Cleanup
```

## Microsoft Store用EXE

Microsoft StoreのMSI/EXE申請では、提出後に内容が変わらないバージョン付きHTTPS URLが必要です。リリーススクリプトは固定名Setupに加え、次の署名済みコピーを生成・保持します。

```text
https://extweigh.nephilim.jp/ExtWeigh-<VERSION>-win-x64-Setup.exe
```

Partner CenterのInstaller parametersには `--silent` を指定します。新バージョンは新しいURLで別submissionとして登録します。

## 既存クライアント

現行ExtWeighには `GithubSource` やアプリ内更新チェックの実行経路がありません。そのため、GitHub ReleasesからR2へ更新元を切り替える踏み台releaseは不要です。将来自動更新を実装する場合は、配信元をハードコードした `SimpleWebSource` と更新経路のテストを別途追加します。

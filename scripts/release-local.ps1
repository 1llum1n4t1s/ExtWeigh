# release-local.ps1 — ローカル署名付き Velopack リリース
#
# Certum SimplySign は Desktop 接続とスマホトークンの承認が必要なため、
# GitHub Actions ではなくこのスクリプトからローカル署名する。
# 生成した成果物は Cloudflare R2 にアップロードし、更新 manifest は最後に公開する。
#
# 前提:
#   - SimplySign Desktop が接続済みで、署名証明書が CurrentUser\My に見えていること
#   - Directory.Build.props の <Version> が配布したいバージョンになっていること
#   - Cloudflare 側に extweigh-updates bucket と extweigh.nephilim.jp が準備済みであること
#   - C:\Users\IMT\dev\Secret\secrets.json に cloudflare.api_token があること
#
# 使い方:
#   pwsh scripts/release-local.ps1
#   pwsh scripts/release-local.ps1 -SkipUpload
#   pwsh scripts/release-local.ps1 -Cleanup
#   pwsh scripts/release-local.ps1 -Runtimes win-x64

[CmdletBinding()]
param(
    [switch]$SkipUpload,
    [switch]$Cleanup,
    [string[]]$Runtimes = @('win-x64')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$WranglerVersion = '4.112.0'
$Bucket = 'extweigh-updates'
$BaseUrl = 'https://extweigh.nephilim.jp'
$AccountId = '10901bfadbf1005164774a7350082985'
$SecretsPath = 'C:\Users\IMT\dev\Secret\secrets.json'
$CertSubjectName = 'Open Source Developer Yuichiro Shinozaki'
$TimestampUrl = 'http://time.certum.pl'
$SignParams = "/n `"$CertSubjectName`" /fd SHA256 /td SHA256 /tr $TimestampUrl"
$RuntimeMatrix = @{
    'win-x64' = @{ Channel = 'win-x64' }
}

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$WorkDir = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'local-release'))
$ArtifactsDir = Join-Path $WorkDir 'artifacts'
$SignatureVerificationDir = Join-Path $WorkDir 'signature-verification'
$RepoPrefix = $RepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $WorkDir.StartsWith($RepoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ローカルリリース出力先がリポジトリ外です: $WorkDir"
}

Set-Location $RepoRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Invoke-Native {
    param(
        [Parameter(Mandatory)]
        [string]$Description,
        [Parameter(Mandatory)]
        [scriptblock]$Block
    )

    & $Block
    if ($LASTEXITCODE -ne 0) {
        throw "$Description が失敗しました (exit $LASTEXITCODE)"
    }
}

function Test-Signature {
    param([Parameter(Mandatory)][string]$Path)

    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne 'Valid' -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notlike "CN=$CertSubjectName*") {
        throw "署名検証失敗: $([System.IO.Path]::GetFileName($Path)) → $($signature.Status)"
    }

    Write-Host "  ✅ $([System.IO.Path]::GetFileName($Path)): Valid ($($signature.SignerCertificate.Subject -replace ',.*$'))"
}

function Test-ArchiveEntrySignature {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$EntryPath,
        [Parameter(Mandatory)][string]$Label
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entry = $archive.GetEntry($EntryPath)
        if ($null -eq $entry) {
            throw "$Label に署名検証対象がありません: $EntryPath"
        }

        $outputName = "$([System.IO.Path]::GetFileNameWithoutExtension($ArchivePath))-$([System.IO.Path]::GetFileName($EntryPath))"
        $outputPath = Join-Path $SignatureVerificationDir $outputName
        $input = $entry.Open()
        try {
            $output = [System.IO.File]::Create($outputPath)
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
            }
        }
        finally {
            $input.Dispose()
        }

        Test-Signature -Path $outputPath
    }
    finally {
        $archive.Dispose()
    }
}

function Send-R2Object {
    param([Parameter(Mandatory)][System.IO.FileInfo]$File)

    Write-Host "  ↑ $($File.Name)"
    Invoke-Native "R2 put ($($File.Name))" {
        pnpm dlx "wrangler@$WranglerVersion" r2 object put "$Bucket/$($File.Name)" --file $File.FullName --remote
    }
}

function Test-PublishedObject {
    param(
        [Parameter(Mandatory)][System.IO.FileInfo]$File,
        [Parameter(Mandatory)][string]$CacheKey
    )

    $encodedName = [uri]::EscapeDataString($File.Name)
    $url = "$BaseUrl/$encodedName`?release=$CacheKey"
    $response = Invoke-WebRequest -Uri $url -Method Head -TimeoutSec 30 -MaximumRetryCount 3 -RetryIntervalSec 5
    if ($response.StatusCode -ne 200) {
        throw "配信確認失敗: $url → HTTP $($response.StatusCode)"
    }

    $contentLength = $response.Headers['Content-Length']
    if ($contentLength -and [long]$contentLength -ne $File.Length) {
        throw "配信サイズ不一致: $($File.Name) (local=$($File.Length), remote=$contentLength)"
    }

    Write-Host "  ✅ $($File.Name): HTTP 200 ($($File.Length) bytes)"
}

Write-Host '== プリフライト ==' -ForegroundColor Cyan

if (-not ${env:ProgramFiles(x86)}) {
    ${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'
}

$vsInstallerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ($env:PATH -notlike "*$vsInstallerDir*") {
    $env:PATH = "$env:PATH;$vsInstallerDir"
}

$env:DOTNET_ROLL_FORWARD = 'Major'

$versionNode = ([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).SelectSingleNode('/Project/PropertyGroup/Version')
$Version = if ($versionNode) { $versionNode.InnerText.Trim() } else { $null }
if (-not $Version) {
    throw 'Directory.Build.props から <Version> を取得できませんでした'
}
Write-Host "バージョン: $Version"

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -like "CN=$CertSubjectName*" -and $_.NotAfter -gt (Get-Date) } |
    Select-Object -First 1
if (-not $certificate) {
    throw "署名証明書 (CN=$CertSubjectName) が見つかりません。SimplySign Desktop を起動してトークンでログインしてください。"
}
Write-Host "署名証明書: $($certificate.Subject) (期限 $($certificate.NotAfter.ToString('yyyy-MM-dd')))"

$vpkVersion = (Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/vpk/index.json' -TimeoutSec 30).versions |
    Where-Object { $_ -notmatch '-' } |
    Select-Object -Last 1
if (-not $vpkVersion) {
    throw 'vpk の最新安定版バージョンの取得に失敗しました (NuGet API)'
}
Write-Host "vpk 最新安定版: $vpkVersion"

$vpkToolLine = dotnet tool list --global |
    Where-Object { $_ -match '^\s*vpk\s+' } |
    Select-Object -First 1
$installedVpkVersion = if ($vpkToolLine) {
    @($vpkToolLine -split '\s+' | Where-Object { $_ })[1]
} else {
    $null
}

if (-not $installedVpkVersion) {
    Invoke-Native 'vpk のインストール' { dotnet tool install --global vpk --version $vpkVersion }
}
elseif ($installedVpkVersion -ne $vpkVersion) {
    Invoke-Native 'vpk の更新' { dotnet tool update --global vpk --version $vpkVersion }
}

foreach ($runtime in $Runtimes) {
    if (-not $RuntimeMatrix.ContainsKey($runtime)) {
        throw "未知の runtime: $runtime"
    }
}

$cloudflareHeaders = $null
$zoneId = $null
if (-not $SkipUpload) {
    if (-not (Test-Path -LiteralPath $SecretsPath)) {
        throw "Cloudflare 認証情報が見つかりません: $SecretsPath"
    }

    $secrets = Get-Content -LiteralPath $SecretsPath -Raw | ConvertFrom-Json
    if (-not $secrets.cloudflare.api_token) {
        throw 'secrets.json に cloudflare.api_token が見つかりません'
    }

    $env:CLOUDFLARE_API_TOKEN = $secrets.cloudflare.api_token
    $env:CLOUDFLARE_ACCOUNT_ID = $AccountId
    $cloudflareHeaders = @{ Authorization = "Bearer $($env:CLOUDFLARE_API_TOKEN)" }

    # upload 前に bucket と zone 権限を確認し、途中まで公開された状態を防ぐ。
    $bucketResponse = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/accounts/$AccountId/r2/buckets/$Bucket" `
        -Headers $cloudflareHeaders -TimeoutSec 30
    if (-not $bucketResponse.success) {
        throw "Cloudflare R2 bucket '$Bucket' を確認できませんでした"
    }

    $zoneName = ([uri]$BaseUrl).Host -replace '^[^.]+\.', ''
    $zoneResponse = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/zones?name=$zoneName" `
        -Headers $cloudflareHeaders -TimeoutSec 30
    if (-not $zoneResponse.success -or @($zoneResponse.result).Count -eq 0) {
        throw "Cloudflare zone '$zoneName' を確認できませんでした"
    }
    $zoneId = $zoneResponse.result[0].id

    try {
        [void][System.Net.Dns]::GetHostAddresses(([uri]$BaseUrl).Host)
    }
    catch {
        throw "配信ドメインが DNS 解決できません: $BaseUrl"
    }
}

if ($Cleanup -and $SkipUpload) {
    throw '-Cleanup は R2 へ接続するため、-SkipUpload と同時には指定できません'
}

if (Test-Path -LiteralPath $WorkDir) {
    Remove-Item -LiteralPath $WorkDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
New-Item -ItemType Directory -Path $SignatureVerificationDir -Force | Out-Null

foreach ($runtime in $Runtimes) {
    $config = $RuntimeMatrix[$runtime]
    $publishDir = Join-Path $WorkDir "publish-$runtime"
    $mainExe = Join-Path $publishDir 'ExtWeigh.UI.exe'

    Write-Host "== restore: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "dotnet restore ($runtime)" {
        dotnet restore ExtWeigh.slnx -r $runtime -p:Configuration=Release --locked-mode
    }

    Write-Host "== publish: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "dotnet publish ($runtime)" {
        dotnet publish src/ExtWeigh.UI/ExtWeigh.UI.csproj -c Release -r $runtime --self-contained true --no-restore -o $publishDir
    }

    if (-not (Test-Path -LiteralPath $mainExe)) {
        throw "ExtWeigh.UI.exe が publish 出力にありません ($runtime)"
    }

    Write-Host "== vpk pack + 署名: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "vpk pack ($runtime)" {
        vpk pack `
            --packId ExtWeigh `
            --packVersion $Version `
            --packTitle 'ExtWeigh' `
            --packAuthors '1llum1n4t1s' `
            --mainExe 'ExtWeigh.UI.exe' `
            --icon (Join-Path 'icon' 'app.ico') `
            --packDir $publishDir `
            --outputDir $ArtifactsDir `
            --channel $config.Channel `
            --shortcuts 'StartMenuRoot,Desktop' `
            --signParallel 1 `
            --signParams $SignParams
    }

    Write-Host "== 署名検証: $runtime ==" -ForegroundColor Cyan
    $fullPackage = Join-Path $ArtifactsDir "ExtWeigh-$Version-$($config.Channel)-full.nupkg"
    $portablePackage = Join-Path $ArtifactsDir "ExtWeigh-$($config.Channel)-Portable.zip"
    $setupPackage = Join-Path $ArtifactsDir "ExtWeigh-$($config.Channel)-Setup.exe"
    if (-not (Test-Path -LiteralPath $fullPackage) -or
        -not (Test-Path -LiteralPath $portablePackage) -or
        -not (Test-Path -LiteralPath $setupPackage)) {
        throw "配布パッケージが生成されませんでした ($runtime)"
    }

    Test-ArchiveEntrySignature -ArchivePath $fullPackage -EntryPath 'lib/app/ExtWeigh.UI.exe' -Label 'フルパッケージ'
    Test-ArchiveEntrySignature -ArchivePath $portablePackage -EntryPath 'current/ExtWeigh.UI.exe' -Label 'Portable パッケージ'
    Test-Signature -Path $setupPackage

    # Microsoft Store の MSI/EXE 申請は、内容が変わらないバージョン付き URL が必須。
    $storeInstaller = Join-Path $ArtifactsDir "ExtWeigh-$Version-$($config.Channel)-Setup.exe"
    Copy-Item -LiteralPath $setupPackage -Destination $storeInstaller
    Test-Signature -Path $storeInstaller
}

$manifestFiles = @(Get-ChildItem -LiteralPath $ArtifactsDir -Filter 'releases.*.json' -File)
if ($manifestFiles.Count -ne $Runtimes.Count) {
    throw "manifest 数が runtime 数と一致しません (manifest=$($manifestFiles.Count), runtime=$($Runtimes.Count))"
}

foreach ($manifest in $manifestFiles) {
    $manifestData = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
    foreach ($asset in @($manifestData.Assets)) {
        if (-not $asset.FileName) {
            throw "manifest に FileName のない asset があります: $($manifest.Name)"
        }
        if (-not (Test-Path -LiteralPath (Join-Path $ArtifactsDir $asset.FileName))) {
            throw "manifest 参照パッケージが artifacts にありません: $($asset.FileName)"
        }
    }
}

if ($SkipUpload) {
    Write-Host "`n✅ -SkipUpload 指定のため公開せず終了します: $ArtifactsDir" -ForegroundColor Green
    Get-ChildItem -LiteralPath $ArtifactsDir -File |
        Select-Object Name, @{ Name = 'Size(MB)'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
        Format-Table -AutoSize
    return
}

# manifest を先に公開すると、クライアントが未到達の package を参照し得る。
# package と固定成果物を先に upload・検証し、manifest は最後に公開する。
$payloadFiles = @(Get-ChildItem -LiteralPath $ArtifactsDir -File |
    Where-Object { $_.Name -notlike 'releases.*.json' } |
    Sort-Object Name)
if ($payloadFiles.Count -eq 0) {
    throw 'R2 へアップロードする成果物がありません'
}

Write-Host '== R2 アップロード: package / 固定成果物 ==' -ForegroundColor Cyan
foreach ($file in $payloadFiles) {
    Send-R2Object -File $file
}

Write-Host '== 配信確認: manifest 公開前 ==' -ForegroundColor Cyan
foreach ($file in $payloadFiles) {
    Test-PublishedObject -File $file -CacheKey "$Version-pre-manifest"
}

Write-Host '== R2 アップロード: manifest ==' -ForegroundColor Cyan
foreach ($manifest in $manifestFiles | Sort-Object Name) {
    Send-R2Object -File $manifest
}

# 固定 URL の Setup / Portable / manifest が旧キャッシュを返さないよう purge する。
Write-Host '== Cloudflare キャッシュパージ ==' -ForegroundColor Cyan
$purgeFiles = @(Get-ChildItem -LiteralPath $ArtifactsDir -File | Where-Object {
    $_.Name -notlike '*.nupkg' -and $_.Name -notmatch "^ExtWeigh-$([regex]::Escape($Version))-.*-Setup\.exe$"
})
$purgeUrls = @($purgeFiles | ForEach-Object { "$BaseUrl/$([uri]::EscapeDataString($_.Name))" })
if ($purgeUrls.Count -gt 0) {
    try {
        for ($offset = 0; $offset -lt $purgeUrls.Count; $offset += 30) {
            $last = [math]::Min($offset + 29, $purgeUrls.Count - 1)
            $batch = @($purgeUrls[$offset..$last])
            $body = @{ files = $batch } | ConvertTo-Json -Depth 3 -Compress
            $purgeResponse = Invoke-RestMethod -Method Post `
                -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
                -Headers $cloudflareHeaders -ContentType 'application/json' -Body $body -TimeoutSec 30
            if (-not $purgeResponse.success) {
                throw ($purgeResponse.errors | ConvertTo-Json -Compress)
            }
        }
        Write-Host "  ✅ $($purgeUrls.Count) URL"
    }
    catch {
        Write-Warning "R2 アップロードは完了しましたが、キャッシュパージに失敗しました: $($_.Exception.Message)"
    }
}

Write-Host '== 配信確認: manifest 公開後 ==' -ForegroundColor Cyan
foreach ($file in @($payloadFiles) + @($manifestFiles)) {
    Test-PublishedObject -File $file -CacheKey "$Version-final"
}

if ($Cleanup) {
    if ($Runtimes.Count -ne $RuntimeMatrix.Count) {
        throw '-Cleanup は全 runtime の manifest を生成した実行でのみ使用できます'
    }

    Write-Host '== manifest 外 nupkg クリーンアップ ==' -ForegroundColor Cyan
    $keep = @{}
    foreach ($manifest in $manifestFiles) {
        $manifestData = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
        foreach ($asset in @($manifestData.Assets)) {
            if ($asset.FileName -like '*.nupkg') {
                $keep[$asset.FileName] = $true
            }
        }
    }
    if ($keep.Count -eq 0) {
        throw '保持対象 nupkg が空のため cleanup を中止します'
    }

    $api = "https://api.cloudflare.com/client/v4/accounts/$AccountId/r2/buckets/$Bucket"
    $allKeys = [System.Collections.Generic.List[string]]::new()
    $cursor = ''
    while ($true) {
        $uri = "$api/objects?per_page=1000" + $(if ($cursor) { "&cursor=$cursor" })
        $response = Invoke-RestMethod -Uri $uri -Headers $cloudflareHeaders -TimeoutSec 30
        foreach ($object in @($response.result)) {
            $allKeys.Add($object.key)
        }

        $info = $response.PSObject.Properties['result_info']
        if (-not $info -or -not $info.Value) { break }
        $isTruncated = $info.Value.PSObject.Properties['is_truncated']
        if (-not $isTruncated -or -not $isTruncated.Value) { break }
        $cursorProperty = $info.Value.PSObject.Properties['cursor']
        $cursor = if ($cursorProperty) { $cursorProperty.Value } else { '' }
        if (-not $cursor) { break }
    }

    $deleteCandidates = @($allKeys | Where-Object { $_ -like '*.nupkg' -and -not $keep.ContainsKey($_) })
    if ($deleteCandidates.Count -eq 0) {
        Write-Host '  ✅ 削除対象なし'
    }
    else {
        Write-Host "  削除対象: $($deleteCandidates.Count) 件"
        foreach ($key in $deleteCandidates) {
            $encodedKey = [uri]::EscapeDataString($key)
            Invoke-RestMethod -Method Delete -Uri "$api/objects/$encodedKey" -Headers $cloudflareHeaders -TimeoutSec 30 | Out-Null
            Write-Host "  🗑️  $key"
        }
    }
}
else {
    Write-Host '== cleanup ==' -ForegroundColor Cyan
    Write-Host '  省略（必要なリリースで -Cleanup を明示してください）'
}

Write-Host "`n🎉 リリース完了: v$Version → $BaseUrl" -ForegroundColor Green

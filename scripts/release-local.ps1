# release-local.ps1 — ローカル署名付き Velopack パッケージ作成
#
# Certum SimplySign は Desktop 接続とスマホトークンの承認が必要なため、
# GitHub Actions ではなくこのスクリプトからローカル署名する。
#
# 前提:
#   - SimplySign Desktop が接続済みで、署名証明書が CurrentUser\My に見えていること
#   - Directory.Build.props の <Version> が配布したいバージョンになっていること
#
# 使い方:
#   pwsh scripts/release-local.ps1
#   pwsh scripts/release-local.ps1 -Runtimes win-x64

[CmdletBinding()]
param(
    [string[]]$Runtimes = @('win-x64')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

Write-Host '== プリフライト ==' -ForegroundColor Cyan

# Git Bash (MSYS) 経由でも VS の探索を安定させる。
if (-not ${env:ProgramFiles(x86)}) {
    ${env:ProgramFiles(x86)} = 'C:\Program Files (x86)'
}

$vsInstallerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ($env:PATH -notlike "*$vsInstallerDir*") {
    $env:PATH = "$env:PATH;$vsInstallerDir"
}

# vpk が要求するランタイムをインストール済みの新しい SDK へロールフォワードする。
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
} else {
    if ($installedVpkVersion -ne $vpkVersion) {
        Invoke-Native 'vpk の更新' { dotnet tool update --global vpk --version $vpkVersion }
    }
}

if (Test-Path -LiteralPath $WorkDir) {
    Remove-Item -LiteralPath $WorkDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null

foreach ($runtime in $Runtimes) {
    $config = $RuntimeMatrix[$runtime]
    if (-not $config) {
        throw "未知の runtime: $runtime"
    }

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
    New-Item -ItemType Directory -Path $SignatureVerificationDir -Force | Out-Null
    $fullPackage = Join-Path $ArtifactsDir "ExtWeigh-$Version-$($config.Channel)-full.nupkg"
    $portablePackage = Join-Path $ArtifactsDir "ExtWeigh-$($config.Channel)-Portable.zip"
    if (-not (Test-Path -LiteralPath $fullPackage) -or -not (Test-Path -LiteralPath $portablePackage)) {
        throw "配布パッケージが生成されませんでした ($runtime)"
    }

    Test-ArchiveEntrySignature -ArchivePath $fullPackage -EntryPath 'lib/app/ExtWeigh.UI.exe' -Label 'フルパッケージ'
    Test-ArchiveEntrySignature -ArchivePath $portablePackage -EntryPath 'current/ExtWeigh.UI.exe' -Label 'Portable パッケージ'
}

$artifactExecutables = @(Get-ChildItem -LiteralPath $ArtifactsDir -Filter '*.exe' -File)
if ($artifactExecutables.Count -eq 0) {
    throw '署名検証対象の Setup.exe が生成されませんでした'
}

Write-Host '== 署名検証: インストーラー ==' -ForegroundColor Cyan
foreach ($executable in $artifactExecutables) {
    Test-Signature -Path $executable.FullName
}

Write-Host "`n✅ 署名付きパッケージを作成しました: $ArtifactsDir" -ForegroundColor Green
Get-ChildItem -LiteralPath $ArtifactsDir -File |
    Select-Object Name, @{ Name = 'Size(MB)'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize

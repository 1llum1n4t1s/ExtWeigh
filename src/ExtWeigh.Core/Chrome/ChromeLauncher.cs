using System.Diagnostics;
using ExtWeigh.Core.Logging;

namespace ExtWeigh.Core.Chrome;

/// <summary>Chrome 起動オプション</summary>
public sealed class ChromeLaunchOptions
{
    /// <summary>chrome.exe のフルパス</summary>
    public required string ChromePath { get; init; }

    /// <summary>専用ユーザーデータディレクトリ（計測ごとに使い捨て）</summary>
    public required string UserDataDir { get; init; }

    /// <summary>この条件で有効にする拡張ルート。空なら拡張 OFF。</summary>
    public IReadOnlyList<string> ExtensionPaths { get; init; } = [];

    /// <summary>true でウィンドウを画面内に表示（既定は画面外配置でフォーカス奪取を防止）</summary>
    public bool ShowBrowser { get; init; }
}

/// <summary>起動済み Chrome インスタンス。Dispose でプロセス kill + プロファイル削除。</summary>
public sealed class ChromeInstance : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _userDataDir;

    /// <summary>ブラウザレベル CDP WebSocket エンドポイント</summary>
    public Uri BrowserWsUri { get; }

    internal ChromeInstance(Process process, string userDataDir, Uri browserWsUri)
    {
        _process = process;
        _userDataDir = userDataDir;
        BrowserWsUri = browserWsUri;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Log($"Chrome プロセスの終了に失敗: {ex.Message}", LogLevel.Warning);
        }
        finally
        {
            _process.Dispose();
        }

        // プロファイルディレクトリ削除（ファイルロック解放待ちでリトライ）
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_userDataDir)) Directory.Delete(_userDataDir, recursive: true);
                break;
            }
            catch
            {
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Chrome をリモートデバッグポート付きで起動する</summary>
public static class ChromeLauncher
{
    /// <summary>
    /// Chrome を専用プロファイル + --remote-debugging-port=0 で起動し、
    /// DevToolsActivePort ファイルからブラウザレベル WebSocket エンドポイントを得る。
    /// </summary>
    public static async Task<ChromeInstance> LaunchAsync(ChromeLaunchOptions options, CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.UserDataDir);

        var psi = new ProcessStartInfo(options.ChromePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (var arg in BuildArgs(options)) psi.ArgumentList.Add(arg);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Chrome の起動に失敗しました: {options.ChromePath}");

        try
        {
            var (port, wsPath) = await WaitForDevToolsActivePortAsync(options.UserDataDir, process, ct).ConfigureAwait(false);
            var wsUri = new Uri($"ws://127.0.0.1:{port}{wsPath}");
            LoggerService.Log($"Chrome 起動完了 (pid={process.Id}, port={port}, extensions={options.ExtensionPaths.Count})", LogLevel.Debug);
            return new ChromeInstance(process, options.UserDataDir, wsUri);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* 起動失敗時の後始末 */ }
            process.Dispose();
            throw;
        }
    }

    /// <summary>計測精度を保つための起動引数を組み立てる</summary>
    internal static List<string> BuildArgs(ChromeLaunchOptions options)
    {
        var args = new List<string>
        {
            "--remote-debugging-port=0",
            $"--user-data-dir={options.UserDataDir}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-sync",
            "--disable-default-apps",
            "--mute-audio",
            // Background Throttling を無効化して画面外配置でも計測精度を維持する
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows",
            "--disable-renderer-backgrounding",
            "--disable-features=CalculateNativeWinOcclusion,Translate,OptimizationHints",
            "--window-size=1920,1080",
        };

        if (options.ShowBrowser)
        {
            args.Add("--start-maximized");
        }
        else
        {
            // 画面外左に配置してユーザーの作業のフォーカスを奪わない
            args.Add("--window-position=-32000,0");
        }

        if (options.ExtensionPaths.Count > 0)
        {
            var paths = string.Join(',', options.ExtensionPaths);
            args.Add($"--disable-extensions-except={paths}");
            args.Add($"--load-extension={paths}");
        }
        else
        {
            args.Add("--disable-extensions");
        }

        args.Add("about:blank");
        return args;
    }

    /// <summary>DevToolsActivePort ファイル（1 行目: ポート、2 行目: ws パス）をポーリングで待つ</summary>
    private static async Task<(int Port, string WsPath)> WaitForDevToolsActivePortAsync(
        string userDataDir, Process process, CancellationToken ct)
    {
        var portFile = Path.Combine(userDataDir, "DevToolsActivePort");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"Chrome が起動直後に終了しました (exit code {process.ExitCode})");
            }

            if (File.Exists(portFile))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(portFile, ct).ConfigureAwait(false);
                    if (lines.Length >= 2 && int.TryParse(lines[0], out var port) && port > 0)
                    {
                        return (port, lines[1]);
                    }
                }
                catch (IOException)
                {
                    // Chrome 書き込み中の読み取り競合は次のポーリングで再試行
                }
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("DevToolsActivePort が 30 秒以内に生成されませんでした（Chrome の起動失敗）");
    }
}

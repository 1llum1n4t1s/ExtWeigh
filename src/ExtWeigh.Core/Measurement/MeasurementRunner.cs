using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using ExtWeigh.Core.Analysis;
using ExtWeigh.Core.Cdp;
using ExtWeigh.Core.Chrome;
using ExtWeigh.Core.Logging;
using ExtWeigh.Core.Models;

namespace ExtWeigh.Core.Measurement;

/// <summary>
/// 単体拡張は ON/OFF、複数拡張は全 OFF/全 ON/1 つ抜き条件で Chrome を起動し、
/// V8 CPU profile + Chrome trace + メトリクスを収集する計測ランナー。
/// </summary>
public sealed class MeasurementRunner(MeasurementPlan plan)
{
    private sealed record MeasurementCondition(
        string Id,
        string FilePrefix,
        string Label,
        IReadOnlyList<MeasurementExtension> EnabledExtensions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Long task を PerformanceObserver で記録する注入スクリプト</summary>
    private const string LongTaskObserverScript =
        """
        (() => {
          try {
            window.__extweighLongTasks = [];
            new PerformanceObserver((list) => {
              for (const e of list.getEntries()) {
                window.__extweighLongTasks.push({ s: e.startTime, d: e.duration });
              }
            }).observe({ type: 'longtask', buffered: true });
          } catch (_) { /* longtask 未対応環境では無視 */ }
        })();
        """;

    /// <summary>Chrome trace の取得カテゴリ</summary>
    private static readonly string[] TraceCategories =
        ["devtools.timeline", "v8.execute", "disabled-by-default-v8.cpu_profiler"];

    /// <summary>V8 プロファイラのサンプリング間隔 (µs)</summary>
    private const int SamplingIntervalUs = 500;

    /// <summary>
    /// プラン全体を実行する。各シナリオ × 反復 × 計測条件の順で直列計測
    /// （並列化は Chrome 同士が CPU を奪い合い計測精度を壊すため行わない）。
    /// </summary>
    public async Task RunAsync(IProgress<MeasurementProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(plan.OutputDir);

        var extensions = plan.GetEffectiveExtensions();
        if (extensions.Count == 0)
        {
            throw new InvalidOperationException("計測対象の拡張がありません");
        }
        var conditions = BuildConditions(extensions);

        // 入力スナップショットを保存（後から再現・検証できるように）
        File.WriteAllText(
            Path.Combine(plan.OutputDir, "plan.json"),
            JsonSerializer.Serialize(plan, JsonOptions));
        var manifestsDir = Path.Combine(plan.OutputDir, "manifests");
        Directory.CreateDirectory(manifestsDir);
        foreach (var extension in extensions)
        {
            var manifestSrc = Path.Combine(extension.Path, "manifest.json");
            if (!File.Exists(manifestSrc)) continue;
            File.Copy(manifestSrc, Path.Combine(manifestsDir, $"{extension.Key}.json"), overwrite: true);
            if (extensions.Count == 1)
            {
                File.Copy(manifestSrc, Path.Combine(plan.OutputDir, "manifest.json"), overwrite: true);
            }
        }

        var totalRuns = plan.Scenarios.Count * plan.Repeat * conditions.Count;
        var doneRuns = 0;

        foreach (var scenario in plan.Scenarios)
        {
            var scenarioDir = Path.Combine(plan.OutputDir, "scenarios", scenario.Slug());
            Directory.CreateDirectory(scenarioDir);

            for (var iteration = 1; iteration <= plan.Repeat; iteration++)
            {
                // 全 OFF → 全 ON → 1 つ抜きの順に直列実行する
                foreach (var condition in conditions)
                {
                    ct.ThrowIfCancellationRequested();
                    var label = $"[{scenario.Name}] {condition.Label} #{iteration}";
                    progress?.Report(new MeasurementProgress($"{label} 計測開始...", doneRuns * 100.0 / totalRuns));

                    var sw = Stopwatch.StartNew();
                    var metrics = await RunSingleAsync(scenario, condition, iteration, scenarioDir, ct).ConfigureAwait(false);
                    doneRuns++;

                    progress?.Report(new MeasurementProgress(
                        $"{label} 完了 ({sw.Elapsed.TotalSeconds:F0}s) — CPU {metrics.CpuTotalMs:F0}ms / LongTasks {metrics.LongTaskCount} / Heap {metrics.JsHeapUsedMb:F1}MB",
                        doneRuns * 100.0 / totalRuns));
                }
            }
        }
    }

    /// <summary>単体は従来の OFF/ON、複数は全 OFF/全 ON と各 1 つ抜き条件を作る</summary>
    private static List<MeasurementCondition> BuildConditions(IReadOnlyList<MeasurementExtension> extensions)
    {
        var conditions = new List<MeasurementCondition>
        {
            new("all-off", extensions.Count == 1 ? "off" : "all-off", "全 OFF", []),
            new("all-on", extensions.Count == 1 ? "on" : "all-on", "全 ON", extensions),
        };
        if (extensions.Count <= 1) return conditions;

        foreach (var excluded in extensions)
        {
            conditions.Add(new MeasurementCondition(
                $"without-{excluded.Key}",
                $"without-{excluded.Key}",
                $"{excluded.Name} のみ OFF",
                [.. extensions.Where(e => e.Key != excluded.Key)]));
        }
        return conditions;
    }

    /// <summary>1 回の起動（1 シナリオ × ON or OFF × 1 反復）を計測する</summary>
    private async Task<SingleRunMetrics> RunSingleAsync(
        Scenario scenario, MeasurementCondition condition, int iteration, string scenarioDir, CancellationToken ct)
    {
        var extensionOn = condition.EnabledExtensions.Count > 0;
        var fileBase = $"{condition.FilePrefix}-{iteration}";
        var metrics = new SingleRunMetrics
        {
            FileBase = fileBase,
            ConditionId = condition.Id,
            EnabledExtensionKeys = [.. condition.EnabledExtensions.Select(e => e.Key)],
            ExtensionOn = extensionOn,
            Iteration = iteration,
        };

        var userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExtWeigh", "chrome-profiles", $"profile-{Guid.NewGuid():N}");

        await using var chrome = await ChromeLauncher.LaunchAsync(new ChromeLaunchOptions
        {
            ChromePath = plan.ChromePath,
            UserDataDir = userDataDir,
            ExtensionPaths = [.. condition.EnabledExtensions.Select(e => e.Path)],
            ShowBrowser = plan.ShowBrowser,
        }, ct).ConfigureAwait(false);

        await using var cdp = await CdpClient.ConnectAsync(chrome.BrowserWsUri, ct).ConfigureAwait(false);

        // 拡張由来ターゲット（SW / Offscreen）を発見次第プロファイラを仕掛ける
        var extraTargets = new List<(string Kind, string Url, CdpSession Session)>();
        var attachedTargetIds = new HashSet<string>();
        var extTargetChannel = Channel.CreateUnbounded<(string TargetId, string Type, string Url)>();
        Task? extAttachTask = null;

        if (extensionOn)
        {
            cdp.EventReceived += evt =>
            {
                if (evt.Method != "Target.targetCreated") return;
                if (!evt.Params.TryGetProperty("targetInfo", out var info)) return;
                var url = info.GetProperty("url").GetString() ?? "";
                var type = info.GetProperty("type").GetString() ?? "";
                var targetId = info.GetProperty("targetId").GetString() ?? "";
                if (!url.StartsWith("chrome-extension://", StringComparison.Ordinal)) return;
                if (type is not ("service_worker" or "page" or "other")) return;
                extTargetChannel.Writer.TryWrite((targetId, type, url));
            };

            extAttachTask = Task.Run(async () =>
            {
                await foreach (var (targetId, type, url) in extTargetChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    if (!attachedTargetIds.Add(targetId)) continue;
                    try
                    {
                        var attach = await cdp.SendAsync("Target.attachToTarget", new { targetId, flatten = true }, ct: ct).ConfigureAwait(false);
                        var session = new CdpSession(cdp, attach.GetProperty("sessionId").GetString()!, targetId);
                        await session.SendAsync("Profiler.enable", ct: ct).ConfigureAwait(false);
                        await session.SendAsync("Profiler.setSamplingInterval", new { interval = SamplingIntervalUs }, ct: ct).ConfigureAwait(false);
                        await session.SendAsync("Profiler.start", ct: ct).ConfigureAwait(false);
                        var kind = type == "service_worker" ? "service_worker"
                            : url.Contains("offscreen", StringComparison.OrdinalIgnoreCase) ? "offscreen"
                            : type;
                        lock (extraTargets) extraTargets.Add((kind, url, session));
                        LoggerService.Log($"拡張ターゲットにアタッチ: {kind} {url}", LogLevel.Debug);
                    }
                    catch (Exception ex)
                    {
                        // ターゲットが短命で attach に失敗するのは想定内（計測は続行）
                        LoggerService.Log($"拡張ターゲットへの attach に失敗 ({url}): {ex.Message}", LogLevel.Warning);
                    }
                }
            }, ct);
        }

        await cdp.SendAsync("Target.setDiscoverTargets", new { discover = true }, ct: ct).ConfigureAwait(false);

        // メインページの target を特定して attach
        var targets = await cdp.SendAsync("Target.getTargets", ct: ct).ConfigureAwait(false);
        string? pageTargetId = null;
        foreach (var t in targets.GetProperty("targetInfos").EnumerateArray())
        {
            var type = t.GetProperty("type").GetString();
            var url = t.GetProperty("url").GetString() ?? "";
            if (type == "page" && !url.StartsWith("chrome-extension://", StringComparison.Ordinal))
            {
                pageTargetId = t.GetProperty("targetId").GetString();
                break;
            }
        }
        if (pageTargetId is null)
        {
            throw new InvalidOperationException("メインページの CDP ターゲットが見つかりませんでした");
        }

        var pageAttach = await cdp.SendAsync("Target.attachToTarget", new { targetId = pageTargetId, flatten = true }, ct: ct).ConfigureAwait(false);
        var page = new CdpSession(cdp, pageAttach.GetProperty("sessionId").GetString()!, pageTargetId);

        await page.SendAsync("Page.enable", ct: ct).ConfigureAwait(false);
        await page.SendAsync("Runtime.enable", ct: ct).ConfigureAwait(false);
        await page.SendAsync("Performance.enable", ct: ct).ConfigureAwait(false);
        await page.SendAsync("Page.addScriptToEvaluateOnNewDocument", new { source = LongTaskObserverScript }, ct: ct).ConfigureAwait(false);
        await page.SendAsync("Profiler.enable", ct: ct).ConfigureAwait(false);
        await page.SendAsync("Profiler.setSamplingInterval", new { interval = SamplingIntervalUs }, ct: ct).ConfigureAwait(false);

        // Chrome trace 開始（ブラウザレベル、失敗しても計測は続行）
        var tracingStarted = false;
        if (plan.EnableTracing)
        {
            try
            {
                await cdp.SendAsync("Tracing.start", new
                {
                    transferMode = "ReturnAsStream",
                    streamFormat = "json",
                    traceConfig = new { includedCategories = TraceCategories },
                }, ct: ct).ConfigureAwait(false);
                tracingStarted = true;
            }
            catch (Exception ex)
            {
                LoggerService.Log($"Tracing.start に失敗（trace なしで続行）: {ex.Message}", LogLevel.Warning);
            }
        }

        var wall = Stopwatch.StartNew();
        await page.SendAsync("Profiler.start", ct: ct).ConfigureAwait(false);

        // ナビゲーション + シナリオステップ実行
        var loadWait = cdp.WaitForEventAsync("Page.loadEventFired", page.SessionId, timeout: TimeSpan.FromSeconds(20), ct: ct);
        await page.SendAsync("Page.navigate", new { url = scenario.Url }, ct: ct).ConfigureAwait(false);
        try
        {
            await loadWait.ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LoggerService.Log($"load イベント待機がタイムアウト（続行）: {scenario.Url}", LogLevel.Warning);
        }

        foreach (var step in scenario.Steps)
        {
            ct.ThrowIfCancellationRequested();
            await ExecuteStepAsync(page, step, ct).ConfigureAwait(false);
        }

        // ---- 収集フェーズ ----
        metrics.WallDurationMs = wall.Elapsed.TotalMilliseconds;

        // シナリオ実行中に Chrome が書き出した設定から対象拡張 ID を確定する
        var loadedExtensionIds = extensionOn
            ? await WaitForLoadedExtensionIdsAsync(userDataDir, condition.EnabledExtensions, ct).ConfigureAwait(false)
            : [];
        foreach (var (key, id) in loadedExtensionIds) metrics.LoadedExtensionIds[key] = id;
        var extensionByChromeId = condition.EnabledExtensions.ToDictionary(
            extension => loadedExtensionIds[extension.Key],
            extension => extension,
            StringComparer.Ordinal);

        // メインページの CPU profile
        var profileResult = await page.SendAsync("Profiler.stop", timeout: TimeSpan.FromSeconds(120), ct: ct).ConfigureAwait(false);
        var profileJson = profileResult.GetProperty("profile").GetRawText();
        await File.WriteAllTextAsync(Path.Combine(scenarioDir, $"{fileBase}.cpuprofile"), profileJson, ct).ConfigureAwait(false);
        var profile = CpuProfile.Parse(profileJson);
        metrics.CpuTotalMs = CpuProfileAnalyzer.ComputeTotalCpuUs(profile) / 1000.0;
        metrics.ExtensionCpuMs = extensionOn
            ? CpuProfileAnalyzer.ComputeCpuUsByUrlPrefixes(
                profile,
                [.. loadedExtensionIds.Values.Select(id => $"chrome-extension://{id}/")]) / 1000.0
            : 0;

        // Long tasks / JS heap
        await CollectPageMetricsAsync(page, metrics, ct).ConfigureAwait(false);

        // 拡張由来ターゲット（SW / Offscreen）の CPU profile
        if (extensionOn)
        {
            extTargetChannel.Writer.TryComplete();
            if (extAttachTask is not null)
            {
                try { await extAttachTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }

            List<(string Kind, string Url, CdpSession Session)> extras;
            lock (extraTargets) extras = [.. extraTargets];
            var kindIndexes = new Dictionary<string, int>();
            foreach (var (kind, url, session) in extras)
            {
                if (!TryGetExtensionId(url, out var extensionId) ||
                    !extensionByChromeId.TryGetValue(extensionId, out var extension)) continue;
                var kindIndex = kindIndexes.GetValueOrDefault(kind) + 1;
                kindIndexes[kind] = kindIndex;
                var suffix = kind == "service_worker"
                    ? (kindIndex == 1 ? "sw" : $"sw{kindIndex}")
                    : $"extra{kindIndexes.Values.Sum() - 1}";
                var file = $"{fileBase}.{extension.Key}.{suffix}.cpuprofile";
                try
                {
                    var extraResult = await session.SendAsync("Profiler.stop", timeout: TimeSpan.FromSeconds(60), ct: ct).ConfigureAwait(false);
                    var extraJson = extraResult.GetProperty("profile").GetRawText();
                    await File.WriteAllTextAsync(Path.Combine(scenarioDir, file), extraJson, ct).ConfigureAwait(false);
                    var extraCpuMs = CpuProfileAnalyzer.ComputeTotalCpuUs(CpuProfile.Parse(extraJson)) / 1000.0;
                    metrics.ExtraTargetsCpuMs += extraCpuMs;
                    metrics.ExtraTargets.Add(new ExtraTargetInfo
                    {
                        ExtensionKey = extension.Key,
                        ExtensionName = extension.Name,
                        Kind = kind,
                        TargetUrl = url,
                        CpuProfileFile = file,
                        CpuTotalMs = extraCpuMs,
                    });
                }
                catch (Exception ex)
                {
                    // SW が既に停止しているケースは想定内
                    LoggerService.Log($"追加ターゲットのプロファイル取得に失敗 ({url}): {ex.Message}", LogLevel.Warning);
                }
            }
        }

        // Chrome trace 回収
        if (tracingStarted)
        {
            try
            {
                await CollectTraceAsync(cdp, Path.Combine(scenarioDir, $"{fileBase}.trace.json"), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggerService.Log($"trace の回収に失敗（続行）: {ex.Message}", LogLevel.Warning);
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(scenarioDir, $"{fileBase}.metrics.json"),
            JsonSerializer.Serialize(metrics, JsonOptions), ct).ConfigureAwait(false);

        return metrics;
    }

    /// <summary>Chrome の Preferences から、指定した展開済み拡張が実際にロードされたことと ID を確認する</summary>
    private static async Task<Dictionary<string, string>> WaitForLoadedExtensionIdsAsync(
        string userDataDir,
        IReadOnlyList<MeasurementExtension> expectedExtensions,
        CancellationToken ct)
    {
        var preferencesPaths = new[]
        {
            Path.Combine(userDataDir, "Default", "Secure Preferences"),
            Path.Combine(userDataDir, "Default", "Preferences"),
        };
        var expectedByPath = expectedExtensions.ToDictionary(
            extension => NormalizePath(extension.Path),
            extension => extension,
            StringComparer.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var loaded = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var preferencesPath in preferencesPaths.Where(File.Exists))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(preferencesPath));
                    if (doc.RootElement.TryGetProperty("extensions", out var extensionsRoot) &&
                        extensionsRoot.TryGetProperty("settings", out var settings) &&
                        settings.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var setting in settings.EnumerateObject())
                        {
                            if (!setting.Value.TryGetProperty("path", out var pathProperty) ||
                                pathProperty.GetString() is not { Length: > 0 } loadedPath) continue;
                            if (expectedByPath.TryGetValue(NormalizePath(loadedPath), out var extension))
                            {
                                loaded[extension.Key] = setting.Name;
                            }
                        }
                    }
                }
                if (loaded.Count == expectedExtensions.Count) return loaded;
            }
            catch (IOException)
            {
                // Chrome が Preferences を書き換えている間は再試行する
            }
            catch (JsonException)
            {
                // 書き込み途中の JSON は次のポーリングで再試行する
            }
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        var names = string.Join(", ", expectedExtensions.Select(extension => extension.Name));
        throw new InvalidOperationException(
            $"指定した拡張を Chrome が読み込めませんでした: {names}。設定で Chrome for Testing を指定してください。");
    }

    private static string NormalizePath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool TryGetExtensionId(string url, out string extensionId)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == "chrome-extension" && uri.Host.Length > 0)
        {
            extensionId = uri.Host;
            return true;
        }
        extensionId = "";
        return false;
    }

    /// <summary>シナリオステップ 1 件を実行する</summary>
    private static async Task ExecuteStepAsync(CdpSession page, ScenarioStep step, CancellationToken ct)
    {
        switch (step.Type)
        {
            case StepType.Idle:
                await Task.Delay(step.DurationMs, ct).ConfigureAwait(false);
                break;

            case StepType.Scroll:
                await page.SendAsync("Runtime.evaluate", new
                {
                    expression = $"window.scrollTo({{ top: {step.ScrollY}, behavior: 'smooth' }})",
                    returnByValue = true,
                }, ct: ct).ConfigureAwait(false);
                await Task.Delay(step.DurationMs, ct).ConfigureAwait(false);
                break;

            case StepType.WaitSelector:
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Max(step.DurationMs, 1000));
                    var selectorJson = JsonSerializer.Serialize(step.Selector ?? "");
                    while (DateTime.UtcNow < deadline)
                    {
                        ct.ThrowIfCancellationRequested();
                        var result = await page.SendAsync("Runtime.evaluate", new
                        {
                            expression = $"!!document.querySelector({selectorJson})",
                            returnByValue = true,
                        }, ct: ct).ConfigureAwait(false);
                        if (result.TryGetProperty("result", out var r) &&
                            r.TryGetProperty("value", out var v) &&
                            v.ValueKind == JsonValueKind.True)
                        {
                            break;
                        }
                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }
                }
                break;

            case StepType.Keyboard:
                {
                    var shortcut = KeyboardShortcut.Parse(step.Shortcut ?? throw new InvalidOperationException("Keyboard ステップに Shortcut がありません"));
                    await page.SendAsync("Input.dispatchKeyEvent", new
                    {
                        type = "rawKeyDown",
                        modifiers = shortcut.Modifiers,
                        key = shortcut.Key,
                        code = shortcut.Code,
                        windowsVirtualKeyCode = shortcut.VirtualKeyCode,
                        nativeVirtualKeyCode = shortcut.VirtualKeyCode,
                    }, ct: ct).ConfigureAwait(false);
                    await page.SendAsync("Input.dispatchKeyEvent", new
                    {
                        type = "keyUp",
                        modifiers = shortcut.Modifiers,
                        key = shortcut.Key,
                        code = shortcut.Code,
                        windowsVirtualKeyCode = shortcut.VirtualKeyCode,
                        nativeVirtualKeyCode = shortcut.VirtualKeyCode,
                    }, ct: ct).ConfigureAwait(false);
                    await Task.Delay(step.DurationMs, ct).ConfigureAwait(false);
                }
                break;
        }
    }

    /// <summary>Long task 件数と JS ヒープをページから収集する</summary>
    private static async Task CollectPageMetricsAsync(CdpSession page, SingleRunMetrics metrics, CancellationToken ct)
    {
        try
        {
            var longTasks = await page.SendAsync("Runtime.evaluate", new
            {
                expression = "JSON.stringify(window.__extweighLongTasks || [])",
                returnByValue = true,
            }, ct: ct).ConfigureAwait(false);
            if (longTasks.TryGetProperty("result", out var r) &&
                r.TryGetProperty("value", out var v) &&
                v.GetString() is { } json)
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var entry in doc.RootElement.EnumerateArray())
                {
                    metrics.LongTaskCount++;
                    metrics.LongTaskTotalMs += entry.GetProperty("d").GetDouble();
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.Log($"Long task の収集に失敗: {ex.Message}", LogLevel.Warning);
        }

        try
        {
            var perf = await page.SendAsync("Performance.getMetrics", ct: ct).ConfigureAwait(false);
            foreach (var metric in perf.GetProperty("metrics").EnumerateArray())
            {
                var name = metric.GetProperty("name").GetString();
                var value = metric.GetProperty("value").GetDouble();
                switch (name)
                {
                    case "JSHeapUsedSize": metrics.JsHeapUsedMb = value / (1024.0 * 1024.0); break;
                    case "JSHeapTotalSize": metrics.JsHeapTotalMb = value / (1024.0 * 1024.0); break;
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.Log($"Performance.getMetrics に失敗: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>Tracing.end → tracingComplete → IO.read で trace JSON をファイルに保存する</summary>
    private static async Task CollectTraceAsync(CdpClient cdp, string outputPath, CancellationToken ct)
    {
        var completeWait = cdp.WaitForEventAsync("Tracing.tracingComplete", timeout: TimeSpan.FromSeconds(60), ct: ct);
        await cdp.SendAsync("Tracing.end", ct: ct).ConfigureAwait(false);
        var complete = await completeWait.ConfigureAwait(false);

        if (!complete.TryGetProperty("stream", out var streamProp) || streamProp.GetString() is not { } handle)
        {
            throw new InvalidOperationException("tracingComplete に stream ハンドルがありません");
        }

        await using var file = File.Create(outputPath);
        await using var writer = new StreamWriter(file);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = await cdp.SendAsync("IO.read", new { handle, size = 1 << 20 }, timeout: TimeSpan.FromSeconds(30), ct: ct).ConfigureAwait(false);
            if (chunk.TryGetProperty("base64Encoded", out var b64) && b64.ValueKind == JsonValueKind.True)
            {
                var bytes = Convert.FromBase64String(chunk.GetProperty("data").GetString() ?? "");
                await writer.FlushAsync(ct).ConfigureAwait(false);
                await file.WriteAsync(bytes, ct).ConfigureAwait(false);
            }
            else
            {
                await writer.WriteAsync(chunk.GetProperty("data").GetString() ?? "").ConfigureAwait(false);
            }
            if (chunk.TryGetProperty("eof", out var eof) && eof.ValueKind == JsonValueKind.True) break;
        }
        await writer.FlushAsync(ct).ConfigureAwait(false);
        try
        {
            await cdp.SendAsync("IO.close", new { handle }, ct: ct).ConfigureAwait(false);
        }
        catch
        {
            // ストリームクローズ失敗は無害
        }
    }
}

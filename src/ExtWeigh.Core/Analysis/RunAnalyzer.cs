using System.Text.Json;
using ExtWeigh.Core.Logging;
using ExtWeigh.Core.Models;

namespace ExtWeigh.Core.Analysis;

/// <summary>
/// 計測出力ディレクトリ（plan.json + scenarios/*/*.metrics.json + *.cpuprofile）を読み、
/// 全 OFF/全 ON 差分、1 つ抜きによる拡張別寄与、拡張由来 hot functions を集計して analysis.json を生成する。
/// </summary>
public static class RunAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>hot functions の表示上限</summary>
    private const int TopFunctionCount = 30;

    /// <summary>出力ディレクトリを解析し、analysis.json を書き出して結果を返す</summary>
    public static AnalysisResult Analyze(string outputDir)
    {
        var planPath = Path.Combine(outputDir, "plan.json");
        var plan = JsonSerializer.Deserialize<MeasurementPlan>(File.ReadAllText(planPath), JsonOptions)
            ?? throw new InvalidDataException($"plan.json の読み取りに失敗しました: {planPath}");
        var extensions = plan.GetEffectiveExtensions();

        var result = new AnalysisResult
        {
            ExtensionName = plan.ExtensionName,
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Repeat = plan.Repeat,
            Extensions = [.. extensions],
        };

        foreach (var scenario in plan.Scenarios)
        {
            var slug = scenario.Slug();
            var scenarioDir = Path.Combine(outputDir, "scenarios", slug);
            if (!Directory.Exists(scenarioDir))
            {
                LoggerService.Log($"シナリオディレクトリがありません（スキップ）: {scenarioDir}", LogLevel.Warning);
                continue;
            }

            var runs = Directory.GetFiles(scenarioDir, "*.metrics.json")
                .Select(f => JsonSerializer.Deserialize<SingleRunMetrics>(File.ReadAllText(f), JsonOptions))
                .Where(m => m is not null)
                .Select(m => m!)
                .ToList();

            var offRuns = runs.Where(IsAllOff).ToList();
            var onRuns = runs.Where(IsAllOn).ToList();

            var analysis = new ScenarioAnalysis
            {
                Name = scenario.Name,
                Url = scenario.Url,
                Slug = slug,
                TotalCpuMs = Statistics.BuildDiff(
                    [.. offRuns.Select(r => r.CpuTotalMs)],
                    [.. onRuns.Select(r => r.CpuTotalMs + r.ExtraTargetsCpuMs)]),
                LongTaskCount = Statistics.BuildDiff(
                    [.. offRuns.Select(r => (double)r.LongTaskCount)],
                    [.. onRuns.Select(r => (double)r.LongTaskCount)]),
                LongTaskTotalMs = Statistics.BuildDiff(
                    [.. offRuns.Select(r => r.LongTaskTotalMs)],
                    [.. onRuns.Select(r => r.LongTaskTotalMs)]),
                JsHeapUsedMb = Statistics.BuildDiff(
                    [.. offRuns.Select(r => r.JsHeapUsedMb)],
                    [.. onRuns.Select(r => r.JsHeapUsedMb)]),
                ExtensionCpuMsMedian = Statistics.Median([.. onRuns.Select(r => r.ExtensionCpuMs)]),
                ExtraTargetsCpuMsMedian = Statistics.Median([.. onRuns.Select(r => r.ExtraTargetsCpuMs)]),
                OffCpuRuns = [.. offRuns.OrderBy(r => r.Iteration).Select(r => Math.Round(r.CpuTotalMs, 1))],
                OnCpuRuns = [.. onRuns.OrderBy(r => r.Iteration).Select(r => Math.Round(r.CpuTotalMs + r.ExtraTargetsCpuMs, 1))],
                HotFunctions = BuildHotFunctions(scenarioDir, onRuns, extensions),
                ExtensionImpacts = BuildExtensionImpacts(runs, onRuns, offRuns, extensions),
            };
            result.Scenarios.Add(analysis);
        }

        File.WriteAllText(Path.Combine(outputDir, "analysis.json"), JsonSerializer.Serialize(result, JsonOptions));
        return result;
    }

    private static bool IsAllOff(SingleRunMetrics run)
        => run.ConditionId == "all-off" || (run.ConditionId is null && !run.ExtensionOn);

    private static bool IsAllOn(SingleRunMetrics run)
        => run.ConditionId == "all-on" || (run.ConditionId is null && run.ExtensionOn);

    private static double TotalCpu(SingleRunMetrics run) => run.CpuTotalMs + run.ExtraTargetsCpuMs;

    /// <summary>全 ON − 1 つ抜きの差を、他の拡張が有効な状態での条件付き寄与として算出する</summary>
    private static List<ExtensionImpactAnalysis> BuildExtensionImpacts(
        List<SingleRunMetrics> runs,
        List<SingleRunMetrics> allOnRuns,
        List<SingleRunMetrics> allOffRuns,
        IReadOnlyList<MeasurementExtension> extensions)
    {
        if (allOnRuns.Count == 0) return [];

        var impacts = new List<ExtensionImpactAnalysis>();
        foreach (var extension in extensions)
        {
            var withoutRuns = extensions.Count == 1
                ? allOffRuns
                : runs.Where(r => r.ConditionId == $"without-{extension.Key}").ToList();
            if (withoutRuns.Count == 0) continue;

            impacts.Add(new ExtensionImpactAnalysis
            {
                ExtensionKey = extension.Key,
                ExtensionName = extension.Name,
                CpuMs = Statistics.BuildDiff(
                    [.. withoutRuns.Select(TotalCpu)],
                    [.. allOnRuns.Select(TotalCpu)]),
                LongTaskCount = Statistics.BuildDiff(
                    [.. withoutRuns.Select(r => (double)r.LongTaskCount)],
                    [.. allOnRuns.Select(r => (double)r.LongTaskCount)]),
                LongTaskTotalMs = Statistics.BuildDiff(
                    [.. withoutRuns.Select(r => r.LongTaskTotalMs)],
                    [.. allOnRuns.Select(r => r.LongTaskTotalMs)]),
                JsHeapUsedMb = Statistics.BuildDiff(
                    [.. withoutRuns.Select(r => r.JsHeapUsedMb)],
                    [.. allOnRuns.Select(r => r.JsHeapUsedMb)]),
            });
        }
        return [.. impacts.OrderByDescending(i => i.CpuMs.Delta)];
    }

    /// <summary>保存済み analysis.json を読み込む（結果画面の再表示用）</summary>
    public static AnalysisResult? TryLoad(string outputDir)
    {
        var path = Path.Combine(outputDir, "analysis.json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<AnalysisResult>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            LoggerService.Log($"analysis.json の読み取りに失敗: {path} - {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    /// <summary>全 ON 反復の cpuprofile（page + SW + offscreen）から拡張由来 hot functions を集計する</summary>
    private static List<HotFunctionEntry> BuildHotFunctions(
        string scenarioDir,
        List<SingleRunMetrics> onRuns,
        IReadOnlyList<MeasurementExtension> extensions)
    {
        // origin+key → 累積 stats
        var merged = new Dictionary<string, (FunctionStats Stats, string Origin)>();

        foreach (var run in onRuns)
        {
            // メインページ: ロード確認済みの対象拡張 URL の関数のみ
            if (run.LoadedExtensionIds.Count > 0)
            {
                foreach (var extension in extensions)
                {
                    if (!run.LoadedExtensionIds.TryGetValue(extension.Key, out var extensionId)) continue;
                    MergeProfile(
                        Path.Combine(scenarioDir, $"{run.FileBase}.cpuprofile"),
                        $"{extension.Name} / page",
                        $"chrome-extension://{extensionId}/",
                        merged);
                }
            }
            else
            {
                // 旧形式の計測結果との互換
                MergeProfile(Path.Combine(scenarioDir, $"{run.FileBase}.cpuprofile"), "page", "chrome-extension://", merged);
            }

            // SW / Offscreen: プロファイル全体が拡張由来
            foreach (var extra in run.ExtraTargets)
            {
                var origin = extra.ExtensionName is { Length: > 0 }
                    ? $"{extra.ExtensionName} / {extra.Kind}"
                    : extra.Kind;
                MergeProfile(Path.Combine(scenarioDir, extra.CpuProfileFile), origin, null, merged);
            }
        }

        return merged.Values
            .OrderByDescending(v => v.Stats.SelfUs)
            .Take(TopFunctionCount)
            .Select(v => new HotFunctionEntry
            {
                FunctionName = v.Stats.FunctionName,
                Url = v.Stats.Url,
                LineNumber = v.Stats.LineNumber,
                Origin = v.Origin,
                SelfMs = Math.Round(v.Stats.SelfUs / 1000.0, 2),
                TotalMs = Math.Round(v.Stats.TotalUs / 1000.0, 2),
                Samples = v.Stats.Samples,
                Callers = TopRelations(v.Stats.Callers, 3),
                Children = TopRelations(v.Stats.Children, 5),
            })
            .ToList();
    }

    /// <summary>1 個の cpuprofile を merged へ加算する</summary>
    private static void MergeProfile(
        string profilePath, string origin, string? urlPrefixFilter,
        Dictionary<string, (FunctionStats Stats, string Origin)> merged)
    {
        if (!File.Exists(profilePath)) return;
        try
        {
            var profile = CpuProfile.Load(profilePath);
            foreach (var (key, stats) in CpuProfileAnalyzer.BuildFunctionStats(profile, urlPrefixFilter))
            {
                var mergedKey = $"{origin}|{key}";
                if (merged.TryGetValue(mergedKey, out var existing))
                {
                    existing.Stats.SelfUs += stats.SelfUs;
                    existing.Stats.TotalUs += stats.TotalUs;
                    existing.Stats.Samples += stats.Samples;
                    foreach (var (ck, cv) in stats.Callers)
                        existing.Stats.Callers[ck] = existing.Stats.Callers.GetValueOrDefault(ck) + cv;
                    foreach (var (ck, cv) in stats.Children)
                        existing.Stats.Children[ck] = existing.Stats.Children.GetValueOrDefault(ck) + cv;
                }
                else
                {
                    merged[mergedKey] = (stats, origin);
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.Log($"cpuprofile の解析に失敗（スキップ）: {profilePath} - {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>呼び出し関係辞書（キー → 寄与 µs）から表示用 Top N を作る</summary>
    private static List<CallRelation> TopRelations(Dictionary<string, double> relations, int count)
        => relations.OrderByDescending(kv => kv.Value)
            .Take(count)
            .Select(kv =>
            {
                // キーは "url|line|functionName"
                var parts = kv.Key.Split('|', 3);
                var url = parts.Length > 0 ? parts[0] : "";
                var line = parts.Length > 1 ? parts[1] : "";
                var fn = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : "(anonymous)";
                var location = string.IsNullOrEmpty(url) ? "" : $"{ShortenUrl(url)}:{line}";
                return new CallRelation(fn, location, Math.Round(kv.Value / 1000.0, 2));
            })
            .ToList();

    /// <summary>URL の表示を末尾ファイル名中心に短縮する</summary>
    public static string ShortenUrl(string url)
    {
        if (url.Length <= 60) return url;
        var lastSlash = url.LastIndexOf('/');
        return lastSlash >= 0 ? "…" + url[lastSlash..] : url[..57] + "…";
    }
}

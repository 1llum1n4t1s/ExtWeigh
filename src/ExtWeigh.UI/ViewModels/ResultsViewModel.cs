using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExtWeigh.Core.Analysis;
using ExtWeigh.Core.Logging;
using ExtWeigh.Core.Report;
using ExtWeigh.UI.Services;

namespace ExtWeigh.UI.ViewModels;

/// <summary>結果一覧の 1 行（過去の計測実行）</summary>
public sealed class RunListItemViewModel
{
    public required string Dir { get; init; }
    public required string DisplayName { get; init; }
    public required string GeneratedAt { get; init; }
}

/// <summary>差分メトリクス表示の 1 行</summary>
public sealed class MetricRowViewModel
{
    public required string Label { get; init; }
    public required string Off { get; init; }
    public required string On { get; init; }
    public required string Delta { get; init; }
    public required string Badge { get; init; }
    public bool IsIncrease { get; init; }
}

/// <summary>hot function 表示の 1 行</summary>
public sealed class HotFunctionRowViewModel
{
    public required string Rank { get; init; }
    public required string FunctionName { get; init; }
    public required string Location { get; init; }
    public required string Origin { get; init; }
    public required string SelfMs { get; init; }
    public required string TotalMs { get; init; }
}

/// <summary>拡張 1 件の条件付き寄与表示</summary>
public sealed class ExtensionImpactRowViewModel
{
    public required string ExtensionName { get; init; }
    public required string CpuDelta { get; init; }
    public required string LongTaskDelta { get; init; }
    public required string HeapDelta { get; init; }
    public required string Badge { get; init; }
    public required string Verdict { get; init; }
    public bool IsIncrease { get; init; }
}

/// <summary>シナリオ 1 件の結果表示</summary>
public sealed class ScenarioResultViewModel
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public List<MetricRowViewModel> Metrics { get; init; } = [];
    public List<ExtensionImpactRowViewModel> ExtensionImpacts { get; init; } = [];
    public List<HotFunctionRowViewModel> HotFunctions { get; init; } = [];
    public required string ExtensionCpuSummary { get; init; }
}

/// <summary>「結果」タブの ViewModel</summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public ObservableCollection<RunListItemViewModel> Runs { get; } = [];

    [ObservableProperty]
    public partial RunListItemViewModel? SelectedRun { get; set; }

    [ObservableProperty]
    public partial string HeaderText { get; set; } = "計測結果を選択してください。";

    public ObservableCollection<ScenarioResultViewModel> ScenarioResults { get; } = [];

    public ResultsViewModel(SettingsService settings)
    {
        _settings = settings;
        Refresh();
    }

    /// <summary>出力ルートを走査して計測実行の一覧を更新する</summary>
    [RelayCommand]
    public void Refresh()
    {
        var selected = SelectedRun?.Dir;
        Runs.Clear();
        var root = _settings.Current.OutputRoot;
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.GetDirectories(root).OrderByDescending(d => d))
        {
            var analysis = RunAnalyzer.TryLoad(dir);
            if (analysis is null) continue;
            Runs.Add(new RunListItemViewModel
            {
                Dir = dir,
                DisplayName = analysis.ExtensionName,
                GeneratedAt = analysis.GeneratedAt,
            });
        }

        if (selected is not null)
        {
            SelectedRun = Runs.FirstOrDefault(r => r.Dir == selected);
        }
    }

    /// <summary>計測完了直後に一覧を更新して該当の実行を選択する</summary>
    public void RefreshAndSelect(string outputDir)
    {
        Refresh();
        SelectedRun = Runs.FirstOrDefault(r => r.Dir == outputDir) ?? SelectedRun;
    }

    partial void OnSelectedRunChanged(RunListItemViewModel? value)
    {
        ScenarioResults.Clear();
        if (value is null)
        {
            HeaderText = "計測結果を選択してください。";
            return;
        }

        var analysis = RunAnalyzer.TryLoad(value.Dir);
        if (analysis is null)
        {
            HeaderText = "⚠️ analysis.json の読み込みに失敗しました。";
            return;
        }

        HeaderText = $"{analysis.ExtensionName} — {analysis.GeneratedAt}（反復 {analysis.Repeat} 回）";
        foreach (var s in analysis.Scenarios)
        {
            ScenarioResults.Add(new ScenarioResultViewModel
            {
                Name = s.Name,
                Url = s.Url,
                Metrics =
                [
                    BuildRow("CPU 合計 (ms)", s.TotalCpuMs, "F0"),
                    BuildRow("Long Tasks (件)", s.LongTaskCount, "F0"),
                    BuildRow("Long Tasks 合計 (ms)", s.LongTaskTotalMs, "F0"),
                    BuildRow("JS ヒープ (MB)", s.JsHeapUsedMb, "F1"),
                ],
                ExtensionImpacts = s.ExtensionImpacts.Select(i => new ExtensionImpactRowViewModel
                {
                    ExtensionName = i.ExtensionName,
                    CpuDelta = FormatDelta(i.CpuMs.Delta, "F0", " ms"),
                    LongTaskDelta = FormatDelta(i.LongTaskCount.Delta, "F0", " 件"),
                    HeapDelta = FormatDelta(i.JsHeapUsedMb.Delta, "F1", " MB"),
                    Badge = i.CpuMs.Badge,
                    Verdict = i.CpuMs.Badge == "NOISE" ? "誤差内"
                        : i.CpuMs.Delta >= 0.5 ? "悪化"
                        : i.CpuMs.Delta <= -0.5 ? "改善"
                        : "影響小",
                    IsIncrease = i.CpuMs.Delta >= 0.5,
                }).ToList(),
                HotFunctions = s.HotFunctions.Take(10).Select((f, i) => new HotFunctionRowViewModel
                {
                    Rank = (i + 1).ToString(),
                    FunctionName = f.FunctionName,
                    Location = $"{RunAnalyzer.ShortenUrl(f.Url)}:{f.LineNumber}",
                    Origin = f.Origin,
                    SelfMs = f.SelfMs.ToString("F2"),
                    TotalMs = f.TotalMs.ToString("F2"),
                }).ToList(),
                ExtensionCpuSummary =
                    $"拡張由来 CPU — page: {s.ExtensionCpuMsMedian:F1} ms / SW・Offscreen: {s.ExtraTargetsCpuMsMedian:F1} ms（ON 中央値）",
            });
        }
    }

    private static MetricRowViewModel BuildRow(string label, MetricDiff d, string format)
        => new()
        {
            Label = label,
            Off = d.OffMedian.ToString(format),
            On = d.OnMedian.ToString(format),
            Delta = d.Delta.ToString("+" + format + ";-" + format + ";" + format),
            Badge = d.Badge,
            IsIncrease = d.Delta >= 0.5,
        };

    private static string FormatDelta(double value, string format, string suffix)
        => value.ToString("+" + format + ";-" + format + ";" + format) + suffix;

    [RelayCommand]
    private void OpenReport()
    {
        if (SelectedRun is null) return;
        var reportPath = Path.Combine(SelectedRun.Dir, "report.html");
        try
        {
            if (!File.Exists(reportPath))
            {
                // 旧実行や失敗時のためにその場で再生成
                var analysis = RunAnalyzer.TryLoad(SelectedRun.Dir);
                if (analysis is null) return;
                reportPath = HtmlReportGenerator.Generate(analysis, SelectedRun.Dir);
            }
            Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LoggerService.Log($"レポートを開けませんでした: {ex.Message}", LogLevel.Warning);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedRun is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SelectedRun.Dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LoggerService.Log($"フォルダを開けませんでした: {ex.Message}", LogLevel.Warning);
        }
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExtWeigh.Core.Chrome;
using ExtWeigh.Core.Logging;
using ExtWeigh.Core.Manifest;
using ExtWeigh.Core.Measurement;
using ExtWeigh.Core.Models;
using ExtWeigh.UI.Services;

namespace ExtWeigh.UI.ViewModels;

/// <summary>「計測」タブの ViewModel</summary>
public sealed partial class MeasureViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private CancellationTokenSource? _cts;
    private int _nextExtensionNumber = 1;

    /// <summary>複数フォルダ選択ダイアログ（View 側から注入）</summary>
    public Func<Task<IReadOnlyList<string>>>? PickFoldersAsync { get; set; }

    /// <summary>計測完了時に出力ディレクトリを通知する</summary>
    public event Action<string>? MeasurementCompleted;

    [ObservableProperty]
    public partial string ExtensionSummary { get; set; } = "拡張フォルダを1つ以上追加してください。複数選択にも対応しています。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartMeasurementCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopMeasurementCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial decimal? RepeatCount { get; set; } = 1;

    [ObservableProperty]
    public partial bool EnableTracing { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowBrowser { get; set; }

    public ObservableCollection<ExtensionItemViewModel> Extensions { get; } = [];

    public ObservableCollection<ScenarioItemViewModel> Scenarios { get; } = [];

    [ObservableProperty]
    public partial string ScenarioSummary { get; set; } = "拡張フォルダを追加すると、対象サイトから候補を作成します。";

    public ObservableCollection<string> LogLines { get; } = [];

    public MeasureViewModel(SettingsService settings)
    {
        _settings = settings;
        RepeatCount = settings.Current.DefaultRepeat;
        EnableTracing = settings.Current.EnableTracing;
        ShowBrowser = settings.Current.ShowBrowser;
        var savedPaths = settings.Current.LastExtensionPaths ?? [];
        var lastPaths = savedPaths.Count > 0
            ? savedPaths
            : settings.Current.LastExtensionPath is { Length: > 0 } legacy ? [legacy] : [];
        foreach (var path in lastPaths.Where(Directory.Exists))
        {
            AddExtensionPath(path, save: false);
        }
    }

    /// <summary>manifest を解析し、重複していない拡張とシナリオを追加する</summary>
    private void AddExtensionPath(string path, bool save)
    {
        if (string.IsNullOrWhiteSpace(path) || Extensions.Any(e =>
                string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) return;

        try
        {
            var manifest = ManifestAnalyzer.Parse(path);
            var permissions = manifest.Permissions.Count > 0 ? string.Join(", ", manifest.Permissions) : "なし";
            Extensions.Add(new ExtensionItemViewModel
            {
                Key = $"extension-{_nextExtensionNumber++:00}",
                Name = manifest.Name,
                Path = path,
                Summary = $"v{manifest.Version} / permissions: {permissions} / content scripts: {manifest.ContentScripts.Count} / SW: {(manifest.HasServiceWorker ? "あり" : "なし")}",
            });

            var durationSec = _settings.Current.DefaultDurationSec;
            foreach (var scenario in ManifestAnalyzer.GenerateScenarios(manifest, durationSec))
            {
                var keyboard = scenario.Steps.FirstOrDefault(s => s.Type == StepType.Keyboard);
                if (Scenarios.Any(s => string.Equals(s.Url, scenario.Url, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.Shortcut, keyboard?.Shortcut, StringComparison.OrdinalIgnoreCase))) continue;
                AddScenarioItem(new ScenarioItemViewModel
                {
                    Name = scenario.Name,
                    Url = scenario.Url,
                    DurationSec = durationSec,
                    Shortcut = keyboard?.Shortcut,
                    SourceDescription = keyboard is null
                        ? "拡張の対象サイトから作った候補"
                        : "拡張が宣言したショートカットから作った候補",
                });
            }
            RefreshExtensionSummary();
            if (save) SaveExtensionPaths();
        }
        catch (Exception ex)
        {
            ExtensionSummary = $"⚠️ {ex.Message}";
        }
        StartMeasurementCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task AddExtensionsAsync()
    {
        if (PickFoldersAsync is null) return;
        var paths = await PickFoldersAsync();
        foreach (var path in paths) AddExtensionPath(path, save: false);
        SaveExtensionPaths();
    }

    [RelayCommand]
    private void RemoveExtension(ExtensionItemViewModel item)
    {
        Extensions.Remove(item);
        if (Extensions.Count == 0) ClearScenarios();
        RefreshExtensionSummary();
        RefreshScenarioSummary();
        SaveExtensionPaths();
        StartMeasurementCommand.NotifyCanExecuteChanged();
    }

    private void RefreshExtensionSummary()
    {
        ExtensionSummary = Extensions.Count switch
        {
            0 => "拡張フォルダを1つ以上追加してください。複数選択にも対応しています。",
            1 => "単体モード: 拡張OFF / ONを比較します。",
            _ => $"複数モード: {Extensions.Count}個を全ONにした条件と、1つずつOFFにした条件を比較します。",
        };
        RefreshScenarioSummary();
    }

    private void SaveExtensionPaths()
        => _settings.MutateAndSave(s =>
        {
            s.LastExtensionPaths = [.. Extensions.Select(e => e.Path)];
            s.LastExtensionPath = Extensions.FirstOrDefault()?.Path;
        });

    [RelayCommand]
    private void AddScenario()
    {
        AddScenarioItem(new ScenarioItemViewModel
        {
            Name = $"普段使うページ {Scenarios.Count + 1}",
            Url = "https://example.com/",
            DurationSec = _settings.Current.DefaultDurationSec,
            SourceDescription = "自分で追加 — URLを普段重く感じるページに置き換えてください",
        });
    }

    [RelayCommand]
    private void RemoveScenario(ScenarioItemViewModel item)
    {
        item.PropertyChanged -= OnScenarioPropertyChanged;
        Scenarios.Remove(item);
        RefreshScenarioSummary();
        StartMeasurementCommand.NotifyCanExecuteChanged();
    }

    private void AddScenarioItem(ScenarioItemViewModel scenario)
    {
        scenario.PropertyChanged += OnScenarioPropertyChanged;
        Scenarios.Add(scenario);
        RefreshScenarioSummary();
        StartMeasurementCommand.NotifyCanExecuteChanged();
    }

    private void ClearScenarios()
    {
        foreach (var scenario in Scenarios) scenario.PropertyChanged -= OnScenarioPropertyChanged;
        Scenarios.Clear();
        RefreshScenarioSummary();
    }

    private void OnScenarioPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScenarioItemViewModel.Enabled) or nameof(ScenarioItemViewModel.Url)
            or nameof(ScenarioItemViewModel.DurationSec))
        {
            RefreshScenarioSummary();
            StartMeasurementCommand.NotifyCanExecuteChanged();
        }
    }

    private void RefreshScenarioSummary()
    {
        var enabledCount = Scenarios.Count(s => s.Enabled);
        ScenarioSummary = enabledCount switch
        {
            0 when Scenarios.Count > 0 => "計測する使い方を、少なくとも1件選んでください。",
            0 => "拡張フォルダを追加すると、対象サイトから候補を作成します。",
            _ => $"{enabledCount}件の使い方を計測します。同じ操作を全OFF・全ON・1つずつOFFで再生して比べます。",
        };
    }

    private bool CanStartMeasurement()
        => !IsRunning && Extensions.Count > 0 && Scenarios.Any(s => s.Enabled);

    [RelayCommand(CanExecute = nameof(CanStartMeasurement))]
    private async Task StartMeasurementAsync()
    {
        var chromePath = _settings.Current.ChromePath is { Length: > 0 } configured && File.Exists(configured)
            ? configured
            : ChromeLocator.FindChrome();
        if (chromePath is null)
        {
            AppendLog("❌ chrome.exe が見つかりません。設定タブで Chrome のパスを指定してください。");
            return;
        }

        var enabledScenarios = Scenarios.Where(s => s.Enabled).Select(s => s.ToScenario()).ToList();
        if (enabledScenarios.Count == 0)
        {
            AppendLog("❌ 有効なシナリオがありません。");
            return;
        }

        var extensionName = Extensions.Count == 1
            ? Extensions[0].Name
            : string.Join(" + ", Extensions.Select(e => e.Name));
        var slugSource = Extensions.Count == 1 ? extensionName : $"{Extensions.Count}-extensions";
        var slug = new string(slugSource.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length == 0) slug = "extension";
        var outputDir = Path.Combine(
            _settings.Current.OutputRoot,
            $"{slug}_{DateTime.Now:yyyyMMdd-HHmmss}");

        var plan = new MeasurementPlan
        {
            ExtensionName = extensionName,
            Extensions = [.. Extensions.Select(e => new MeasurementExtension
            {
                Key = e.Key,
                Name = e.Name,
                Path = e.Path,
            })],
            Scenarios = enabledScenarios,
            Repeat = (int)Math.Clamp(RepeatCount ?? 1, 1, 9),
            ChromePath = chromePath,
            OutputDir = outputDir,
            EnableTracing = EnableTracing,
            ShowBrowser = ShowBrowser,
        };

        var conditionCount = Extensions.Count == 1 ? 2 : Extensions.Count + 2;
        var estimatedMin = enabledScenarios.Sum(s => s.Steps.Sum(st => st.DurationMs)) / 1000.0 * plan.Repeat * conditionCount / 60.0;
        AppendLog($"🔍 計測開始: 拡張 {Extensions.Count} 個 — シナリオ {enabledScenarios.Count} 件 × 条件 {conditionCount} 個 × {plan.Repeat} 回（推定 {estimatedMin:F0} 分 + 起動オーバーヘッド）");
        AppendLog($"📁 出力先: {outputDir}");

        IsRunning = true;
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();
        var progress = new Progress<MeasurementProgress>(p =>
        {
            ProgressPercent = p.Percent;
            StatusText = p.Message;
            AppendLog(p.Message);
        });

        try
        {
            var runner = new MeasurementRunner(plan);
            await Task.Run(() => runner.RunAsync(progress, _cts.Token));

            AppendLog("📊 解析中...");
            var analysis = await Task.Run(() => Core.Analysis.RunAnalyzer.Analyze(outputDir));
            var reportPath = await Task.Run(() => Core.Report.HtmlReportGenerator.Generate(analysis, outputDir));
            AppendLog($"✅ 完了！レポート: {reportPath}");
            StatusText = "計測完了";
            ProgressPercent = 100;
            MeasurementCompleted?.Invoke(outputDir);
        }
        catch (OperationCanceledException)
        {
            AppendLog("⏹ 計測を中断しました。");
            StatusText = "中断";
        }
        catch (Exception ex)
        {
            LoggerService.LogException("計測に失敗", ex);
            AppendLog($"❌ 計測に失敗しました: {ex.Message}");
            StatusText = "エラー";
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanStopMeasurement() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStopMeasurement))]
    private void StopMeasurement()
    {
        _cts?.Cancel();
        AppendLog("⏹ 中断要求を送信しました（現在の起動の終了を待っています）...");
    }

    private void AppendLog(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        // ログの肥大防止
        while (LogLines.Count > 500) LogLines.RemoveAt(0);
    }
}

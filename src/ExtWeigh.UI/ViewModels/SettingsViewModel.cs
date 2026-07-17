using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExtWeigh.Core.Chrome;
using ExtWeigh.Core.Logging;
using ExtWeigh.UI.Services;

namespace ExtWeigh.UI.ViewModels;

/// <summary>「設定」タブの ViewModel（変更即保存）</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private bool _loading;

    /// <summary>ファイル選択ダイアログ（View 側から注入）</summary>
    public Func<Task<string?>>? PickExeFileAsync { get; set; }

    /// <summary>フォルダ選択ダイアログ（View 側から注入）</summary>
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    [ObservableProperty]
    public partial string ChromePath { get; set; } = "";

    [ObservableProperty]
    public partial string OutputRoot { get; set; } = "";

    [ObservableProperty]
    public partial decimal? DefaultRepeat { get; set; } = 1;

    [ObservableProperty]
    public partial decimal? DefaultDurationSec { get; set; } = 30;

    [ObservableProperty]
    public partial string DetectedChromeText { get; set; } = "";

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        _loading = true;
        ChromePath = settings.Current.ChromePath ?? "";
        OutputRoot = settings.Current.OutputRoot;
        DefaultRepeat = settings.Current.DefaultRepeat;
        DefaultDurationSec = settings.Current.DefaultDurationSec;
        _loading = false;

        var detected = ChromeLocator.FindChrome();
        DetectedChromeText = detected is null
            ? "⚠️ Chrome を自動検出できませんでした。パスを手動指定してください。"
            : $"自動検出: {detected}";
    }

    partial void OnChromePathChanged(string value) => SaveIfLoaded(s => s.ChromePath = string.IsNullOrWhiteSpace(value) ? null : value);
    partial void OnOutputRootChanged(string value) => SaveIfLoaded(s => { if (!string.IsNullOrWhiteSpace(value)) s.OutputRoot = value; });
    partial void OnDefaultRepeatChanged(decimal? value) => SaveIfLoaded(s => s.DefaultRepeat = (int)Math.Clamp(value ?? 1, 1, 9));
    partial void OnDefaultDurationSecChanged(decimal? value) => SaveIfLoaded(s => s.DefaultDurationSec = (int)Math.Clamp(value ?? 30, 10, 600));

    private void SaveIfLoaded(Action<AppSettings> mutate)
    {
        if (_loading) return;
        _settings.MutateAndSave(mutate);
    }

    [RelayCommand]
    private async Task BrowseChromeAsync()
    {
        if (PickExeFileAsync is null) return;
        var path = await PickExeFileAsync();
        if (path is not null) ChromePath = path;
    }

    [RelayCommand]
    private async Task BrowseOutputRootAsync()
    {
        if (PickFolderAsync is null) return;
        var path = await PickFolderAsync();
        if (path is not null) OutputRoot = path;
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LoggerService.GetLogDirectory()}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LoggerService.Log($"ログフォルダを開けませんでした: {ex.Message}", LogLevel.Warning);
        }
    }
}

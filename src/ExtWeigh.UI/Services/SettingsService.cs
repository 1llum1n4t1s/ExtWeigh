using System.Text.Json;
using ExtWeigh.Core.Logging;

namespace ExtWeigh.UI.Services;

/// <summary>アプリ設定（settings.json に永続化）</summary>
public sealed class AppSettings
{
    /// <summary>chrome.exe のパス（null なら自動検出）</summary>
    public string? ChromePath { get; set; }

    /// <summary>計測結果の出力ルート</summary>
    public string OutputRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ExtWeigh");

    /// <summary>既定の繰り返し回数</summary>
    public int DefaultRepeat { get; set; } = 1;

    /// <summary>既定のシナリオ計測時間（秒）</summary>
    public int DefaultDurationSec { get; set; } = 30;

    /// <summary>Chrome trace (trace.json) も取得するか</summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>ブラウザウィンドウを画面内に表示するか</summary>
    public bool ShowBrowser { get; set; }

    /// <summary>直近に計測した拡張のパス</summary>
    public string? LastExtensionPath { get; set; }

    /// <summary>直近に計測した拡張パス一覧</summary>
    public List<string> LastExtensionPaths { get; set; } = [];
}

/// <summary>
/// %APPDATA%\ExtWeigh\settings.json への設定永続化。
/// UI スレッドからのみ触る前提の素朴な実装。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ExtWeigh", "settings.json");

    /// <summary>現在の設定</summary>
    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    /// <summary>settings.json から読み込む（破損時は既定値で継続）</summary>
    private void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            LoggerService.Log($"設定の読み込みに失敗（既定値で継続）: {ex.Message}", LogLevel.Warning);
            Current = new AppSettings();
        }
    }

    /// <summary>設定を変更して保存する</summary>
    public void MutateAndSave(Action<AppSettings> mutate)
    {
        mutate(Current);
        Save();
    }

    /// <summary>settings.json へ保存する</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            LoggerService.Log($"設定の保存に失敗: {ex.Message}", LogLevel.Warning);
        }
    }
}

using SuperLightLogger;

namespace ExtWeigh.Core.Logging;

/// <summary>ログレベル</summary>
public enum LogLevel
{
    /// <summary>デバッグレベル</summary>
    Debug,

    /// <summary>情報レベル</summary>
    Info,

    /// <summary>警告レベル</summary>
    Warning,

    /// <summary>エラーレベル</summary>
    Error
}

/// <summary>
/// SuperLightLogger を使用したログ出力クラス。
/// %APPDATA%\ExtWeigh\logs にローテーション出力する。
/// </summary>
public static class LoggerService
{
    /// <summary>ロガーインスタンス</summary>
    private static ILog? _logger;

    /// <summary>初期化済みフラグ（スレッドセーフ: 0=未, 1=済）</summary>
    private static int _isConfigured;

    /// <summary>ログ出力ディレクトリ</summary>
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ExtWeigh",
        "logs");

    /// <summary>ログファイル名のプレフィックス</summary>
    private const string FilePrefix = "ExtWeigh";

    /// <summary>ログ保持日数</summary>
    private const int RetentionDays = 7;

    /// <summary>最小ログレベル</summary>
    private static readonly LogLevel MinLogLevel =
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Info;
#endif

    /// <summary>ロガーを初期化する（多重呼び出しは無視）</summary>
    public static void Initialize()
    {
        if (Interlocked.CompareExchange(ref _isConfigured, 1, 0) != 0) return;

        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"LoggerService.Initialize: ログディレクトリ '{LogDirectory}' の作成に失敗。ファイルログを無効化します: {ex.Message}");
            return;
        }

        var minLevel =
#if DEBUG
            "Debug";
#else
            "Information";
#endif

        LogManager.Configure(builder =>
        {
            builder.SetMinimumLevel(minLevel);
            builder.AddSuperLightFile(opt =>
            {
                opt.FileName = Path.Combine(LogDirectory, $"{FilePrefix}_${{shortdate}}.log");
                opt.Layout = "${longdate} [${level:uppercase=true}] ${message}${onexception:inner=${newline}${exception:format=tostring}}";
                opt.ArchiveAboveSize = 10 * 1024 * 1024;
                opt.ArchiveFileName = Path.Combine(LogDirectory, $"{FilePrefix}_${{shortdate}}_{{#}}.log");
                opt.ArchiveNumbering = ArchiveNumbering.Sequence;
                opt.MaxArchiveFiles = 10;
                opt.Encoding = System.Text.Encoding.UTF8;
                opt.MinLevelName = minLevel;
            });
        });

        _logger = LogManager.GetLogger(FilePrefix);
        CleanupOldLogFiles();
        Log("Logger initialized", LogLevel.Debug);
    }

    /// <summary>アプリケーション終了時に呼び出してログをフラッシュする</summary>
    public static void Shutdown()
    {
        LogManager.Shutdown();
        Interlocked.Exchange(ref _isConfigured, 0);
    }

    /// <summary>ログ出力ディレクトリのパスを取得する</summary>
    public static string GetLogDirectory() => LogDirectory;

    /// <summary>ログを出力する</summary>
    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (level < MinLogLevel) return;
        if (Volatile.Read(ref _isConfigured) == 0) Initialize();

        switch (level)
        {
            case LogLevel.Debug: _logger?.Debug(message); break;
            case LogLevel.Info: _logger?.Info(message); break;
            case LogLevel.Warning: _logger?.Warn(message); break;
            case LogLevel.Error: _logger?.Error(message); break;
            default: _logger?.Info(message); break;
        }
    }

    /// <summary>例外情報を含むログを出力する（常に Error レベル）</summary>
    public static void LogException(string message, Exception exception)
    {
        if (Volatile.Read(ref _isConfigured) == 0) Initialize();
        _logger?.Error(message, exception);
    }

    /// <summary>保持期間を超えた古いログファイルを削除する</summary>
    private static void CleanupOldLogFiles()
    {
        try
        {
            var cutoffDate = DateTime.Now.Date.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(LogDirectory, $"{FilePrefix}_*.log"))
            {
                // ${shortdate} は "2026-07-18" 形式で展開される
                var fileName = Path.GetFileNameWithoutExtension(file);
                var parts = fileName.Split('_');
                if (parts.Length >= 2 &&
                    DateTime.TryParseExact(parts[1], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fileDate) &&
                    fileDate < cutoffDate)
                {
                    try { File.Delete(file); } catch { /* 使用中なら次回に回す */ }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"ログファイルのクリーンアップ中にエラー: {ex.Message}", LogLevel.Warning);
        }
    }
}

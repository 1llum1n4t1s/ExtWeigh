using Avalonia;
using ExtWeigh.Core.Logging;
using Velopack;

namespace ExtWeigh.UI;

internal static class Program
{
    /// <summary>単一インスタンス制御用 Mutex（GC 回収防止のため static 保持）</summary>
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        // インストール・更新・アンインストール時の Velopack フックを UI 起動前に処理する。
        VelopackApp.Build().Run();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\ExtWeigh_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // 既に起動済みなら静かに終了する
            return;
        }

        try
        {
            LoggerService.Initialize();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LoggerService.LogException("アプリケーションが致命的エラーで終了しました", ex);
            throw;
        }
        finally
        {
            LoggerService.Shutdown();
            _singleInstanceMutex.ReleaseMutex();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

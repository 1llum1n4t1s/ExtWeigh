using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ExtWeigh.Core.Logging;
using ExtWeigh.UI.Services;
using ExtWeigh.UI.ViewModels;
using ExtWeigh.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ExtWeigh.UI;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 未捕捉例外は最低限ログに残す
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LoggerService.LogException("未捕捉例外 (AppDomain)", e.ExceptionObject as Exception ?? new InvalidOperationException("unknown"));
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LoggerService.LogException("未捕捉例外 (Task)", e.Exception);
                e.SetObserved();
            };

            var services = new ServiceCollection();
            services.AddSingleton<SettingsService>();
            services.AddSingleton<MeasureViewModel>();
            services.AddSingleton<ResultsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<MainWindowViewModel>();
            var provider = services.BuildServiceProvider();

            desktop.MainWindow = new MainWindow(provider.GetRequiredService<MainWindowViewModel>());
        }

        base.OnFrameworkInitializationCompleted();
    }
}

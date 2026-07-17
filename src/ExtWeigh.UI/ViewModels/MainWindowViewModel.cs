using CommunityToolkit.Mvvm.ComponentModel;

namespace ExtWeigh.UI.ViewModels;

/// <summary>メインウィンドウの ViewModel（3 タブの束ね役）</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MeasureViewModel Measure { get; }
    public ResultsViewModel Results { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    public MainWindowViewModel(MeasureViewModel measure, ResultsViewModel results, SettingsViewModel settings)
    {
        Measure = measure;
        Results = results;
        Settings = settings;

        // 計測完了 → 結果タブへ自動遷移
        Measure.MeasurementCompleted += outputDir =>
        {
            Results.RefreshAndSelect(outputDir);
            SelectedTabIndex = 1;
        };
    }
}

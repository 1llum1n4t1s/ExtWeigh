using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ExtWeigh.UI.ViewModels;

namespace ExtWeigh.UI.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // ダイアログ表示は View の責務なのでデリゲート注入で VM から分離する
        viewModel.Measure.PickFoldersAsync = PickExtensionFoldersAsync;
        viewModel.Settings.PickFolderAsync = () => PickFolderAsync("出力フォルダを選択");
        viewModel.Settings.PickExeFileAsync = PickExeAsync;

        // ログ追記時に末尾へ自動スクロール
        viewModel.Measure.LogLines.CollectionChanged += OnLogLinesChanged;
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            LogScroll.ScrollToEnd();
        }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    private async Task<IReadOnlyList<string>> PickExtensionFoldersAsync()
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "拡張フォルダを追加（複数選択可）",
            AllowMultiple = true,
        });
        return [.. result.Select(item => item.Path.LocalPath)];
    }

    private async Task<string?> PickExeAsync()
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "chrome.exe を選択",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("実行ファイル") { Patterns = ["*.exe"] }],
        });
        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }
}

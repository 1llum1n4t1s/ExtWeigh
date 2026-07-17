using CommunityToolkit.Mvvm.ComponentModel;

namespace ExtWeigh.UI.ViewModels;

/// <summary>計測対象の拡張一覧に表示する 1 行</summary>
public sealed partial class ExtensionItemViewModel : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Summary { get; init; }
}

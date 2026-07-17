using CommunityToolkit.Mvvm.ComponentModel;
using ExtWeigh.Core.Models;

namespace ExtWeigh.UI.ViewModels;

/// <summary>計測シナリオ一覧の 1 行（編集可能）</summary>
public sealed partial class ScenarioItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Url { get; set; } = "";

    /// <summary>計測時間（秒）。NumericUpDown の Value (decimal?) に合わせて nullable</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExecutionDescription))]
    [NotifyPropertyChangedFor(nameof(EstimatedDurationDescription))]
    public partial decimal? DurationSec { get; set; } = 30;

    /// <summary>ショートカット trigger 型シナリオなら発火するショートカット（例: "Ctrl+Shift+Y"）</summary>
    public string? Shortcut { get; init; }

    /// <summary>候補がどこから作られたかを利用者向けに示す説明</summary>
    [ObservableProperty]
    public partial string SourceDescription { get; set; } = "自分で追加";

    /// <summary>この行が通常閲覧か、拡張のショートカットを試すかを示す</summary>
    public bool IsShortcutScenario => !string.IsNullOrWhiteSpace(Shortcut);

    /// <summary>利用者に見せる、実際に再生される操作列</summary>
    public string ExecutionDescription => IsShortcutScenario
        ? $"ページを開く → 3秒待機 → {Shortcut} を押す → 残りを待機"
        : "ページを開く → 5秒待機 → 2,000px スクロール → 8秒待機 → 4,000px スクロール → 残りを待機";

    /// <summary>設定値と実際のステップ列から算出した実行時間の目安</summary>
    public string EstimatedDurationDescription
    {
        get
        {
            var durationSec = (int)Math.Clamp(DurationSec ?? 30, 10, 600);
            var totalMs = IsShortcutScenario
                ? 3000 + Math.Max(durationSec * 1000 - 3000, 5000)
                : Scenario.DefaultBrowsingSteps(durationSec).Sum(step => step.DurationMs);
            return $"実行時間の目安: 約{totalMs / 1000.0:0.#}秒";
        }
    }

    /// <summary>カード見出しに使う、技術用語を避けた操作種別</summary>
    public string ScenarioTypeLabel => IsShortcutScenario ? "ショートカットを試す" : "ページを読み進める";

    /// <summary>この行の内容から実行用シナリオを組み立てる</summary>
    public Scenario ToScenario()
    {
        var durationSec = (int)Math.Clamp(DurationSec ?? 30, 10, 600);
        if (!string.IsNullOrEmpty(Shortcut))
        {
            return new Scenario
            {
                Name = Name,
                Url = Url,
                Steps =
                [
                    ScenarioStep.Idle(3000),
                    ScenarioStep.Key(Shortcut, Math.Max(durationSec * 1000 - 3000, 5000)),
                ],
            };
        }
        return new Scenario
        {
            Name = Name,
            Url = Url,
            Steps = Scenario.DefaultBrowsingSteps(durationSec),
        };
    }
}

using System.Text.Json.Serialization;

namespace ExtWeigh.Core.Models;

/// <summary>シナリオステップ種別</summary>
public enum StepType
{
    /// <summary>指定ミリ秒待機</summary>
    Idle,

    /// <summary>指定 Y 座標へスムーススクロールし、Duration ミリ秒待機</summary>
    Scroll,

    /// <summary>セレクタが現れるまで待機（タイムアウトしても続行）</summary>
    WaitSelector,

    /// <summary>キーボードショートカット発火（例: "Ctrl+Shift+Y"）</summary>
    Keyboard,
}

/// <summary>シナリオ内の 1 ステップ</summary>
public sealed class ScenarioStep
{
    [JsonConverter(typeof(JsonStringEnumConverter<StepType>))]
    public StepType Type { get; set; }

    /// <summary>Idle: 待機 ms / Scroll: スクロール後の待機 ms / WaitSelector: タイムアウト ms</summary>
    public int DurationMs { get; set; }

    /// <summary>Scroll: 目標 Y 座標 (px)</summary>
    public int ScrollY { get; set; }

    /// <summary>WaitSelector: CSS セレクタ</summary>
    public string? Selector { get; set; }

    /// <summary>Keyboard: ショートカット文字列（例: "Ctrl+Shift+Y"）</summary>
    public string? Shortcut { get; set; }

    public static ScenarioStep Idle(int ms) => new() { Type = StepType.Idle, DurationMs = ms };
    public static ScenarioStep Scroll(int y, int waitMs) => new() { Type = StepType.Scroll, ScrollY = y, DurationMs = waitMs };
    public static ScenarioStep WaitFor(string selector, int timeoutMs) => new() { Type = StepType.WaitSelector, Selector = selector, DurationMs = timeoutMs };
    public static ScenarioStep Key(string shortcut, int waitMs) => new() { Type = StepType.Keyboard, Shortcut = shortcut, DurationMs = waitMs };
}

/// <summary>計測シナリオ（代表 URL + 操作ステップ列）</summary>
public sealed class Scenario
{
    public required string Name { get; set; }
    public required string Url { get; set; }
    public List<ScenarioStep> Steps { get; set; } = [];

    /// <summary>出力ディレクトリ名などに使う安全なスラグを生成する</summary>
    public string Slug()
    {
        var chars = Name.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? "scenario" : slug;
    }

    /// <summary>標準的な閲覧ステップ（初期待機 → スクロール 2 回 → 残り待機）を生成する</summary>
    public static List<ScenarioStep> DefaultBrowsingSteps(int totalDurationSec)
    {
        var totalMs = Math.Max(totalDurationSec, 10) * 1000;
        // 固定部: 5s idle + scroll(1s 待ち) + 8s idle + scroll(1.5s 待ち) = 15.5s
        var fixedMs = 5000 + 1000 + 8000 + 1500;
        var tailMs = Math.Max(totalMs - fixedMs, 2000);
        return
        [
            ScenarioStep.Idle(5000),
            ScenarioStep.Scroll(2000, 1000),
            ScenarioStep.Idle(8000),
            ScenarioStep.Scroll(4000, 1500),
            ScenarioStep.Idle(tailMs),
        ];
    }
}

namespace ExtWeigh.Core.Models;

/// <summary>計測対象に含める展開済み Chrome 拡張</summary>
public sealed class MeasurementExtension
{
    /// <summary>計測内で一意な識別子（ファイル名にも使用）</summary>
    public required string Key { get; init; }

    /// <summary>manifest の拡張名</summary>
    public required string Name { get; init; }

    /// <summary>manifest.json が直下にある拡張ルート</summary>
    public required string Path { get; init; }
}

/// <summary>計測プラン（1 回の計測実行の入力一式）</summary>
public sealed class MeasurementPlan
{
    /// <summary>計測対象の拡張一覧。複数時は全 ON と 1 つ抜き条件を計測する。</summary>
    public List<MeasurementExtension> Extensions { get; init; } = [];

    /// <summary>結果一覧に表示する計測名</summary>
    public string ExtensionName { get; init; } = "extension";

    /// <summary>旧 plan.json 互換用の単体拡張パス</summary>
    public string? ExtensionPath { get; init; }

    /// <summary>実行するシナリオ</summary>
    public required List<Scenario> Scenarios { get; init; }

    /// <summary>各シナリオ × ON/OFF の繰り返し回数（中央値±σ算出用）</summary>
    public int Repeat { get; init; } = 1;

    /// <summary>chrome.exe のパス</summary>
    public required string ChromePath { get; init; }

    /// <summary>出力ディレクトリ（この計測実行専用、タイムスタンプ付き）</summary>
    public required string OutputDir { get; init; }

    /// <summary>Chrome trace (trace.json) も取得するか</summary>
    public bool EnableTracing { get; init; } = true;

    /// <summary>ブラウザウィンドウを画面内に表示するか</summary>
    public bool ShowBrowser { get; init; }

    /// <summary>新旧どちらの plan.json からも有効な拡張一覧を得る</summary>
    public IReadOnlyList<MeasurementExtension> GetEffectiveExtensions()
    {
        if (Extensions.Count > 0) return Extensions;
        if (!string.IsNullOrWhiteSpace(ExtensionPath))
        {
            return
            [
                new MeasurementExtension
                {
                    Key = "extension-1",
                    Name = ExtensionName,
                    Path = ExtensionPath,
                },
            ];
        }
        return [];
    }
}

/// <summary>拡張由来の追加プロファイル対象（Service Worker / Offscreen Document 等）</summary>
public sealed class ExtraTargetInfo
{
    /// <summary>対象拡張の計測内キー</summary>
    public string? ExtensionKey { get; init; }

    /// <summary>対象拡張名</summary>
    public string? ExtensionName { get; init; }

    /// <summary>種別: "service_worker" / "offscreen" / "page" / "other"</summary>
    public required string Kind { get; init; }

    /// <summary>ターゲット URL</summary>
    public required string TargetUrl { get; init; }

    /// <summary>保存した cpuprofile ファイル名</summary>
    public required string CpuProfileFile { get; init; }

    /// <summary>この対象の CPU 時間合計 (ms)</summary>
    public double CpuTotalMs { get; init; }
}

/// <summary>1 回の起動（1 シナリオ × ON or OFF × 1 反復）の計測結果</summary>
public sealed class SingleRunMetrics
{
    /// <summary>ファイル名ベース（例: "on-1"）</summary>
    public required string FileBase { get; init; }

    /// <summary>計測条件: all-off / all-on / without-&lt;extension-key&gt;</summary>
    public string? ConditionId { get; init; }

    /// <summary>この実行で有効にした拡張キー</summary>
    public List<string> EnabledExtensionKeys { get; init; } = [];

    /// <summary>計測内キー → Chrome が割り当てた拡張 ID</summary>
    public Dictionary<string, string> LoadedExtensionIds { get; init; } = [];

    /// <summary>拡張 ON での計測か</summary>
    public bool ExtensionOn { get; init; }

    /// <summary>反復番号（1 始まり）</summary>
    public int Iteration { get; init; }

    /// <summary>計測の実時間 (ms)</summary>
    public double WallDurationMs { get; set; }

    /// <summary>メインページの CPU 時間合計 (ms、(idle) を除く)</summary>
    public double CpuTotalMs { get; set; }

    /// <summary>拡張由来 (chrome-extension://) サンプルの CPU 時間 (ms、ON のみ)</summary>
    public double ExtensionCpuMs { get; set; }

    /// <summary>Service Worker / Offscreen 等の追加ターゲット CPU 時間合計 (ms)</summary>
    public double ExtraTargetsCpuMs { get; set; }

    /// <summary>Long task (>50ms) の件数</summary>
    public int LongTaskCount { get; set; }

    /// <summary>Long task の合計時間 (ms)</summary>
    public double LongTaskTotalMs { get; set; }

    /// <summary>計測終了時の JS ヒープ使用量 (MB)</summary>
    public double JsHeapUsedMb { get; set; }

    /// <summary>計測終了時の JS ヒープ確保量 (MB)</summary>
    public double JsHeapTotalMb { get; set; }

    /// <summary>追加ターゲットの詳細</summary>
    public List<ExtraTargetInfo> ExtraTargets { get; set; } = [];
}

/// <summary>計測の進捗通知</summary>
public sealed record MeasurementProgress(string Message, double Percent);

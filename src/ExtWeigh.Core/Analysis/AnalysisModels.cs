using ExtWeigh.Core.Models;

namespace ExtWeigh.Core.Analysis;

/// <summary>ON/OFF 差分（中央値ベース）と信頼性バッジ</summary>
public sealed class MetricDiff
{
    public double OffMedian { get; set; }
    public double OnMedian { get; set; }
    public double Delta { get; set; }
    public double SigmaOff { get; set; }
    public double SigmaOn { get; set; }

    /// <summary>"SIGNIF"（有意）/ "NOISE"（測定誤差内）/ "-"（判定不能: 反復 1 回）</summary>
    public string Badge { get; set; } = "-";
}

/// <summary>呼び出し関係の 1 エントリ</summary>
public sealed record CallRelation(string Function, string Location, double Ms);

/// <summary>拡張由来 hot function の 1 エントリ</summary>
public sealed class HotFunctionEntry
{
    public required string FunctionName { get; init; }
    public required string Url { get; init; }
    public int LineNumber { get; init; }

    /// <summary>実行元: "page"（content script）/ "service_worker" / "offscreen" 等</summary>
    public required string Origin { get; init; }

    /// <summary>Self Time (ms、全 ON 反復の合計)</summary>
    public double SelfMs { get; set; }

    /// <summary>Total Time (ms、全 ON 反復の合計)</summary>
    public double TotalMs { get; set; }

    public int Samples { get; set; }

    /// <summary>この関数を呼んでいる親（寄与 Total 降順 Top 3）</summary>
    public List<CallRelation> Callers { get; set; } = [];

    /// <summary>この関数が呼んでいる子（寄与 Total 降順 Top 5）</summary>
    public List<CallRelation> Children { get; set; } = [];
}

/// <summary>全 ON と「この拡張だけ OFF」の差から求めた条件付き寄与</summary>
public sealed class ExtensionImpactAnalysis
{
    public required string ExtensionKey { get; init; }
    public required string ExtensionName { get; init; }
    public MetricDiff CpuMs { get; set; } = new();
    public MetricDiff LongTaskCount { get; set; } = new();
    public MetricDiff LongTaskTotalMs { get; set; } = new();
    public MetricDiff JsHeapUsedMb { get; set; } = new();
}

/// <summary>シナリオ 1 件の解析結果</summary>
public sealed class ScenarioAnalysis
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public required string Slug { get; init; }

    public MetricDiff TotalCpuMs { get; set; } = new();
    public MetricDiff LongTaskCount { get; set; } = new();
    public MetricDiff LongTaskTotalMs { get; set; } = new();
    public MetricDiff JsHeapUsedMb { get; set; } = new();

    /// <summary>拡張由来サンプルの CPU 時間中央値 (ms、ON のみ)</summary>
    public double ExtensionCpuMsMedian { get; set; }

    /// <summary>SW / Offscreen の CPU 時間中央値 (ms、ON のみ)</summary>
    public double ExtraTargetsCpuMsMedian { get; set; }

    /// <summary>全反復の生値（レポート表示用）</summary>
    public List<double> OffCpuRuns { get; set; } = [];
    public List<double> OnCpuRuns { get; set; } = [];

    /// <summary>拡張由来 hot functions Top 30（Self Time 降順）</summary>
    public List<HotFunctionEntry> HotFunctions { get; set; } = [];

    /// <summary>拡張別の条件付き寄与（CPU 増分降順）</summary>
    public List<ExtensionImpactAnalysis> ExtensionImpacts { get; set; } = [];
}

/// <summary>計測実行 1 回分の解析結果（analysis.json の中身）</summary>
public sealed class AnalysisResult
{
    public required string ExtensionName { get; init; }
    public required string GeneratedAt { get; init; }
    public int Repeat { get; init; }
    public List<MeasurementExtension> Extensions { get; init; } = [];
    public List<ScenarioAnalysis> Scenarios { get; init; } = [];
}

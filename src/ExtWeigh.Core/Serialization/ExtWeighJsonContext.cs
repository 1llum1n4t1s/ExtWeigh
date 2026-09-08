using System.Text.Json.Serialization;
using ExtWeigh.Core.Analysis;
using ExtWeigh.Core.Models;

namespace ExtWeigh.Core.Serialization;

/// <summary>計測データ (plan.json / metrics.json / analysis.json / .cpuprofile) 用 JSON シリアライズコンテキスト(NativeAOT/トリミング対応)。</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MeasurementPlan))]
[JsonSerializable(typeof(SingleRunMetrics))]
[JsonSerializable(typeof(AnalysisResult))]
[JsonSerializable(typeof(CpuProfile))]
[JsonSerializable(typeof(string))]
internal sealed partial class ExtWeighJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExtWeigh.Core.Analysis;

/// <summary>V8 CPU profile (.cpuprofile) のコールフレーム</summary>
public sealed class CallFrame
{
    [JsonPropertyName("functionName")]
    public string FunctionName { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }
}

/// <summary>V8 CPU profile のノード</summary>
public sealed class ProfileNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("callFrame")]
    public CallFrame CallFrame { get; set; } = new();

    [JsonPropertyName("hitCount")]
    public int HitCount { get; set; }

    [JsonPropertyName("children")]
    public int[]? Children { get; set; }
}

/// <summary>V8 CPU profile (.cpuprofile フォーマット)</summary>
public sealed class CpuProfile
{
    [JsonPropertyName("nodes")]
    public ProfileNode[] Nodes { get; set; } = [];

    [JsonPropertyName("startTime")]
    public long StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public long EndTime { get; set; }

    [JsonPropertyName("samples")]
    public int[] Samples { get; set; } = [];

    [JsonPropertyName("timeDeltas")]
    public int[] TimeDeltas { get; set; } = [];

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>JSON 文字列から読み込む</summary>
    public static CpuProfile Parse(string json)
        => JsonSerializer.Deserialize<CpuProfile>(json, ParseOptions)
           ?? throw new InvalidDataException("cpuprofile の解析に失敗しました");

    /// <summary>ファイルから読み込む</summary>
    public static CpuProfile Load(string path) => Parse(File.ReadAllText(path));
}

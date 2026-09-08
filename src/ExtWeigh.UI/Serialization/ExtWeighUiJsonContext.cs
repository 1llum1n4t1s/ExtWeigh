using System.Text.Json.Serialization;
using ExtWeigh.UI.Services;

namespace ExtWeigh.UI.Serialization;

/// <summary>UI 設定 (settings.json) 用 JSON シリアライズコンテキスト(NativeAOT/トリミング対応)。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class ExtWeighUiJsonContext : JsonSerializerContext;

using System.Text.Json;
using ExtWeigh.Core.Models;

namespace ExtWeigh.Core.Manifest;

/// <summary>manifest.json の構造化情報</summary>
public sealed class ManifestInfo
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public int ManifestVersion { get; init; }
    public List<string> Permissions { get; init; } = [];
    public List<string> HostPermissions { get; init; } = [];
    public List<ContentScriptInfo> ContentScripts { get; init; } = [];
    public List<string> CommandShortcuts { get; init; } = [];
    public bool HasServiceWorker { get; init; }
}

/// <summary>content_scripts 1 エントリの要約</summary>
public sealed class ContentScriptInfo
{
    public List<string> Matches { get; init; } = [];
    public List<string> Js { get; init; } = [];
    public string? RunAt { get; init; }
}

/// <summary>
/// manifest.json を解析し、match パターンから代表 URL シナリオを推測する。
/// cxcx スキルの Step 2（LLM 推測）のヒューリスティック実装版。
/// </summary>
public static class ManifestAnalyzer
{
    /// <summary>既知ドメイン → 代表 URL の対応表（シナリオ名, URL）</summary>
    private static readonly (string HostKey, string Name, string Url)[] RepresentativeUrls =
    [
        ("youtube.com", "YouTube watch", "https://www.youtube.com/watch?v=jNQXAC9IVRw"),
        ("youtube.com", "YouTube home", "https://www.youtube.com/"),
        ("instagram.com", "Instagram explore", "https://www.instagram.com/explore/"),
        ("tiktok.com", "TikTok home", "https://www.tiktok.com/"),
        ("twitter.com", "X explore", "https://x.com/explore"),
        ("x.com", "X explore", "https://x.com/explore"),
        ("amazon.co.jp", "Amazon.co.jp home", "https://www.amazon.co.jp/"),
        ("amazon.com", "Amazon.com home", "https://www.amazon.com/"),
        ("google.com", "Google search", "https://www.google.com/search?q=performance+test"),
        ("wikipedia.org", "Wikipedia article", "https://en.wikipedia.org/wiki/JavaScript"),
        ("github.com", "GitHub repo", "https://github.com/microsoft/vscode"),
        ("reddit.com", "Reddit home", "https://www.reddit.com/"),
        ("nicovideo.jp", "ニコニコ動画 top", "https://www.nicovideo.jp/"),
    ];

    /// <summary>全 URL マッチ時のフォールバック代表 URL</summary>
    private static readonly (string Name, string Url)[] FallbackUrls =
    [
        ("Wikipedia article", "https://en.wikipedia.org/wiki/JavaScript"),
        ("Example.com", "https://example.com/"),
    ];

    /// <summary>拡張ルートの manifest.json を読み取り構造化する。MV3 以外は例外。</summary>
    public static ManifestInfo Parse(string extensionRoot)
    {
        var manifestPath = Path.Combine(extensionRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"manifest.json が見つかりません。Chrome 拡張のルートフォルダを指定してください: {extensionRoot}");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;

        var manifestVersion = root.TryGetProperty("manifest_version", out var mv) ? mv.GetInt32() : 0;
        if (manifestVersion != 3)
        {
            throw new InvalidOperationException($"manifest_version が {manifestVersion} です。ExtWeigh は MV3 拡張のみ対応しています。");
        }

        var contentScripts = new List<ContentScriptInfo>();
        if (root.TryGetProperty("content_scripts", out var cs) && cs.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in cs.EnumerateArray())
            {
                contentScripts.Add(new ContentScriptInfo
                {
                    Matches = ReadStringArray(entry, "matches"),
                    Js = ReadStringArray(entry, "js"),
                    RunAt = entry.TryGetProperty("run_at", out var ra) ? ra.GetString() : null,
                });
            }
        }

        var commandShortcuts = new List<string>();
        if (root.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Object)
        {
            foreach (var cmd in commands.EnumerateObject())
            {
                // _execute_action はツールバーボタン相当なのでシナリオ化しない
                if (cmd.Name == "_execute_action") continue;
                if (cmd.Value.TryGetProperty("suggested_key", out var sk) &&
                    sk.TryGetProperty("default", out var def) &&
                    def.GetString() is { Length: > 0 } shortcut)
                {
                    commandShortcuts.Add(shortcut);
                }
            }
        }

        return new ManifestInfo
        {
            Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "unknown" : "unknown",
            Description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            Version = root.TryGetProperty("version", out var ver) ? ver.GetString() : null,
            ManifestVersion = manifestVersion,
            Permissions = ReadStringArray(root, "permissions"),
            HostPermissions = ReadStringArray(root, "host_permissions"),
            ContentScripts = contentScripts,
            CommandShortcuts = commandShortcuts,
            HasServiceWorker = root.TryGetProperty("background", out var bg) && bg.TryGetProperty("service_worker", out _),
        };
    }

    /// <summary>
    /// manifest の match パターン + commands から代表シナリオ（最大 maxScenarios 件）を生成する。
    /// </summary>
    public static List<Scenario> GenerateScenarios(ManifestInfo manifest, int durationSec = 30, int maxScenarios = 5)
    {
        var scenarios = new List<Scenario>();
        var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allPatterns = manifest.ContentScripts.SelectMany(c => c.Matches)
            .Concat(manifest.HostPermissions)
            .ToList();

        var hasCatchAll = allPatterns.Any(IsCatchAllPattern);
        var hosts = allPatterns.Where(p => !IsCatchAllPattern(p))
            .Select(ExtractHostKey)
            .Where(h => h is not null)
            .Select(h => h!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 既知ドメインへのマッチを代表 URL に変換
        foreach (var (hostKey, name, url) in RepresentativeUrls)
        {
            if (scenarios.Count >= maxScenarios) break;
            if (hosts.Any(h => h.EndsWith(hostKey, StringComparison.OrdinalIgnoreCase)) && usedUrls.Add(url))
            {
                scenarios.Add(new Scenario { Name = name, Url = url, Steps = Scenario.DefaultBrowsingSteps(durationSec) });
            }
        }

        // 未知ホストはそのままトップページを開く
        foreach (var host in hosts)
        {
            if (scenarios.Count >= maxScenarios) break;
            if (RepresentativeUrls.Any(r => host.EndsWith(r.HostKey, StringComparison.OrdinalIgnoreCase))) continue;
            var url = $"https://{host}/";
            if (usedUrls.Add(url))
            {
                scenarios.Add(new Scenario { Name = host, Url = url, Steps = Scenario.DefaultBrowsingSteps(durationSec) });
            }
        }

        // 全 URL マッチのフォールバック
        if (hasCatchAll || scenarios.Count == 0)
        {
            foreach (var (name, url) in FallbackUrls)
            {
                if (scenarios.Count >= maxScenarios) break;
                if (usedUrls.Add(url))
                {
                    scenarios.Add(new Scenario { Name = name, Url = url, Steps = Scenario.DefaultBrowsingSteps(durationSec) });
                }
            }
        }

        // ショートカット trigger 型: キーボード発火シナリオを追加
        foreach (var shortcut in manifest.CommandShortcuts)
        {
            if (scenarios.Count >= maxScenarios) break;
            scenarios.Add(new Scenario
            {
                Name = $"Shortcut {shortcut}",
                Url = "https://en.wikipedia.org/wiki/JavaScript",
                Steps =
                [
                    ScenarioStep.Idle(3000),
                    ScenarioStep.Key(shortcut, 15000),
                ],
            });
        }

        return scenarios;
    }

    /// <summary>全 URL を対象にする match パターンか</summary>
    internal static bool IsCatchAllPattern(string pattern)
        => pattern is "<all_urls>" or "*://*/*" or "http://*/*" or "https://*/*";

    /// <summary>match パターンからホストキーを抽出する（例: "*://*.youtube.com/*" → "youtube.com"）</summary>
    internal static string? ExtractHostKey(string pattern)
    {
        var p = pattern;
        var schemeIndex = p.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0) p = p[(schemeIndex + 3)..];
        var slashIndex = p.IndexOf('/');
        if (slashIndex >= 0) p = p[..slashIndex];
        if (p.StartsWith("*.", StringComparison.Ordinal)) p = p[2..];
        if (p.Length == 0 || !p.Contains('.')) return null;
        // ホスト名として妥当な文字だけを許容する（"<all_urls>" や "*" 残りを弾く）
        if (!p.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) return null;
        return p;
    }

    private static List<string> ReadStringArray(JsonElement parent, string property)
    {
        var list = new List<string>();
        if (parent.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.GetString() is { Length: > 0 } s) list.Add(s);
            }
        }
        return list;
    }
}

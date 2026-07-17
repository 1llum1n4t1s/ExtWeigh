using ExtWeigh.Core.Manifest;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class ManifestAnalyzerTests
{
    private string _tempDir = "";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"extweigh-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 掃除失敗は無視 */ }
    }

    private void WriteManifest(string json) => File.WriteAllText(Path.Combine(_tempDir, "manifest.json"), json);

    [TestMethod]
    public void Parse_MV3のmanifestを構造化できる()
    {
        WriteManifest("""
            {
              "manifest_version": 3,
              "name": "テスト拡張",
              "version": "1.2.3",
              "permissions": ["storage", "activeTab"],
              "background": { "service_worker": "sw.js" },
              "content_scripts": [
                { "matches": ["*://*.youtube.com/*"], "js": ["content.js"], "run_at": "document_start" }
              ]
            }
            """);

        var info = ManifestAnalyzer.Parse(_tempDir);

        Assert.AreEqual("テスト拡張", info.Name);
        Assert.AreEqual("1.2.3", info.Version);
        Assert.AreEqual(3, info.ManifestVersion);
        Assert.IsTrue(info.HasServiceWorker);
        Assert.AreEqual(1, info.ContentScripts.Count);
        Assert.AreEqual("document_start", info.ContentScripts[0].RunAt);
        CollectionAssert.Contains(info.Permissions, "storage");
    }

    [TestMethod]
    public void Parse_MV2は例外()
    {
        WriteManifest("""{ "manifest_version": 2, "name": "old", "version": "1.0" }""");
        Assert.ThrowsExactly<InvalidOperationException>(() => ManifestAnalyzer.Parse(_tempDir));
    }

    [TestMethod]
    public void Parse_manifestが無ければ例外()
    {
        Assert.ThrowsExactly<FileNotFoundException>(() => ManifestAnalyzer.Parse(_tempDir));
    }

    [TestMethod]
    public void GenerateScenarios_YouTubeマッチから代表URLを生成する()
    {
        WriteManifest("""
            {
              "manifest_version": 3, "name": "yt", "version": "1.0",
              "content_scripts": [{ "matches": ["*://*.youtube.com/*"], "js": ["c.js"] }]
            }
            """);

        var scenarios = ManifestAnalyzer.GenerateScenarios(ManifestAnalyzer.Parse(_tempDir));

        Assert.IsTrue(scenarios.Any(s => s.Url.Contains("youtube.com")), "YouTube の代表 URL が含まれること");
        Assert.IsTrue(scenarios.All(s => s.Steps.Count > 0), "全シナリオにステップがあること");
    }

    [TestMethod]
    public void GenerateScenarios_全URLマッチはフォールバックURLを生成する()
    {
        WriteManifest("""
            {
              "manifest_version": 3, "name": "all", "version": "1.0",
              "content_scripts": [{ "matches": ["<all_urls>"], "js": ["c.js"] }]
            }
            """);

        var scenarios = ManifestAnalyzer.GenerateScenarios(ManifestAnalyzer.Parse(_tempDir));

        Assert.IsTrue(scenarios.Count > 0);
        Assert.IsTrue(scenarios.Any(s => s.Url.Contains("example.com") || s.Url.Contains("wikipedia.org")));
    }

    [TestMethod]
    public void GenerateScenarios_commandsからキーボードシナリオを生成する()
    {
        WriteManifest("""
            {
              "manifest_version": 3, "name": "hotkey", "version": "1.0",
              "commands": {
                "capture": { "suggested_key": { "default": "Ctrl+Shift+Y" } },
                "_execute_action": { "suggested_key": { "default": "Ctrl+Shift+E" } }
              }
            }
            """);

        var info = ManifestAnalyzer.Parse(_tempDir);
        var scenarios = ManifestAnalyzer.GenerateScenarios(info);

        CollectionAssert.Contains(info.CommandShortcuts, "Ctrl+Shift+Y");
        Assert.IsFalse(info.CommandShortcuts.Contains("Ctrl+Shift+E"), "_execute_action は除外されること");
        Assert.IsTrue(scenarios.Any(s => s.Steps.Any(st => st.Shortcut == "Ctrl+Shift+Y")));
    }

    [TestMethod]
    public void GenerateScenarios_最大件数を超えない()
    {
        WriteManifest("""
            {
              "manifest_version": 3, "name": "many", "version": "1.0",
              "content_scripts": [{
                "matches": [
                  "*://*.youtube.com/*", "*://*.instagram.com/*", "*://*.tiktok.com/*",
                  "*://*.amazon.co.jp/*", "*://*.github.com/*", "*://*.reddit.com/*",
                  "<all_urls>"
                ],
                "js": ["c.js"]
              }]
            }
            """);

        var scenarios = ManifestAnalyzer.GenerateScenarios(ManifestAnalyzer.Parse(_tempDir), maxScenarios: 5);
        Assert.IsTrue(scenarios.Count <= 5, $"5 件以下であること（実際: {scenarios.Count}）");
    }

    [TestMethod]
    public void ExtractHostKey_パターンからホストを取り出せる()
    {
        Assert.AreEqual("youtube.com", ManifestAnalyzer.ExtractHostKey("*://*.youtube.com/*"));
        Assert.AreEqual("example.com", ManifestAnalyzer.ExtractHostKey("https://example.com/path/*"));
        Assert.IsNull(ManifestAnalyzer.ExtractHostKey("*://*/*"));
        Assert.IsNull(ManifestAnalyzer.ExtractHostKey("<all_urls>"));
    }
}

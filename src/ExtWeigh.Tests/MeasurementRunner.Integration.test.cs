using ExtWeigh.Core.Analysis;
using ExtWeigh.Core.Chrome;
using ExtWeigh.Core.Measurement;
using ExtWeigh.Core.Models;
using ExtWeigh.Core.Report;

namespace ExtWeigh.Tests;

/// <summary>
/// 実 Chrome を起動して CDP 経由の計測パイプライン全体を検証する統合テスト。
/// 通常の CI では `--filter TestCategory!=Integration` で除外する。
/// </summary>
[TestClass]
public sealed class MeasurementRunnerIntegrationTests
{
    [TestMethod]
    [TestCategory("Integration")]
    [Timeout(300_000)]
    public async Task 複数拡張の一つ抜き計測で高負荷拡張を特定できる()
    {
        var chromePath = FindExtensionTestChrome();
        if (chromePath is null)
        {
            Assert.Inconclusive("Chrome が見つからないためスキップします");
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"extweigh-multi-{Guid.NewGuid():N}");
        var extADir = Path.Combine(workDir, "ext-a");
        var extBDir = Path.Combine(workDir, "ext-b");
        var outDir = Path.Combine(workDir, "out");
        Directory.CreateDirectory(extADir);
        Directory.CreateDirectory(extBDir);

        try
        {
            File.WriteAllText(Path.Combine(extADir, "manifest.json"), """
                {
                  "manifest_version": 3,
                  "name": "No-op A",
                  "version": "1.0.0",
                  "content_scripts": [
                    { "matches": ["https://example.com/*"], "js": ["content.js"] }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(extADir, "content.js"), "globalThis.extweighNoop = true;");

            File.WriteAllText(Path.Combine(extBDir, "manifest.json"), """
                {
                  "manifest_version": 3,
                  "name": "CPU Burner B",
                  "version": "1.0.0",
                  "content_scripts": [
                    { "matches": ["https://example.com/*"], "js": ["content.js"] }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(extBDir, "content.js"), """
                function burnB() {
                  const end = performance.now() + 60;
                  while (performance.now() < end) { /* busy */ }
                }
                setInterval(burnB, 200);
                """);

            var plan = new MeasurementPlan
            {
                ExtensionName = "No-op A + CPU Burner B",
                Extensions =
                [
                    new MeasurementExtension { Key = "a", Name = "No-op A", Path = extADir },
                    new MeasurementExtension { Key = "b", Name = "CPU Burner B", Path = extBDir },
                ],
                Scenarios =
                [
                    new Scenario
                    {
                        Name = "multi",
                        Url = "https://example.com/",
                        Steps = [ScenarioStep.Idle(5000)],
                    },
                ],
                Repeat = 1,
                ChromePath = chromePath,
                OutputDir = outDir,
                EnableTracing = false,
                ShowBrowser = false,
            };

            await new MeasurementRunner(plan).RunAsync(ct: CancellationToken.None);
            var analysis = RunAnalyzer.Analyze(outDir);
            var scenarioDir = Path.Combine(outDir, "scenarios", "multi");

            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "all-off-1.metrics.json")));
            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "all-on-1.metrics.json")));
            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "without-a-1.metrics.json")));
            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "without-b-1.metrics.json")));

            var scenario = analysis.Scenarios.Single();
            var b = scenario.ExtensionImpacts.Single(i => i.ExtensionKey == "b");
            Console.WriteLine($"B CPU contribution = {b.CpuMs.Delta:F1} ms");
            Assert.IsTrue(b.CpuMs.Delta > 200,
                "B を外すと CPU 時間が明確に下がり、B の正の寄与として検出されること");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* 一時ファイル掃除の失敗は無視 */ }
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [Timeout(240_000)]
    public async Task 実ChromeでミニチュアON_OFF計測が通る()
    {
        var chromePath = FindExtensionTestChrome();
        if (chromePath is null)
        {
            Assert.Inconclusive("Chrome が見つからないためスキップします");
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"extweigh-integration-{Guid.NewGuid():N}");
        var extDir = Path.Combine(workDir, "ext");
        var outDir = Path.Combine(workDir, "out");
        Directory.CreateDirectory(extDir);

        try
        {
            // CPU を意図的に食うミニ拡張を生成する
            File.WriteAllText(Path.Combine(extDir, "manifest.json"), """
                {
                  "manifest_version": 3,
                  "name": "ExtWeigh Smoke",
                  "version": "1.0.0",
                  "content_scripts": [
                    { "matches": ["https://example.com/*"], "js": ["content.js"] }
                  ],
                  "background": { "service_worker": "sw.js" }
                }
                """);
            File.WriteAllText(Path.Combine(extDir, "content.js"), """
                // ExtWeigh 統合テスト用: 300ms ごとに 50ms のビジーループで CPU を食う
                function extweighBurn() {
                  const end = performance.now() + 50;
                  while (performance.now() < end) { /* busy */ }
                }
                setInterval(extweighBurn, 300);
                """);
            File.WriteAllText(Path.Combine(extDir, "sw.js"), """
                // ExtWeigh 統合テスト用 Service Worker
                console.log('ExtWeigh smoke SW started');
                """);

            var plan = new MeasurementPlan
            {
                ExtensionPath = extDir,
                ExtensionName = "ExtWeigh Smoke",
                Scenarios =
                [
                    new Scenario
                    {
                        Name = "smoke",
                        Url = "https://example.com/",
                        Steps = [ScenarioStep.Idle(5000)],
                    },
                ],
                Repeat = 1,
                ChromePath = chromePath,
                OutputDir = outDir,
                EnableTracing = true,
                ShowBrowser = false,
            };

            var logs = new List<string>();
            var progress = new Progress<MeasurementProgress>(p => logs.Add(p.Message));
            await new MeasurementRunner(plan).RunAsync(progress, CancellationToken.None);

            // 出力ファイルの存在検証
            var scenarioDir = Path.Combine(outDir, "scenarios", "smoke");
            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "off-1.cpuprofile")), "OFF cpuprofile が存在すること");
            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "on-1.cpuprofile")), "ON cpuprofile が存在すること");
            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "off-1.metrics.json")), "OFF metrics が存在すること");
            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "on-1.metrics.json")), "ON metrics が存在すること");
            Assert.IsTrue(File.Exists(Path.Combine(scenarioDir, "on-1.trace.json")), "ON trace が存在すること");

            // 解析 → レポート生成まで通ること
            var analysis = RunAnalyzer.Analyze(outDir);
            Assert.AreEqual(1, analysis.Scenarios.Count);
            var reportPath = HtmlReportGenerator.Generate(analysis, outDir);
            Assert.IsTrue(File.Exists(reportPath));

            // 拡張が実際にロードされ CPU が計上されていること
            // （Chrome 137+ の branded ビルドは --load-extension を無視するため、その検出を兼ねる）
            var smoke = analysis.Scenarios[0];
            Console.WriteLine($"ExtensionCpuMsMedian = {smoke.ExtensionCpuMsMedian}");
            Console.WriteLine($"ExtraTargetsCpuMsMedian = {smoke.ExtraTargetsCpuMsMedian}");
            Console.WriteLine($"HotFunctions = {string.Join(", ", smoke.HotFunctions.Select(f => f.FunctionName))}");
            Assert.IsTrue(
                smoke.ExtensionCpuMsMedian > 0 || smoke.HotFunctions.Count > 0,
                "拡張由来の CPU サンプルが検出されること（0 なら --load-extension がこの Chrome で無効の可能性）");
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* 一時ファイル掃除の失敗は無視 */ }
        }
    }

    /// <summary>拡張のCLI読み込みが許可された Chrome for Testing を優先する</summary>
    private static string? FindExtensionTestChrome()
    {
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "puppeteer", "chrome");
        if (Directory.Exists(cacheRoot))
        {
            var candidates = Directory.GetFiles(cacheRoot, "chrome.exe", SearchOption.AllDirectories);
            if (candidates.Length > 0) return candidates.OrderDescending().First();
        }
        return ChromeLocator.FindChrome();
    }
}

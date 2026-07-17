using System.Text.Json;
using ExtWeigh.Core.Analysis;
using ExtWeigh.Core.Models;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class RunAnalyzerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [TestMethod]
    public void Analyze_全ONと一つ抜きから拡張別寄与を算出する()
    {
        var root = Path.Combine(Path.GetTempPath(), $"extweigh-analysis-{Guid.NewGuid():N}");
        var scenarioDir = Path.Combine(root, "scenarios", "browse");
        Directory.CreateDirectory(scenarioDir);
        try
        {
            var plan = new MeasurementPlan
            {
                ExtensionName = "A + B",
                Extensions =
                [
                    new MeasurementExtension { Key = "a", Name = "A", Path = @"C:\ext-a" },
                    new MeasurementExtension { Key = "b", Name = "B", Path = @"C:\ext-b" },
                ],
                Scenarios = [new Scenario { Name = "browse", Url = "https://example.com", Steps = [] }],
                Repeat = 2,
                ChromePath = "chrome.exe",
                OutputDir = root,
            };
            File.WriteAllText(Path.Combine(root, "plan.json"), JsonSerializer.Serialize(plan, JsonOptions));

            WriteMetrics(scenarioDir, "all-off-1", "all-off", 1, 100, []);
            WriteMetrics(scenarioDir, "all-off-2", "all-off", 2, 102, []);
            WriteMetrics(scenarioDir, "all-on-1", "all-on", 1, 160, ["a", "b"]);
            WriteMetrics(scenarioDir, "all-on-2", "all-on", 2, 162, ["a", "b"]);
            WriteMetrics(scenarioDir, "without-a-1", "without-a", 1, 150, ["b"]);
            WriteMetrics(scenarioDir, "without-a-2", "without-a", 2, 149, ["b"]);
            WriteMetrics(scenarioDir, "without-b-1", "without-b", 1, 110, ["a"]);
            WriteMetrics(scenarioDir, "without-b-2", "without-b", 2, 111, ["a"]);

            var result = RunAnalyzer.Analyze(root);

            var scenario = result.Scenarios.Single();
            Assert.AreEqual(60, scenario.TotalCpuMs.Delta, 0.001);
            Assert.AreEqual("B", scenario.ExtensionImpacts[0].ExtensionName);
            Assert.AreEqual(50.5, scenario.ExtensionImpacts[0].CpuMs.Delta, 0.001);
            Assert.AreEqual("SIGNIF", scenario.ExtensionImpacts[0].CpuMs.Badge);
            Assert.AreEqual("A", scenario.ExtensionImpacts[1].ExtensionName);
            Assert.AreEqual(11.5, scenario.ExtensionImpacts[1].CpuMs.Delta, 0.001);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* テスト一時領域 */ }
        }
    }

    private static void WriteMetrics(
        string scenarioDir,
        string fileBase,
        string conditionId,
        int iteration,
        double cpuMs,
        List<string> enabledKeys)
    {
        var metrics = new SingleRunMetrics
        {
            FileBase = fileBase,
            ConditionId = conditionId,
            EnabledExtensionKeys = enabledKeys,
            ExtensionOn = enabledKeys.Count > 0,
            Iteration = iteration,
            CpuTotalMs = cpuMs,
        };
        File.WriteAllText(
            Path.Combine(scenarioDir, $"{fileBase}.metrics.json"),
            JsonSerializer.Serialize(metrics, JsonOptions));
    }
}

using ExtWeigh.Core.Analysis;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class CpuProfileAnalyzerTests
{
    /// <summary>
    /// 合成プロファイル:
    ///   (root id=1) ── pageFunc (id=2, ページ由来)
    ///              └─ extFunc (id=3, 拡張由来) ── extChild (id=4, 拡張由来)
    ///   (idle id=5)
    /// samples: [2, 3, 4, 4, 5], timeDeltas: [100, 200, 300, 400, 500] (µs)
    /// </summary>
    private static CpuProfile BuildSyntheticProfile() => CpuProfile.Parse("""
        {
          "nodes": [
            { "id": 1, "callFrame": { "functionName": "(root)", "url": "", "lineNumber": -1 }, "hitCount": 0, "children": [2, 3, 5] },
            { "id": 2, "callFrame": { "functionName": "pageFunc", "url": "https://example.com/app.js", "lineNumber": 10 }, "hitCount": 1 },
            { "id": 3, "callFrame": { "functionName": "extFunc", "url": "chrome-extension://abc123/content.js", "lineNumber": 42 }, "hitCount": 1, "children": [4] },
            { "id": 4, "callFrame": { "functionName": "extChild", "url": "chrome-extension://abc123/content.js", "lineNumber": 99 }, "hitCount": 2 },
            { "id": 5, "callFrame": { "functionName": "(idle)", "url": "", "lineNumber": -1 }, "hitCount": 1 }
          ],
          "startTime": 0,
          "endTime": 1500,
          "samples": [2, 3, 4, 4, 5],
          "timeDeltas": [100, 200, 300, 400, 500]
        }
        """);

    [TestMethod]
    public void ComputeTotalCpuUs_idleを除いて合計する()
    {
        var total = CpuProfileAnalyzer.ComputeTotalCpuUs(BuildSyntheticProfile());
        // 100 + 200 + 300 + 400 = 1000 ((idle) の 500 は除外)
        Assert.AreEqual(1000, total, 0.001);
    }

    [TestMethod]
    public void ComputeCpuUsByUrlPrefix_拡張由来のみ合計する()
    {
        var ext = CpuProfileAnalyzer.ComputeCpuUsByUrlPrefix(BuildSyntheticProfile(), "chrome-extension://");
        // 200 (extFunc) + 300 + 400 (extChild) = 900
        Assert.AreEqual(900, ext, 0.001);
    }

    [TestMethod]
    public void ComputeCpuUsByUrlPrefixes_対象外の内蔵拡張を除外する()
    {
        var profile = CpuProfile.Parse("""
            {
              "nodes": [
                { "id": 1, "callFrame": { "functionName": "target", "url": "chrome-extension://target-id/content.js", "lineNumber": 1 } },
                { "id": 2, "callFrame": { "functionName": "builtin", "url": "chrome-extension://builtin-id/service_worker.js", "lineNumber": 1 } }
              ],
              "samples": [1, 2],
              "timeDeltas": [300, 700]
            }
            """);

        var cpu = CpuProfileAnalyzer.ComputeCpuUsByUrlPrefixes(
            profile,
            ["chrome-extension://target-id/"]);

        Assert.AreEqual(300, cpu, 0.001);
    }

    [TestMethod]
    public void BuildFunctionStats_SelfとTotalが正しい()
    {
        var stats = CpuProfileAnalyzer.BuildFunctionStats(BuildSyntheticProfile());

        var extFunc = stats.Values.Single(s => s.FunctionName == "extFunc");
        Assert.AreEqual(200, extFunc.SelfUs, 0.001);
        // Total = 自身 200 + 子 extChild 700 = 900
        Assert.AreEqual(900, extFunc.TotalUs, 0.001);

        var extChild = stats.Values.Single(s => s.FunctionName == "extChild");
        Assert.AreEqual(700, extChild.SelfUs, 0.001);
        Assert.AreEqual(700, extChild.TotalUs, 0.001);
        Assert.AreEqual(2, extChild.Samples);
    }

    [TestMethod]
    public void BuildFunctionStats_拡張フィルタでページ関数が除外される()
    {
        var stats = CpuProfileAnalyzer.BuildFunctionStats(BuildSyntheticProfile(), "chrome-extension://");

        Assert.IsTrue(stats.Values.All(s => s.Url.StartsWith("chrome-extension://")));
        Assert.IsFalse(stats.Values.Any(s => s.FunctionName == "pageFunc"));
        Assert.AreEqual(2, stats.Count);
    }

    [TestMethod]
    public void BuildFunctionStats_呼び出し関係が記録される()
    {
        var stats = CpuProfileAnalyzer.BuildFunctionStats(BuildSyntheticProfile());

        var extFunc = stats.Values.Single(s => s.FunctionName == "extFunc");
        // extFunc の子に extChild（寄与 700µs）
        var childKey = FunctionStats.MakeKey("chrome-extension://abc123/content.js", 99, "extChild");
        Assert.IsTrue(extFunc.Children.ContainsKey(childKey));
        Assert.AreEqual(700, extFunc.Children[childKey], 0.001);

        var extChild = stats.Values.Single(s => s.FunctionName == "extChild");
        var parentKey = FunctionStats.MakeKey("chrome-extension://abc123/content.js", 42, "extFunc");
        Assert.IsTrue(extChild.Callers.ContainsKey(parentKey));
    }

    [TestMethod]
    public void BuildFunctionStats_idleは含まれない()
    {
        var stats = CpuProfileAnalyzer.BuildFunctionStats(BuildSyntheticProfile());
        Assert.IsFalse(stats.Values.Any(s => s.FunctionName == "(idle)"));
    }
}

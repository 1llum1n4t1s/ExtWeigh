using ExtWeigh.Core.Analysis;

namespace ExtWeigh.Tests;

[TestClass]
public sealed class StatisticsTests
{
    [TestMethod]
    public void Median_奇数個は中央の値()
    {
        Assert.AreEqual(20, Statistics.Median([30, 10, 20]));
    }

    [TestMethod]
    public void Median_偶数個は中央2つの平均()
    {
        Assert.AreEqual(15, Statistics.Median([10, 20, 30, 5]));
    }

    [TestMethod]
    public void Median_空は0()
    {
        Assert.AreEqual(0, Statistics.Median([]));
    }

    [TestMethod]
    public void StdDev_1個以下は0()
    {
        Assert.AreEqual(0, Statistics.StdDev([42]));
        Assert.AreEqual(0, Statistics.StdDev([]));
    }

    [TestMethod]
    public void StdDev_母標準偏差を計算する()
    {
        // [2, 4, 4, 4, 5, 5, 7, 9] の母標準偏差は 2
        Assert.AreEqual(2, Statistics.StdDev([2, 4, 4, 4, 5, 5, 7, 9]), 0.0001);
    }

    [TestMethod]
    public void BuildDiff_反復1回はバッジ判定不能()
    {
        var diff = Statistics.BuildDiff([100], [300]);
        Assert.AreEqual(200, diff.Delta, 0.001);
        Assert.AreEqual("-", diff.Badge);
    }

    [TestMethod]
    public void BuildDiff_大きな差はSIGNIF()
    {
        // σ が小さく Δ が大きい → SIGNIF
        var diff = Statistics.BuildDiff([100, 102, 98], [500, 505, 495]);
        Assert.AreEqual("SIGNIF", diff.Badge);
        Assert.IsTrue(diff.Delta > 300);
    }

    [TestMethod]
    public void BuildDiff_誤差内の差はNOISE()
    {
        // Δ が σ 合計の半分未満 → NOISE
        var diff = Statistics.BuildDiff([100, 200, 300], [110, 210, 310]);
        Assert.AreEqual("NOISE", diff.Badge);
    }
}

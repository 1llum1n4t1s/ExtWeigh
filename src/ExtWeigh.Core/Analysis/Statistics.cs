namespace ExtWeigh.Core.Analysis;

/// <summary>中央値・標準偏差・有意性バッジの計算</summary>
public static class Statistics
{
    /// <summary>中央値（空なら 0）</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    /// <summary>母標準偏差（要素 1 個以下なら 0）</summary>
    public static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count <= 1) return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }

    /// <summary>
    /// ON/OFF 生値から差分と有意性バッジを算出する。
    /// |Δ| &gt; σon+σoff なら SIGNIF、|Δ| &lt; (σon+σoff)/2 なら NOISE。反復 1 回は判定不能 "-"。
    /// </summary>
    public static MetricDiff BuildDiff(IReadOnlyList<double> offValues, IReadOnlyList<double> onValues)
    {
        var diff = new MetricDiff
        {
            OffMedian = Median(offValues),
            OnMedian = Median(onValues),
            SigmaOff = StdDev(offValues),
            SigmaOn = StdDev(onValues),
        };
        diff.Delta = diff.OnMedian - diff.OffMedian;

        if (offValues.Count >= 2 && onValues.Count >= 2)
        {
            var sigmaSum = diff.SigmaOn + diff.SigmaOff;
            var absDelta = Math.Abs(diff.Delta);
            diff.Badge = absDelta > sigmaSum ? "SIGNIF"
                : absDelta < sigmaSum / 2 ? "NOISE"
                : "-";
        }
        return diff;
    }
}

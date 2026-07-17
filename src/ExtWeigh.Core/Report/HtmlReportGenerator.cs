using System.Net;
using System.Text;
using ExtWeigh.Core.Analysis;

namespace ExtWeigh.Core.Report;

/// <summary>
/// analysis.json の内容から自己完結の report.html を生成する。
/// flamegraph は同梱しない代わりに、各 .cpuprofile を Chrome DevTools /
/// speedscope.app へ読み込む手順をレポート内に明記する。
/// </summary>
public static class HtmlReportGenerator
{
    /// <summary>report.html を出力ディレクトリ直下に生成し、そのパスを返す</summary>
    public static string Generate(AnalysisResult analysis, string outputDir)
    {
        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="ja">
            <head>
            <meta charset="utf-8">
            <title>ExtWeigh レポート — {{H(analysis.ExtensionName)}}</title>
            <style>
              :root { color-scheme: dark; }
              body { font-family: 'Segoe UI', 'Yu Gothic UI', sans-serif; background: #16181d; color: #e6e8eb; margin: 0; padding: 24px 32px; }
              h1 { font-size: 22px; margin: 0 0 4px; }
              h2 { font-size: 17px; margin: 32px 0 8px; border-left: 4px solid #4f8ef7; padding-left: 10px; }
              .meta { color: #9aa2ad; font-size: 13px; margin-bottom: 20px; }
              .cards { display: flex; gap: 14px; flex-wrap: wrap; margin: 18px 0; }
              .card { background: #1e222a; border: 1px solid #2b313c; border-radius: 10px; padding: 14px 20px; min-width: 150px; }
              .card .label { font-size: 12px; color: #9aa2ad; }
              .card .value { font-size: 22px; font-weight: 600; margin-top: 4px; }
              .card .value.pos { color: #ff7b72; }
              .card .value.neg { color: #56d364; }
              table { border-collapse: collapse; width: 100%; font-size: 13px; margin: 10px 0 24px; }
              th, td { border: 1px solid #2b313c; padding: 6px 10px; text-align: right; }
              th { background: #1e222a; cursor: pointer; user-select: none; }
              td.l, th.l { text-align: left; }
              tr:nth-child(even) td { background: #191c22; }
              .badge { display: inline-block; padding: 1px 8px; border-radius: 8px; font-size: 11px; font-weight: 600; }
              .badge.signif { background: #b3261e; color: #fff; }
              .badge.noise { background: #5f6368; color: #fff; }
              .badge.none { background: #2b313c; color: #9aa2ad; }
              .delta-pos { color: #ff7b72; font-weight: 600; }
              .delta-neg { color: #56d364; font-weight: 600; }
              .url { color: #9aa2ad; font-size: 11px; }
              .hint { background: #1e2a1e; border: 1px solid #2d4a2d; border-radius: 8px; padding: 10px 14px; font-size: 13px; color: #b9d4b9; }
              details { margin: 4px 0; }
              summary { cursor: pointer; color: #8ab4f8; }
              code { background: #262b34; border-radius: 4px; padding: 1px 5px; font-size: 12px; }
            </style>
            </head>
            <body>
            <h1>⚖️ ExtWeigh レポート — {{H(analysis.ExtensionName)}}</h1>
            <div class="meta">生成: {{H(analysis.GeneratedAt)}} / 反復: {{analysis.Repeat}} 回 / シナリオ: {{analysis.Scenarios.Count}} 件</div>
            """);

        // サマリーカード
        var avgDelta = analysis.Scenarios.Count > 0 ? analysis.Scenarios.Average(s => s.TotalCpuMs.Delta) : 0;
        var totalLongTaskDelta = analysis.Scenarios.Sum(s => s.LongTaskCount.Delta);
        var profilePrefix = analysis.Extensions.Count > 1 ? "all-on" : "on";
        sb.Append($"""
            <div class="cards">
              <div class="card"><div class="label">平均 CPU 増分 (ON−OFF)</div><div class="value {(avgDelta >= 0 ? "pos" : "neg")}">{avgDelta:+0;-0;0} ms</div></div>
              <div class="card"><div class="label">Long Tasks 増分合計</div><div class="value {(totalLongTaskDelta >= 0 ? "pos" : "neg")}">{totalLongTaskDelta:+0;-0;0} 件</div></div>
              <div class="card"><div class="label">シナリオ数</div><div class="value">{analysis.Scenarios.Count}</div></div>
            </div>
            <div class="hint">💡 複数拡張の寄与は「全 ON − その拡張だけ OFF」です。正なら悪化、負なら改善を示します。flamegraph は <code>scenarios/&lt;シナリオ&gt;/{profilePrefix}-1.cpuprofile</code> を Chrome DevTools または speedscope.app で開けます。</div>
            """);

        foreach (var s in analysis.Scenarios)
        {
            sb.Append($"""
                <h2>{H(s.Name)}</h2>
                <div class="meta"><a href="{H(s.Url)}" style="color:#8ab4f8">{H(s.Url)}</a> — プロファイル: <code>scenarios/{H(s.Slug)}/</code></div>
                <h3>拡張別の条件付き寄与</h3>
                <table class="sortable">
                  <tr><th class="l">拡張</th><th>CPU 寄与 (ms)</th><th>Long Tasks (件)</th><th>Long Tasks (ms)</th><th>JS ヒープ (MB)</th><th>CPU 判定</th></tr>
                  {string.Join("", s.ExtensionImpacts.Select(ImpactRow))}
                </table>
                <h3>全 OFF / 全 ON</h3>
                <table>
                  <tr><th class="l">指標</th><th>全 OFF (中央値)</th><th>全 ON (中央値)</th><th>Δ</th><th>σOFF</th><th>σON</th><th>判定</th></tr>
                  {MetricRow("CPU 合計 (ms)", s.TotalCpuMs, "F0")}
                  {MetricRow("Long Tasks (件)", s.LongTaskCount, "F0")}
                  {MetricRow("Long Tasks 合計 (ms)", s.LongTaskTotalMs, "F0")}
                  {MetricRow("JS ヒープ (MB)", s.JsHeapUsedMb, "F1")}
                </table>
                """);

            if (s.OnCpuRuns.Count > 1)
            {
                sb.Append($"""
                    <div class="meta">生値 — OFF: [{string.Join(", ", s.OffCpuRuns.Select(v => v.ToString("F0")))}] ms / ON: [{string.Join(", ", s.OnCpuRuns.Select(v => v.ToString("F0")))}] ms</div>
                    """);
            }

            sb.Append($"""
                <div class="meta">全拡張由来 CPU (page): {s.ExtensionCpuMsMedian:F1} ms / SW・Offscreen: {s.ExtraTargetsCpuMsMedian:F1} ms（全 ON 中央値）</div>
                """);

            if (s.HotFunctions.Count > 0)
            {
                sb.Append("""
                    <table class="sortable">
                      <tr><th>#</th><th class="l">関数</th><th class="l">場所</th><th class="l">実行元</th><th>Self (ms)</th><th>Total (ms)</th><th>Samples</th></tr>
                    """);
                var rank = 1;
                foreach (var f in s.HotFunctions)
                {
                    sb.Append($"""
                        <tr><td>{rank++}</td><td class="l">{H(f.FunctionName)}{RelationDetails(f)}</td><td class="l"><span class="url">{H(RunAnalyzer.ShortenUrl(f.Url))}:{f.LineNumber}</span></td><td class="l">{H(f.Origin)}</td><td>{f.SelfMs:F2}</td><td>{f.TotalMs:F2}</td><td>{f.Samples}</td></tr>
                        """);
                }
                sb.Append("</table>");
            }
            else
            {
                sb.Append("""<div class="meta">拡張由来の hot function は検出されませんでした（サンプルなし）。</div>""");
            }
        }

        // テーブルソート用の最小 JS
        sb.Append("""
            <script>
            document.querySelectorAll('table.sortable th').forEach((th, idx) => {
              th.addEventListener('click', () => {
                const table = th.closest('table');
                const rows = [...table.querySelectorAll('tr')].slice(1);
                const asc = th.dataset.asc !== 'true';
                th.dataset.asc = asc;
                rows.sort((a, b) => {
                  const av = a.children[idx].textContent.trim();
                  const bv = b.children[idx].textContent.trim();
                  const an = parseFloat(av), bn = parseFloat(bv);
                  const cmp = (!isNaN(an) && !isNaN(bn)) ? an - bn : av.localeCompare(bv);
                  return asc ? cmp : -cmp;
                });
                rows.forEach(r => table.appendChild(r));
              });
            });
            </script>
            </body></html>
            """);

        var path = Path.Combine(outputDir, "report.html");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    /// <summary>差分テーブルの 1 行を生成する</summary>
    private static string MetricRow(string label, MetricDiff d, string format)
    {
        var badgeClass = d.Badge switch { "SIGNIF" => "signif", "NOISE" => "noise", _ => "none" };
        var deltaClass = d.Delta >= 0.5 ? "delta-pos" : d.Delta <= -0.5 ? "delta-neg" : "";
        return $"""
            <tr><td class="l">{H(label)}</td><td>{d.OffMedian.ToString(format)}</td><td>{d.OnMedian.ToString(format)}</td><td class="{deltaClass}">{d.Delta.ToString("+" + format + ";-" + format + ";" + format)}</td><td>{d.SigmaOff.ToString(format)}</td><td>{d.SigmaOn.ToString(format)}</td><td><span class="badge {badgeClass}">{H(d.Badge)}</span></td></tr>
            """;
    }

    private static string ImpactRow(ExtensionImpactAnalysis impact)
    {
        var badgeClass = impact.CpuMs.Badge switch { "SIGNIF" => "signif", "NOISE" => "noise", _ => "none" };
        var deltaClass = impact.CpuMs.Delta >= 0.5 ? "delta-pos" : impact.CpuMs.Delta <= -0.5 ? "delta-neg" : "";
        return $"""
            <tr><td class="l">{H(impact.ExtensionName)}</td><td class="{deltaClass}">{impact.CpuMs.Delta:+0;-0;0}</td><td>{impact.LongTaskCount.Delta:+0;-0;0}</td><td>{impact.LongTaskTotalMs.Delta:+0;-0;0}</td><td>{impact.JsHeapUsedMb.Delta:+0.0;-0.0;0.0}</td><td><span class="badge {badgeClass}">{H(impact.CpuMs.Badge)}</span></td></tr>
            """;
    }

    /// <summary>Callers / Children の折りたたみ表示を生成する</summary>
    private static string RelationDetails(HotFunctionEntry f)
    {
        if (f.Callers.Count == 0 && f.Children.Count == 0) return "";
        var sb = new StringBuilder("<details><summary>呼び出し関係</summary>");
        if (f.Callers.Count > 0)
        {
            sb.Append("<div class=\"url\">Callers: ");
            sb.Append(string.Join(" / ", f.Callers.Select(c => $"{H(c.Function)} ({c.Ms:F1}ms)")));
            sb.Append("</div>");
        }
        if (f.Children.Count > 0)
        {
            sb.Append("<div class=\"url\">Children: ");
            sb.Append(string.Join(" / ", f.Children.Select(c => $"{H(c.Function)} ({c.Ms:F1}ms)")));
            sb.Append("</div>");
        }
        sb.Append("</details>");
        return sb.ToString();
    }

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
}

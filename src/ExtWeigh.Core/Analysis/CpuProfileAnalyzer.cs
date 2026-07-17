namespace ExtWeigh.Core.Analysis;

/// <summary>関数（url + line + functionName でグループ化）ごとの集計値</summary>
public sealed class FunctionStats
{
    public required string FunctionName { get; init; }
    public required string Url { get; init; }
    public int LineNumber { get; init; }

    /// <summary>Self Time (µs): この関数自身が直接実行されていた時間</summary>
    public double SelfUs { get; set; }

    /// <summary>Total Time (µs): 自身 + 子孫を含む時間</summary>
    public double TotalUs { get; set; }

    /// <summary>サンプル数</summary>
    public int Samples { get; set; }

    /// <summary>この関数を呼んでいる親関数（キー → 寄与 Total µs）</summary>
    public Dictionary<string, double> Callers { get; } = [];

    /// <summary>この関数が呼んでいる子関数（キー → 寄与 Total µs）</summary>
    public Dictionary<string, double> Children { get; } = [];

    /// <summary>グループ化キー</summary>
    public string Key => MakeKey(Url, LineNumber, FunctionName);

    public static string MakeKey(string url, int line, string functionName)
        => $"{url}|{line}|{functionName}";
}

/// <summary>
/// V8 CPU profile を解析して Self/Total Time・呼び出し関係・拡張由来フィルタを提供する。
/// </summary>
public static class CpuProfileAnalyzer
{
    /// <summary>集計から除外するメタノード名（CPU 時間に数えない）</summary>
    private static readonly HashSet<string> IdleNames = ["(idle)"];

    /// <summary>
    /// プロファイル全体の CPU 時間 (µs) を返す（(idle) サンプルを除く）。
    /// </summary>
    public static double ComputeTotalCpuUs(CpuProfile profile)
    {
        var nodeById = profile.Nodes.ToDictionary(n => n.Id);
        double total = 0;
        for (var i = 0; i < profile.Samples.Length && i < profile.TimeDeltas.Length; i++)
        {
            var delta = profile.TimeDeltas[i];
            if (delta <= 0) continue;
            if (nodeById.TryGetValue(profile.Samples[i], out var node) &&
                !IdleNames.Contains(node.CallFrame.FunctionName))
            {
                total += delta;
            }
        }
        return total;
    }

    /// <summary>
    /// 指定 URL プレフィックス（例: "chrome-extension://"）に属するサンプルの CPU 時間 (µs) を返す。
    /// </summary>
    public static double ComputeCpuUsByUrlPrefix(CpuProfile profile, string urlPrefix)
    {
        var nodeById = profile.Nodes.ToDictionary(n => n.Id);
        double total = 0;
        for (var i = 0; i < profile.Samples.Length && i < profile.TimeDeltas.Length; i++)
        {
            var delta = profile.TimeDeltas[i];
            if (delta <= 0) continue;
            if (nodeById.TryGetValue(profile.Samples[i], out var node) &&
                node.CallFrame.Url.StartsWith(urlPrefix, StringComparison.Ordinal))
            {
                total += delta;
            }
        }
        return total;
    }

    /// <summary>複数の URL プレフィックスのいずれかに属する CPU 時間 (µs) を返す。</summary>
    public static double ComputeCpuUsByUrlPrefixes(CpuProfile profile, IReadOnlyCollection<string> urlPrefixes)
    {
        if (urlPrefixes.Count == 0) return 0;
        var nodeById = profile.Nodes.ToDictionary(n => n.Id);
        double total = 0;
        for (var i = 0; i < profile.Samples.Length && i < profile.TimeDeltas.Length; i++)
        {
            var delta = profile.TimeDeltas[i];
            if (delta <= 0) continue;
            if (nodeById.TryGetValue(profile.Samples[i], out var node) &&
                urlPrefixes.Any(prefix => node.CallFrame.Url.StartsWith(prefix, StringComparison.Ordinal)))
            {
                total += delta;
            }
        }
        return total;
    }

    /// <summary>
    /// 関数単位の統計（Self/Total/Callers/Children）を集計する。
    /// </summary>
    /// <param name="profile">解析対象プロファイル</param>
    /// <param name="urlPrefixFilter">null 以外なら、この URL プレフィックスの関数のみ結果に残す（Callers/Children はフィルタ外も含む）</param>
    public static Dictionary<string, FunctionStats> BuildFunctionStats(CpuProfile profile, string? urlPrefixFilter = null)
    {
        var nodeById = profile.Nodes.ToDictionary(n => n.Id);

        // ノードごとの Self Time (µs) をサンプル列から積算
        var selfUsByNode = new Dictionary<int, double>();
        var samplesByNode = new Dictionary<int, int>();
        for (var i = 0; i < profile.Samples.Length && i < profile.TimeDeltas.Length; i++)
        {
            var delta = profile.TimeDeltas[i];
            if (delta <= 0) continue;
            var nodeId = profile.Samples[i];
            selfUsByNode[nodeId] = selfUsByNode.GetValueOrDefault(nodeId) + delta;
            samplesByNode[nodeId] = samplesByNode.GetValueOrDefault(nodeId) + 1;
        }

        // 親リンクを構築し、葉から順に Total Time を積み上げる（反復・非再帰）
        var parentByNode = new Dictionary<int, int>();
        foreach (var node in profile.Nodes)
        {
            if (node.Children is null) continue;
            foreach (var child in node.Children) parentByNode[child] = node.Id;
        }

        var totalUsByNode = new Dictionary<int, double>();
        foreach (var node in profile.Nodes) totalUsByNode[node.Id] = selfUsByNode.GetValueOrDefault(node.Id);

        // 子から親へ伝播: トポロジカル順（深さ降順）で積み上げる
        var depth = new Dictionary<int, int>();
        foreach (var node in profile.Nodes) depth[node.Id] = ComputeDepth(node.Id, parentByNode, depth);
        foreach (var node in profile.Nodes.OrderByDescending(n => depth[n.Id]))
        {
            if (parentByNode.TryGetValue(node.Id, out var parentId))
            {
                totalUsByNode[parentId] = totalUsByNode.GetValueOrDefault(parentId) + totalUsByNode.GetValueOrDefault(node.Id);
            }
        }

        // 関数キー単位に集約
        var stats = new Dictionary<string, FunctionStats>();
        foreach (var node in profile.Nodes)
        {
            var frame = node.CallFrame;
            if (IdleNames.Contains(frame.FunctionName)) continue;

            var key = FunctionStats.MakeKey(frame.Url, frame.LineNumber, frame.FunctionName);
            if (!stats.TryGetValue(key, out var s))
            {
                stats[key] = s = new FunctionStats
                {
                    FunctionName = string.IsNullOrEmpty(frame.FunctionName) ? "(anonymous)" : frame.FunctionName,
                    Url = frame.Url,
                    LineNumber = frame.LineNumber,
                };
            }
            s.SelfUs += selfUsByNode.GetValueOrDefault(node.Id);
            s.TotalUs += totalUsByNode.GetValueOrDefault(node.Id);
            s.Samples += samplesByNode.GetValueOrDefault(node.Id);

            // 呼び出し関係（親→この関数、この関数→子）を Total 寄与で記録
            if (parentByNode.TryGetValue(node.Id, out var parentId) && nodeById.TryGetValue(parentId, out var parent))
            {
                var parentKey = FunctionStats.MakeKey(parent.CallFrame.Url, parent.CallFrame.LineNumber, parent.CallFrame.FunctionName);
                if (parentKey != key)
                {
                    s.Callers[parentKey] = s.Callers.GetValueOrDefault(parentKey) + totalUsByNode.GetValueOrDefault(node.Id);
                }
            }
            if (node.Children is not null)
            {
                foreach (var childId in node.Children)
                {
                    if (!nodeById.TryGetValue(childId, out var child)) continue;
                    var childKey = FunctionStats.MakeKey(child.CallFrame.Url, child.CallFrame.LineNumber, child.CallFrame.FunctionName);
                    if (childKey != key)
                    {
                        s.Children[childKey] = s.Children.GetValueOrDefault(childKey) + totalUsByNode.GetValueOrDefault(childId);
                    }
                }
            }
        }

        if (urlPrefixFilter is not null)
        {
            return stats.Where(kv => kv.Value.Url.StartsWith(urlPrefixFilter, StringComparison.Ordinal))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        return stats;
    }

    /// <summary>ルートからの深さをメモ化付き・非再帰で計算する（ルート = 0）</summary>
    private static int ComputeDepth(int nodeId, Dictionary<int, int> parentByNode, Dictionary<int, int> memo)
    {
        // 未計算の祖先をスタックに積み、既知の深さ（またはルート）まで遡る
        var stack = new Stack<int>();
        var current = nodeId;
        while (!memo.ContainsKey(current))
        {
            stack.Push(current);
            if (!parentByNode.TryGetValue(current, out var parent))
            {
                memo[current] = 0;
                break;
            }
            current = parent;
            if (stack.Count > 1_000_000) break; // 循環防御
        }

        // 遡った経路を根本側から埋め直す
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (memo.ContainsKey(n)) continue;
            memo[n] = parentByNode.TryGetValue(n, out var p) ? memo.GetValueOrDefault(p) + 1 : 0;
        }
        return memo.GetValueOrDefault(nodeId);
    }
}

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>共用、定向、水密邊界，供 CAD 與視覺化匯出。</summary>
/// <remarks>由 Hank Scan3D 的 SolidModel 搬入，邏輯不改。</remarks>
public sealed class SolidModel
{
    /// <summary>頂點。</summary>
    public List<ScanVector> Vertices { get; } = [];

    /// <summary>三角形。</summary>
    public List<SolidTriangle> Triangles { get; } = [];

    /// <summary>平面。</summary>
    public List<SolidPlane> Planes { get; } = [];

    /// <summary>重建摘要。</summary>
    public List<SolidReconstruction> Reconstructions { get; } = [];

    /// <summary>匯出參數。</summary>
    public required SolidExportOptions Options { get; init; }

    /// <summary>檢查封閉流形並回傳拓樸。</summary>
    public SolidTopology Validate()
    {
        if (Triangles.Count == 0)
        {
            throw new InvalidDataException("沒有可輸出的實體面。");
        }

        var edges = new Dictionary<(int, int), (int Count, int Direction)>();
        var incident = new Dictionary<int, List<(int, int)>>();
        var volumes = new Dictionary<int, double>();
        var solidVertices = new Dictionary<int, HashSet<int>>();
        var solidEdges = new Dictionary<int, HashSet<(int, int)>>();
        foreach (var t in Triangles)
        {
            var a = Vertices[t.A];
            var b = Vertices[t.B];
            var c = Vertices[t.C];
            if ((b - a).Cross(c - a).Length < 1e-10)
            {
                throw new InvalidDataException("實體包含退化面。");
            }

            // Translate before the determinant to avoid cancellation for large scanner coordinates.
            var reference = Reconstructions.Count > 0 ? Reconstructions[0].Origin : Vertices[0];
            volumes[t.Solid] = volumes.GetValueOrDefault(t.Solid) + (a - reference).Dot((b - reference).Cross(c - reference)) / 6;
            if (!solidVertices.ContainsKey(t.Solid))
            {
                solidVertices[t.Solid] = [];
                solidEdges[t.Solid] = [];
            }

            solidVertices[t.Solid].UnionWith([t.A, t.B, t.C]);
            foreach (var (u, v) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var key = u < v ? (u, v) : (v, u);
                var previous = edges.GetValueOrDefault(key);
                edges[key] = (previous.Count + 1, previous.Direction + (u < v ? 1 : -1));
                solidEdges[t.Solid].Add(key);
            }

            foreach (var (v, u, w) in new[] { (t.A, t.B, t.C), (t.B, t.C, t.A), (t.C, t.A, t.B) })
            {
                if (!incident.TryGetValue(v, out var links))
                {
                    incident[v] = links = [];
                }

                links.Add((u, w));
            }
        }

        if (edges.Values.Any(e => e.Count != 2 || e.Direction != 0))
        {
            throw new InvalidDataException("實體不是封閉且方向一致的流形，已取消模型輸出。");
        }

        foreach (var links in incident.Values)
        {
            var reached = new HashSet<int> { links[0].Item1 };
            var queue = new Queue<int>(reached);
            var neighbours = new Dictionary<int, List<int>>();
            foreach (var (a, b) in links)
            {
                if (!neighbours.ContainsKey(a))
                {
                    neighbours[a] = [];
                }

                if (!neighbours.ContainsKey(b))
                {
                    neighbours[b] = [];
                }

                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }

            while (queue.TryDequeue(out int v))
            {
                foreach (int next in neighbours[v])
                {
                    if (reached.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            if (reached.Count != neighbours.Count || neighbours.Values.Any(n => n.Count != 2))
            {
                throw new InvalidDataException("實體頂點存在不連續的面，已取消模型輸出。");
            }
        }

        if (volumes.Values.Any(v => v <= 0))
        {
            throw new InvalidDataException("實體體積或方向無效。");
        }

        int holes = 0;
        foreach (int solid in volumes.Keys)
        {
            int chi = solidVertices[solid].Count - solidEdges[solid].Count + Triangles.Count(t => t.Solid == solid);
            if (chi > 2 || chi % 2 != 0)
            {
                throw new InvalidDataException("實體拓樸無效。");
            }

            holes += (2 - chi) / 2;
        }

        return new SolidTopology(incident.Count, Triangles.Count, edges.Count, volumes.Count, holes, volumes.Values.Sum(), true);
    }
}

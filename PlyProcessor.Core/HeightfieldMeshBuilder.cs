namespace PlyProcessor.Core;

/// <summary>
/// Rebuilds the processed scan with the same heightfield idea used by the web
/// exporter: one vertex per occupied cell, triangles only across complete 2x2
/// cell blocks, then one outer boundary ring for side walls.
/// </summary>
public static class HeightfieldMeshBuilder
{
    public static MeshBuilder.Mesh Build(
        PlyPoint[] pts,
        float cellSize = 2f,
        float thickness = 6f,
        int smoothIter = 4,
        int erodeN = 1,
        int bridgeFill = 0,
        bool fillHoles = true)
    {
        if (pts.Length < 4)
            return MeshBuilder.Build(pts, cellSize, thickness, smoothIter, erodeN);

        var plane = PcaFitter.Fit(pts);
        var us = new float[pts.Length];
        var vs = new float[pts.Length];
        var hs = new float[pts.Length];
        float minU = float.MaxValue, maxU = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;

        for (int i = 0; i < pts.Length; i++)
        {
            float dx = pts[i].X - plane.Cx;
            float dy = pts[i].Y - plane.Cy;
            float dz = pts[i].Z - plane.Cz;
            float u = dx * plane.E1[0] + dy * plane.E1[1] + dz * plane.E1[2];
            float v = dx * plane.E2[0] + dy * plane.E2[1] + dz * plane.E2[2];
            float h = dx * plane.E3[0] + dy * plane.E3[1] + dz * plane.E3[2];
            us[i] = u;
            vs[i] = v;
            hs[i] = h;
            if (u < minU) minU = u;
            if (u > maxU) maxU = u;
            if (v < minV) minV = v;
            if (v > maxV) maxV = v;
        }

        int cols = Math.Max(1, (int)MathF.Ceiling((maxU - minU) / cellSize)) + 2;
        int rows = Math.Max(1, (int)MathF.Ceiling((maxV - minV) / cellSize)) + 2;
        if ((long)rows * cols > 4_000_000)
            throw new InvalidOperationException("Mesh grid is too large. Increase mesh cell size.");

        float originU = minU - cellSize;
        float originV = minV - cellSize;
        var sumH = new double[rows, cols];
        var cntH = new int[rows, cols];
        var occupied = new bool[rows, cols];
        var height = new float[rows, cols];

        for (int i = 0; i < pts.Length; i++)
        {
            int c = (int)MathF.Floor((us[i] - originU) / cellSize);
            int r = (int)MathF.Floor((vs[i] - originV) / cellSize);
            if (r < 0 || r >= rows || c < 0 || c >= cols) continue;
            sumH[r, c] += hs[i];
            cntH[r, c]++;
        }

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            if (cntH[r, c] == 0) continue;
            occupied[r, c] = true;
            height[r, c] = (float)(sumH[r, c] / cntH[r, c]);
        }

        if (fillHoles)
            FillInteriorHoles(occupied, height, rows, cols);

        BridgeGaps(occupied, height, rows, cols, bridgeFill);
        Erode(occupied, rows, cols, erodeN);
        SmoothHeights(occupied, height, rows, cols, smoothIter);

        var frontPos = new List<float[]>();
        var cellId = new int[rows, cols];
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            cellId[r, c] = -1;

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            if (!occupied[r, c]) continue;
            float u = originU + (c + 0.5f) * cellSize;
            float v = originV + (r + 0.5f) * cellSize;
            cellId[r, c] = frontPos.Count;
            frontPos.Add(ToWorld(plane, u, v, height[r, c]));
        }

        int vertexCount = frontPos.Count;
        if (vertexCount < 3)
            return MeshBuilder.Build(pts, cellSize, thickness, smoothIter, erodeN);

        var frontTris = new List<int[]>();
        for (int r = 0; r < rows - 1; r++)
        for (int c = 0; c < cols - 1; c++)
        {
            if (!(occupied[r, c] && occupied[r, c + 1] && occupied[r + 1, c] && occupied[r + 1, c + 1]))
                continue;

            int v00 = cellId[r, c];
            int v01 = cellId[r, c + 1];
            int v10 = cellId[r + 1, c];
            int v11 = cellId[r + 1, c + 1];
            frontTris.Add(Orient([v00, v01, v11], plane.E3, frontPos));
            frontTris.Add(Orient([v00, v11, v10], plane.E3, frontPos));
        }

        if (frontTris.Count == 0)
            return MeshBuilder.Build(pts, cellSize, thickness, smoothIter, erodeN);

        var vertexNormals = VertexNormals(frontPos, frontTris);
        var backPos = new List<float[]>(vertexCount);
        for (int i = 0; i < vertexCount; i++)
        {
            var p = frontPos[i];
            var n = vertexNormals[i];
            backPos.Add([p[0] - thickness * n[0], p[1] - thickness * n[1], p[2] - thickness * n[2]]);
        }

        var allPos = new List<float[]>(frontPos.Count + backPos.Count);
        allPos.AddRange(frontPos);
        allPos.AddRange(backPos);

        var backTris = new List<int[]>(frontTris.Count);
        foreach (var tri in frontTris)
        {
            var fn = TriNormal(frontPos, tri);
            backTris.Add(Orient([tri[0] + vertexCount, tri[1] + vertexCount, tri[2] + vertexCount], Neg(fn), allPos));
        }

        var ring = TraceLargestBoundaryRing(occupied, rows, cols);
        var ringVids = ring
            .Select(rc => cellId[rc.R, rc.C])
            .Where(id => id >= 0)
            .ToList();

        if (ringVids.Count < 3)
            return MeshBuilder.Build(pts, cellSize, thickness, smoothIter, erodeN);

        if (SignedAreaUv(ringVids, frontPos, plane) < 0)
            ringVids.Reverse();

        SmoothBackRing(backPos, ringVids, smoothIter);

        var sideTris = new List<int[]>();
        for (int k = 0; k < ringVids.Count; k++)
        {
            int a = ringVids[k];
            int b = ringVids[(k + 1) % ringVids.Count];
            var (ua, va) = ProjectUv(frontPos[a], plane);
            var (ub, vb) = ProjectUv(frontPos[b], plane);
            float du = ub - ua;
            float dv = vb - va;
            float el = MathF.Sqrt(du * du + dv * dv);
            if (el < 1e-9f) continue;
            float nu = dv / el;
            float nv = -du / el;
            float[] target = [
                nu * plane.E1[0] + nv * plane.E2[0],
                nu * plane.E1[1] + nv * plane.E2[1],
                nu * plane.E1[2] + nv * plane.E2[2]
            ];
            sideTris.Add(Orient([a, b, vertexCount + b], target, allPos));
            sideTris.Add(Orient([a, vertexCount + b, vertexCount + a], target, allPos));
        }

        var tris = new List<int[]>(frontTris.Count + backTris.Count + sideTris.Count);
        tris.AddRange(frontTris);
        tris.AddRange(backTris);
        tris.AddRange(sideTris);
        return new MeshBuilder.Mesh(allPos, tris);
    }

    static void FillInteriorHoles(bool[,] occ, float[,] h, int rows, int cols)
    {
        var exterior = new bool[rows, cols];
        var q = new Queue<(int R, int C)>();
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            if (r != 0 && c != 0 && r != rows - 1 && c != cols - 1) continue;
            if (occ[r, c]) continue;
            exterior[r, c] = true;
            q.Enqueue((r, c));
        }

        int[] dr = [-1, 1, 0, 0];
        int[] dc = [0, 0, -1, 1];
        while (q.Count > 0)
        {
            var (r, c) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nr = r + dr[k], nc = c + dc[k];
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols || occ[nr, nc] || exterior[nr, nc]) continue;
                exterior[nr, nc] = true;
                q.Enqueue((nr, nc));
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (occ[r, c] || exterior[r, c]) continue;
                if (!TryNeighborAverage(occ, h, rows, cols, r, c, out float avg)) continue;
                occ[r, c] = true;
                h[r, c] = avg;
                changed = true;
            }
        }
    }

    static void BridgeGaps(bool[,] occ, float[,] h, int rows, int cols, int maxGap)
    {
        if (maxGap <= 0) return;

        for (int r = 0; r < rows; r++)
        {
            int prev = -1;
            float prevH = 0;
            for (int c = 0; c < cols; c++)
            {
                if (!occ[r, c]) continue;
                if (prev >= 0 && c - prev - 1 > 0 && c - prev - 1 <= maxGap)
                {
                    int gap = c - prev;
                    for (int gc = prev + 1; gc < c; gc++)
                    {
                        float t = (float)(gc - prev) / gap;
                        occ[r, gc] = true;
                        h[r, gc] = prevH * (1 - t) + h[r, c] * t;
                    }
                }
                prev = c;
                prevH = h[r, c];
            }
        }

        for (int c = 0; c < cols; c++)
        {
            int prev = -1;
            float prevH = 0;
            for (int r = 0; r < rows; r++)
            {
                if (!occ[r, c]) continue;
                if (prev >= 0 && r - prev - 1 > 0 && r - prev - 1 <= maxGap)
                {
                    int gap = r - prev;
                    for (int gr = prev + 1; gr < r; gr++)
                    {
                        float t = (float)(gr - prev) / gap;
                        if (occ[gr, c]) continue;
                        occ[gr, c] = true;
                        h[gr, c] = prevH * (1 - t) + h[r, c] * t;
                    }
                }
                prev = r;
                prevH = h[r, c];
            }
        }
    }

    static void Erode(bool[,] occ, int rows, int cols, int passes)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            var remove = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (!occ[r, c]) continue;
                int n = (IsOcc(occ, rows, cols, r - 1, c) ? 1 : 0)
                      + (IsOcc(occ, rows, cols, r + 1, c) ? 1 : 0)
                      + (IsOcc(occ, rows, cols, r, c - 1) ? 1 : 0)
                      + (IsOcc(occ, rows, cols, r, c + 1) ? 1 : 0);
                if (n < 4) remove[r, c] = true;
            }
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (remove[r, c]) occ[r, c] = false;
        }
    }

    static void SmoothHeights(bool[,] occ, float[,] h, int rows, int cols, int iterations)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            var next = (float[,])h.Clone();
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (!occ[r, c]) continue;
                float sum = h[r, c];
                int count = 1;
                if (IsOcc(occ, rows, cols, r - 1, c)) { sum += h[r - 1, c]; count++; }
                if (IsOcc(occ, rows, cols, r + 1, c)) { sum += h[r + 1, c]; count++; }
                if (IsOcc(occ, rows, cols, r, c - 1)) { sum += h[r, c - 1]; count++; }
                if (IsOcc(occ, rows, cols, r, c + 1)) { sum += h[r, c + 1]; count++; }
                next[r, c] = sum / count;
            }
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                h[r, c] = next[r, c];
        }
    }

    static List<(int R, int C)> TraceLargestBoundaryRing(bool[,] occ, int rows, int cols)
    {
        var adj = new Dictionary<int, List<Edge>>();

        int Key(int r, int c) => r * (cols + 1) + c;

        void AddEdge(int pr, int pc, int qr, int qc, int cellR, int cellC)
        {
            int pk = Key(pr, pc);
            int qk = Key(qr, qc);
            if (!adj.TryGetValue(pk, out var a))
                adj[pk] = a = [];
            if (!adj.TryGetValue(qk, out var b))
                adj[qk] = b = [];
            a.Add(new Edge(qr, qc, cellR, cellC));
            b.Add(new Edge(pr, pc, cellR, cellC));
        }

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            if (!occ[r, c]) continue;
            if (!IsOcc(occ, rows, cols, r - 1, c)) AddEdge(r, c, r, c + 1, r, c);
            if (!IsOcc(occ, rows, cols, r + 1, c)) AddEdge(r + 1, c, r + 1, c + 1, r, c);
            if (!IsOcc(occ, rows, cols, r, c - 1)) AddEdge(r, c, r + 1, c, r, c);
            if (!IsOcc(occ, rows, cols, r, c + 1)) AddEdge(r, c + 1, r + 1, c + 1, r, c);
        }

        var best = new List<(int R, int C)>();
        foreach (var (startKey, edges) in adj)
        {
            foreach (var e0 in edges)
            {
                if (e0.Used) continue;
                var loop = new List<(int R, int C)>();
                int curKey = startKey;
                var curEdge = e0;
                int guard = 0;

                while (guard++ < 400_000)
                {
                    curEdge.Used = true;
                    int nextKey = Key(curEdge.ToR, curEdge.ToC);
                    var reverse = adj.TryGetValue(nextKey, out var nextEdges)
                        ? nextEdges.FirstOrDefault(x => !x.Used && Key(x.ToR, x.ToC) == curKey)
                        : null;
                    if (reverse is not null)
                        reverse.Used = true;

                    var cell = (curEdge.CellR, curEdge.CellC);
                    if (loop.Count == 0 || loop[^1] != cell)
                        loop.Add(cell);

                    if (nextKey == startKey)
                        break;

                    curKey = nextKey;
                    if (!adj.TryGetValue(curKey, out var opts))
                        break;
                    curEdge = opts.FirstOrDefault(x => !x.Used);
                    if (curEdge is null)
                        break;
                }

                if (loop.Count > best.Count)
                    best = loop;
            }
        }

        if (best.Count > 1 && best[0] == best[^1])
            best.RemoveAt(best.Count - 1);
        return best;
    }

    static void SmoothBackRing(List<float[]> backPos, List<int> ringVids, int smoothIter)
    {
        if (smoothIter <= 0 || ringVids.Count < 3) return;
        int n = ringVids.Count;
        for (int iter = 0; iter < smoothIter * 2; iter++)
        {
            var snap = ringVids.Select(vi => (float[])backPos[vi].Clone()).ToArray();
            for (int k = 0; k < n; k++)
            {
                int vi = ringVids[k];
                var prev = snap[(k - 1 + n) % n];
                var cur = snap[k];
                var next = snap[(k + 1) % n];
                backPos[vi][0] = (prev[0] + cur[0] + next[0]) / 3f;
                backPos[vi][1] = (prev[1] + cur[1] + next[1]) / 3f;
                backPos[vi][2] = (prev[2] + cur[2] + next[2]) / 3f;
            }
        }
    }

    static bool TryNeighborAverage(bool[,] occ, float[,] h, int rows, int cols, int r, int c, out float avg)
    {
        float sum = 0;
        int count = 0;
        if (IsOcc(occ, rows, cols, r - 1, c)) { sum += h[r - 1, c]; count++; }
        if (IsOcc(occ, rows, cols, r + 1, c)) { sum += h[r + 1, c]; count++; }
        if (IsOcc(occ, rows, cols, r, c - 1)) { sum += h[r, c - 1]; count++; }
        if (IsOcc(occ, rows, cols, r, c + 1)) { sum += h[r, c + 1]; count++; }
        avg = count == 0 ? 0 : sum / count;
        return count > 0;
    }

    static bool IsOcc(bool[,] occ, int rows, int cols, int r, int c) =>
        r >= 0 && r < rows && c >= 0 && c < cols && occ[r, c];

    static float[] ToWorld(PcaPlane plane, float u, float v, float h) => [
        plane.Cx + u * plane.E1[0] + v * plane.E2[0] + h * plane.E3[0],
        plane.Cy + u * plane.E1[1] + v * plane.E2[1] + h * plane.E3[1],
        plane.Cz + u * plane.E1[2] + v * plane.E2[2] + h * plane.E3[2]
    ];

    static (float U, float V) ProjectUv(float[] p, PcaPlane plane)
    {
        float dx = p[0] - plane.Cx;
        float dy = p[1] - plane.Cy;
        float dz = p[2] - plane.Cz;
        return (
            dx * plane.E1[0] + dy * plane.E1[1] + dz * plane.E1[2],
            dx * plane.E2[0] + dy * plane.E2[1] + dz * plane.E2[2]
        );
    }

    static float SignedAreaUv(List<int> ring, List<float[]> pos, PcaPlane plane)
    {
        float area = 0;
        for (int k = 0; k < ring.Count; k++)
        {
            var (x0, y0) = ProjectUv(pos[ring[k]], plane);
            var (x1, y1) = ProjectUv(pos[ring[(k + 1) % ring.Count]], plane);
            area += x0 * y1 - x1 * y0;
        }
        return area;
    }

    static int[] Orient(int[] tri, float[] targetNormal, List<float[]> verts)
    {
        var n = TriNormal(verts, tri);
        return Dot(n, targetNormal) < 0 ? [tri[0], tri[2], tri[1]] : tri;
    }

    static float[][] VertexNormals(List<float[]> verts, List<int[]> tris)
    {
        var normals = Enumerable.Range(0, verts.Count).Select(_ => new float[3]).ToArray();
        foreach (var tri in tris)
        {
            var n = TriNormal(verts, tri);
            foreach (int vi in tri)
            {
                normals[vi][0] += n[0];
                normals[vi][1] += n[1];
                normals[vi][2] += n[2];
            }
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = Normalize(normals[i]);
        return normals;
    }

    static float[] TriNormal(List<float[]> verts, int[] tri)
    {
        var p0 = verts[tri[0]];
        var p1 = verts[tri[1]];
        var p2 = verts[tri[2]];
        return Normalize(Cross(Sub(p1, p0), Sub(p2, p0)));
    }

    static float[] Sub(float[] a, float[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    static float[] Neg(float[] a) => [-a[0], -a[1], -a[2]];
    static float Dot(float[] a, float[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    static float[] Cross(float[] a, float[] b) => [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0]
    ];

    static float[] Normalize(float[] v)
    {
        float len = MathF.Sqrt(Dot(v, v));
        return len < 1e-9f ? [0, 0, 1] : [v[0] / len, v[1] / len, v[2] / len];
    }

    sealed class Edge(int toR, int toC, int cellR, int cellC)
    {
        public int ToR { get; } = toR;
        public int ToC { get; } = toC;
        public int CellR { get; } = cellR;
        public int CellC { get; } = cellC;
        public bool Used { get; set; }
    }
}

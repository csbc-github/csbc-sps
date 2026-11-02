using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 擬合兩量測面、把足跡投影到規則網格、保留已解析孔洞，並把摺疊板擠成封閉實體。
/// 角平分線位移為 t/cos(面角)，因此每面偏移 t。
/// </summary>
/// <remarks>由 Hank Scan3D 的 AngleSolidBuilder 搬入，邏輯不改。</remarks>
public static class AngleSolidBuilder
{
    /// <summary>由單一點集建立實體。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.AngleSolidBuilder</c>。</remarks>
    /// <param name="points">點。</param>
    /// <param name="options">匯出參數。</param>
    public static SolidModel Build(IEnumerable<ScanVector> points, SolidExportOptions options)
    {
        return Build([points.ToArray()], options);
    }

    /// <summary>由多個點集建立實體。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.AngleSolidBuilder</c>。</remarks>
    /// <param name="bodies">各實體點集。</param>
    /// <param name="options">匯出參數。</param>
    public static SolidModel Build(IReadOnlyList<ScanVector[]> bodies, SolidExportOptions options)
    {
        options.Validate();
        var model = new SolidModel { Options = options };
        foreach (var points in bodies)
        {
            Append(model, points);
        }

        model.Validate();
        return model;
    }

    private static void Append(SolidModel model, ScanVector[] points)
    {
        if (points.Length < 500 || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)))
        {
            throw new ArgumentException("角鋼模型需要至少 500 個有效點。");
        }

        double cell = model.Options.CellMm;
        double ridge = StitchMath.Median(points.GroupBy(p => (long)Math.Floor(p.Y / 4)).Select(g => g.MaxBy(p => p.Z).X));
        var left = StitchMath.FitPlane(points.Where(p => p.X < ridge - 3).ToArray(), .1);
        var right = StitchMath.FitPlane(points.Where(p => p.X > ridge + 3).ToArray(), .1);
        if (left.A - right.A < .2)
        {
            throw new InvalidDataException("模型未辨識到凸角鋼的兩個翼板。");
        }

        double dx = -(left.B - right.B) / (left.A - right.A), intercept = -(left.C - right.C) / (left.A - right.A);
        double y = StitchMath.Median(points.Select(p => p.Y)), x = dx * y + intercept;
        var origin = new ScanVector(x, y, left.A * x + left.B * y + left.C);
        var axis = new ScanVector(dx, 1, left.A * dx + left.B).Normalize();
        var nl = new ScanVector(-left.A, -left.B, 1).Normalize();
        var nr = new ScanVector(-right.A, -right.B, 1).Normalize();
        double normalCos = nl.Dot(nr);
        if (normalCos < Math.Cos(150 * Math.PI / 180) || normalCos > Math.Cos(30 * Math.PI / 180))
        {
            throw new InvalidDataException("翼板夾角不符合可輸出的角鋼。");
        }

        var up = (nl + nr).Normalize();
        var cross = axis.Cross(up).Normalize();
        up = cross.Cross(axis).Normalize();
        double slope = Math.Abs(nl.Dot(cross) / nl.Dot(up));
        double depth = model.Options.ThicknessMm * Math.Sqrt(1 + slope * slope);
        var uv = points.Select(p =>
        {
            var d = p - origin;
            return new ScanVector(d.Dot(cross), d.Dot(axis), d.Dot(up));
        }).ToArray();
        var bounds = ScanBounds.Of(uv);
        if (model.Options.ThicknessMm >= Math.Min(-bounds.MinX, bounds.MaxX) / Math.Sqrt(1 + slope * slope))
        {
            throw new ArgumentException("板厚相對翼板過大，請確認輸入的 mm 數值。");
        }

        int c0 = (int)Math.Floor(bounds.MinX / cell), c1 = (int)Math.Ceiling(bounds.MaxX / cell);
        int r0 = (int)Math.Floor(bounds.MinY / cell), r1 = (int)Math.Ceiling(bounds.MaxY / cell);
        int cols = c1 - c0, rows = r1 - r0;
        if (cols < 4 || rows < 4 || (long)(cols + 1) * (rows + 1) > 600_000)
        {
            throw new ArgumentException("模型網格過大或範圍不足，請增加 --mesh-cell 解析度數值。");
        }

        var buckets = new Dictionary<(int, int), List<ScanVector>>();
        foreach (var p in uv)
        {
            var key = ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));
            if (!buckets.TryGetValue(key, out var bucket))
            {
                buckets[key] = bucket = [];
            }

            bucket.Add(p);
        }

        var occupied = new bool[rows, cols];
        double radius2 = .85 * .85 * cell * cell;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double u = (c + c0 + .5) * cell, v = (r + r0 + .5) * cell;
                for (int dr = -1; dr <= 1 && !occupied[r, c]; dr++)
                {
                    for (int dc = -1; dc <= 1 && !occupied[r, c]; dc++)
                    {
                        if (buckets.TryGetValue((c + c0 + dc, r + r0 + dr), out var bucket))
                        {
                            occupied[r, c] = bucket.Any(p => (p.X - u) * (p.X - u) + (p.Y - v) * (p.Y - v) <= radius2);
                        }
                    }
                }
            }
        }

        // Only enclosed voids up to four cells are treated as sampling gaps. Larger voids stay open.
        int filled = 0, ambiguous = 0, small = 0;
        var seen = new bool[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (!occupied[r, c] && !seen[r, c])
                {
                    var region = Flood(r, c, false, seen);
                    bool exterior = region.Any(p => p.R == 0 || p.C == 0 || p.R == rows - 1 || p.C == cols - 1);
                    if (!exterior && region.Count <= 4)
                    {
                        foreach (var p in region)
                        {
                            occupied[p.R, p.C] = true;
                            filled++;
                        }
                    }
                }
            }
        }

        // Diagonal-only contacts would create non-manifold vertices. Remove one cell, never bridge a hole.
        bool changed;
        do
        {
            changed = false;
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    bool a = occupied[r, c], b = occupied[r, c + 1], d = occupied[r + 1, c], e = occupied[r + 1, c + 1];
                    if (a && e && !b && !d)
                    {
                        Remove(r, c, r + 1, c + 1);
                        changed = true;
                    }
                    else if (b && d && !a && !e)
                    {
                        Remove(r, c + 1, r + 1, c);
                        changed = true;
                    }
                }
            }
        }
        while (changed);

        var labels = new int[rows, cols];
        seen = new bool[rows, cols];
        int solidStart = model.Triangles.Count == 0 ? 0 : model.Triangles.Max(t => t.Solid) + 1, solidCount = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (occupied[r, c] && !seen[r, c])
                {
                    var region = Flood(r, c, true, seen);
                    if (region.Count < 16)
                    {
                        foreach (var p in region)
                        {
                            occupied[p.R, p.C] = false;
                            small++;
                        }

                        continue;
                    }

                    foreach (var p in region)
                    {
                        labels[p.R, p.C] = solidStart + solidCount;
                    }

                    solidCount++;
                }
            }
        }

        if (solidCount == 0)
        {
            throw new InvalidDataException("網格沒有足夠的連續板面，請調整解析度。");
        }

        var nodes = new Dictionary<(int, int), (int Top, int Bottom)>();
        (int Top, int Bottom) Node(int r, int c)
        {
            if (nodes.TryGetValue((r, c), out var node))
            {
                return node;
            }

            double u = (c + c0) * cell, v = (r + r0) * cell, w = -slope * Math.Abs(u);
            int top = model.Vertices.Count;
            model.Vertices.Add(origin + cross.Scale(u) + axis.Scale(v) + up.Scale(w));
            model.Vertices.Add(origin + cross.Scale(u) + axis.Scale(v) + up.Scale(w - depth));
            return nodes[(r, c)] = (top, top + 1);
        }

        var groups = new Dictionary<(int Solid, int Kind, int Coordinate), int>();
        void Tri(int a, int b, int c, int solid, int kind, int coordinate = 0)
        {
            var key = (solid, kind, coordinate);
            if (!groups.TryGetValue(key, out int group))
            {
                group = model.Planes.Count;
                groups[key] = group;
                var p = model.Vertices[a];
                var n = (model.Vertices[b] - p).Cross(model.Vertices[c] - p).Normalize();
                model.Planes.Add(new SolidPlane(p, n));
            }

            model.Triangles.Add(new SolidTriangle(a, b, c, group, solid));
        }

        bool Has(int r, int c)
        {
            return r >= 0 && c >= 0 && r < rows && c < cols && occupied[r, c];
        }

        int occupiedCount = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (occupied[r, c])
                {
                    occupiedCount++;
                    int solid = labels[r, c], wing = c + c0 < 0 ? 0 : 1;
                    var a = Node(r, c);
                    var b = Node(r, c + 1);
                    var d = Node(r + 1, c);
                    var e = Node(r + 1, c + 1);
                    Tri(a.Top, b.Top, e.Top, solid, wing);
                    Tri(a.Top, e.Top, d.Top, solid, wing);
                    Tri(a.Bottom, e.Bottom, b.Bottom, solid, wing + 2);
                    Tri(a.Bottom, d.Bottom, e.Bottom, solid, wing + 2);
                    void Wall((int Top, int Bottom) p, (int Top, int Bottom) q, int kind, int coordinate)
                    {
                        Tri(p.Top, q.Bottom, q.Top, solid, kind, coordinate);
                        Tri(p.Top, p.Bottom, q.Bottom, solid, kind, coordinate);
                    }

                    if (!Has(r - 1, c))
                    {
                        Wall(a, b, 4, r + r0);
                    }

                    if (!Has(r, c + 1))
                    {
                        Wall(b, e, 5, c + c0 + 1);
                    }

                    if (!Has(r + 1, c))
                    {
                        Wall(e, d, 6, r + r0 + 1);
                    }

                    if (!Has(r, c - 1))
                    {
                        Wall(d, a, 7, c + c0);
                    }
                }
            }
        }

        model.Reconstructions.Add(new SolidReconstruction(
            origin, cross, axis, up, slope, model.Options.ThicknessMm, depth,
            left.RmsMm, right.RmsMm, filled, ambiguous, small, occupiedCount));
        return;

        List<(int R, int C)> Flood(int startR, int startC, bool value, bool[,] visited)
        {
            var cells = new List<(int R, int C)>();
            var queue = new Queue<(int R, int C)>();
            visited[startR, startC] = true;
            queue.Enqueue((startR, startC));
            while (queue.TryDequeue(out var p))
            {
                cells.Add(p);
                foreach (var n in new[] { (R: p.R - 1, p.C), (R: p.R + 1, p.C), (p.R, C: p.C - 1), (p.R, C: p.C + 1) })
                {
                    if (n.R >= 0 && n.C >= 0 && n.R < rows && n.C < cols && !visited[n.R, n.C] && occupied[n.R, n.C] == value)
                    {
                        visited[n.R, n.C] = true;
                        queue.Enqueue(n);
                    }
                }
            }

            return cells;
        }

        void Remove(int ar, int ac, int br, int bc)
        {
            int Degree(int rr, int cc)
            {
                return new[] { (rr - 1, cc), (rr + 1, cc), (rr, cc - 1), (rr, cc + 1) }
                    .Count(p => p.Item1 >= 0 && p.Item2 >= 0 && p.Item1 < rows && p.Item2 < cols && occupied[p.Item1, p.Item2]);
            }

            if (Degree(ar, ac) <= Degree(br, bc))
            {
                occupied[ar, ac] = false;
            }
            else
            {
                occupied[br, bc] = false;
            }

            ambiguous++;
        }
    }
}

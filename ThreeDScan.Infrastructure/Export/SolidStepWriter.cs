using System.Globalization;
using System.Text;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>ISO 10303-21 / AP214 faceted B-rep，平面面、孔洞、mm 單位與產品定義。</summary>
/// <remarks>由 Hank Scan3D 的 SolidStepWriter 搬入，邏輯不改。</remarks>
internal static class SolidStepWriter
{
    /// <summary>寫出 STEP。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.SolidStepWriter</c>。</remarks>
    /// <param name="path">路徑。</param>
    /// <param name="model">實體。</param>
    internal static void Write(string path, SolidModel model)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('Reconstructed angle steel solid'),'2;1');");
        writer.WriteLine($"FILE_NAME('stitched.step','{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}',(''),(''),'Scan3D.Processing.Legacy.Stitching','PlyProcessor','');");
        writer.WriteLine("FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));\nENDSEC;\nDATA;");
        int id = 0;
        int Entity(string body)
        {
            int next = ++id;
            writer.WriteLine($"#{next}={body};");
            return next;
        }

        static string F(double value)
        {
            string text = value.ToString("0.#################", CultureInfo.InvariantCulture);
            return text.Contains('.') ? text : text + ".";
        }

        int Vector(string entity, ScanVector p)
        {
            return Entity($"{entity}('',({F(p.X)},{F(p.Y)},{F(p.Z)}))");
        }

        string Refs(IEnumerable<int> ids)
        {
            return string.Join(",", ids.Select(i => $"#{i}"));
        }

        int length = Entity("(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.))");
        int angle = Entity("(NAMED_UNIT(*) PLANE_ANGLE_UNIT() SI_UNIT($,.RADIAN.))");
        int solidAngle = Entity("(NAMED_UNIT(*) SI_UNIT($,.STERADIAN.) SOLID_ANGLE_UNIT())");
        int uncertainty = Entity($"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(0.000001),#{length},'distance_accuracy_value','')");
        int context = Entity($"(GEOMETRIC_REPRESENTATION_CONTEXT(3) GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{uncertainty})) GLOBAL_UNIT_ASSIGNED_CONTEXT((#{length},#{angle},#{solidAngle})) REPRESENTATION_CONTEXT('',''))");
        int app = Entity("APPLICATION_CONTEXT('core data for automotive mechanical design processes')");
        Entity($"APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#{app})");
        int productContext = Entity($"PRODUCT_CONTEXT('',#{app},'mechanical')");
        int product = Entity($"PRODUCT('angle_steel','angle_steel','Reconstructed from stitched scan', (#{productContext}))");
        int formation = Entity($"PRODUCT_DEFINITION_FORMATION('','',#{product})");
        int definitionContext = Entity($"PRODUCT_DEFINITION_CONTEXT('part definition',#{app},'design')");
        int definition = Entity($"PRODUCT_DEFINITION('design','',#{formation},#{definitionContext})");
        int definitionShape = Entity($"PRODUCT_DEFINITION_SHAPE('','',#{definition})");
        var pointIds = new Dictionary<int, int>();
        int Point(int index)
        {
            if (!pointIds.TryGetValue(index, out int value))
            {
                pointIds[index] = value = Vector("CARTESIAN_POINT", model.Vertices[index]);
            }

            return value;
        }

        var facesBySolid = new Dictionary<int, List<int>>();
        foreach (var group in model.Triangles.GroupBy(t => t.PlaneGroup))
        {
            var directed = new Dictionary<(int, int), (int A, int B, int Count)>();
            foreach (var t in group)
            {
                foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    var key = a < b ? (a, b) : (b, a);
                    var previous = directed.GetValueOrDefault(key);
                    directed[key] = (a, b, previous.Count + 1);
                }
            }

            var outgoing = new Dictionary<int, int>();
            foreach (var edge in directed.Values.Where(e => e.Count == 1))
            {
                if (!outgoing.TryAdd(edge.A, edge.B))
                {
                    throw new InvalidDataException("STEP 平面輪廓分岔。");
                }
            }

            var plane = model.Planes[group.Key];
            var loops = new List<int[]>();
            while (outgoing.Count > 0)
            {
                int start = outgoing.First().Key, current = start;
                var loop = new List<int>();
                do
                {
                    loop.Add(current);
                    if (!outgoing.Remove(current, out int next))
                    {
                        throw new InvalidDataException("STEP 平面輪廓未封閉。");
                    }

                    current = next;
                }
                while (current != start);
                if (loop.Count < 3)
                {
                    throw new InvalidDataException("STEP 平面輪廓退化。");
                }

                loops.Add(loop.ToArray());
            }

            var basis = (model.Vertices[loops[0][1]] - model.Vertices[loops[0][0]]).Normalize();
            var perpendicular = plane.Normal.Cross(basis).Normalize();
            (double X, double Y) Project(int index)
            {
                var p = model.Vertices[index] - plane.Origin;
                return (p.Dot(basis), p.Dot(perpendicular));
            }

            double Area(int[] loop)
            {
                double area = 0;
                for (int i = 0; i < loop.Length; i++)
                {
                    var p = Project(loop[i]);
                    var q = Project(loop[(i + 1) % loop.Length]);
                    area += p.X * q.Y - p.Y * q.X;
                }

                return area / 2;
            }

            bool Contains(int[] loop, int vertex)
            {
                var p = Project(vertex);
                bool inside = false;
                for (int i = 0, j = loop.Length - 1; i < loop.Length; j = i++)
                {
                    var a = Project(loop[i]);
                    var b = Project(loop[j]);
                    if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    {
                        inside = !inside;
                    }
                }

                return inside;
            }

            var outers = loops.Where(loop => Area(loop) > 1e-9).ToArray();
            var holes = loops.Where(loop => Area(loop) < -1e-9).ToArray();
            int normal = Vector("DIRECTION", plane.Normal), reference = Vector("DIRECTION", basis);
            int position = Entity($"AXIS2_PLACEMENT_3D('',#{Point(loops[0][0])},#{normal},#{reference})");
            int surface = Entity($"PLANE('',#{position})");
            int usedHoles = 0;
            int Bound(int[] loop, bool outer)
            {
                int polygon = Entity($"POLY_LOOP('',({Refs(loop.Select(Point))}))");
                return Entity($"{(outer ? "FACE_OUTER_BOUND" : "FACE_BOUND")}('',#{polygon},.T.)");
            }

            foreach (var outer in outers)
            {
                var bounds = new List<int> { Bound(outer, true) };
                foreach (var hole in holes.Where(hole => Contains(outer, hole[0])))
                {
                    bounds.Add(Bound(hole, false));
                    usedHoles++;
                }

                int face = Entity($"FACE_SURFACE('',({Refs(bounds)}),#{surface},.T.)");
                int solid = group.First().Solid;
                if (!facesBySolid.TryGetValue(solid, out var faces))
                {
                    facesBySolid[solid] = faces = [];
                }

                faces.Add(face);
            }

            if (outers.Length == 0 || usedHoles != holes.Length)
            {
                throw new InvalidDataException("STEP 內外輪廓配對失敗。");
            }
        }

        var breps = new List<int>();
        foreach (var (solid, faces) in facesBySolid.OrderBy(pair => pair.Key))
        {
            int shell = Entity($"CLOSED_SHELL('',({Refs(faces)}))");
            breps.Add(Entity($"FACETED_BREP('angle_steel_{solid + 1}',#{shell})"));
        }

        int representation = Entity($"FACETED_BREP_SHAPE_REPRESENTATION('',({Refs(breps)}),#{context})");
        Entity($"SHAPE_DEFINITION_REPRESENTATION(#{definitionShape},#{representation})");
        writer.WriteLine("ENDSEC;\nEND-ISO-10303-21;");
    }
}

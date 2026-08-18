using System.Globalization;
using System.Text;

namespace PlyProcessor.Core;

public static class StepExporter
{
    public static byte[] Export(MeshBuilder.Mesh mesh, string name = "scan")
    {
        var sb = new StringBuilder();
        var verts = mesh.Vertices;
        var tris = mesh.Triangles;
        int id = 0;
        string Next() => $"#{++id}";

        // Deduplicate edges (each interior edge referenced by exactly 2 faces)
        var edgeMap = new Dictionary<(int,int), int>();
        int EdgeId(int a, int b) {
            var key = a < b ? (a,b) : (b,a);
            if (!edgeMap.TryGetValue(key, out int eid)) { eid = ++id; edgeMap[key]=eid; }
            return eid;
        }

        var lines = new List<string>();
        lines.Add("ISO-10303-21;");
        lines.Add("HEADER;");
        lines.Add($"FILE_DESCRIPTION(('PLY Processor STEP export'),('2;1'));");
        lines.Add($"FILE_NAME('{name}.stp','',(''),(''),'','','');");
        lines.Add("FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));");
        lines.Add("ENDSEC;");
        lines.Add("DATA;");

        // Vertex points
        int[] vertIds = new int[verts.Count];
        foreach (var (v, i) in verts.Select((v,i)=>(v,i)))
        {
            id++;
            vertIds[i] = id;
            lines.Add($"#{id}=CARTESIAN_POINT('',({F(v[0])},{F(v[1])},{F(v[2])}));");
        }

        // Vertex points for vertex_point
        int[] vpIds = new int[verts.Count];
        foreach (var i in Enumerable.Range(0, verts.Count))
        {
            id++;
            vpIds[i] = id;
            lines.Add($"#{id}=VERTEX_POINT('',#{vertIds[i]});");
        }

        // Build face lists
        var faceRefs = new List<int>();
        foreach (var tri in tris)
        {
            int a=tri[0], b=tri[1], c=tri[2];
            int eAB=EdgeId(a,b), eBC=EdgeId(b,c), eCA=EdgeId(c,a);
            // oriented edge uses
            int oAB=++id; lines.Add($"#{oAB}=ORIENTED_EDGE('',*,*,#{eAB},.T.);");
            int oBC=++id; lines.Add($"#{oBC}=ORIENTED_EDGE('',*,*,#{eBC},.T.);");
            int oCA=++id; lines.Add($"#{oCA}=ORIENTED_EDGE('',*,*,#{eCA},.T.);");
            int loop=++id; lines.Add($"#{loop}=EDGE_LOOP('',({oAB},{oBC},{oCA}));");
            int fob=++id; lines.Add($"#{fob}=FACE_OUTER_BOUND('',#{loop},.T.);");
            // Plane normal via cross product
            float[] p0=verts[a],p1=verts[b],p2=verts[c];
            float nx=(p1[1]-p0[1])*(p2[2]-p0[2])-(p1[2]-p0[2])*(p2[1]-p0[1]);
            float ny=(p1[2]-p0[2])*(p2[0]-p0[0])-(p1[0]-p0[0])*(p2[2]-p0[2]);
            float nz=(p1[0]-p0[0])*(p2[1]-p0[1])-(p1[1]-p0[1])*(p2[0]-p0[0]);
            float len=MathF.Sqrt(nx*nx+ny*ny+nz*nz)+1e-9f;
            int dir=++id; lines.Add($"#{dir}=DIRECTION('',({F(nx/len)},{F(ny/len)},{F(nz/len)}));");
            int axis=++id; lines.Add($"#{axis}=AXIS2_PLACEMENT_3D('',#{vertIds[a]},#{dir},#{dir});");
            int srf=++id; lines.Add($"#{srf}=PLANE('',#{axis});");
            int face=++id; lines.Add($"#{face}=ADVANCED_FACE('',({fob}),#{srf},.T.);");
            faceRefs.Add(face);
        }

        int faceSet=++id; lines.Add($"#{faceSet}=CLOSED_SHELL('',({string.Join(",", faceRefs.Select(x=>"#"+x))}));");
        int mSolid=++id; lines.Add($"#{mSolid}=MANIFOLD_SOLID_BREP('{name}',#{faceSet});");
        int adv=++id; lines.Add($"#{adv}=ADVANCED_BREP_SHAPE_REPRESENTATION('',(#{mSolid}),#1);");
        id++; lines.Insert(7,$"#1=( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) );");

        lines.Add("ENDSEC;");
        lines.Add("END-ISO-10303-21;");
        return Encoding.UTF8.GetBytes(string.Join("\n", lines));
    }

    static string F(float v) => v.ToString("F6", CultureInfo.InvariantCulture);
}

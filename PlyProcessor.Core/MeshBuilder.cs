namespace PlyProcessor.Core;

/// <summary>
/// Projects the scan onto its dominant plane, builds a 2D occupancy grid,
/// optionally erodes/smooths, traces the boundary, then extrudes by thickness.
/// </summary>
public static class MeshBuilder
{
    public record Mesh(List<float[]> Vertices, List<int[]> Triangles);

    public static Mesh Build(PlyPoint[] pts, float cellSize = 2f, float thickness = 6f,
        int smoothIter = 4, int erodeN = 1, bool flatBottom = false, int bridgeFill = 0)
    {
        var plane = PcaFitter.Fit(pts);
        float[] cx = [plane.E1[0], plane.E1[1], plane.E1[2]];
        float[] cy = [plane.E2[0], plane.E2[1], plane.E2[2]];
        float[] cz = [plane.E3[0], plane.E3[1], plane.E3[2]];

        // Project every point into plane-local UV space
        float minU=float.MaxValue, minV2=float.MaxValue;
        float maxU=float.MinValue, maxV=float.MinValue;
        var uvs = new (float u, float v)[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            float dx=pts[i].X-plane.Cx, dy=pts[i].Y-plane.Cy, dz=pts[i].Z-plane.Cz;
            float u=dx*cx[0]+dy*cx[1]+dz*cx[2];
            float v=dx*cy[0]+dy*cy[1]+dz*cy[2];
            uvs[i]=(u,v);
            if(u<minU)minU=u; if(u>maxU)maxU=u; if(v<minV2)minV2=v; if(v>maxV)maxV=v;
        }
        int gW=(int)MathF.Ceiling((maxU-minU)/cellSize)+2;
        int gH=(int)MathF.Ceiling((maxV-minV2)/cellSize)+2;
        var grid = new bool[gW,gH];
        foreach (var (u,v) in uvs)
        {
            int gi=(int)((u-minU)/cellSize); int gj=(int)((v-minV2)/cellSize);
            if(gi>=0&&gi<gW&&gj>=0&&gj<gH) grid[gi,gj]=true;
        }

        // Bridge dilation: expand the grid to close gaps between nearby regions
        // (e.g. the gap between two projected flanges of an L-shaped angle steel).
        // Applied BEFORE erosion so the gap is filled first, then edge-cleaned.
        for (int d = 0; d < bridgeFill; d++)
        {
            var ng = new bool[gW,gH];
            for (int i=1;i<gW-1;i++) for (int j=1;j<gH-1;j++)
                ng[i,j] = grid[i,j] || grid[i-1,j] || grid[i+1,j] || grid[i,j-1] || grid[i,j+1];
            grid = ng;
        }

        // Erosion
        for (int e = 0; e < erodeN; e++)
        {
            var ng = new bool[gW,gH];
            for (int i=1;i<gW-1;i++) for (int j=1;j<gH-1;j++)
                ng[i,j]=grid[i,j]&&grid[i-1,j]&&grid[i+1,j]&&grid[i,j-1]&&grid[i,j+1];
            grid=ng;
        }
        // Smoothing (dilation then erosion approximation via majority vote)
        for (int s = 0; s < smoothIter; s++)
        {
            var ng = new bool[gW,gH];
            for (int i=1;i<gW-1;i++) for (int j=1;j<gH-1;j++)
            {
                int on=0;
                for(int di=-1;di<=1;di++) for(int dj=-1;dj<=1;dj++) if(grid[i+di,j+dj]) on++;
                ng[i,j]=on>=5;
            }
            grid=ng;
        }

        // Collect filled cells as quads → triangles
        // Top face (at height 0 in normal direction), bottom face (at -thickness)
        var verts = new List<float[]>();
        var tris = new List<int[]>();
        var vtop = new Dictionary<(int,int), int>();
        var vbot = new Dictionary<(int,int), int>();

        int AddTop(int i, int j) {
            var key=(i,j);
            if (!vtop.TryGetValue(key,out int vi)) {
                float u=minU+i*cellSize, v=minV2+j*cellSize;
                float[] world=PlanePt(u,v,0,plane);
                vtop[key]=vi=verts.Count; verts.Add(world);
            }
            return vi;
        }
        int AddBot(int i, int j) {
            var key=(i,j);
            if (!vbot.TryGetValue(key,out int vi)) {
                float u=minU+i*cellSize, v=minV2+j*cellSize;
                float[] world=PlanePt(u,v,-thickness,plane);
                vbot[key]=vi=verts.Count; verts.Add(world);
            }
            return vi;
        }

        for (int i=0;i<gW-1;i++) for (int j=0;j<gH-1;j++)
        {
            if (!grid[i,j]||!grid[i+1,j]||!grid[i,j+1]||!grid[i+1,j+1]) continue;
            int t00=AddTop(i,j),t10=AddTop(i+1,j),t01=AddTop(i,j+1),t11=AddTop(i+1,j+1);
            tris.Add([t00,t10,t11]); tris.Add([t00,t11,t01]);
            int b00=AddBot(i,j),b10=AddBot(i+1,j),b01=AddBot(i,j+1),b11=AddBot(i+1,j+1);
            tris.Add([b00,b11,b10]); tris.Add([b00,b01,b11]);
            // Side walls
            if (!grid[i,j>0?j-1:j]) { tris.Add([t00,t10,b10]); tris.Add([t00,b10,b00]); }
            if (!grid[i,j+1<gH?j+1:j]) { tris.Add([t01,b01,t11]); tris.Add([b01,b11,t11]); }
            if (!grid[i>0?i-1:i,j]) { tris.Add([t00,b00,t01]); tris.Add([b00,b01,t01]); }
            if (!grid[i+1<gW?i+1:i,j]) { tris.Add([t10,t11,b10]); tris.Add([b10,t11,b11]); }
        }

        return new Mesh(verts, tris);
    }

    static float[] PlanePt(float u, float v, float h, PcaPlane plane)
    {
        float x = plane.Cx + u*plane.E1[0] + v*plane.E2[0] + h*plane.E3[0];
        float y = plane.Cy + u*plane.E1[1] + v*plane.E2[1] + h*plane.E3[1];
        float z = plane.Cz + u*plane.E1[2] + v*plane.E2[2] + h*plane.E3[2];
        return [x, y, z];
    }
}

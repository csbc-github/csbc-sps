namespace PlyProcessor.Core;

/// <summary>
/// Builds a mesh by voxelizing the point cloud in 3D and extracting the surface
/// (every face of an occupied voxel that borders an empty voxel becomes a triangle pair).
///
/// Unlike MeshBuilder which projects everything onto a single 2D plane (and therefore
/// splits an L-shaped angle steel into two disconnected regions), this approach works
/// directly in 3D space and correctly reconstructs ANY shape — including L, T, U, etc.
/// </summary>
public static class SurfaceMeshBuilder
{
    public static MeshBuilder.Mesh Build(PlyPoint[] pts, float voxelSize = 5f)
    {
        if (pts.Length == 0) return new MeshBuilder.Mesh([], []);

        // ── 1. Bounds ─────────────────────────────────────────────────────────
        float minX=float.MaxValue, minY=float.MaxValue, minZ=float.MaxValue;
        foreach (var p in pts)
        {
            if(p.X<minX)minX=p.X; if(p.Y<minY)minY=p.Y; if(p.Z<minZ)minZ=p.Z;
        }
        float vs = voxelSize;

        // ── 2. Build 3D voxel occupancy ───────────────────────────────────────
        var occupied = new HashSet<(int,int,int)>(pts.Length / 4);
        foreach (var p in pts)
        {
            int gx = (int)MathF.Floor((p.X - minX) / vs);
            int gy = (int)MathF.Floor((p.Y - minY) / vs);
            int gz = (int)MathF.Floor((p.Z - minZ) / vs);
            occupied.Add((gx, gy, gz));
        }

        // ── 3. Surface face extraction ────────────────────────────────────────
        // For each occupied voxel, emit a quad for each face that borders empty space.
        // Each quad (a,b,c,d) is split into two triangles (a,b,c) and (a,c,d).
        // Corner (cx,cy,cz) ∈ {0,1}³ of voxel (gx,gy,gz) maps to world vertex
        // at grid position (gx+cx, gy+cy, gz+cz).  Shared across adjacent voxels.

        // 6 faces: (neighbour direction, quad CCW corners seen from outside)
        // Corner tuple = (Δx,Δy,Δz) offset within the voxel (0 or 1 per axis)
        (int nx, int ny, int nz, (int,int,int)[] quad)[] faces =
        {
            ( 1, 0, 0, [(1,0,0),(1,0,1),(1,1,1),(1,1,0)]),  // +X face
            (-1, 0, 0, [(0,0,0),(0,1,0),(0,1,1),(0,0,1)]),  // -X face
            ( 0, 1, 0, [(0,1,0),(1,1,0),(1,1,1),(0,1,1)]),  // +Y face
            ( 0,-1, 0, [(0,0,0),(0,0,1),(1,0,1),(1,0,0)]),  // -Y face
            ( 0, 0, 1, [(0,0,1),(0,1,1),(1,1,1),(1,0,1)]),  // +Z face
            ( 0, 0,-1, [(0,0,0),(1,0,0),(1,1,0),(0,1,0)]),  // -Z face
        };

        var verts   = new List<float[]>(occupied.Count * 2);
        var tris    = new List<int[]>(occupied.Count * 4);
        var vertMap = new Dictionary<(int,int,int), int>(occupied.Count * 4);

        int GetVert(int px, int py, int pz)
        {
            var key = (px, py, pz);
            if (!vertMap.TryGetValue(key, out int vi))
            {
                vertMap[key] = vi = verts.Count;
                verts.Add([minX + px*vs, minY + py*vs, minZ + pz*vs]);
            }
            return vi;
        }

        foreach (var (gx, gy, gz) in occupied)
        {
            foreach (var (nx, ny, nz, quad) in faces)
            {
                if (occupied.Contains((gx+nx, gy+ny, gz+nz))) continue;  // interior face, skip

                int v0 = GetVert(gx+quad[0].Item1, gy+quad[0].Item2, gz+quad[0].Item3);
                int v1 = GetVert(gx+quad[1].Item1, gy+quad[1].Item2, gz+quad[1].Item3);
                int v2 = GetVert(gx+quad[2].Item1, gy+quad[2].Item2, gz+quad[2].Item3);
                int v3 = GetVert(gx+quad[3].Item1, gy+quad[3].Item2, gz+quad[3].Item3);
                tris.Add([v0, v1, v2]);
                tris.Add([v0, v2, v3]);
            }
        }

        // Apply Laplacian smoothing to soften the voxel staircase pattern
        Smooth(verts, tris, iterations: 3);

        return new MeshBuilder.Mesh(verts, tris);
    }

    static void Smooth(List<float[]> verts, List<int[]> tris, int iterations)
    {
        // Build vertex→neighbor adjacency
        var adj = new Dictionary<int, HashSet<int>>(verts.Count);
        for (int i = 0; i < verts.Count; i++) adj[i] = [];
        foreach (var t in tris)
        {
            adj[t[0]].Add(t[1]); adj[t[0]].Add(t[2]);
            adj[t[1]].Add(t[0]); adj[t[1]].Add(t[2]);
            adj[t[2]].Add(t[0]); adj[t[2]].Add(t[1]);
        }

        for (int iter = 0; iter < iterations; iter++)
        {
            var newPos = new float[verts.Count][];
            for (int i = 0; i < verts.Count; i++)
            {
                var neighbors = adj[i];
                if (neighbors.Count == 0) { newPos[i] = verts[i]; continue; }
                float sx = 0, sy = 0, sz = 0;
                foreach (int n in neighbors) { sx += verts[n][0]; sy += verts[n][1]; sz += verts[n][2]; }
                float c = neighbors.Count;
                // Weighted average: 50% original + 50% neighbors (mild smoothing)
                newPos[i] = [
                    verts[i][0] * 0.5f + sx / c * 0.5f,
                    verts[i][1] * 0.5f + sy / c * 0.5f,
                    verts[i][2] * 0.5f + sz / c * 0.5f
                ];
            }
            for (int i = 0; i < verts.Count; i++) verts[i] = newPos[i];
        }
    }
}

namespace PlyProcessor.Core;

public record ClusterObject(string Name, PlyPoint[] Points);

public static class VoxelClusterer
{
    public static List<ClusterObject> Cluster(PlyPoint[] pts, string baseName,
        float voxelSize = 10f, int minSize = 200)
    {
        float minX=float.MaxValue, minY=float.MaxValue, minZ=float.MaxValue;
        float maxX=float.MinValue, maxY=float.MinValue, maxZ=float.MinValue;
        foreach (var p in pts)
        {
            if(p.X<minX)minX=p.X; if(p.X>maxX)maxX=p.X;
            if(p.Y<minY)minY=p.Y; if(p.Y>maxY)maxY=p.Y;
            if(p.Z<minZ)minZ=p.Z; if(p.Z>maxZ)maxZ=p.Z;
        }

        int nx = (int)MathF.Ceiling((maxX-minX)/voxelSize)+2;
        int ny = (int)MathF.Ceiling((maxY-minY)/voxelSize)+2;

        var voxelPts = new Dictionary<long, List<int>>(pts.Length / 4);
        for (int i = 0; i < pts.Length; i++)
        {
            int cx=(int)((pts[i].X-minX)/voxelSize);
            int cy=(int)((pts[i].Y-minY)/voxelSize);
            int cz=(int)((pts[i].Z-minZ)/voxelSize);
            long key = (long)cx + (long)cy*nx + (long)cz*nx*ny;
            if (!voxelPts.TryGetValue(key, out var list))
                voxelPts[key] = list = [];
            list.Add(i);
        }

        long[] dirs = [1,-1,nx,-nx,(long)nx*ny,-(long)nx*ny];
        var visited = new HashSet<long>(voxelPts.Count);
        var components = new List<(int Count, List<long> Keys)>();

        foreach (var startKey in voxelPts.Keys)
        {
            if (visited.Contains(startKey)) continue;
            var queue = new Queue<long>();
            var comp = new List<long>();
            queue.Enqueue(startKey);
            visited.Add(startKey);
            int count = 0;
            while (queue.Count > 0)
            {
                var k = queue.Dequeue();
                comp.Add(k);
                count += voxelPts[k].Count;
                foreach (var d in dirs)
                {
                    var nk = k + d;
                    if (!visited.Contains(nk) && voxelPts.ContainsKey(nk))
                    {
                        visited.Add(nk);
                        queue.Enqueue(nk);
                    }
                }
            }
            if (count >= 5)
                components.Add((count, comp));
        }

        components.Sort((a, b) => b.Count.CompareTo(a.Count));

        var result = new List<ClusterObject>();
        int objIdx = 0;
        foreach (var (cnt, keys) in components)
        {
            if (cnt < minSize) break;
            var indexSet = new HashSet<int>(cnt);
            foreach (var k in keys)
                foreach (var i in voxelPts[k])
                    indexSet.Add(i);
            var objPts = new PlyPoint[indexSet.Count];
            int j = 0;
            foreach (var i in indexSet) objPts[j++] = pts[i];
            result.Add(new ClusterObject($"{baseName}_obj{++objIdx}", objPts));
        }
        return result;
    }
}

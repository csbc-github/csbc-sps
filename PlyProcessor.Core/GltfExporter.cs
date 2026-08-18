using System.Text;
using System.Text.Json;

namespace PlyProcessor.Core;

public static class GltfExporter
{
    public static byte[] Export(MeshBuilder.Mesh mesh)
    {
        var verts = mesh.Vertices;
        var tris  = mesh.Triangles;
        int V = verts.Count, T = tris.Count;

        // Compute per-vertex normals averaged from adjacent triangle normals
        var normAcc = new float[V * 3];
        foreach (var tri in tris)
        {
            var p0=verts[tri[0]]; var p1=verts[tri[1]]; var p2=verts[tri[2]];
            float nx=(p1[1]-p0[1])*(p2[2]-p0[2])-(p1[2]-p0[2])*(p2[1]-p0[1]);
            float ny=(p1[2]-p0[2])*(p2[0]-p0[0])-(p1[0]-p0[0])*(p2[2]-p0[2]);
            float nz=(p1[0]-p0[0])*(p2[1]-p0[1])-(p1[1]-p0[1])*(p2[0]-p0[0]);
            foreach (var i in tri) { normAcc[i*3]+=nx; normAcc[i*3+1]+=ny; normAcc[i*3+2]+=nz; }
        }

        var posArr = new float[V*3]; var norArr = new float[V*3];
        float minX=float.MaxValue,minY=float.MaxValue,minZ=float.MaxValue;
        float maxX=float.MinValue,maxY=float.MinValue,maxZ=float.MinValue;
        for (int i=0;i<V;i++)
        {
            posArr[i*3]=verts[i][0]; posArr[i*3+1]=verts[i][1]; posArr[i*3+2]=verts[i][2];
            if(verts[i][0]<minX)minX=verts[i][0]; if(verts[i][0]>maxX)maxX=verts[i][0];
            if(verts[i][1]<minY)minY=verts[i][1]; if(verts[i][1]>maxY)maxY=verts[i][1];
            if(verts[i][2]<minZ)minZ=verts[i][2]; if(verts[i][2]>maxZ)maxZ=verts[i][2];
            float len=MathF.Sqrt(normAcc[i*3]*normAcc[i*3]+normAcc[i*3+1]*normAcc[i*3+1]+normAcc[i*3+2]*normAcc[i*3+2]);
            if(len<1e-9f)len=1;
            norArr[i*3]=normAcc[i*3]/len; norArr[i*3+1]=normAcc[i*3+1]/len; norArr[i*3+2]=normAcc[i*3+2]/len;
        }

        bool useU32 = V > 65535;
        byte[] idxBytes;
        if (useU32) {
            var idx = new uint[T*3];
            for (int t=0;t<T;t++){idx[t*3]=(uint)tris[t][0];idx[t*3+1]=(uint)tris[t][1];idx[t*3+2]=(uint)tris[t][2];}
            idxBytes = AsBytes(idx);
        } else {
            var idx = new ushort[T*3];
            for (int t=0;t<T;t++){idx[t*3]=(ushort)tris[t][0];idx[t*3+1]=(ushort)tris[t][1];idx[t*3+2]=(ushort)tris[t][2];}
            idxBytes = AsBytes(idx);
        }

        byte[] posBytes = AsBytes(posArr), norBytes = AsBytes(norArr);
        int Align4(int n) => (n+3)&~3;
        int norOff=Align4(posBytes.Length), idxOff=Align4(norOff+norBytes.Length);
        int binLen=Align4(idxOff+idxBytes.Length);
        var bin = new byte[binLen];
        posBytes.CopyTo(bin,0); norBytes.CopyTo(bin,norOff); idxBytes.CopyTo(bin,idxOff);

        var gltf = new {
            asset=new{version="2.0",generator="PlyProcessor"},
            scene=0,
            scenes=new[]{new{nodes=new[]{0}}},
            nodes=new[]{new{mesh=0}},
            meshes=new[]{new{name="scan",primitives=new[]{new{
                attributes=new{POSITION=0,NORMAL=1},indices=2,material=0,mode=4}}}},
            materials=new[]{new{name="steel",pbrMetallicRoughness=new{
                baseColorFactor=new[]{0.65f,0.70f,0.75f,1.0f},metallicFactor=0.3f,roughnessFactor=0.55f},doubleSided=false}},
            accessors=new object[]{
                new{bufferView=0,byteOffset=0,componentType=5126,count=V,type="VEC3",
                    min=new[]{minX,minY,minZ},max=new[]{maxX,maxY,maxZ}},
                new{bufferView=1,byteOffset=0,componentType=5126,count=V,type="VEC3"},
                new{bufferView=2,byteOffset=0,componentType=useU32?5125:5123,count=T*3,type="SCALAR"}},
            bufferViews=new object[]{
                new{buffer=0,byteOffset=0,byteLength=posBytes.Length,target=34962},
                new{buffer=0,byteOffset=norOff,byteLength=norBytes.Length,target=34962},
                new{buffer=0,byteOffset=idxOff,byteLength=idxBytes.Length,target=34963}},
            buffers=new[]{new{byteLength=binLen}}
        };

        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(gltf));
        int jsonPad = Align4(jsonBytes.Length);
        var jsonPadded = new byte[jsonPad]; Array.Fill(jsonPadded,(byte)0x20); jsonBytes.CopyTo(jsonPadded,0);

        int total = 12 + 8 + jsonPad + 8 + binLen;
        var glb = new byte[total];
        int off = 0;
        void WriteU32(uint v) { BitConverter.GetBytes(v).CopyTo(glb,off); off+=4; }
        WriteU32(0x46546C67u); WriteU32(2u); WriteU32((uint)total);
        WriteU32((uint)jsonPad); WriteU32(0x4E4F534Au); jsonPadded.CopyTo(glb,off); off+=jsonPad;
        WriteU32((uint)binLen); WriteU32(0x004E4942u); bin.CopyTo(glb,off);
        return glb;
    }

    static byte[] AsBytes<T>(T[] arr) where T : struct
    {
        var bytes = new byte[arr.Length * System.Runtime.InteropServices.Marshal.SizeOf<T>()];
        Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

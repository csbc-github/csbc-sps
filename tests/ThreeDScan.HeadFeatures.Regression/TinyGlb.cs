using System.Text;
using System.Text.Json;

internal static class TinyGlb
{
    internal const string ScanConvention = "(x,y,z) -> (0.001*x,0.001*z,-0.001*y)";
    internal static byte[] Create(string? convention, bool staleAccessorBounds = false)
    {
        // Standard glTF encoding of scanner-mm triangle (100,200,300),
        // (200,200,300), (100,300,300), with outward scanner normal +Z.
        float[][] vertices = [[.1f,.3f,-.2f],[.2f,.3f,-.2f],[.1f,.3f,-.3f]];
        using var binStream = new MemoryStream();
        using (var binary = new BinaryWriter(binStream, Encoding.UTF8, true))
        {
            foreach (var p in vertices) foreach (float x in p) binary.Write(x);
            for (int i=0;i<3;i++){binary.Write(0f);binary.Write(1f);binary.Write(0f);}
            foreach (uint i in new uint[]{0,1,2})binary.Write(i);
        }
        var root = new
        {
            asset=new{version="2.0"},
            buffers=new[]{new{byteLength=(int)binStream.Length}},
            bufferViews=new[]{new{buffer=0,byteOffset=0,byteLength=36},new{buffer=0,byteOffset=36,byteLength=36},new{buffer=0,byteOffset=72,byteLength=12}},
            accessors=new object[]{new{bufferView=0,componentType=5126,count=3,type="VEC3",min=staleAccessorBounds?new[]{-99d,-99d,-99d}:new[]{.1,.3,-.3},max=staleAccessorBounds?new[]{99d,99d,99d}:new[]{.2,.3,-.2}},
                new{bufferView=1,componentType=5126,count=3,type="VEC3"},new{bufferView=2,componentType=5125,count=3,type="SCALAR"}},
            meshes=new[]{new{primitives=new[]{new{attributes=new{POSITION=0,NORMAL=1},indices=2}}}},
            extras=convention is null?null:new{scanMillimetresToGltfMetres=convention},
        };
        byte[] json=JsonSerializer.SerializeToUtf8Bytes(root);
        int jsonLength=(json.Length+3)&~3,binLength=((int)binStream.Length+3)&~3;
        using var output=new MemoryStream();using var writer=new BinaryWriter(output,Encoding.UTF8,true);
        writer.Write(0x46546c67u);writer.Write(2u);writer.Write((uint)(12+8+jsonLength+8+binLength));
        writer.Write((uint)jsonLength);writer.Write(0x4e4f534au);writer.Write(json);
        for(int i=json.Length;i<jsonLength;i++)writer.Write((byte)' ');
        writer.Write((uint)binLength);writer.Write(0x004e4942u);writer.Write(binStream.ToArray());
        while(output.Length%4!=0)writer.Write((byte)0);
        return output.ToArray();
    }
}

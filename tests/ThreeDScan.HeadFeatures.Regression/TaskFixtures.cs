using System.Text.Json;

internal sealed record ArtifactFixture(string Root,string Glb,string Ply,string TaskId);
internal static class TaskFixtures
{
    internal static ArtifactFixture Create(string workspace,string id,bool heads=true,bool twoSegments=false,
        double? modelThickness=6,double? measuredLeft=null,double? measuredRight=null,bool malformed=false)
    {
        string root=Path.Combine(workspace,"tasks",id),complete=Path.Combine(root,"complete");Directory.CreateDirectory(complete);
        string glb=Path.Combine(complete,"stitched.glb"),ply=Path.Combine(complete,"stitched.ply");
        File.WriteAllBytes(glb,TinyGlb.Create(TinyGlb.ScanConvention));
        File.WriteAllText(ply,"ply\nformat ascii 1.0\nelement vertex 8\nproperty float x\nproperty float y\nproperty float z\nend_header\n10 -30 5\n50 -30 5\n10 70 5\n50 70 5\n10 -30 25\n50 -30 25\n10 70 25\n50 70 25\n");
        object? firstHeads=heads?new{leftCorner=new{x=11.25,y=22.5,z=-3.75},apex=new{x=44.125,y=55.25,z=66.5},rightCorner=new{x=77.75,y=88.875,z=-9.125}}:null;
        var segments=new List<object>{new{number=1,response=new{headFeatures=firstHeads,segmentZDelta=(double?)(heads?-6.4:null),thicknessLeft=measuredLeft,thicknessRight=measuredRight}}};
        if(twoSegments)segments.Add(new{number=2,response=new{headFeatures=new{leftCorner=new{x=901d,y=902d,z=903d},apex=new{x=904d,y=905d,z=906d},rightCorner=new{x=907d,y=908d,z=909d}},segmentZDelta=-99d,thicknessLeft=(double?)19,thicknessRight=(double?)20}});
        var doc=new{taskId=id,kind="Surface",status="Completed",materialThicknessMm=modelThickness,result=new{segmentCount=segments.Count,thicknessMm=modelThickness},segments};
        File.WriteAllText(Path.Combine(root,"task.json"),malformed?"{ invalid JSON":JsonSerializer.Serialize(doc,new JsonSerializerOptions{WriteIndented=true}));
        return new(root,glb,ply,id);
    }
}

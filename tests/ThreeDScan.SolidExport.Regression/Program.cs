using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
string? Arg(string name)=>args.SkipWhile(a=>a!=name).Skip(1).FirstOrDefault();
string output=Path.GetFullPath(Arg("--output")??Path.Combine(Path.GetTempPath(),"ThreeDScan.SolidExport.Regression",Guid.NewGuid().ToString("N")));
if(Directory.Exists(output))throw new IOException("Choose a new output directory.");Directory.CreateDirectory(output);
double thickness=double.Parse(Arg("--thickness")??"6",CultureInfo.InvariantCulture);
var results=new List<object>();int failures=0;
void Check(string name,Func<object> run)
{
    try{results.Add(new{name,passed=true,result=run()});Console.WriteLine($"PASS {name}");}
    catch(Exception error){failures++;results.Add(new{name,passed=false,error=error.ToString()});Console.WriteLine($"FAIL {name}: {error.Message}");}
}
if(Arg("--replay-scanner") is { } replay)
    Check("replay-scanner",()=>Replay(Path.GetFullPath(replay)));
else
{
    OutlineFillChecks.Run(Check, output);
    foreach(double yaw in new[]{0,35,79.6,90,125})
    {
        double angle=yaw;Check($"yaw-{angle:G}",()=>Positive($"yaw-{angle:G}",SolidScenes.Roof(angle)));
    }
    Check("full-XYZ-pitch17-roll31",()=>Positive("full-XYZ-pitch17-roll31",SolidScenes.Roof(79.6,17,31)));
    Check("full-XYZ-pitch55-rollminus63",()=>Positive("full-XYZ-pitch55-rollminus63",SolidScenes.Roof(90,55,-63)));
    Check("full-XYZ-inverted-roll137",()=>Positive("full-XYZ-inverted-roll137",SolidScenes.Roof(-25,-22,137)));
    Check("legal-unequal-flat-L",()=>Positive("legal-unequal-flat-L",SolidScenes.FlatL()));
    Check("resolved-large-hole",()=>Positive("resolved-large-hole",SolidScenes.Roof(79.6,17,31,hole:true)));
    Check("off-wing-tail-island-does-not-create-extra-solid",()=>Positive("off-wing-tail-island-does-not-create-extra-solid",SolidScenes.RoofWithOffPlaneTailIsland()));
    Check("measured-rounded-ridge-keeps-wings-connected",()=>Positive("measured-rounded-ridge-keeps-wings-connected",SolidScenes.RoofWithMeasuredRoundedRidge()));
    Check("reject-single-plane",()=>Reject(Grid((u,v)=>new(u,v,0)),thickness));
    Check("reject-single-wing",()=>Reject(Grid((u,v)=>new(u,v,u)),thickness));
    Check("reject-excessive-thickness",()=>Reject(SolidScenes.Roof(0).Points,60));
    Check("reject-nonfinite",()=>Reject([..SolidScenes.Roof(0).Points,new(double.NaN,0,0)],thickness));
    Check("reject-physical-twist",()=>Reject(SolidScenes.Roof(0,twist:true).Points,thickness));
}
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{failures,thicknessMm=thickness,results},ExtractionJson.Options));
Console.WriteLine($"{(failures==0?"PASS":"FAIL")}: {results.Count} solid checks; output {output}");
return failures==0?0:1;

object Positive(string name,SolidScene scene)
{
    var before=(ScanVector[])scene.Points.Clone();var model=AngleSolidBuilder.Build(scene.Points,new SolidExportOptions(thickness));
    Require(Unchanged(before,scene.Points),"Solid build changed measured input points.");
    var topology=ValidateMesh(model);Require(topology.ClosedSolidCount==1,"A single continuous angle became multiple bodies.");
    if(scene.Hole)Require(topology.ThroughHoleCount>=1,"A resolved measured hole was filled.");
    double maxThicknessError=0;int thicknessSamples=0;
    foreach(var tri in model.Triangles.Where(t=>(t.A&1)==0&&(t.B&1)==0&&(t.C&1)==0))
    {
        var a=model.Vertices[tri.A];var b=model.Vertices[tri.B];var c=model.Vertices[tri.C];
        var normal=(b-a).Cross(c-a).Normalize();var centroid=scene.ToLocal((a+b+c).Scale(1d/3));
        ScanVector expectedNormal;
        if(scene.FlatL)
        {
            bool horizontal=Math.Abs(centroid.Z)<=.04&&centroid.X>=-2&&centroid.X<=92;
            bool vertical=Math.Abs(centroid.X)<=.04&&centroid.Z>=-2&&centroid.Z<=52;
            Require(horizontal||vertical,"Reconstruction mirrored the flat L away from the measured surface.");
            expectedNormal=scene.RotateNormal(horizontal?new(0,0,-1):new(-1,0,0));
        }
        else
        {
            Require(Math.Abs(centroid.Z+Math.Abs(centroid.X))<.04&&Math.Abs(centroid.X)<=42&&Math.Abs(centroid.Y)<=72,
                "Reconstruction left the independent analytic measured wing/footprint.");
            expectedNormal=scene.RotateNormal(new ScanVector(centroid.X<0?-1:1,0,1).Normalize());
            if(scene.Hole)Require((centroid.X+22)*(centroid.X+22)+(centroid.Y-15)*(centroid.Y-15)>16,
                "A top triangle covered the unobserved centre of the resolved hole.");
        }
        Require(normal.Dot(expectedNormal)>.999,"Top-face orientation contradicts independent outward wing normals.");
        foreach(int index in new[]{tri.A,tri.B,tri.C})
        {
            Require(index+1<model.Vertices.Count,"Missing actual inner surface vertex.");
            double signed=(model.Vertices[index+1]-model.Vertices[index]).Dot(normal);
            maxThicknessError=Math.Max(maxThicknessError,Math.Abs(signed+thickness));thicknessSamples++;
        }
    }
    Require(thicknessSamples>100&&maxThicknessError<.001,$"Actual face-normal thickness error {maxThicknessError} mm.");
    ExportAndCheck(model,Path.Combine(output,name),scene.Hole);
    return new{topology,maxThicknessErrorMm=maxThicknessError,thicknessSamples,inputUnchanged=true};
}
object Replay(string path)
{
    string before=Hash(path);var cloud=ScanPly.Read(path);var input=cloud.Points.Select(p=>p.Position).ToArray();var snapshot=(ScanVector[])input.Clone();
    var model=AngleSolidBuilder.Build(input,new SolidExportOptions(thickness));var topology=ValidateMesh(model);
    if(Arg("--expected-solids") is { } expected)
        Require(topology.ClosedSolidCount==int.Parse(expected,CultureInfo.InvariantCulture),$"Replay contains {topology.ClosedSolidCount} closed solids; expected {expected}.");
    double maxError=0;int checkedCount=0;
    foreach(var t in model.Triangles.Where(t=>(t.A&1)==0&&(t.B&1)==0&&(t.C&1)==0))
    {
        var a=model.Vertices[t.A];var n=(model.Vertices[t.B]-a).Cross(model.Vertices[t.C]-a).Normalize();
        foreach(int i in new[]{t.A,t.B,t.C}){maxError=Math.Max(maxError,Math.Abs((model.Vertices[i+1]-model.Vertices[i]).Dot(n)+thickness));checkedCount++;}
    }
    Require(checkedCount>100&&maxError<.001,"Replay mesh does not have the requested actual face-normal thickness.");
    ExportAndCheck(model,Path.Combine(output,"replay"),false);
    Require(Unchanged(snapshot,input)&&Hash(path)==before,"Replay changed the original measured cloud.");
    return new{source=path,sourceSha256=before,inputPoints=cloud.Points.Length,topology,maxThicknessErrorMm=maxError,measuredCloudUnchanged=true};
}
object Reject(ScanVector[] points,double requestedThickness)
{
    var before=(ScanVector[])points.Clone();bool rejected=false;
    try{AngleSolidBuilder.Build(points,new SolidExportOptions(requestedThickness));}
    catch(Exception error) when(error is InvalidOperationException or InvalidDataException or ArgumentException){rejected=true;}
    Require(rejected,"Unsupported/invalid surface unexpectedly produced a solid.");
    Require(Unchanged(before,points),"Rejected solid build changed input points.");return new{rejected,inputUnchanged=true};
}
SolidTopology ValidateMesh(SolidModel model)
{
    var edges=new Dictionary<(int,int),List<(int,int)>>();
    foreach(var t in model.Triangles)
    foreach(var e in new[]{(t.A,t.B),(t.B,t.C),(t.C,t.A)})
    {
        var key=e.Item1<e.Item2?e:(e.Item2,e.Item1);
        if(!edges.TryGetValue(key,out var uses))edges[key]=uses=[];uses.Add(e);
    }
    Require(edges.Values.All(e=>e.Count==2&&e[0].Item1==e[1].Item2&&e[0].Item2==e[1].Item1),"Mesh edge is not used by exactly two opposite faces.");
    var reference=model.Vertices[0];double volume=model.Triangles.Sum(t=>(model.Vertices[t.A]-reference).Dot((model.Vertices[t.B]-reference).Cross(model.Vertices[t.C]-reference))/6);
    Require(volume>0,"Independent signed mesh volume is not positive.");var report=model.Validate();
    Require(report.ClosedAndOriented&&Math.Abs(report.VolumeMm3-volume)<Math.Max(.001,volume*1e-8),"Solid topology volume disagrees with actual triangles.");return report;
}
void ExportAndCheck(SolidModel model,string directory,bool expectHole)
{
    Directory.CreateDirectory(directory);string step=Path.Combine(directory,"model.step"),gltf=Path.Combine(directory,"model.gltf"),glb=Path.Combine(directory,"model.glb");
    SolidStepWriter.Write(step,model);SolidGltfWriter.Write(gltf,glb,model);
    string text=File.ReadAllText(step);Require(text.Contains("CLOSED_SHELL(")&&text.Contains("FACETED_BREP(")&&text.Contains("SI_UNIT(.MILLI.,.METRE.)"),"STEP lacks a closed millimetre B-rep.");
    if(expectHole)Require(text.Contains("=FACE_BOUND("),"STEP discarded the resolved inner loop.");
    using var doc=JsonDocument.Parse(File.ReadAllText(gltf));var root=doc.RootElement;var accessors=root.GetProperty("accessors");
    Require(accessors[0].GetProperty("count").GetInt32()==model.Vertices.Count&&accessors[2].GetProperty("count").GetInt32()==model.Triangles.Count*3,"glTF accessor counts disagree with the validated mesh.");
    string uri=root.GetProperty("buffers")[0].GetProperty("uri").GetString()!;var buffer=Convert.FromBase64String(uri[(uri.IndexOf(',')+1)..]);
    float X=BitConverter.ToSingle(buffer,0),Y=BitConverter.ToSingle(buffer,4),Z=BitConverter.ToSingle(buffer,8);var first=model.Vertices[0];
    Require(Math.Abs(X-first.X*.001)<1e-6&&Math.Abs(Y-first.Z*.001)<1e-6&&Math.Abs(Z+first.Y*.001)<1e-6,"glTF XYZ/mm to Y-up/metres conversion changed geometry.");
    var binary=File.ReadAllBytes(glb);Require(BinaryPrimitives.ReadUInt32LittleEndian(binary)==0x46546c67&&BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(4))==2
        &&BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(8))==binary.Length,"GLB header/length is invalid.");
}
static ScanVector[] Grid(Func<int,int,ScanVector> point)=>(from v in Enumerable.Range(-70,141) from u in Enumerable.Range(-40,81) select point(u,v)).ToArray();
static bool Unchanged(ScanVector[] a,ScanVector[] b)=>a.Length==b.Length&&a.Zip(b).All(p=>p.First.X.Equals(p.Second.X)&&p.First.Y.Equals(p.Second.Y)&&p.First.Z.Equals(p.Second.Z));
static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(input));}
static void Require(bool test,string message){if(!test)throw new InvalidOperationException(message);}

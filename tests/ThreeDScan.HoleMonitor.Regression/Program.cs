using System.Text.Json;
using ThreeDScan.Infrastructure.Scan;

string parent = Path.Combine(Path.GetTempPath(), "ThreeDScan.HoleMonitor.Regression");
string work = Path.Combine(parent, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(work, "_monitor"));
string file = Path.Combine(work, "_monitor", "latest-hole-boundary.json");
var reader = new HoleBoundaryReader();
int checks = 0;
try
{
    File.WriteAllText(file, """{"featureBoundary":[{"x":101.25,"y":202.5,"z":303.75}],"rawPlyPath":"raw.ply"}""");
    var legacy = reader.TryRead(work)!;
    Require(legacy.Points.Single().X == 101.25 && legacy.Features.Count == 0 && legacy.RawPlyPath == "raw.ply"
        && legacy.AbsoluteCoordinatesCalibrated is null,
        "Legacy boundary must retain exact coordinates without inventing a feature or scanner overlay.");

    File.WriteAllText(file, """
        {"taskId":"HOLE-TWO","capturedAt":"2026-11-26T16:32:00+08:00","selectedFeatureId":"H02","rawPlyPath":"raw.ply","coordinateFrame":"ConfiguredAbsolute","absoluteCoordinatesCalibrated":false,"features":[
          {"id":"H01","faceId":"F01","status":"CLOSED","closed":true,"boundary":[{"x":101,"y":102,"z":103}],"scannerBoundary":[{"x":1,"y":2,"z":3}]},
          {"id":"H02","faceId":"F02","status":"CLOSED","closed":true,"boundary":[{"x":201,"y":202,"z":203}],"scannerBoundary":[{"x":11,"y":12,"z":13}]},
          {"id":"P01","faceId":"F02","status":"PARTIAL","closed":false,"boundary":[{"x":301,"y":302,"z":303}],"scannerBoundary":[{"x":21,"y":22,"z":23}]}
        ]}
        """);
    var multiple = reader.TryRead(work)!;
    Require(multiple.Features.Count == 3 && multiple.Features.Count(f => f.Closed) == 2 && multiple.SelectedFeatureId == "H02"
        && multiple.Features[1].Boundary.Single().X == 201 && multiple.Features[1].ScannerBoundary.Single().X == 11
        && multiple.AbsoluteCoordinatesCalibrated == false && multiple.CoordinateFrame == "ConfiguredAbsolute"
        && multiple.CapturedAt == DateTimeOffset.Parse("2026-11-26T16:32:00+08:00"),
        "Separate holes and native/public coordinate frames must survive monitor serialization.");

    File.WriteAllText(file, """{"Features":[{"Id":"P01","Status":"PARTIAL","Closed":true,"Boundary":[{"X":7,"Y":8,"Z":9}]}]}""");
    var partial = reader.TryRead(work)!;
    Require(!partial.Features.Single().Closed && partial.Features.Single().Boundary.Single().Z == 9,
        "Partial status cannot be promoted to a complete hole; PascalCase legacy fields must still parse.");

    File.WriteAllText(file, """{"featureBoundary":[{"x":1,"y":2}]}""");
    Require(reader.TryRead(work) is null, "Missing measured Z must not silently become an invented zero.");

    File.WriteAllText(file, """{"features":[],"featureBoundary":[],"warnings":["No complete opening"],"absoluteCoordinatesCalibrated":true}""");
    var empty = reader.TryRead(work)!;
    Require(empty.Features.Count == 0 && empty.Points.Count == 0 && empty.Warnings.Count == 1 && empty.AbsoluteCoordinatesCalibrated == true,
        "A completed scan with no features must remain an explicit empty result.");

    File.WriteAllText(file, "{" );
    Require(reader.TryRead(work) is null, "A partially written monitor file must be retryable without replacing good data.");
    Console.WriteLine($"PASS: {checks} hole monitor contract checks.");
}
finally
{
    string resolved = Path.GetFullPath(work);
    if (!resolved.StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _)) throw new IOException("Unexpected test directory.");
    Directory.Delete(resolved, true);
}

void Require(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}

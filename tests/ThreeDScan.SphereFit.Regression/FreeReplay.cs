using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export.Models;
using ThreeDScan.Infrastructure.Measure;

internal static class FreeReplay
{
    internal static int Run(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        double? nominalOverride = Arg("--nominal") is { } nominal ? double.Parse(nominal, CultureInfo.InvariantCulture) : null;
        string fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "manifest.json")));
        var fixtures = manifest.RootElement.GetProperty("fixtures").EnumerateArray().ToArray();
        string latest = fixtures.Select(f => f.GetProperty("capture").GetString()!).Order(StringComparer.Ordinal).Last();
        var results = new List<object>();
        int trueCount = 0, acceptedTrue = 0, negativeCount = 0, rejectedNegative = 0, failures = 0;
        var analytical = args.Contains("--only-fixtures") ? [] : AnalyticalTests.Run().ToArray();
        foreach (var test in analytical)
        {
            if (!test.Pass) failures++;
            Console.WriteLine($"{(test.Pass ? "PASS" : "FAIL")} {test.Name}: {test.Error}");
        }
        foreach (var f in fixtures)
        {
            if (args.Contains("--only-analytical")) break;
            string id = f.GetProperty("id").GetString()!, capture = f.GetProperty("capture").GetString()!, ball = f.GetProperty("ball").GetString()!;
            if (args.Contains("--height-pair") && !f.TryGetProperty("heightTranslationRegression", out _)) continue;
            if (args.Contains("--new-failures") && !f.TryGetProperty("newFailureRegression", out _)) continue;
            if (args.Contains("--recorded") && f.GetProperty("seedSource").GetString() != "recorded-pattern-match") continue;
            if (args.Contains("--recent") && string.CompareOrdinal(capture, "20261126_101900") < 0) continue;
            if (args.Contains("--latest") && capture != latest) continue;
            if (Arg("--capture") is { } selected && capture != selected) continue;
            string fileName = f.GetProperty("data").GetString()!;
            string sourceSha = f.GetProperty("sourceHeightSha256").GetString()!;
            if (fileName != id + ".height-u16le.gz" || id != capture + "-" + ball
                || sourceSha.Length != 64 || !sourceSha.All(Uri.IsHexDigit)
                || f.GetProperty("sourceHeight").GetString() != $"SURF-CAM-01/{capture}/capture_height.png"
                || f.GetProperty("sourcePixelX").GetInt32() * (long)f.GetProperty("pitchX").GetUInt32() != f.GetProperty("xStart").GetInt32()
                || f.GetProperty("sourcePixelY").GetInt32() * (long)f.GetProperty("pitchY").GetUInt32() != f.GetProperty("yStart").GetInt32())
                throw new InvalidDataException($"{id}: fixture identity/provenance mismatch.");
            using var file = File.OpenRead(Path.Combine(fixtureRoot, fileName));
            string sha = Convert.ToHexStringLower(SHA256.HashData(file));
            if (sha != f.GetProperty("dataSha256").GetString()) throw new InvalidDataException($"{id}: fixture hash changed.");
            file.Position = 0;
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            int width = f.GetProperty("width").GetInt32(), height = f.GetProperty("height").GetInt32();
            var bytes = new byte[width * height * 2]; gzip.ReadExactly(bytes);
            if (gzip.ReadByte() != -1) throw new InvalidDataException($"{id}: wrong raw pixel count.");
            var raw = new ushort[width * height];
            for (int i = 0; i < raw.Length; i++) raw[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
            var original = (ushort[])raw.Clone();
            var frame = new HeightFrame
            {
                Info = new HeightInfo
                {
                    XPointCount = (ushort)width, YLineCount = (ushort)height,
                    XStart = f.GetProperty("xStart").GetInt32(), YStart = f.GetProperty("yStart").GetInt32(),
                    PitchX = f.GetProperty("pitchX").GetUInt32(), PitchY = f.GetProperty("pitchY").GetUInt32(), PitchZ = f.GetProperty("pitchZ").GetUInt32(),
                },
                ProfileCount = height, Heights = raw,
            };
            double historicalNominal = f.GetProperty("expectedDiameterMm").GetDouble();
            double appliedNominal = nominalOverride ?? historicalNominal, error = f.GetProperty("maxDiameterErrorMm").GetDouble();
            double seedX = f.GetProperty("seedX").GetDouble(), seedY = f.GetProperty("seedY").GetDouble();
            var fit = RobustSphereFitter.Fit(frame, seedX, seedY, appliedNominal, error);
            bool unchanged = original.AsSpan().SequenceEqual(raw), expected = f.GetProperty("expectedSuccess").GetBoolean();
            object? heightTranslation = null;
            if (f.TryGetProperty("heightTranslationRegression", out var heightControl) && heightControl.GetBoolean())
            {
                var shifted = new HeightFrame
                {
                    Info = frame.Info, ProfileCount = height,
                    Heights = raw.Select(v => v == 0 ? (ushort)0 : checked((ushort)(v + 1000))).ToArray(),
                };
                var shiftedFit = RobustSphereFitter.Fit(shifted, seedX, seedY, appliedNominal, error);
                double expectedDeltaZ = frame.Info.PitchZ / 100d;
                bool modelAvailable = fit.DiameterMm > 0 && shiftedFit.DiameterMm > 0;
                double deltaD = shiftedFit.DiameterMm - fit.DiameterMm, deltaX = shiftedFit.X - fit.X,
                    deltaY = shiftedFit.Y - fit.Y, deltaZ = shiftedFit.Z - fit.Z;
                bool pass = fit.Success == shiftedFit.Success && Math.Abs(deltaD) < 1e-5
                    && Math.Abs(deltaX) < 1e-5 && Math.Abs(deltaY) < 1e-5 && (!modelAvailable || Math.Abs(deltaZ - expectedDeltaZ) < 1e-5);
                if (!pass) failures++;
                heightTranslation = new { pass, modelAvailable, expectedDeltaZ, deltaD, deltaX, deltaY, deltaZ, shiftedFit };
            }
            if (!unchanged) failures++;
            if (expected) { trueCount++; if (fit.Success) acceptedTrue++; }
            else { negativeCount++; if (!fit.Success) rejectedNegative++; }
            if (args.Contains("--strict-real") && fit.Success != expected) failures++;
            results.Add(new
            {
                id, capture, ball, expectedSuccess = expected, unchanged, seedX, seedY,
                historicalNominalMm = historicalNominal, effectiveNominalMm = appliedNominal,
                officialNominalMm = f.GetProperty("officialStandardDiameterMm").GetDouble(), maxDiameterErrorMm = error,
                coreRadiusMm = appliedNominal * .5 * .60, validationRadiusMm = appliedNominal * .5 * .76,
                fixtureSha256 = sha, fit, heightTranslation,
            });
            Console.WriteLine($"{(fit.Success ? "ACCEPT" : expected ? "DIAGNOSTIC-REJECT" : "EXPECTED-REJECT")} {id}: nominal={appliedNominal:F3} D={fit.DiameterMm:F6} Cz={fit.Z:F6} core={fit.CoreRmsMm:F6} outer={fit.ValidationRmsMm:F6} {fit.FailureReason}");
        }
        string report = Path.GetFullPath(Arg("--report") ?? Path.Combine(Path.GetTempPath(), "ThreeDScan.SphereFit.Regression", Guid.NewGuid().ToString("N"), "free-replay.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            method = "Original free-radius Fit only; actual ROI recomputed from applied nominal. No known-radius mode or diameter-based ROI selection.",
            officialCorrection = new { historicalNominalMm = 24.5, officialNominalMm = 24.9, appliedNominalOverrideMm = nominalOverride, reason = "User corrected official standard sphere diameter to 24.9 mm; historical fixture annotations and raw pixels remain unchanged." },
            failures, trueCount, acceptedTrue, rejectedTrue = trueCount - acceptedTrue, negativeCount, rejectedNegative, analytical, results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Free replay: {acceptedTrue}/{trueCount} true accepted, {rejectedNegative}/{negativeCount} support rejected. Report: {report}");
        return failures == 0 ? 0 : 1;
    }
}

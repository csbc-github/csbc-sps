using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export.Models;
using ThreeDScan.Infrastructure.Measure;

if (!args.Contains("--calibration-mode")) return FreeReplay.Run(args);

double? realNominalOverride = args.SkipWhile(a => a != "--nominal").Skip(1).FirstOrDefault() is { } nominalText
    ? double.Parse(nominalText, CultureInfo.InvariantCulture) : null;

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "manifest.json")));
var fixtureEntries = manifest.RootElement.GetProperty("fixtures").EnumerateArray().ToArray();
string latestCapture = fixtureEntries.Select(f => f.GetProperty("capture").GetString()!).Order(StringComparer.Ordinal).Last();
var results = new List<object>();
int failures = 0;
int expectedPositiveRejections = 0;
int trueCandidates = 0, acceptedTrueCandidates = 0, negativeCandidates = 0, rejectedNegativeCandidates = 0;
int calibrationAcceptedTrueCandidates = 0, calibrationRejectedNegativeCandidates = 0;
var analytical = args.Contains("--only-fixtures") ? [] : AnalyticalTests.Run().Concat(CalibrationSphereTests.Run()).Concat(LocalizedContaminationTests.Run()).ToArray();
foreach (var test in analytical)
{
    if (!test.Pass) failures++;
    Console.WriteLine($"{(test.Pass ? "PASS" : "FAIL")} {test.Name}: {test.Error}");
}
foreach (var fixture in fixtureEntries)
{
    if (args.Contains("--only-analytical")) break;
    string id = fixture.GetProperty("id").GetString()!;
    if (args.Contains("--latest") && fixture.GetProperty("capture").GetString() != latestCapture) continue;
    if (args.Contains("--recent") && string.CompareOrdinal(fixture.GetProperty("capture").GetString(), "20261126_101900") < 0) continue;
    if (args.Contains("--recorded") && fixture.GetProperty("seedSource").GetString() != "recorded-pattern-match") continue;
    if (args.Contains("--height-pair") && !fixture.TryGetProperty("heightTranslationRegression", out _)) continue;
    if (args.Contains("--new-failures") && !fixture.TryGetProperty("newFailureRegression", out _)) continue;
    int width = fixture.GetProperty("width").GetInt32(), height = fixture.GetProperty("height").GetInt32();
    string capture = fixture.GetProperty("capture").GetString()!, ball = fixture.GetProperty("ball").GetString()!;
    string dataFile = fixture.GetProperty("data").GetString()!;
    string sourceSha = fixture.GetProperty("sourceHeightSha256").GetString()!;
    if (id != capture + "-" + ball || dataFile != id + ".height-u16le.gz"
        || sourceSha.Length != 64 || !sourceSha.All(Uri.IsHexDigit)
        || fixture.GetProperty("sourceHeight").GetString() != $"SURF-CAM-01/{capture}/capture_height.png"
        || fixture.GetProperty("sourcePixelX").GetInt32() * (long)fixture.GetProperty("pitchX").GetUInt32() != fixture.GetProperty("xStart").GetInt32()
        || fixture.GetProperty("sourcePixelY").GetInt32() * (long)fixture.GetProperty("pitchY").GetUInt32() != fixture.GetProperty("yStart").GetInt32()
        || fixture.GetProperty("sourcePixelX").GetInt32() + width > fixture.GetProperty("sourceImageWidth").GetInt32())
        throw new InvalidDataException($"{id}: invalid source provenance");
    using var input = File.OpenRead(Path.Combine(fixtureRoot, fixture.GetProperty("data").GetString()!));
    string checksum = Convert.ToHexStringLower(SHA256.HashData(input));
    if (checksum != fixture.GetProperty("dataSha256").GetString()) throw new InvalidDataException($"{id}: fixture checksum mismatch");
    input.Position = 0;
    using var gzip = new GZipStream(input, CompressionMode.Decompress);
    var bytes = new byte[width * height * 2];
    gzip.ReadExactly(bytes);
    if (gzip.ReadByte() != -1) throw new InvalidDataException($"{id}: extra height pixels");
    var raw = new ushort[width * height];
    for (int i = 0; i < raw.Length; i++) raw[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2, 2));
    var frame = new HeightFrame
    {
        Info = new HeightInfo
        {
            XPointCount = (ushort)width, YLineCount = (ushort)height,
            XStart = fixture.GetProperty("xStart").GetInt32(), YStart = fixture.GetProperty("yStart").GetInt32(),
            PitchX = fixture.GetProperty("pitchX").GetUInt32(), PitchY = fixture.GetProperty("pitchY").GetUInt32(),
            PitchZ = fixture.GetProperty("pitchZ").GetUInt32(),
        },
        ProfileCount = height, Heights = raw,
    };
    var original = (ushort[])raw.Clone();
    double historicalNominal = fixture.GetProperty("expectedDiameterMm").GetDouble();
    double officialNominal = fixture.GetProperty("officialStandardDiameterMm").GetDouble();
    double appliedNominal = realNominalOverride ?? officialNominal;
    var fit = RobustSphereFitter.Fit(frame, fixture.GetProperty("seedX").GetDouble(),
        fixture.GetProperty("seedY").GetDouble(), appliedNominal,
        fixture.GetProperty("maxDiameterErrorMm").GetDouble());
    var calibration = RobustSphereFitter.FitCalibrationSphere(frame, fixture.GetProperty("seedX").GetDouble(),
        fixture.GetProperty("seedY").GetDouble(), appliedNominal,
        fixture.GetProperty("maxDiameterErrorMm").GetDouble());
    bool expected = fixture.GetProperty("expectedSuccess").GetBoolean();
    var calibrationErrors = new List<string>();
    bool isHeightPair = fixture.TryGetProperty("heightTranslationRegression", out var heightControl) && heightControl.GetBoolean();
    bool newFailureCase = fixture.TryGetProperty("newFailureRegression", out var newFailure) && newFailure.GetBoolean();
    bool calibrationStrict = isHeightPair || newFailureCase || !expected;
    if (calibrationStrict && calibration.Success != expected)
        calibrationErrors.Add($"Known-radius calibration success={calibration.Success}, expected={expected}: {calibration.FailureReason}");
    if (calibration.Success && (!calibration.UsesKnownRadius || Math.Abs(calibration.NominalDiameterMm - appliedNominal) > 1e-10))
        calibrationErrors.Add("Calibration mode did not identify the applied known radius.");
    if (calibration.Success && Math.Abs(calibration.DiameterMm - fit.DiameterMm) > 1e-5)
        calibrationErrors.Add("Calibration overwrote or changed the independent free-diameter diagnostic.");
    if (calibration.Success && (Math.Abs(calibration.FullFreeCoreRmsMm - fit.CoreRmsMm) > 1e-5
        || Math.Abs(calibration.FullFreeCoreP95Mm - fit.CoreP95Mm) > 1e-5
        || Math.Abs(calibration.FullFreeValidationRmsMm - fit.ValidationRmsMm) > 1e-5))
        calibrationErrors.Add("Calibration replaced the original untrimmed raw-surface quality diagnostics.");
    if (newFailureCase && expected && !fit.IsGeometryValid && calibration.Success && !calibration.UsedLocalizedOutlierValidation)
        calibrationErrors.Add("A raw-quality failure became eligible without independent localized-outlier validation.");
    if (calibration.Success && calibration.IsDiameterWithinTolerance != (Math.Abs(fit.DiameterMm - appliedNominal) <= .5))
        calibrationErrors.Add("Free-diameter tolerance flag does not match the unchanged gate.");
    if (appliedNominal == 24.5 && id == "20261126_105507_ffeaa39e-bottom"
        && (!calibration.Success || calibration.IsDiameterWithinTolerance || Math.Abs(calibration.DiameterMm - 25.2446373864) > 1e-4))
        calibrationErrors.Add("Complete-frame true sphere must have a usable calibrated center and retain its out-of-tolerance 25.245 mm free measurement.");
    if (appliedNominal == 24.9 && id == "20261126_105507_ffeaa39e-bottom"
        && (!calibration.Success || !calibration.IsDiameterWithinTolerance || Math.Abs(calibration.DiameterMm - 25.259185) > 1e-4))
        calibrationErrors.Add("Official 24.9 mm complete sphere must retain its usable center and in-tolerance full-point free diameter 25.259185 mm.");
    if (appliedNominal == 24.9 && id == "20261126_105203_29b77e35-right"
        && (!calibration.Success || calibration.IsDiameterWithinTolerance || Math.Abs(calibration.DiameterMm - 24.317635) > 1e-4))
        calibrationErrors.Add("Official 24.9 mm start right sphere must retain a usable center and its out-of-tolerance full-point free diameter 24.317635 mm.");
    var calibrationSeedStudy = new List<object>();
    if (isHeightPair && expected && calibration.Success)
    {
        foreach ((double dx, double dy) in new[] { (.75d, -.5d), (-.5d, .75d) })
        {
            var offsetFit = RobustSphereFitter.FitCalibrationSphere(frame, fixture.GetProperty("seedX").GetDouble() + dx,
                fixture.GetProperty("seedY").GetDouble() + dy, appliedNominal,
                fixture.GetProperty("maxDiameterErrorMm").GetDouble());
            bool stable = offsetFit.Success && Math.Abs(offsetFit.Z - calibration.Z) <= .04
                && Math.Abs(offsetFit.X - calibration.X) <= .05 && Math.Abs(offsetFit.Y - calibration.Y) <= .05;
            if (!stable) calibrationErrors.Add($"Offset seed ({dx},{dy}) did not yield a stable measured center: {offsetFit.FailureReason}");
            calibrationSeedStudy.Add(new { dx, dy, stable, fit = offsetFit });
        }
    }
    object? heightTranslation = null;
    bool translationPass = true;
    if (isHeightPair)
    {
        const int rawOffset = 1000;
        var shiftedRaw = raw.Select(v => v == 0 ? (ushort)0 : checked((ushort)(v + rawOffset))).ToArray();
        var shifted = new HeightFrame { Info = frame.Info, ProfileCount = height, Heights = shiftedRaw };
        var shiftedFit = RobustSphereFitter.Fit(shifted, fixture.GetProperty("seedX").GetDouble(),
            fixture.GetProperty("seedY").GetDouble(), appliedNominal,
            fixture.GetProperty("maxDiameterErrorMm").GetDouble());
        double expectedDeltaZ = rawOffset * frame.Info.PitchZ / 100000d;
        bool hasMeasuredModel = fit.DiameterMm > 0 && shiftedFit.DiameterMm > 0;
        double deltaD = shiftedFit.DiameterMm - fit.DiameterMm, deltaX = shiftedFit.X - fit.X,
            deltaY = shiftedFit.Y - fit.Y, deltaZ = shiftedFit.Z - fit.Z;
        translationPass = shiftedFit.Success == fit.Success && Math.Abs(deltaD) < 1e-5
            && Math.Abs(deltaX) < 1e-5 && Math.Abs(deltaY) < 1e-5
            && (!hasMeasuredModel || Math.Abs(deltaZ - expectedDeltaZ) < 1e-5);
        heightTranslation = new { pass = translationPass, hasMeasuredModel, expectedDeltaZ, deltaD, deltaX, deltaY, deltaZ, shiftedFit };
        var shiftedCalibration = RobustSphereFitter.FitCalibrationSphere(shifted, fixture.GetProperty("seedX").GetDouble(),
            fixture.GetProperty("seedY").GetDouble(), appliedNominal,
            fixture.GetProperty("maxDiameterErrorMm").GetDouble());
        bool hasCalibrationCenter = calibration.Success && shiftedCalibration.Success;
        double calibrationDeltaZ = shiftedCalibration.Z - calibration.Z;
        bool calibratedHeightPass = shiftedCalibration.Success == calibration.Success
            && shiftedCalibration.IsDiameterWithinTolerance == calibration.IsDiameterWithinTolerance
            && Math.Abs(shiftedCalibration.DiameterMm - calibration.DiameterMm) < 1e-5
            && Math.Abs(shiftedCalibration.X - calibration.X) < 1e-5 && Math.Abs(shiftedCalibration.Y - calibration.Y) < 1e-5
            && (!hasCalibrationCenter || Math.Abs(calibrationDeltaZ - expectedDeltaZ) < 1e-5);
        if (!calibratedHeightPass) calibrationErrors.Add("Known-radius center failed the controlled raw-Z translation.");
        heightTranslation = new
        {
            pass = translationPass, hasMeasuredModel, expectedDeltaZ, deltaD, deltaX, deltaY, deltaZ, shiftedFit,
            calibratedHeightPass, calibrationDeltaZ, shiftedCalibration,
        };
    }
    bool strict = args.Contains("--strict-real");
    bool unchanged = original.AsSpan().SequenceEqual(raw);
    bool expectationMatched = fit.Success == expected;
    bool pass = (!strict || expectationMatched) && unchanged && translationPass && calibrationErrors.Count == 0;
    if (expected && !fit.Success) expectedPositiveRejections++;
    if (expected) { trueCandidates++; if (fit.Success) acceptedTrueCandidates++; if (calibration.Success) calibrationAcceptedTrueCandidates++; }
    else { negativeCandidates++; if (!fit.Success) rejectedNegativeCandidates++; if (!calibration.Success) calibrationRejectedNegativeCandidates++; }
    if (!pass) failures++;
    results.Add(new
    {
        id, capture, ball,
        expected, strict, expectationMatched, pass, unchanged, historicalNominalMm = historicalNominal,
        effectiveNominalMm = appliedNominal, officialNominalMm = officialNominal,
        seedX = fixture.GetProperty("seedX").GetDouble(), seedY = fixture.GetProperty("seedY").GetDouble(),
        seedSource = fixture.GetProperty("seedSource").GetString(), fixtureSha256 = checksum, fit, heightTranslation,
        calibration, calibrationStrict, calibrationErrors, calibrationSeedStudy,
    });
    string status = !pass ? "FAIL" : fit.Success ? "ACCEPT" : expected ? "DIAGNOSTIC-REJECT" : "EXPECTED-REJECT";
    Console.WriteLine($"{status} {id}: D={fit.DiameterMm:F5} core={fit.CoreRmsMm:F5} ring={fit.ValidationRmsMm:F5} success={fit.Success} {fit.FailureReason}");
    Console.WriteLine($"  calibration: usable={calibration.Success}, freeDWithinTolerance={calibration.IsDiameterWithinTolerance}, Cz={calibration.Z:F5} {string.Join("; ", calibrationErrors)}");
}
string reportPath = Path.GetFullPath(args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault()
    ?? Path.Combine(Path.GetTempPath(), "ThreeDScan.SphereFit.Regression", Guid.NewGuid().ToString("N"), "summary.json"));
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(new
{
    failures, expectedPositiveRejections, trueCandidates, acceptedTrueCandidates,
    negativeCandidates, rejectedNegativeCandidates, calibrationAcceptedTrueCandidates, calibrationRejectedNegativeCandidates,
    officialCorrection = new { historicalNominalMm = 24.5, officialNominalMm = 24.9, appliedNominalOverrideMm = realNominalOverride },
    strictReal = args.Contains("--strict-real"), analytical, results,
}, json));
Console.WriteLine($"Measured replay: {acceptedTrueCandidates}/{trueCandidates} true candidates accepted; {expectedPositiveRejections} out-of-gate/quality rejections; {rejectedNegativeCandidates}/{negativeCandidates} negative candidates rejected.");
Console.WriteLine($"Calibration replay: {calibrationAcceptedTrueCandidates}/{trueCandidates} true centers usable; {calibrationRejectedNegativeCandidates}/{negativeCandidates} support candidates rejected.");
Console.WriteLine($"Report: {reportPath}");
return failures == 0 ? 0 : 1;

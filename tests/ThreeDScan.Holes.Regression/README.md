# Hole-scan regression

Run the production hole reader, detector, analysis wrapper and three native JSON exporters without scanner hardware, SQL, API or WPF:

```powershell
dotnet run --project tests/ThreeDScan.Holes.Regression -c Release -- --output LocalDevelop/holes-check-new
```

Five analytic checks cover two independently generated enclosed openings, a truncated partial opening, a measured solid plate, ROI-to-source PLY vertex provenance, and rejection of nonfinite input. All exported boundaries and scan-row edge pairs must retain their exact measured scanner XYZ. ROI mapping is checked against the original unfiltered input vertices. Unknown expected hole counts stay null, and export files cannot replace previous results.

The standalone reference's saved binary fixtures and JSON baselines can be verified without modifying or rebuilding its source:

```powershell
dotnet run --project tests/ThreeDScan.Holes.Regression -c Release -- --output LocalDevelop/holes-parity-new --reference-root 'C:\Users\CT\Desktop\csharp(辨識孔洞)'
```

This adds 20 saved cases and compares the full native analysis, all three schema v2 exports, hole ordering, closure, discrete counts, source indices and measured boundary coordinates. Floating calculations allow an absolute difference of 1e-8; discrete values and measured point provenance must match exactly. Three historical PLY manifest entries refer to files on another machine and are not silently counted as passing cases.

Available real recordings can be compared directly against the supplied standalone `PlyProcessor.Holes.dll`, using identical scanner XYZ arrays:

```powershell
dotnet run --project tests/ThreeDScan.Holes.Regression -c Release -- --only-replay --output LocalDevelop/holes-real-new --reference-root 'C:\Users\CT\Desktop\csharp(辨識孔洞)' --replay 'C:\path\to\raw.ply'
```

Repeat `--replay` for multiple recordings. Large real-file comparisons include every opening, measured boundary, scan-row endpoint, source index, face, diagnostic and all three native JSON exports. Display-only interior face samples are excluded from the large replay JSON comparison to avoid allocating gigabytes of repeated preview data; saved golden fixtures still compare their complete sample arrays. This verifies software agreement, rather than asserting the physical workpiece has a particular number of holes. The reference detector uses four measured faces and preserves every CLOSED and PARTIAL opening. Expected hole count is metadata; it never fills, truncates or changes detection.

`HoleAnalysisResult.Detection` retains native detector-array indices; `AnalysisSourcePointIndices` maps those indices to the source PLY. `FeatureOpening.MeasuredBoundary` and `EdgePairs` already contain the mapped source PLY indices. `HoleExporter.Save` passes the map and records `sourcePointIndexBasis: SourcePlyVertexIndex` in native exports. Scanner-frame exports and the API's separately transformed absolute coordinates must not be mixed.

Six angle-scope checks independently label an organized scanner grid containing a floor, raised support and two measured roof wings. Real wing openings remain unchanged while separate support/floor cutouts are excluded from the API and every native export. Rotation, tilted ground and a negative Z translation guard against fixed-height or face-ID selection. A request ROI containing only one visible wing must still use the full capture's two-wing evidence and retain original full-file vertex indices. A capture with only one measured wing, only floor/support planes, or an isolated flat plate must record no angle holes and explain the unavailable scope. Partial closure remains partial rather than being filled by a crop or a fitted plane.

The audited 2026-11-26 `HOLE-68993acd8ef04ec2b45ea7406226432a` recording can separately check angle-only recording with `--only-replay --scope-replay INPUT.ply`. It must retain the same five enclosed slots and one partial wing opening, with identical status, XYZ, source indices and row endpoints, while excluding the three measured background openings from all three JSON files. This fixture-specific expectation is never a production count or face-ID rule. A reference root is not required for this check.

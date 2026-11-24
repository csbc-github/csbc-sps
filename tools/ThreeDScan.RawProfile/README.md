# Raw profile measurement trial

This offline command analyzes the original scanner XY in this project's raw PLY recordings. It does not start a scanner, access SQL, change settings, send callbacks, or change the production API's default measurement mode.

```powershell
dotnet run --project tools/ThreeDScan.RawProfile -c Release -- --input "C:\captures\line_raw.ply" --output "C:\captures\raw-analysis" --scans 20
```

The output directory must be new or empty. The source recording is read only.

The command requires consecutive source scans and computes an arithmetic XY mean separately for each original ray. Each point retains its actual valid sample count. It does not apply the existing range-cluster mean, support-percentage filtering, calibration, spatial interpolation, fitted line intersections, table projection, dimension multipliers, or fixed offsets.

Automatic corner detection selects existing averaged samples. The report retains the left/apex/right ray IDs and native XY. Width is the distance between the selected feet; each wing is the distance to the selected apex; height is the perpendicular distance from the apex to the feet chord. A visible upper-face fragment without both supported endpoints is marked incomplete rather than extended to the table. These are visible 2D dimensions, not longitudinal length or plate thickness.

Outputs are `average-raw.json`, `measurements.json`, `measurements.csv` and `observed-points.svg`. The plot marks the actual selected samples using the same scale for both native axes.

To inspect a different set of observed corners, pass `--selection points.json`:

```json
[
  { "leftRay": 618, "apexRay": 590, "rightRay": 545 }
]
```

These IDs are an example from the saved diagnostic scene, not production defaults. The command rejects missing or degenerate selections. It does not manufacture replacement coordinates.

Optional `--reference dimensions.json` values are used only to report errors **after** detection:

```json
[
  { "index": 1, "leftWidth": 103.5, "rightWidth": 101, "height": 76, "width": 140 }
]
```

Reference dimensions never participate in corner selection or measurement. Index comparisons need confirmation when object counts or ordering differ.

An intentionally aborted diagnostic recording can be examined only with explicit `--allow-incomplete-recording`. Its reports retain `recordingComplete=false` and a prominent warning. This analyzes intact available frames; it does not validate or promote an aborted recording into a completed scan result.

Regression coverage runs with:

```powershell
dotnet run --project tests/ThreeDScan.LineScan.Regression -c Release -p:Platform=x64
```

# Surface-scan regression

Run from the repository root:

```powershell
dotnet run --project tests/ThreeDScan.SurfaceScan.Regression -c Release
```

The console compiles the production geometry sources directly, with no scanner SDK, WPF, EF, SQL, API host or external test packages. It never connects to a device or changes app settings.

Analytic fixtures independently label sloped ground, 90° roof wings and low measured edges. Checks require 90% of the observed 2.5–10.5 mm edges to survive a 12 mm seed threshold while fewer than 0.5% of output points are ground. Roofs at 0°, 35°, 80°, 90° and 125° verify that the long axis need not follow scanner Y. A raised flat plate with measured 3 mm thickness sides must retain a point cloud without being called two-wing angle steel; a missing second wing cannot be replaced with ground. A table with about 0.42 mm RMS and a positive noise seam on the wing extension checks that recovery clearance follows measured noise. A small connected non-wing patch extending beyond the roof checks that fitted end stations and corners use wing-supported measurements. Ground-only, dominant raised-top/perimeter-floor and cancellation cases guard the ground estimator. Every exported scanner coordinate, measurement attribute and raw index is checked against the untouched source; the tests do not allow invented or interpolated points.

Nine additional support-platform checks exercise a raised board touching the object's XY footprint, without an artificial empty gap. Positive scenes cover a true 45°/45° roof, a roof rotated 35° on a tilted board, two separate roofs sharing one board, and a legal unequal L with a 90 mm horizontal wing measured 5 mm above the board plus a 50 mm upright wing. The unequal L is also tested after a 35° rotation on an inclined board, so rejecting a support plane cannot simply discard every shallow real wing. They require less than 0.5% support/floor contamination, at least 90% measured low-edge recall, correct axes and angle, separate child candidates for separate objects, and retained parent-rank lineage. A lone board, its measured 3 mm thickness side, and a single wing on a board must not create two-wing geometry. Explicit Planar selection must retain the original parent and at least 95% of its measured board samples for hole analysis.

The final 21 analytic checks pass. Platform workpieces retain 100% of the labelled measured low samples with zero support/floor contamination; the two-object case offers two independently validated children. Both unequal L orientations retain all 13,536 low-wing samples, including the wing only 5 mm above the board. Planar selection retains all 90,020 board samples. Exact scanner coordinates, attributes, unique source indices, and parent lineage are verified for these results.

The low horizontal L wing is deliberately below the existing 12 mm high-seed threshold. After a reliable support plane is identified, measured residuals above the support-noise clearance may form a child, which must still contain at least 100 original high seeds and meet the original connected-cell and complete-wing checks. The support plane is a segmentation aid and does not redefine the global floor or transform original measurements. A missing wing is never supplied by the support board.

The original baseline failed five of the first seven platform cases: four positive workpieces lost reliable geometry, while the board plus one visible wing was incorrectly identified as a wing pair. An independent ridge check placed the false platform's intersection 135 mm from its nearest measured end; true roof and L-wing intersections were within 1.42 mm. The endpoint guard is tested alongside valid unequal wings rather than assuming every broad near-horizontal plane is background.

Run only the nine platform cases with `--only-platform`:

```powershell
dotnet run --project tests/ThreeDScan.SurfaceScan.Regression -c Release -- --only-platform --output LocalDevelop/platform-regression-new
```

Use `--keep` to retain the GUID-named temporary workspace. Failure always retains its artifacts. `--output NEW_DIRECTORY` retains results in an explicitly chosen new directory; existing files are never replaced. Successful default runs remove only their verified GUID-named directory under the system temporary test folder.

Optional real recording replay uses the exact production `SegmentProcessor` and checks provenance, while writing candidate, ground and geometry diagnostics. The original PLY is read-only and is not copied into the repository:

```powershell
dotnet run --project tests/ThreeDScan.SurfaceScan.Regression -c Release -- --only-replay --output LocalDevelop/surface-regression-new --replay 'C:\path\to\scan_raw.ply'
```

Repeat `--replay` to process multiple files sequentially. Replay reports geometry availability rather than assuming every real recording contains two sufficiently observed wings. The three screenshot recordings are `run_01594c0fa71c`, `run_dfaa4422bc15` and `run_0111b8c52b1a` under the user's external extraction-app exports; the 2026-11-26 failure is `SURFACE-ab2fcaec637f45d1a7bb51a5b3c33630/segment-001/raw.ply` in the local API task output.

The later screenshot recording `run_4cf36b980700/scan_raw.ply` contains 1,386,198 measured vertices. Replaying it through the current production `SegmentProcessor` selects a 242,689-point validated child above the broad planar parent, with an 87.577° wing-normal angle and 0.340/0.348 mm plane RMS. The retained scanner vertices and their source indices exactly match the untouched input. This differs from the separate legacy extraction executable's 425,369-point selection shown in the screenshot; the replay does not alter or validate that external executable. Reports are `LocalDevelop/diagnostics/ground-strip-20261126-baseline/regression-summary.json` and `LocalDevelop/diagnostics/ground-strip-20261126-final-21/regression-summary.json`. Those real-scene observations are diagnostics, not independently labelled contamination or physical-dimension ground truth.

Six additional checks in `WingStabilityChecks.cs` bring the analytic suite to 27 checks. A 319 mm broad wing, a 50 mm upright and sparse measured samples on their 3.9 mm rounded bend exercise the physical bias of trimming 1% of a broad face. Four variants retain the same independently labelled longitudinal axis and both wing supports after a 35° rotation or removal of 163 interior samples; no missing points are filled. A separate sparse 39 mm side wall exercises pair ranking: its longer intersection must not outweigh the independently denser upright wing. Negative fixtures retain rejection of a supporting plate crossing through the ridge, a 6 mm thickness end, an unmeasured 7 mm rim gap and ridge-adjacent dots confined to one end.

The original 5 mm physical ridge tolerance remains unchanged. If population-trimmed extents alone miss it, the fitter inspects original plane-supported points within 5 mm of the fitted ridge and within the common measured longitudinal range. It requires at least `max(100, ceil(planePointCount * 0.001))` points and sufficient support in at least six of ten longitudinal bins. An intersection inside the central body of a broad support plate remains invalid. Pair ranking uses the geometric mean of the two independent plane-support counts, reducing the influence of a sparse side wall beside a very dense broad face; angle, width, overlap, RMS and total-support requirements remain in place.

The final 27-check run is recorded in `LocalDevelop/diagnostics/wing-stability-final-20261126/regression-summary.json`. An isolated copy with the former trimmed-rim check restored fails the broad rounded-L fixture while retaining the other negative guards (`wing-stability-old-guard-20261126`). Restoring only the former sum-of-support ranking, with the corrected rim check retained, independently fails the sparse-side-wall axis check (`wing-stability-old-score-report-20261126`). These analytic tests establish the intended geometry and labels independently; the real scan replays are additional diagnostics rather than physical metrology ground truth.

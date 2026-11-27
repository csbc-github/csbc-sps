# Head feature display regression

This Windows/x64 console runner source-links the display metadata reader, camera point reader and GLB loader. It does not reference the application project or construct a WPF `Application`. It starts no scanner, socket, timer, database or API host.

Run the 18 independent checks with small generated task/PLY/GLB fixtures:

```powershell
dotnet run --project tests/ThreeDScan.HeadFeatures.Regression -- --output LocalDevelop/diagnostics/head-features
```

The checks cover:

- Identical authoritative `segments[0].response.headFeatures` for PLY and GLB paths, rather than refitting a head from the cloud.
- The first segment's `segmentZDelta` and the caption `第1段前緣／共2段／依相機位姿合併` for a two-segment task.
- Caller-supplied model thickness 6 mm kept separate from absent or independently measured left/right thickness.
- Bounds-only clouds having dimensions without fake head annotations; missing, malformed, partial or mismatched task metadata never triggering a legacy API-task refit.
- Switching tasks clearing previous head points, bounds and thickness values; cancellation; finite points and a legitimate point at `(0,0,0)`.
- `Clear()` disabling head points before its first property-change notification, so camera observers cannot use the old head.
- Exact-tagged GLB positions restored from metre/Y-up to scanner XYZ millimetres, corresponding normals, recomputed bounds and preserved triangle winding. Untagged files and different convention strings retain legacy behavior.
- Every generated input file remaining byte-for-byte unchanged after reading; GLB byte arrays remaining unchanged.

Add two integration checks without loading the application or large point cloud:

```powershell
dotnet run --project tests/ThreeDScan.HeadFeatures.Regression -- `
  --output LocalDevelop/diagnostics/head-features-actual `
  --task-root ThreeDScan.Api/bin/x64/Debug/net10.0-windows/export/tasks/SURFACE-eeea2318bc37463c986d9a603ce3b103 `
  --wpf-assembly LocalDevelop/validation/wpf-surface-heads-20261126/ThreeDScan.dll
```

`--task-root` reads only the small actual `task.json`, verifies its first segment's nine coordinate values and Z delta through both artifact paths, and checks the task hash remains unchanged. It requires the two-segment, manual-6-mm, unavailable-measured-thickness fixture described above. It does not load the saved PLY or GLB mesh.

`--wpf-assembly` loads a built application DLL and its nearby dependencies for reflection. It creates the view model without running its constructor and invokes the actual `TryApplyArtifactFeatures` method. Stale versions and paths must be rejected, current missing data must clear old annotations, and a late old result must not restore them. No `Application` or view-model timer is started.

All checks are strict and return a nonzero exit code on failure. The runner writes `summary.json` and generated tiny fixtures under the requested output directory. It does not overwrite saved scan data. These checks verify metadata and display coordinate handling; they do not establish physical measurement calibration or visually verify the rendered application.

Validation on 2026-11-26: **20/20 passed** with both integration arguments. Report: `LocalDevelop/diagnostics/wpf-head-features-20261126/final-01/summary.json`.

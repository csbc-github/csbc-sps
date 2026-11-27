# Hole capture regression

This source-links the production OneClick-compatible acquisition protocol and native-buffer validation. It does not load a scanner SDK, open a controller, trigger a capture, change settings, use WPF, or connect to a database.

```powershell
dotnet run --project tests/ThreeDScan.HoleCapture.Regression -c Release -- --output LocalDevelop/diagnostics/hole-capture-new
```

The checks independently verify native structure layouts, previous-image rejection, transient reads, valid data with `committed=0`, 444 actual rows, the 1000-profile transfer cap, same-image split reads, mismatched geometry/headers, fatal errors, cleanup, cancellation, timeout, and explicit-only program changes. Export goldens verify raw zero filtering, retention of `65535`, unsigned luminance, exact F6 vertex bytes under a comma-decimal culture, local-grid zero origin, immutable height arrays, and the unchanged surface default SDK origin.

To compare the same synthetic sensor samples directly with the supplied reference executable's exporter, add `--reference` pointing to its existing `Ljs8.OneClick.dll`. This only invokes its in-memory PLY exporter by reflection; it never invokes its scan or native SDK functions.

```powershell
dotnet run --project tests/ThreeDScan.HoleCapture.Regression -c Release -- --output LocalDevelop/diagnostics/hole-capture-reference-new --reference 'C:\Users\CT\Desktop\CSharp(20261021掃描)\Ljs8.OneClick\bin\Debug\net8.0-windows\Ljs8.OneClick.dll'
```

An explicit output directory must not already exist. All test files and `summary.json` remain in the reported unique directory for review; no existing files are replaced.

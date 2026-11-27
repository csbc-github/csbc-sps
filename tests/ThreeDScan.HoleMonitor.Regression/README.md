# Hole monitor contract regression

Run `dotnet run --project tests/ThreeDScan.HoleMonitor.Regression -c Release`.

The six checks compile the production monitor reader and DTO sources without a device, database or WPF host. They verify legacy absolute-only boundaries, separate native/public coordinates for multiple holes, partial-versus-closed status, PascalCase compatibility, explicit empty results and retryable incomplete JSON. Missing measured coordinates are rejected rather than replaced by zero. Test artifacts are created in a fresh GUID directory and removed only after its resolved path is checked.

`tests/ThreeDScan.Wpf.Regression/HoleMonitorChecks.cs` additionally exercises actual monitor selection, clearing stale data after an empty scan, legacy display, native coordinate markers and preservation of the orbit camera when the selected hole changes. Run that project with an isolated `--artifacts-path` while the main application is running.

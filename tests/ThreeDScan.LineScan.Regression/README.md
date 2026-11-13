# Line-scan regression

Run from the solution root:

```powershell
dotnet run --project tests/ThreeDScan.LineScan.Regression -c Release -p:Platform=x64
```

This console tool uses the existing .NET 10 Windows SDK and project dependencies; it does not require a test-framework package. It does not open the WPF app, connect to a scanner, run the production API entry point, load production appsettings, start SQL Server, modify authentication settings, or send callbacks.

The seven original scan snapshots and `scene-reference.json` are fixed references captured from the independently verified IP_control guarded scene implementation on 2026-11-13. Expected measurements are not recomputed by the target code. Mirrored cases independently reverse the reference legs and calibrated X coordinates. `protocol-python-reference.json` contains three original CoLa B packets plus 100 range/scale/angle/quality variants and their Python-parser reference results.

Coverage includes both 3600-ray exact 10 mm cluster-boundary averagers, the guarded face/apex/contact rules, unchanged input points, actual two-frame encoder-positioned `LineScanMeasureService`, actual Application DTO mapping, and the real scan/task controllers over a temporary loopback HTTP server in the `Testing` environment. The HTTP application's I/O boundary is a test bridge: it supplies the real measurement/mapping results without invoking database repositories, scanner services, background workers, or production authentication. It therefore verifies controller routing, JSON/error/status contracts, not production database or physical-device integration.

The context test also writes a tiny two-frame recording using the real `Recorder`, including its encoder and frozen calibration/settings sidecars. It streams the private measurement context, checks actual supported geometry and encoder travel, verifies that public reconstructed PLY and metadata still apply the configured 3 mm height crop, and covers legacy frozen-settings metadata, incomplete recordings, cancellation and missing-raw rejection. Only a verified GUID-named test directory under the system temp directory is removed afterward.

The no-motion regression uses the real `EncoderScanSession` with two valid scans and unchanged encoder positions. It requires the reconstruction guard to reject that recording without inventing Y coordinates or a completed 3D artifact. It then executes the real accepted-work queue and scoped `ScanAppService` against in-memory repository/device/callback boundaries, asserting a persisted `Failed` task, released device, failed latest/query payloads and correct callback trace metadata. It covers both the original reconstruction exception and the actual device host → SICK adapter → Domain exception path. The latter injects an already-recorded capture into the real host with an unstarted scanner; no socket or hardware is used. A simulated callback delivery exception must not put the task back into `Processing`; no callback network request is sent.

The existing DAS dimension definitions are preserved: API `width` is the head-foot chord distance and API `height` is the apex above the interpolated foot chord. These intentionally differ from standalone profile calibrated-X span and calibrated-baseline height when a visible shallow endpoint remains above the table. Wing widths and all returned head/tail features use the same supported geometry.

Encoder simulation is selected only by JSON. The simulation tests bind the actual Infrastructure DI policy from JSON under both Production and Development host labels; either label honors the same enabled/disabled switch. They cover stationary/missing encoder channels, both direction signs, duplicate packets, sensor-clock wrap, 20-frame averaging, frozen replay, untouched real XY, explicit synthetic markers in every artifact, invalid speeds, missing frames, scanner faults and clock reset. The real-mode no-motion tests remain active. A tiny fake CoLa B sender on a random loopback port additionally exercises the real device Host start/capture/finalize path; it has no physical scanner, database, production API entry point or callback connection.

The 20-frame encoder averaging case includes duplicate telegrams and compares streaming output with both legacy `Cloud` reconstruction and a no-duplicate reference recording. Geometry, source ray/counter/timestamps and original raw frame IDs are checked. Genuine scan-counter gaps and empty geometry frames must still split averaging into separate blocks.

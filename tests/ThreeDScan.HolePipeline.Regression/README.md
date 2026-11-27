# Hole pipeline regression

Run `dotnet run --project tests/ThreeDScan.HolePipeline.Regression -c Release -- --output NEW_DIRECTORY`.

The test invokes the production `ScanOrchestrator.ExecuteHoleAsync` with an in-memory task repository and an explicitly recorded capture. It uses no network, device, SQL or callback. Two independently cut circular openings exercise the real detector and the three reference JSON exporters.

Checks cover omitted Navigator preserving the active program, explicit Navigator selecting only the requested program, the hole-only capture route, separate feature boundaries, measurement source indices, replay flags, downloadable artifact registration and monitor publication. A nonzero SDK origin followed by a 90-degree camera rotation verifies the order of coordinate transforms. A scanner-frame ROI selects one of the two holes while exported source indices still refer to the complete original PLY. Existing SDK-origin data must not receive the origin twice.

Hardware and reference detector parity are tested separately; these tests cannot certify physical coordinate calibration or drill geometry.

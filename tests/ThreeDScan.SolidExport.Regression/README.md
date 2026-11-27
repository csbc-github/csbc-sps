# Solid export regression

This .NET 10 console source-links the real solid builder, geometry fitting, topology validator, and STEP/glTF/GLB writers. It requires no scanner SDK, hardware, WPF, API host, database, or external test packages.

```powershell
dotnet run --project tests/ThreeDScan.SolidExport.Regression -- --output LocalDevelop/solid-regression-new
dotnet run --project tests/ThreeDScan.SolidExport.Regression -- --replay-scanner 'C:\path\to\selected_scanner.ply' --thickness 6 --output LocalDevelop/solid-replay-new
dotnet run --project tests/ThreeDScan.SolidExport.Regression -- --replay-scanner 'C:\path\to\joined.ply' --thickness 6 --expected-solids 1 --output LocalDevelop/joined-solid-replay-new
```

Use an already extracted scanner PLY for replay; the runner does not reacquire or repeat raw extraction. The output directory must be new. The default thickness is 6 mm, and the default output is a new GUID directory under the operating-system temporary folder. Reports and generated models are retained for review.

The 17 synthetic checks cover:

- Rigid yaw 0°, 35°, 79.6°, 90°, and 125°.
- Three full XYZ rotations, including pitch/roll and an inverted 137° roll, using independent rotation matrices rather than a projected A/C pose.
- A legal unequal L with a horizontal and upright wing; its independently known outward normals also verify extrusion direction.
- A large resolved hole, which must remain open in the footprint and become a through-hole in the closed solid.
- A small off-wing measured patch beyond the real tail, which must not become a second CAD body when projected onto the footprint. The actual measured PLY/array remains unchanged and its resolved hole remains open.
- A measured circular ridge of radius 6 mm tangent to the analytic wings, which must keep both wings connected even though the curved samples lie off their flat extensions. This verifies footprint connectivity; exported sharp-bend geometry remains an explicit fitted reconstruction.
- Rejection of a single plane, one wing, excessive requested thickness, nonfinite input, and a physically twisted nonplanar angle.

Positive cases require actual top vertices to lie on the independent analytic outer faces and within their measured footprint. Actual triangle normals and paired inner-surface vertices must give a signed face-normal offset of −6 mm; the test does not trust the thickness metadata alone. Every mesh edge must have exactly two opposite face uses, independently computed signed volume must be positive, and the full topology validator must accept the closed body.

Eleven additional outline-repair checks use independently constructed folded raster solids with known cells. The default 10 mm threshold must preserve 9 mm and exactly 10 mm notches while repairing an 11 mm notch; a 1.25 mm grid repeats this boundary check in physical millimetres. Explicit threshold zero exercises complete outer-envelope infill. Exact enclosed-hole cells, source mesh/frame data, oriented closure and actual face-normal thickness are checked, including a fully rotated 4.5 mm thick model. A straight envelope adds no material. Only an eligible tiny edge fragment may be excluded; proximity uses the nearest fragment endpoint to either actual outer edge, including fragments outside the main footprint. Tests preserve fragments farther than 5 mm from the outside edge and fragments within an internal void far from either outside edge. A substantial secondary body, an empty ridge corridor and a declared 3 mm longitudinal material gap remain separate. Cancellation must leave the source unchanged. Added outer material is inferred model geometry, never new measured PLY evidence.

Actual writer checks require STEP closed-shell B-rep entities and millimetre units, inner hole bounds when applicable, glTF vertex/index counts and the XYZ-mm to Y-up-metres conversion `(X,Z,−Y)*0.001`, plus GLB magic, version, and byte length. These are structural and geometric checks of the repository's exporters, rather than an external CAD import certification.

The latest saved `SURFACE-8ee019051d184c7bb3c0ee03125b4fe3` scanner replay passed with 1,083,686 measured input points. Its reconstructed mesh has 89,560 vertices, 179,120 triangles, one closed oriented solid, positive volume, and one preserved resolved void. The actual mesh face-normal thickness error is below 10⁻⁹ mm numerically. This is a reconstruction consistency result, not a physical thickness measurement or verification that the void is a real drilled hole.

The input arrays and original PLY SHA are checked unchanged. The separate reconstructed solid contains fitted outer faces and inferred hidden inner faces, side walls, and sharp bend geometry. Its 6 mm thickness comes from the explicit caller dimension; a single measured surface cannot determine unseen material thickness. Small enclosed sampling gaps of up to four cells follow the inherited builder's filling rule, while larger voids remain open.

The joined `SURFACE-eeea2318bc37463c986d9a603ce3b103` replay contains 2,109,588 original measurements. With `--expected-solids 1`, the final reconstruction passed as one closed oriented solid (170,226 vertices, 340,448 triangles), with actual face-normal thickness error below 10⁻¹² mm and unchanged input PLY SHA-256 `8037ff933b410103d5554261f8a86baae5571eeb8d15fd4a1599d0a77a20dd4e`. The original selected joined PLY was retained; only the separate model footprint excludes off-wing noise while preserving measured rounded-ridge neighbours. Report: `LocalDevelop/diagnostics/solid-tail-island-20261126/actual-joined-01/summary.json`.

Replay models remain in the scanner frame. The application stitching pipeline separately applies configured rigid placement. Uncalibrated scanner-to-machine transforms, physical lengths, bend radii, and hidden surfaces are not established by these tests; full measured XYZ axes are used rather than assuming an exact A/C projection.

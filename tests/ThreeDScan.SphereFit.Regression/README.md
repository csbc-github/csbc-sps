# Sphere fitting regression

The .NET 10 console project source-links the production fitter and height-frame models. It needs no scanner, SDK, API host, WPF, database, or external test packages. It does not acquire data or write shared measurement logs.

## Historical free-diameter replay

```powershell
dotnet run --project tests/ThreeDScan.SphereFit.Regression
dotnet run --project tests/ThreeDScan.SphereFit.Regression -- --only-analytical
dotnet run --project tests/ThreeDScan.SphereFit.Regression -- --nominal 24.9 --recent --only-fixtures --report LocalDevelop/free-official-replay.json
```

The default strictly checks the original 12 analytic cases, fixture hashes, provenance, raw-buffer immutability, and controlled height translations. Real free-diameter results are diagnostic reports: `ACCEPT`, `DIAGNOSTIC-REJECT`, measured diameter, quality, and reason. A zero exit code does not mean every real true sphere passed the diameter gate. `--strict-real` also fails when a real free-fit expectation does not match its positive/negative annotation.

The user corrected the official real-sphere diameter from 24.5 to **24.9 mm**. Earlier fixtures retain `expectedDiameterMm=24.5` as the historical incorrect setting; the three 11:26–11:28 captures already used 24.9 mm and record that setting. The separate `officialStandardDiameterMm=24.9` and correction source preserve this distinction without changing pixels or labels. Free replay uses each historical setting unless `--nominal` overrides it. Reports list `historicalNominalMm`, `effectiveNominalMm`, and `officialNominalMm`.

`--nominal 24.9` actually recomputes free fitting with core radius 7.47 mm and outer radius 9.462 mm. Across the four 10:19–10:22 captures and 10:52/10:55 pair, it accepts 13/18 true candidates and rejects 6/6 supports. Complete accepts all three true spheres; start and the four earlier captures reject the right sphere below the 24.4 mm lower bound. These are fresh fits, not old diameters moved into a new gate.

## Known-radius calibration center

```powershell
dotnet run --project tests/ThreeDScan.SphereFit.Regression -- --calibration-mode --only-analytical
dotnet run --project tests/ThreeDScan.SphereFit.Regression -- --calibration-mode --nominal 24.9 --height-pair --only-fixtures --report LocalDevelop/calibration-official-pair.json
dotnet run --project tests/ThreeDScan.SphereFit.Regression -- --calibration-mode --new-failures --only-fixtures --report LocalDevelop/calibration-new-captures.json
```

Calibration mode uses the recorded official 24.9 mm diameter unless explicitly overridden. Its center is a known-radius fit for an externally confirmed standard sphere; the original full-point free diameter and its tolerance flag remain separate diagnostics. This is not a general independent diameter inspection mode. Similar wrong-size caps at sufficiently high noise can become geometrically indistinguishable under the compatibility budget.

The calibration regression retains the 12 original free-fit analytic cases and adds known-center checks for noisy standards, unchanged free-diameter diagnostics, true 27 mm spheres at three noise levels, planes, cylinders, incompatible holdouts, seed stability, raw height translation, crop/sample-partition consistency, cancellation, and unchanged pixels. Independently generated 24.5 mm analytic spheres remain true 24.5 mm cases.

It also checks official-nominal 24.9 mm true standards and 27 mm negatives at all three noise levels. A separate size/noise audit found that true 25.5 and 26 mm caps at Z-noise sigma 0.15/0.25 mm can pass the known-center shape checks; these limitations are not converted into positive sphere annotations or hidden by changing gates.

The 10:52 start and 10:55 complete pair strictly require six usable true centers and two rejected supports. Start's right sphere must keep its full-point free diameter 24.317635 mm and `IsDiameterWithinTolerance=false`. Complete's bottom sphere must keep 25.259185 mm and `IsDiameterWithinTolerance=true`. The new 11:26–11:28 captures strictly require all nine true centers to be usable and all three false supports to fail. All nine annotated supports are required to fail calibration, including earlier captures. Other historical true-center outcomes remain diagnostic reports.

The final 35 analytic checks pass: the original 12 free-fit cases, 19 calibration cases, and four localized-contamination cases. The latter require accurate recovery from a small measured 1.2 mm drop, rejection of broad core corruption and correlated half-cap deformation, and unchanged invalid pixels in a small measured hole.

The final official-nominal replay of all 52 patches gives **43/43 usable real sphere centers and 9/9 rejected supports**. This includes all seven earlier single-sphere frames and all nine candidates from the 10:16–10:17 three-sphere frames. Free fitting passes 36/43 true candidates: five fail its diameter gate and two fail its original raw-surface quality checks. Those two quality failures are retained, not relabeled as clean measurements. Single-sphere frames are counted as one each, not three.

For the two new raw-quality failures, successful center estimation requires `UsedLocalizedOutlierValidation=true` after unchanged checkerboard A training and B validation. The original free diameter, raw RMS/P95, and raw geometry status remain separate diagnostics. The 11:26 bottom sphere retains full-free ring RMS 0.52042 mm; the 11:28 left sphere retains full-free core P95 0.82050 mm. Their sector-deletion center sensitivities are approximately 0.01712 and 0.04336 mm. A usable calibration center does not mean these raw-surface diagnostics passed.

The ring is a pure holdout: no ring point enters center fitting. Its fraction of residuals above 2 mm is reported without a separate 2% veto; ring inlier support, eight-direction coverage, bounded loss, model compatibility, and rescue-only center-sensitivity checks still apply. Core gross contamination retains its training-quality guard. This distinction permits independently stable centers with localized validation tails without treating all contaminated samples as clean.

For the new pair, offset candidate seeds must recover stable measured centers. Adding exactly 1,000 raw units (+10.2 mm) must preserve free diameter, XY, tolerance flag, and acceptance/rejection; every usable calibration center Z must increase exactly 10.2 mm. These controls do not assume spatial labels identify the same physical sphere across moved setups.

## Fixtures and options

There are 52 patches from 19 snapshots: seven single-sphere frames and twelve three-sphere frames, with nine known false support candidates. The small gzip files retain unmodified unsigned 16-bit height pixels, including zero invalid samples, extracted with official `LHeightImage.Read` and `HeightImageFrameAdapter`. `manifest.json` records original image hashes, source pixel offsets, world origin, pitch, candidate provenance, compressed patch hashes, and historical/official nominal values.

The reference is byte-identical to the 10:15:45 capture and is not an independent snapshot. Candidate coordinates for three-sphere frames come from recorded pattern matches. This runner tests candidate measurement and rejection; it does not test native pattern search or deduplication.

`--height-pair` selects the 10:52/10:55 pair. `--new-failures` selects the three 11:26–11:28 captures. `--recent` selects captures from 10:19 onward. `--latest` selects the newest capture. `--capture ID` selects one capture in free replay. `--only-fixtures` skips analytic cases. `--report PATH` writes a JSON report; otherwise a new operating-system temporary directory is used. Original images and fixtures are never edited.

An earlier independent four-ROI study remains under `LocalDevelop/diagnostics/instrument-heightpair-20261126`. It used every predeclared radius 6/7.35/8/9.31 mm, without selecting by diameter. Its 24.5 mm known-radius model is historical after the official diameter correction; it does not validate the 24.9 mm calibration model.

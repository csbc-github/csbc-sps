# PLY IO equivalence and allocation regression

```powershell
dotnet run --project tests/ThreeDScan.PlyIO.Regression -c Release -- --output LocalDevelop/ply-io-new
```

This package-free .NET 10 console source-links `ScanPly` and retains the pre-optimization implementation as `BaselineScanPly`. It never loads a scanner, API host or database. Output directories must be new.

The 12 checks compare parsed coordinate **bits**, canonical extra-attribute text, source indices, original file SHA-256 and complete writer byte hashes. Fixtures cover signed zero, IEEE extremes, random finite values and rounding boundaries, both coordinate precision modes, scientific notation, thousands separators, invariant process culture, Unicode and repeated whitespace, leading-zero attributes, undeclared extra tokens, BOM/UTF16/mixed line endings, zero vertices, and ignored trailing data larger than the reader buffer. Malformed headers/records, short files, invalid and nonfinite numbers must keep the baseline exception type/message. Failed writes and destination collisions must leave no temporary/published partial file and preserve an existing destination.

The intensity string cache shares only exact canonical spellings `0` through `255`. It does not change `001`, `+1`, `1.0`, Unicode tokens or arbitrary extra fields. The streaming checksum hashes original encoded bytes, including BOM, original line endings and ignored trailing elements. It drains unread bytes after the declared vertex count; decoded/normalized text is never used as the checksum input.

`Quantize` continues to format `F6` and parse with the invariant provider. It does not substitute binary arithmetic rounding. The writer reuses its first `F6` format to select the same `F6`/round-trip `R` spelling as the baseline. Its exact-format fixture includes negative-zero and nonfinite writer behavior.

An optional read-only complete-file comparison runs one baseline/candidate pair and checks **every** point:

```powershell
dotnet run --project tests/ThreeDScan.PlyIO.Regression -c Release -- --benchmark-read 'C:\path\raw.ply' --output LocalDevelop/ply-io-read-new
```

The saved 9,881,837-point, 355 MB eeea recording measured 11.201 s / 4.067 GB allocated for the frozen reader and 4.563 s / 1.347 GB for the candidate, with identical coordinate bits, attributes, indices and full-file SHA. This is one sequential pair, with cached local storage, rather than a whole-application latency claim. The small `Quantize` check verifies allocation removal; its isolated execution can be slower despite allocating zero strings. Use the separate full-pipeline benchmark to judge the complete speed change under the real memory load.

Reports are retained under `LocalDevelop/diagnostics/ply-io-perf-20261126/` for the original implementation pass.

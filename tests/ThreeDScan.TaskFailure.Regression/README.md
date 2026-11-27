# Task failure diagnostics regression

```powershell
dotnet run --project tests/ThreeDScan.TaskFailure.Regression -c Release
```

This standalone harness references the real Application and Domain projects without a database, scanner SDK or API host. Thirteen checks cover structured business-failure diagnostics across a serialized repository round trip, the actual public `ScanAppService.GetTaskResultAsync` polling path and the callback mapper, legacy error JSON when details are absent, internal errors, terminal tasks, an abort racing with failure, missing tasks and stale error suppression for nonfailed results. The public polling check specifically catches a separate mapping that used to drop diagnostics despite the callback mapper preserving them. It writes no scan files and makes no API or callback requests.

Completion-policy checks exercise both strict and warning-only point-cloud delivery using the recorded 2.063516/1.5 mm boundary failure, all nine permitted boundary metrics and explicit nullable alignment/boundary flags. RMS, P95, wing-support, mixed or unknown failures remain blocked; missing/malformed failure evidence and legacy reports without the new flags fail closed. The policy cannot rewrite the strict validation result or its original measured values and thresholds.

`ScanOrchestrator.MarkFailedAsync` uses the same detail-preservation assignment for synchronous capture failures; this harness directly exercises the background executor used by surface completion.

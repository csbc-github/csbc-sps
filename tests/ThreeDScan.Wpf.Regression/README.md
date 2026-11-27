# WPF profile-selection regression

```powershell
dotnet run --project tests/ThreeDScan.Wpf.Regression -c Release -p:Platform=x64
```

Runs the real `FeedingViewModel` and `LidarScanView` on a Windows STA dispatcher with asynchronous loopback HTTP fixtures. Verifies profile freezing, ROI selection/save/clear, two-point calibration/save/clear and preview start/stop without background-thread WPF or collection access.

Also verifies that asynchronous result polling displays a failed task and its measurement error, including the feature location, on the UI dispatcher.

The merged point-cloud fixture invokes the real surface-result pointer consumer without constructing its hardware-polling view model. A completed PLY must select point-cloud mode, clear an older GLB, load both measured longitudinal sections and saved task metadata, and preserve exact bounds. Existing GLB selection, unsupported-extension rejection and stale asynchronous point-cloud results are also checked.

The preview lifecycle fixture simulates recording, an empty recording-to-idle transition, and a restored setup connection. It verifies automatic recovery, a temporarily busy scanner followed by a successful retry, throttled requests, one setup start under concurrent live polls, and protection for frozen profiles, ROI/two-point selection and a manual stop. All live/setup requests stay on the test loopback server; retry deadlines are advanced through the view model's private test boundary instead of waiting for wall-clock delays.

Two deterministic start/stop races hold a setup/start response until either a manual stop or window cleanup has finished. A late successful start must trigger another stop request, leave the fixture connection idle and keep the user's stopped intent and UI state.

The constructor's production polling and hardware probes are disabled using WPF design-mode metadata. Only the test executable's output configuration is temporarily replaced and then restored. No production API, scanner, database, authentication flow or callback is started.

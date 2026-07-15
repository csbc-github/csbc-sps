# ThreeDScan 架構摘要（SD V1.2）

## API

| 方法 | 路徑 | 對應 |
|------|------|------|
| POST | `/api/calibration/instrument` | 儀器校正 UC-01：台船傳 `cameraExpectedPosition` + `standardBalls[]` → 補償 offset → `CompensationHistory` + SignalR |
| POST | `/api/calibration/reference-setup` | Ref 建立（LJScanWinform **校正**，文件外）→ `ScannerConfig` + ref 圖檔 |

## 資料庫

連線：`appsettings.json` → `DefaultConnection` / `ThreeDScan`（本機 `127.0.0.1`，資料庫 `ThreeDScan`）。

建議依序執行 `database/` 腳本：

| 腳本 | 用途 |
|------|------|
| `schema-v1.2.sql` | `CompensationHistory`、`OperationLog`、`ScannerConfig` 等 |
| `schema-v1.3-scannerconfig-calibration.sql` | `ScannerConfig` 球徑、基準面 ROI 欄位 |
| `schema-v1.4-scanner-program-settings.sql` | 拍攝群組參數備份 blob |
| `schema-v1.5-scanner-program-group-names.sql` | 群組顯示名稱 |
| `schema-v1.6-scannerconfig-per-scanner.sql` | `ScannerConfig` 改為每台掃描器一筆 |
| `seed-scannerconfig.sql` | Ref 種子（每台掃描器一筆） |

### 資料模型（Ref vs 拍攝群組）

| 資料 | 主鍵 | 說明 |
|------|------|------|
| `ScannerConfig` | `ScannerId` | 每台攝影機 **一筆** Ref 座標、量測參數、`ref_*.png` 目錄 |
| `ScannerProgramSettings` | `(ScannerId, ProgramNo)` | WPF Save 的 **拍攝參數** blob（16 群組） |
| `ScannerProgramGroupName` | `(ScannerId, ProgramNo)` | 群組顯示名稱 |

- API `navigatorScanNo`（1～16）= 拍攝群組 Scan 編號 → 內部 `programNo = navigatorScanNo - 1`
- Ref **與群組無關**；`instrument` / `reference-setup` 的 `navigatorScanNo` 只決定**用哪個群組的拍攝參數掃描**

## 設定

- 靜態：`appsettings.json` → `Scanners`、`Storage`、`Calibration`
- 掃描器校正參數（球徑、基準面 ROI）：`ScannerConfig` 表
- 執行時 Ref：`ScannerConfig` 表 + `{ContentsRoot}/{ScannerId}/ref_*.png`

## 專案分層

- `ThreeDScan.Domain` — 模型與介面
- `ThreeDScan.Application` — `CalibrationAppService`
- `ThreeDScan.Infrastructure` — EF、DB、Scanner registry、LJ SDK
- `ThreeDScan.Api` — HTTP + SignalR `/hubs/calibration-preview`

## 已接入（LJScanWinform）

- `Ljs8SurfaceScannerCapture` — `CaptureFromScanner`
- `LjDeveloperMeasureCore` + `BallSettingsBridge` — `ljd_measure`
- `LjDeveloperMeasureEngine` — 量測 / Ref 建立流程
- WPF **掃描器拍攝參數** — 16 群組 Save / 讀取 / DB 備份
- WPF **校正監看** Tab — SignalR `http://localhost:5295/hubs/calibration-preview`

## 文件

- [`docs/api-calibration.md`](docs/api-calibration.md) — 校正 API 呼叫說明（Postman / curl）

## 執行前確認

1. `LJScanTest\dll\` 內有 `LjDevCommon.dll`、`LjDevCommunication.dll`、`LjDevMeasure.dll`（建置會複製到輸出目錄）
2. Api 已啟動：`dotnet run --project ThreeDScan.Api`
3. 每台 Scanner：`seed-scannerconfig.sql` → `POST /api/calibration/reference-setup`（Ref 與群組無關，每台一次即可）
4. 儀器校正：`POST /api/calibration/instrument`（可帶 `navigatorScanNo` 指定拍攝群組）

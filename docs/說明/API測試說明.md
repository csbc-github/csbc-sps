# ThreeDScan API 測試說明

規格 REST 的連線設定與各支 API。尚未實作或尚未整理測試步驟的標 **待補充**。  
JSON 為 camelCase；`Content-Type: application/json`。改設定後須重啟 Api。Development 會用 `appsettings.Development.json` 覆寫同名鍵。

---

## 1. 連線／參數說明

### 1.1 網址

以 Visual Studio 啟動後主控台的 `Now listening on:` 為準。

| launch profile | 網址 |
|---|---|
| `http` | `http://localhost:5295` |
| `https` | `https://localhost:7211` |

### 1.2 參數

| 參數 | 位置 | 說明 |
|---|---|---|
| `Calibration:TestMode` | `appsettings.json` | `true`：不連掃描器、不寫補償／task，回固定範例（仍檢查必填與 `final`／`taskId`）。測完改回 `false`。 |
| `Calibration:ResponseMode` | 正式檔預設 `Production`；Development 覆寫為 `Debug` | `Production`：instrument 只回規格欄位。`Debug`：多回 balls、matchedBalls 等。 |
| `Authentication:Enabled` | 正式檔 `true`；Development 覆寫為 `false` | `false`：可不帶 Token。`true`：須 JWT Bearer（`eyJ` 開頭、中間有 `.`）。 |

`TestMode` 下 `scannerId` 不必存在於 `Scanners`；正式模式須為 `SURF-CAM-01` 或 `SURF-CAM-02`。

### 1.3 共通回應

| HTTP | 情況 |
|---|---|
| 200 | `{ success, timestamp, data, errorCode, errorMessage }`，`success: true` |
| 400 | 同結構，`success: false`（業務錯誤或缺必填） |
| 401 | 已開驗證且 Token 無效；`WWW-Authenticate: Bearer` |
| 501 | 尚未實作，`errorCode` 為 `SYS_NYI` |

---

## 2. API 說明

| 方法 | 路徑 | 狀態 |
|---|---|---|
| POST | `/api/calibration/instrument` | 可測（TestMode） |
| POST | `/api/calibration/roller` | 待補充 |
| POST | `/api/scan/line` | 待補充 |
| POST | `/api/scan/surface` | 可測（連掃描器） |
| GET | `/api/scan/surface/{taskId}/attachments` | 可測 |
| POST | `/api/scan/hole` | 待補充 |

### 2.1 POST `/api/calibration/instrument`

儀器校正，兩次拍攝。JSON 欄位名是 **`final`**（不是 `isFinal`）。

| 次數 | `final` | `taskId` |
|---|---|---|
| 第一次 | `false` | 不要帶（空或省略） |
| 第二次 | `true` | 第一次回傳的 `data.taskId` |

配錯 → 400，`CAL_001`（TestMode 也檢查）。

必填：`scannerId`、`cameraExpectedPosition`、`standardBalls`（1～10 顆）。每顆 `ballId` 僅 `A-Z`／`0-9`／`-`；座標 ±10000。  
選填：`navigatorScanNo`、`operator`。

TestMode 回固定範例，與你傳的 `knownX/Y/Z` 無關；`taskId`、`traceId` 每次新產生。`final=true` 時仍必填 `taskId`（不查 DB）。

#### 第一次 request

```json
{
  "scannerId": "SURF-CAM-01",
  "final": false,
  "cameraExpectedPosition": { "x": 90, "y": 95, "z": 1100 },
  "standardBalls": [
    { "ballId": "BALL-01", "knownX": 481.48, "knownY": 694.51, "knownZ": 321.25 },
    { "ballId": "BALL-02", "knownX": 744.96, "knownY": 576.74, "knownZ": 321.25 },
    { "ballId": "BALL-03", "knownX": 676.83, "knownY": 776.70, "knownZ": 321.25 }
  ]
}
```

#### 第一次 response（TestMode + Debug）

```json
{
  "success": true,
  "timestamp": "2026-08-21T14:00:00+08:00",
  "data": {
    "scannerId": "SURF-CAM-01",
    "traceId": "（新 GUID）",
    "compensation": { "offsetX": 120.423, "offsetY": 313.99, "offsetZ": -262.907 },
    "actualScannerPosition": { "x": 210.423, "y": 408.99, "z": 837.093 },
    "rmseXY": 0.08,
    "scannerToFloorZ": 837.093,
    "taskId": "（新 GUID，第二次要帶這個）",
    "seq": 1,
    "status": "AwaitingNextShot",
    "balls": [
      { "index": 0, "x": 270.69, "y": 286.39, "z": 54.16, "diameter": 25.286, "ok": true },
      { "index": 1, "x": 466.11, "y": 368.39, "z": 61.15, "diameter": 24.553, "ok": true },
      { "index": 2, "x": 533.91, "y": 168.18, "z": 59.73, "diameter": 24.34, "ok": true }
    ],
    "matchedBalls": [
      { "ballId": "BALL-01", "measuredX": 270.69, "measuredY": 286.39, "measuredZ": 54.16, "knownX": 481.48, "knownY": 694.51, "knownZ": 321.25, "residualMm": 0.08 },
      { "ballId": "BALL-02", "measuredX": 533.91, "measuredY": 168.18, "measuredZ": 59.73, "knownX": 744.96, "knownY": 576.74, "knownZ": 321.25, "residualMm": 0.08 },
      { "ballId": "BALL-03", "measuredX": 466.11, "measuredY": 368.39, "measuredZ": 61.15, "knownX": 676.83, "knownY": 776.7, "knownZ": 321.25, "residualMm": 0.08 }
    ]
  },
  "errorCode": null,
  "errorMessage": null
}
```

Production 第一次 `data` 只有：`taskId`、`seq`、`status`、`traceId`。

#### 第二次 request

```json
{
  "scannerId": "SURF-CAM-01",
  "final": true,
  "taskId": "（第一次回傳的 taskId）",
  "cameraExpectedPosition": { "x": 90, "y": 95, "z": 1100 },
  "standardBalls": [
    { "ballId": "BALL-01", "knownX": 481.48, "knownY": 694.51, "knownZ": 375.47 },
    { "ballId": "BALL-02", "knownX": 744.96, "knownY": 576.74, "knownZ": 375.47 },
    { "ballId": "BALL-03", "knownX": 676.83, "knownY": 776.70, "knownZ": 375.47 }
  ]
}
```

#### 第二次 response（TestMode + Debug）

```json
{
  "success": true,
  "data": {
    "scannerId": "SURF-CAM-01",
    "seq": 2,
    "taskId": "（你傳入的 taskId）",
    "rmseXY": 0.08,
    "zDeviation": 0.277,
    "scannerToFloorZ": 836.955,
    "actualScannerPosition": { "x": 207.279, "y": 410.698, "z": 836.955 },
    "compensation": { "offsetX": 117.279, "offsetY": 315.698, "offsetZ": -263.045 },
    "status": null,
    "balls": [
      { "index": 0, "x": 273.72, "y": 290.28, "z": 108.2, "diameter": 25.051, "ok": true },
      { "index": 1, "x": 535.48, "y": 168.78, "z": 114.11, "diameter": 24.864, "ok": true },
      { "index": 2, "x": 470.13, "y": 369.73, "z": 114.55, "diameter": 24.265, "ok": true }
    ],
    "matchedBalls": [
      { "ballId": "BALL-01", "measuredX": 273.72, "measuredY": 290.28, "measuredZ": 108.2, "knownX": 481.48, "knownY": 694.51, "knownZ": 375.47, "residualMm": 0.08 },
      { "ballId": "BALL-02", "measuredX": 535.48, "measuredY": 168.78, "measuredZ": 114.11, "knownX": 744.96, "knownY": 576.74, "knownZ": 375.47, "residualMm": 0.08 },
      { "ballId": "BALL-03", "measuredX": 470.13, "measuredY": 369.73, "measuredZ": 114.55, "knownX": 676.83, "knownY": 776.7, "knownZ": 375.47, "residualMm": 0.08 }
    ]
  }
}
```

Production 第二次 `data` 只有：`scannerId`、`compensation`、`rmseXY`、`zDeviation`、`calibratedAt`、`traceId`。

### 2.2 POST `/api/calibration/roller`

待補充。路由已掛，回 **501** `SYS_NYI`。

### 2.3 POST `/api/scan/line`

待補充。路由已掛，回 **501** `SYS_NYI`。

### 2.4 POST `/api/scan/surface`

公開點雲／GLB／ZIP／`headFeatures` = **相對座標 + `cameraPosition` + UC-01 補償 offset**（三軸都加）。`innerLeft`／`innerRight` 與公開點雲同一絕對框（測距儀機台座標）。沒有補償歷程時 offset 當 0，公開檔仍加 `cameraPosition`，回應 `compensationApplied: false`。

`scannerId` 須為 `SURF-CAM-01` 或 `SURF-CAM-02`。省略 `navigatorScanNo` 時用該掃描器 appsettings 預設。  
同一 `taskId` 多次呼叫：每次先獨立掃描並存該段點雲；`final=true` 且已有兩段以上時，依 Hank 方式拼接（各段 PCA 最長軸對齊 X、兩端裁 3%、Y／Z 中位對齊、Z 歸零、沿 X 首尾相接）後回傳合併點雲／ZIP。單次 `final=true`（只有一段）不拼接。`segmentZDelta` 為本段沿長度包角線最大 Z 差（相對座標，不加 camera）。

拼接後座標不再維持機台絕對框（現有拼接會沿長度重排）。`PlyProcessor` 可調 `StitchGap`、`StitchTrimPercent`、`StitchAlignYZ`、`StitchZFloor`（預設對齊 Hank 網頁）。

`jobId` 必填（台船工單號）；同一根角鋼噴印／割切／研磨帶同一值。

- 有帶 `innerLeft`／`innerRight`：在公開絕對點雲上量 `thicknessLeft`／`thicknessRight`，寫入 `JobThickness`。
- 沒帶：依 `jobId` 讀噴印厚度組模型。找不到該工單厚度則 400 `SCAN_THK_002`。

WPF 噴印「開始掃描」不經此 API，模型仍用假定 `MeshThickness`（預設 6 mm）。

| 次數 | `final` | `taskId` |
|---|---|---|
| 單次完成 | `true` | 省略 |
| 第一次（還要再掃） | `false` | 省略 |
| 後續 | `false` 或最後一次 `true` | 第一次回傳的 `data.taskId` |

必填：`scannerId`、`jobId`、`cameraPosition`。JSON 欄位名是 **`final`**。

#### 單次 request

```json
{
  "scannerId": "SURF-CAM-01",
  "jobId": "JOB-001",
  "final": true,
  "cameraPosition": { "x": 90, "y": 95, "z": 1100 }
}
```

#### 單次 request（含厚度）

```json
{
  "scannerId": "SURF-CAM-01",
  "jobId": "JOB-001",
  "final": true,
  "cameraPosition": { "x": 90, "y": 95, "z": 1100 },
  "innerLeft": { "x": 617.04, "y": 791.67, "z": 1256.26 },
  "innerRight": { "x": 653.27, "y": 776.46, "z": 1258.75 }
}
```

`innerLeft`／`innerRight` 與公開點雲同一絕對座標：舊相對測試點 + `cameraPosition` + 補償 offset。上例是相對 (527.04, 696.67, 156.26)／(563.27, 681.46, 158.75) 加上 `cameraPosition` (90, 95, 1100)、補償為 0。若 `compensationApplied` 為 true，三軸再加 offset。

#### 成功 `data`

| 欄位 | 說明 |
|---|---|
| `taskId` | 任務識別碼 |
| `seq` | 本任務第幾次掃描 |
| `status` | `Continue` 或 `Final` |
| `pointCloudUrl` | 本段公開點雲（相對 + cameraPosition + 補償）；`final=true` 且多段時為拼接後的 `angle_steel.ply` |
| `compensationApplied` | 是否套用 UC-01 補償。`false`＝沒有補償歷程，公開檔只加了 `cameraPosition` |
| `thicknessLeft` | 左側厚度 mm；未帶 innerLeft 時省略 |
| `thicknessRight` | 右側厚度 mm；未帶 innerRight 時省略 |
| `segmentZDelta` | 本段包角線最大 Z 差（mm，相對座標） |
| `headFeatures` | 前緣左下／頂點／右下（公開絕對座標） |
| `traceId` | 本次追溯 ID |

點雲靜態檔路徑前綴：`/scan-files/`（對應 `Storage:ExportRoot`）。`POST /api/scan/surface-from-file` 同樣必填 `cameraPosition`。

部署須執行：

- `database/schema-v1.8-job-thickness.sql`（工單最新厚度 `JobThickness`，割切組模型用）
- `database/schema-v1.9-scan-call-log.sql`（掃描 API 呼叫紀錄 `ScanCallLog`，每次呼叫一列，含 request／response 與 PLY／ZIP SHA256）

| `errorCode` | 情況 |
|---|---|
| `SCAN_CFG_001` | 未知 `scannerId` |
| `SCAN_001` | `taskId` 不存在、掃描器不符、或任務已 `Final` |
| `SCAN_THK_001` | 厚度量測失敗（缺 A/B、或點雲沒有可用點） |
| `SCAN_THK_002` | 未帶 innerLeft／innerRight，且資料庫沒有該 `jobId` 的噴印厚度 |
| `SCAN_SDK_001` | 擷取或 Pipeline 失敗 |

### 2.5 GET `/api/scan/surface/{taskId}/attachments`

成功時 **直接下載 ZIP**（`Content-Type: application/zip`），內含 STEP／glTF／PNG／PLY／JSON；ZIP 內座標為公開絕對座標（相對 + cameraPosition + 補償）。**不是** `{ success, data }` 包裝。  
失敗時仍為共通 JSON（400，`SCAN_001`）。

`taskId` 用 2.4 回傳的值。單段為該次 ZIP；`final=true` 且多段時為拼接後的 ZIP。

### 2.6 POST `/api/scan/hole`

待補充。路由已掛，回 **501** `SYS_NYI`。

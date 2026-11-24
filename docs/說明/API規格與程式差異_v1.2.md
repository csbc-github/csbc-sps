# 3D 掃描應用服務系統 API 規格與程式差異（v1.2）

對照「3D 掃描應用服務系統 API 串接規格文件 v1.2」與實際程式（controller、request／response model、validator、錯誤處理）。依據為本次閱讀的程式，不是測試用 fixture。

每題格式：結論、程式碼依據、建議文件寫法。

---

## Q1. `POST /api/scan/hole` Request

**結論：文件要改。** 程式接受的欄位比文件多。`selectedFeatureId`、`boundarySelection` 是結果欄位，不是 Request。

**程式碼依據：** `HoleScanRequest` 定義在 `ThreeDScan.Application/Scan/Models/HoleScanRequest.cs` 第 7–36 行。控制器 `ScanController.ScanHole`（`ThreeDScan.Api/Controllers/ScanController.cs` 第 176–182 行）把這個型別交給 `StartHoleScanAsync`（`ScanAppService.cs` 第 948–966 行），再進 `ScanOrchestrator.StartHoleAsync`（第 417–447 行）。JSON 為 camelCase（`DasApiJson.cs` 第 16–18 行）。`[ApiController]` 會先做 DataAnnotations，不過才進業務檢查。

| JSON 欄位 | 型別 | 必填 | 預設 | 驗證 |
| --- | --- | --- | --- | --- |
| `scannerId` | string | 是 | `""` | 長度 1–50；業務再限制不可空白且 ≤ 250 |
| `jobId` | string | 是 | `""` | 長度 1–64；業務再限制不可空白且 ≤ 250 |
| `holeType` | string? | 否 | `null` | 長度 ≤ 32。`null` 表示通用處理。有值時只能是 `Circle`、`Slot`、`EdgeNotch`（大小寫須完全一致），否則 `INVALID_HOLE_TYPE` |
| `callbackUrl` | string | 是 | `""` | 長度 1–2048；須為無帳密的 http(s) URL，且主機在允許清單 |
| `cameraPosition` | object? | 否 | `null` | 見下表。省略則結果用掃描器座標 |
| `roi` | object? | 否 | `null` | 見下表。省略時改用該掃描器設定的 `HoleRoi`（有設定才用） |
| `featureId` | string? | 否 | `null` | 模型上沒有額外驗證。有值但分析結果沒有此 id 時回 `FEATURE_NOT_FOUND` |
| `navigatorScanNo` | int? | 否 | `null` | 1–16。省略則用掃描器註冊的預設拍攝群組 |

`cameraPosition`（`CameraPoseDto.cs` 第 6–24 行）：

| 欄位 | 型別 | 預設 | 驗證 |
| --- | --- | --- | --- |
| `x`、`y`、`z` | double | `0` | 模型範圍 −10000～10000；業務再要求有限值且絕對值 ≤ 10⁹ |
| `a`、`c` | double | `0` | 模型無範圍。業務要求有限值，絕對值 < 10⁶，單位是度（`Pose5.cs` 第 14–18 行；`ScanOrchestrator.cs` 第 1195–1205 行） |

`roi`（`ScannerRoi.cs` 第 4–22 行）：`minX`、`maxX`、`minY`、`maxY`、`minZ`、`maxZ`，皆為 double，省略時為 `0`。有送 `roi` 時每軸須有限且 minimum < maximum，否則 `INVALID_ROI`（`ScanOrchestrator.cs` 第 1211–1222 行）。

`selectedFeatureId`、`boundarySelection` 在 `ExecuteHoleAsync` 寫進結果（`ScanOrchestrator.cs` 第 604–615 行）。未帶 `featureId` 時 `boundarySelection` 用分析結果；有帶則固定 `"ExplicitFeatureId"`。

**建議文件寫法：** Request 列出上表八個欄位，並註明後四個可省略。另寫：`selectedFeatureId`、`boundarySelection` 只出現在 callback／`GET` 結果。`cameraPosition` 省略時座標框為 `Scanner`；有送時為 `ConfiguredAbsolute`。

---

## Q2. 線掃描高度基準（zDistance）

**結論：文件要改，程式已拿掉遺留檢查。** 高度換算不使用 `ZDistance`，寫入端點也已關閉。`line/start` 與 `line/complete` 不再讀 `RollerCalibration`，也不再回 `SCAN_LINE_003`。文件的 `CAL_CFG_001` 檢查範圍仍和程式不一致。

### (a) zDistance 從哪裡來

資料表 `RollerCalibration.ZDistance` 仍在，`RollerCalibrationRepository.GetAsync`（第 20–38 行）也能讀成 `RollerBaselineRecord.ZDistance`（`RollerBaselineRecord.cs` 第 9–10 行，單位 mm）。線掃描開始、完成與量測都不再呼叫它。

寫入路徑已關掉：

- `SaveAsync` 整段註解（同檔第 41–62 行）
- `POST /api/calibration/roller` 整段註解（`CalibrationController.cs` 第 65–73 行）
- 裝置層 `CalibrateRollerAsync` 在 `#if false` 內（`LineScanDeviceHost.cs` 第 31–95 行）

設定檔與其他現行端點都沒有寫入 `ZDistance`。`database/seed-test-machine.sql` 寫明要手動 `INSERT`。

現行量測不讀這個數字。註冊的實作是 `SickCSharpLineScanner`（`DependencyInjection.cs` 第 52–53 行），高度來自桌面兩點校正。

### (b) `SCAN_LINE_003`

正式路徑已不再觸發。`ScanAppService` 的 `line/start` 與 `line/complete` 已移除對 `RollerCalibration` 的讀取與 `SCAN_LINE_003`。`ISickLineScanner.MeasureAsync` 也不再接收滾輪基準。

資料表、`RollerCalibrationRepository` 與已註解的寫入端點仍留在程式裡，沒有呼叫端。舊的 `CSharpLineMeasureService` 仍含 `sensorY - ZDistance`，但 DI 沒有註冊它。

### (c) 線掃描的 `CAL_CFG_001` 實際查什麼

`POST /api/scan/line/start` 在進裝置前只查兩項，都是 `CAL_CFG_001`（`ScanAppService.cs` 第 133–146 行）：

- 資料庫沒有兩點桌面校正（`calibrationFirst` 與 `calibrationSecond` 四個值都要有，`LineScannerSettingsRecord.cs` 第 40–46 行）。訊息：「請先設定桌面校正的 calibrationFirst 與 calibrationSecond。」
- ROI 四個邊界沒有齊。訊息：「請先設定掃描有效範圍（ROI）。」

未知 `scannerId` 與埠不在 1–65535 是 `SCAN_LINE_002`（第 128–131、159–161 行），不是 `CAL_CFG_001`。

接著 `StartRecordingAsync` 呼叫 `ValidateForStartAsync`（`LineScanDeviceHost.cs` 第 104 行、`LineScanOptionsResolver.cs` 第 91–109 行）：

| 檢查 | 錯誤碼 |
| --- | --- |
| 未知 scannerId；IP／埠／連線逾時無效；兩點校正 `Validate()` 失敗；ROI 有值但不合法 | `CAL_CFG_001` |
| `PositionSource` 不是 `encoder`；`StreamingReconstruction.Validate` 失敗（滾輪半徑、每轉脈衝、方向、Encoder 通道、X／高度範圍等，`StreamingReconstruction.cs` 第 208–215 行） | `SCAN_LINE_CONFIG`（訊息含「重建設定無效」或「目前原生線掃描後端要求 Encoder 定位。」） |

與文件「桌面兩點校正、ROI、IP／埠／Encoder 重建參數都算 `CAL_CFG_001`」不相符。Encoder 重建參數是 `SCAN_LINE_CONFIG`。

**建議文件寫法：** 刪除 `zDistance` 與 `SCAN_LINE_003`。線掃描高度寫成「由桌面兩點校正換算，不另需滾輪高度基準」。`CAL_CFG_001` 只列兩點校正、ROI、以及裝置層的 IP／埠／逾時與校正幾何。Encoder 與重建參數改列 `SCAN_LINE_CONFIG`。

---

## Q3. 面掃描厚度

**結論：兩者都要改。** `POST /api/scan/surface/start` 走 `StartSurfaceScanAsync` → `StartSurfaceAsync`（`ScanAppService.cs` 第 730–752 行、`ScanOrchestrator.cs` 第 117–126 與 876–910 行）。舊的 `ScanSurfaceAsync` 註解寫明不再由這個路徑呼叫（`ScanAppService.cs` 第 435–436 行）。

### 未帶 inner、該 jobId 沒有厚度紀錄

`innerLeft` 與 `innerRight` 都是 null，且 `JobThickness` 沒有此工單時，還要再看兩個厚度來源。三者同時都沒有才回錯誤：請求的 `materialThicknessMm`、工單紀錄、掃描器設定 `MaterialThicknessMm`（`ScanOrchestrator.cs` 第 117–126 行）。

- 三者都沒有：HTTP 400，`SCAN_THK_002`，訊息「未提供 inner 座標，且找不到此工單的既有厚度。」
- 請求帶了 `materialThicknessMm`，或該掃描器設定有預設厚度：不報錯。start 成功時 `thicknessLeft`／`thicknessRight` 仍是 null，JSON 會省略這兩欄（`SurfaceScanStartData.cs` 第 22–28 行；`DasApiJson.cs` 第 17 行）。該數值只寫進任務，供之後拼接使用（第 134 行）。

只帶一側 inner 不會進這個錯誤。

### 未帶 inner、該 jobId 已有厚度紀錄

不報錯。start 成功時把既有紀錄抄進回應：`thicknessLeft = ThicknessA`、`thicknessRight = ThicknessB`（第 876–881、906–910 行）。單位 mm。

### 有帶 inner、該處點雲沒有資料

不報 `SCAN_THK_002`。該側厚度維持 null 並省略欄位，回應仍成功，並在 `warnings` 加上「左厚度不可算：…」或「右厚度不可算：…」（`SegmentProcessor.cs` 第 225–232 行）。

判定在 `GeometryFitting.Thickness`（`GeometryFitting.cs` 第 344–370 行）：翼板局部半徑內不足 100 點、局部平面擬合失敗、平面 RMS 超標、有向板厚不在 0.1～80 mm、或投影點距最近實測點超過 30 mm。任一種都讓該側 `ThicknessMm` 為 null。

若整段無法擬合兩翼，才是另一個錯誤：`SURF_GEOMETRY_UNAVAILABLE`，例外內寫 HTTP 422（`ScanOrchestrator.cs` 第 857–863 行），對外仍被收成 HTTP 400（見 Q5）。

**建議文件寫法：**

- 未帶兩側 inner，且沒有工單厚度、沒有 `materialThicknessMm`、掃描器也沒有預設厚度：`SCAN_THK_002`。
- 未帶 inner 但工單已有厚度：回傳既有 `thicknessLeft`／`thicknessRight`。
- 有帶 inner 但該側點雲不足以計算：該側省略（null），並在 `warnings` 說明原因；任務仍成功。
- 刪除「未帶 inner 就一律 null」這句。

---

## Q4. BUSY 錯誤碼

**結論：文件要改。** 程式同時使用 `SCAN_LINE_004`、`SCAN_BUSY`、`SCANNER_BUSY`。可以收成一個碼，但現在沒有收，而且語意不完全相同。

全部經 `DasApiActionResult` 時對外都是 HTTP 400。`DeviceCoordinator` 雖把 `SCANNER_BUSY` 標成 409（第 18 行），這個狀態碼沒有傳到 HTTP。

| 代碼 | 情境 | 訊息 |
| --- | --- | --- |
| `SCAN_LINE_004` | `POST /api/scan/line/start`，此光達已有未結束線掃描任務（`ScanAppService.cs` 第 154–156 行） | 此光達已有進行中的線掃描。 |
| `SCAN_BUSY` | 線掃描裝置已佔用該 IP:埠（`LineScanDeviceHost.cs` 第 391–398 行） | 此掃描器已有量測任務進行中。 |
| `SCAN_BUSY` | 同一線掃描任務重複開始（第 143–145 行） | 此線掃描任務已開始。 |
| `SCAN_BUSY` | 同一條連線已在做輪廓平均，例如 `POST /api/scan/profile`（`LineScanLease.cs` 第 241–243 行） | 此連線已有輪廓擷取進行中 |
| `SCANNER_BUSY` | 面掃描或孔洞 `NewTask` 佔用 `live:{ip}:{port}` 失敗（`DeviceCoordinator.cs` 第 13–18 行、`ScanOrchestrator.cs` 第 1078–1079 行） | 掃描器已有未結束任務，請先 complete 或 abort。 |
| `SCANNER_BUSY` | `POST /api/scan/line/setup/start` 時已有線掃描任務（`ScanAppService.cs` 第 1237–1239 行） | 掃描器已有進行中的線掃描任務。 |
| `SCANNER_BUSY` | `POST /api/scan/profile` 時已有線掃描任務（第 1276–1278 行） | 掃描器已有進行中的任務。 |
| `SCANNER_BUSY` | 修改線掃描兩點校正或 ROI 時仍有任務（`LineScannersController.cs` 第 53–55、78–80、102–104 行） | 此掃描器仍有任務進行中，請先完成或中止，再修改校正與框選範圍。 |

**建議文件寫法：** 三個碼分端點列表，並寫明 HTTP 都是 400。若要統一，需先選定一個碼再改程式；以現況直接寫成單一 `SCANNER_BUSY` 會和程式不符。

---

## Q5. HTTP 狀態碼

**結論：文件對「同步失敗是 400」這句，包含 `CAL_SYS_001` 與 `SYS_INTERNAL`。** 這兩個都不是 500。文件若寫「一律 400」，要排除兩個 501。

`DasApiActionResult.From`（`DasApiActionResult.cs` 第 10–25 行）：

- `SYS_NYI` 或 `EDGE_NOTCH_UNVERIFIED`：HTTP 501。
- 其他任何 `error`：HTTP 400。
- 成功：HTTP 200。非同步接受端點成功才是 202（第 30–41 行）。

`CAL_CFG_001`、`CAL_SDK_001`、`CAL_SYS_001` 來自 `CalibrateInstrumentAsync`（`CalibrationAppService.cs` 第 186–200 行），`instrument/start` 用 `DasApiActionResult.From`（`CalibrationController.cs` 第 37–38 行），所以都是 400。`CAL_SYS_001` 是未分類例外被接住後的業務碼，不是 HTTP 500。

`SYS_INTERNAL` 在 `ToTaskFail`（`ScanAppService.cs` 第 1960–1972 行），同樣進 `DasApiActionResult`，HTTP 400。

`ScanTaskException.StatusCode`（409、410、422、404）沒有被控制器使用，對外仍是 400。程式沒有自訂例外中介軟體（`Program.cs` 第 86–96 行）。沒被應用層接住的例外才會落到 ASP.NET 預設 500，那不會帶 `CAL_SYS_001` 或 `SYS_INTERNAL`。

`reference-setup` 走 `SpecApiActionResult`（第 10–26 行），失敗同樣是 400，`SYS_NYI`／`EDGE_NOTCH_UNVERIFIED` 是 501。

**建議文件寫法：** 6.2 維持這三個校正碼為 HTTP 400。7.5、9.2 寫成「同步業務失敗為 HTTP 400；`SYS_NYI` 與 `EDGE_NOTCH_UNVERIFIED` 為 HTTP 501。」已接受的 complete 失敗改由 callback／`GET` 的 `error` 表示，該次 HTTP 已是 202。

---

## Q6. EdgeNotch

**結論：錯誤碼這段與程式一致。** 類型表若讓人以為 EdgeNotch 可以掃完，文件要加註。

送 `holeType = "EdgeNotch"` 會在建立任務前丟出 HTTP 501、`EDGE_NOTCH_UNVERIFIED`，訊息「目前設備孔緣核心尚未驗證開放缺口；不會將掃描截斷誤當 EdgeNotch。」（`ScanOrchestrator.cs` 第 444–446 行；`DasApiActionResult.cs` 第 13–18 行）。

合法字面值只有省略（`null`）、`Circle`、`Slot`、`EdgeNotch`（第 439–441 行）。其他字串，含大小寫不同，是 HTTP 400、`INVALID_HOLE_TYPE`，訊息「holeType 需為 Circle、Slot 或 EdgeNotch。」模型只限制長度 ≤ 32（`HoleScanRequest.cs` 第 18–19 行），沒有列舉驗證。

**建議文件寫法：** 已知類型仍列這三個。EdgeNotch 旁註明「接受此值，但立即回 HTTP 501 `EDGE_NOTCH_UNVERIFIED`，不建立任務。」省略 `holeType` 為通用處理。

---

## Q7. width／height／length／leftWidth／rightWidth

**結論：文件要改。** 正式 callback 的數字來自 `LineScanMeasureService.MeasureLine`（第 103–131 行），經 `ToAngleIron` 原樣抄出（`ScanAppService.cs` 第 1406–1412 行）。單位是 mm（重建註記 `StreamingReconstruction.cs` 第 232–233 行；座標框 `calibrated_local_encoder`，`LineScanDeviceHost.cs` 第 140 行）。距離都是三維歐氏距離。

只用頭端截面，沒有頭尾平均。`LineScanAngleIronDto` 第 32–36 行的「頭尾截面各自量完再平均」與這段計算不符。已停用的 `CSharpLineMeasureService` 才取中段中位數，而且 DI 沒有註冊它。

| 欄位 | 公式 |
| --- | --- |
| `width` | 頭端左角點到右角點的距離 |
| `height` | 頭端頂點 Z，減去左右角點連線在頂點 X 處內插得到的 Z。`atApex = left.Z + (right.Z - left.Z) * (apex.X - left.X) / (right.X - left.X)`，`height = apex.Z - atApex` |
| `length` | 頭端頂點到尾端頂點的距離 |
| `leftWidth` | 頭端頂點到頭端左角點的距離 |
| `rightWidth` | 頭端頂點到頭端右角點的距離 |
| `leftLength`／`rightLength` | 頭端左／右角點到尾端同側角點的距離 |

`width` 是左–右這條底邊。`leftWidth` 與 `rightWidth` 是頂點到兩角的兩邊。三角形兩邊和大於等於第三邊，所以 `leftWidth + rightWidth` 大於等於 `width`；只有頂點落在左右角點連線上（高度為 0 且三點共線）才相等。它們不是同一個量的兩種寫法。

**建議文件寫法：** 照上表寫公式與 mm，並寫明只取頭端。刪除「左右翼寬相加等於 width」或「頭尾平均」這類描述。

---

## Q8. 面掃描座標與 a、c

**結論：文件要改。** 正式面掃描有做旋轉，不是只做平移加法。

`surface/start` 與 `segment` 把 `cameraPosition` 轉成 `Pose5`（`ScanAppService.cs` 第 1976–1990 行），`SegmentProcessor` 用 `RigidTransforms.FromPose` 組成 4×4（`SegmentProcessor.cs` 第 46 行、`RigidTransforms.cs` 第 29–56 行）。

- `a`、`c` 的單位是度（`Rotation` 第 168 行把度換成弧度；驗證訊息寫「度」，`ScanOrchestrator.cs` 第 1205 行）。
- 預設 A 繞 X、C 繞 Z（`CoordinateTransformOptions.cs` 第 15–18 行）。`appsettings.json` 的 `SURF-CAM-01`／`02` 也是 `"AAxis": "X"`、`"CAxis": "Z"`（第 65–68、89–92 行）。
- `ApplyCFirst` 預設 false，旋轉順序是 R(C)×R(A)，也就是先 Rx(A) 再 Rz(C)（`CoordinateTransformOptions.cs` 第 26–27 行；`GeometryFitting.cs` 第 270 行的註記相同）。
- 平移是 `cameraPosition` 的 x／y／z，加上設定的世界原點，再加最新儀器補償 `offsetX/Y/Z`（`ScanOrchestrator.cs` 第 1103–1124 行）。補償只加進平移。
- 其後再乘上 `ScannerToMount`。這組矩陣可以含安裝旋轉，預設是單位矩陣。

點的算法是：先套 `ScannerToMount`，再套 A／C 旋轉，再加上上述平移。

舊的 `RawPlyScanPipeline.ResolveTranslation` 只做平移，但 `surface/start` 不走那條路徑。

**建議文件寫法：** 絕對座標 = R(C)×R(A)×(ScannerToMount × 相對座標) + (cameraPosition.xyz + 世界原點 + offsetX/Y/Z)。a、c 單位為度；預設 A 繞 X、C 繞 Z。補償只平移，不旋轉。

---

## Q9. 其他錯誤碼

### (a) `TASK_NOT_FOUND`

**結論：GET 這個端點文件可維持共用一個碼。** 其他操作查無任務時不是這個碼。

`GET /api/tasks/{taskId}/result`（`TasksController.cs` 第 22–25 行）對所有任務走同一條：`GetTaskResultAsync`（`ScanAppService.cs` 第 980–990、1935–1947 行）。

- `taskId` 空白：`TASK_NOT_FOUND`，「taskId 必填。」
- 掃描任務檔與結果快取都沒有：`TASK_NOT_FOUND`，「找不到對應的任務。」

成功與失敗都是 `TaskResultData`（`taskId`、`status`、`result`、`error`、`traceId`）。失敗經 `DasApiActionResult` 變成 HTTP 400、本體 `{ error: { code, message } }`。線掃描、面掃描、孔洞、以及仍留在記憶體快取裡的儀器校正，查無時都是這個碼與這個格式。

依種類操作時，缺任務用別的碼（`ScanTaskRepository.cs` 第 251–261 行），訊息都是「任務不存在或已過保存期限。」：

- 面掃描 segment／complete／abort／附件：`SURF_001`
- 線掃描 complete／abort 查無：`SCAN_LINE_001`（`ScanAppService.cs` 第 216–219 行）
- 儀器校正 complete 查無：`CAL_001`，「找不到對應的校正任務。」（`CalibrationAppService.cs` 第 1004–1007 行）

`Missing()` 裡的 `Calibration` → `CAL_001` 分支目前沒有呼叫端。

**建議文件寫法：** `TASK_NOT_FOUND` 只用於 `GET /api/tasks/{taskId}/result`。各操作端點查無任務改列上面的碼。

### (b) 面掃描四個狀態碼

**結論：文件要改。** 正式路徑上這四個沒有在同一次呼叫重疊。`ScanAppService.FinalizeSurfaceTaskAsync` 裡多句 `SURF_001`（第 1005–1026 行）沒有呼叫端，`surface/complete` 實際走 `CompleteSurfaceAsync`（第 802–809 行）。

| 代碼 | 實際觸發 | 訊息 |
| --- | --- | --- |
| `SURF_001` | 面掃描 `GetAsync(..., "Surface")` 找不到、種類不符或已過保存期限（`ScanTaskRepository.cs` 第 251–261 行）。segment、complete、abort、下載附件都會碰到 | 任務不存在或已過保存期限。 |
| `TASK_STATE_CONFLICT` | `ExpectPhase`（`ScanOrchestrator.cs` 第 1165–1169 行）。segment 須在 `Collecting`；complete 須在 `Collecting`（已是 `Stitching` 或 `Completed` 則直接再回處理中，不報錯）；拼接收尾須在 `Stitching`；寫入段結果須在 `Capturing` | 此任務狀態不允許這個操作。 |
| `TASK_ALREADY_FINISHED` | 只在 abort：任務已是終態且狀態不是 `Aborted`（第 665–667、674–676 行）。已是 `Aborted` 的再 abort 會成功回傳，不報錯 | 「已完成或失敗的任務不能再取消。」或「任務已完成。」 |
| `TASK_ABORTED` | 兩處：segment 用同一個 `segmentKey` 重送，而任務已取消（第 224–226 行），訊息「任務已取消。」；下載已取消任務的附件（第 711–713 行），訊息「此任務已取消，附件不可下載。」 | 見左 |

abort 已完成任務用 `TASK_ALREADY_FINISHED`；segment／complete 碰到不允許的階段用 `TASK_STATE_CONFLICT`。同一個請求不會同時回這兩個碼。可以在文件裡分成「查無任務／階段不對／不能再取消／已取消後重送或下載」，不適合併成一個碼，除非要改客戶端契約。

### (c) `INVALID_ROI`

**結論：文件要改。** 這個碼會因呼叫端輸入而觸發，也會因孔洞的內部 `HoleRoi` 而觸發。

`ScanOrchestrator.ValidateRoi`（第 1211–1222 行）在 `roi` 不是 null，且座標非有限或任一軸 minimum ≥ maximum 時拋出。呼叫點：

- `surface/start`、`surface/segment`、`hole` 的請求 `roi`。
- 孔洞請求沒帶 `roi` 時，改驗證該掃描器設定的 `HoleRoi`（第 512–518 行；`appsettings.json` 第 99–107 行有 `SURF-CAM-02` 的值）。

請求省略 `roi` 時，面掃描的處理預設 ROI 若壞掉，丟的是 `ArgumentException`「scanner ROI 必須為有效有限範圍。」（`SegmentProcessor.cs` 第 381–387 行），對外變成 `SYS_INTERNAL`，不是 `INVALID_ROI`。線掃描 ROI 設定錯誤是 `CAL_CFG_001`，也不是這個碼。

**建議文件寫法：** `INVALID_ROI`：請求的 `roi` 不合法，或孔洞未帶 `roi` 而掃描器 `HoleRoi` 不合法。訊息：「ROI 必須提供有限座標，且每軸 minimum 小於 maximum。」

---

## Q10. 儀器校正 callback 的 result

**結論：文件 5.3 那張表與 callback 的 `result` 一致。** 沒有多出或缺少表上的欄位。

`RunInstrumentCompleteJobAsync` 成功時組出的 `result` 只有這五項（`CalibrationAppService.cs` 第 1113–1124 行；型別 `InstrumentCalibrationCompleteResult.cs` 第 6–19 行）：

- `scannerId`
- `compensation.offsetX`／`offsetY`／`offsetZ`（`CompensationDto.cs` 第 4–13 行，單位 mm）
- `rmseXY`
- `zDeviation`
- `calibratedAt`（ISO 8601，`ToString("O")`，第 454 行）

內部的 `InstrumentCalibrationData` 還有 `actualScannerPosition`、`balls`、`matchedBalls`、`datumPlane`、`scannerToFloorZ`、`taskId`、`seq`、`traceId`（`InstrumentCalibrationData.cs` 第 20–71 行），但沒有抄進 callback 的 `result`。

callback 本體是整包 `TaskResultData`：`taskId`、`status`、`result`、`error`、`traceId`（第 1113–1131 行）。`traceId` 在信封上，不在 `result` 裡。

**建議文件寫法：** 5.3 的 result 表維持這五欄。信封另表：`taskId`、`status`（`Completed`／`Failed`）、`result`、`error`、`traceId`。失敗時 `result` 為 null，`error.code`／`error.message` 有值。

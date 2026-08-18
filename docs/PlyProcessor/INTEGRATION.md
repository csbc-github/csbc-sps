# PlyProcessor 搬入檔案與完整串接

來源：`Hank/code-V2_260716`（對方文件：`docs/PlyProcessor/Ply_processor.md`）

---

## 1. 搬了什麼／沒搬什麼

### 已搬入

| 對方路徑 | ThreeDScan 位置 | 說明 |
|---|---|---|
| `csharp/PlyProcessor.Core/*` | `PlyProcessor.Core/` | 角度、分群、ZIP／JSON／STEP／GLB／PNG |
| `ply_processor/ply_processor_fixed.exe` + `_internal` | `ThirdParty/ply_processor/` | Python 前處理 backend（角鋼 PLY） |
| `docs/Ply_processor.md` | `docs/PlyProcessor/Ply_processor.md` | 對方盤點文件 |
| `tools/json_formatter.py` | `ThirdParty/ply_processor_tools/` | 工具，不參與主流程 |

### 未搬入（刻意）

| 項目 | 原因 |
|---|---|
| `PlyProcessor.App`（WinForms） | UI 功能接到 ThreeDScan 掃描流程 |
| `PlyProcessor.Api` | 已有 `ThreeDScan.Api` |

### 為串接新增／修改的 ThreeDScan 檔

| 檔案 | 角色 |
|---|---|
| `HankPlyProcessorClient.cs` | 對齊對方 `PythonBackendClient`：啟動 exe、`POST /process`、`GET /status` |
| `AngleSteelPipelineExporter.cs` | 對齊 WinForms：角度 + `Pipeline.RunProcessed` → 寫 ZIP／JSON |
| `PlyProcessorOptions.cs` | `appsettings.json` → Python 參數 + C# Pipeline 參數 |
| `Ljs8SurfaceScannerCapture.cs` | 掃描後串起整條鏈 |
| `ScannerCaptureFactory.cs` | 注入 Client + Exporter |
| `DependencyInjection.cs` | DI 註冊 |
| `ISurfaceScannerCapture.cs`（`ScannerCaptureResult`） | 增加 `ZipPath`／`JsonPath`／角度欄位 |
| `ScannerTestScanViewModel.cs` | 狀態列顯示點數／角度／ZIP |
| `ThreeDScan.slnx` | 加入 `PlyProcessor.Core` 專案 |
| `ThreeDScan.Infrastructure.csproj` | `ProjectReference` → Core |
| `ThreeDScan.csproj` / Api | `Import PlyProcessor.targets`（建置時複製 exe） |
| `appsettings.json` | `PlyProcessor` 區段 |

---

## 2. 專案參考關係

```text
ThreeDScan.slnx
├─ PlyProcessor.Core
├─ ThreeDScan.Domain          ← ScannerCaptureResult 擴充
├─ ThreeDScan.Infrastructure  ← 參考 Domain + PlyProcessor.Core
│     HankPlyProcessorClient
│     AngleSteelPipelineExporter
│     Ljs8SurfaceScannerCapture
├─ ThreeDScan (WPF)           ← 參考 Infrastructure；Import PlyProcessor.targets
└─ ThreeDScan.Api             ← Import PlyProcessor.targets（若 API 端也要 exe）
```

建置時 `ThirdParty/PlyProcessor.targets` 把 exe 複製到：

`bin\x64\Debug\net10.0-windows\ply_processor\`

---

## 3. 執行期串接（對齊對方 WinForms）

```text
UI 測試掃描 / 面掃描
  └─ ScannerCaptureFactory.GetCapture(scannerId)
       └─ Ljs8SurfaceScannerCapture.CaptureAsync
            │
            ├─① LJS8 SDK 擷取高度／灰階
            ├─② 寫入 export/{ScannerId}/{時間}_{id}/
            │     capture_height.png
            │     capture_gray.png          ← 校正用（路徑經 ScannerCaptureResult 傳出）
            │     raw_scene.ply             ← HeightImagePlyExporter（ASCII）
            │
            ├─③ HankPlyProcessorClient.ProcessAsync(raw_scene.ply)
            │     ├─ 啟動／連線 ply_processor_fixed.exe (:8000)
            │     ├─ 複製 raw → ply_processor/scans/raw_scene.ply
            │     ├─ POST /process (file, filename, z_thresh, cell)
            │     ├─ GET  /status  SSE（略過重播舊 done；要 mtime 新鮮）
            │     └─ 回傳 scans/raw_scene_angle_steel.ply
            │
            ├─④ 複製為同資料夾 angle_steel.ply
            │
            ├─⑤ AngleSteelPipelineExporter.Export(angle_steel.ply)
            │     ├─ PlyReader.Parse
            │     ├─ AngleSteelAngleEstimator.Estimate
            │     ├─ Pipeline.RunProcessed  → ZIP 內含 PLY/JSON/STEP/GLB/三視圖
            │     └─ 寫出 angle_steel_export.zip、angle_steel_coords.json
            │
            └─⑥ 回傳 ScannerCaptureResult
                  PlyPath / PlyPointCount
                  ZipPath / JsonPath
                  AngleDegrees / AngleDeviationFromSquareDegrees
                  HeightImagePath / GrayImagePath   ← 校正／預覽繼續用
```

對方 WinForms 對照：

| WinForms | ThreeDScan |
|---|---|
| `MainForm.RunPipeline` | `Ljs8SurfaceScannerCapture.CaptureCoreAsync` |
| `PythonBackendClient` | `HankPlyProcessorClient` |
| `AngleSteelAngleEstimator` + `Pipeline.RunProcessed` | `AngleSteelPipelineExporter` |
| 另存 ZIP／JSON | 直接寫進該次 `export\...` 資料夾 |

---

## 4. DI 註冊

`ThreeDScan.Infrastructure/DependencyInjection.cs`：

```csharp
services.Configure<PlyProcessorOptions>(configuration.GetSection("PlyProcessor"));
services.AddSingleton(sp => new HankPlyProcessorClient(...));
services.AddSingleton<AngleSteelPipelineExporter>();
// ScannerCaptureFactory 建構時注入以上兩者 → 各 Scanner 的 Ljs8SurfaceScannerCapture
```

---

## 5. 設定（`appsettings.json` → `PlyProcessor`）

| 鍵 | 用途 | 對應對方 |
|---|---|---|
| `RootPath` | exe 相對目錄（預設 `ply_processor`） | backend 根目錄 |
| `Port` | 8000 | localhost backend |
| `ZThresh` / `Cell` | `POST /process` | WinForms z_thresh / NoiseVoxel→cell |
| `NoiseVoxel`…`MeshBridgeFill` | `Pipeline.RunProcessed` | `PipelineOptions` |

`RootPath` 解析為：`AppContext.BaseDirectory\ply_processor`（建置輸出目錄）。

---

## 6. 單次掃描輸出清單

`export/{ScannerId}/{yyyyMMdd_HHmmss}_{id8}/`

```text
capture_height.png
capture_gray.png              ← 校正／監看：經 Result 路徑 + ContentsRoot 複本
raw_scene.ply
angle_steel.ply               ← Python 產出
angle_steel_export.zip        ← C# Pipeline（內含 STEP/GLB/PNG/…）
angle_steel_coords.json
ply_processor.log
```

校正路徑不變：用 `GrayImagePath`／`HeightImagePath`，不寫死 export 路徑。

---

## 7. PlyProcessor.Core 檔案職責（搬入後）

| 檔案 | 何時被呼叫 |
|---|---|
| `PlyReader` / `PlyPoint` | Exporter 讀角鋼 PLY |
| `AngleSteelAngleEstimator` | 估兩翼夾角 |
| `Pipeline.RunProcessed` | 分群／匯出／打 ZIP |
| `VoxelClusterer` / `PcaPlane` / `MeshBuilder` / … | Pipeline 內部 |
| `PlyExporter` / `JsonExporter` / `StepExporter` / `GltfExporter` / `OrthoViewRenderer` | ZIP 內容 |
| `ScanProcessor`（Core 內） | **目前掃描流程不用**（那是純 C# 取代 Python 的 `Pipeline.Run`）；WinForms／我們走的是 Python + `RunProcessed` |

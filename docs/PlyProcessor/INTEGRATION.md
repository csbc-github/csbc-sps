# PlyProcessor 搬入檔案與完整串接

來源：`Hank/code-V2_260716`（對方文件：`docs/PlyProcessor/Ply_processor.md`）

---

## 1. 搬了什麼／沒搬什麼

### 已搬入

| 對方路徑 | ThreeDScan 位置 | 說明 |
|---|---|---|
| `csharp/PlyProcessor.Core/*` | `PlyProcessor.Core/` | 去地、分群、ZIP／JSON／STEP／GLB／PNG、角度 |
| `docs/Ply_processor.md` | `docs/PlyProcessor/Ply_processor.md` | 對方盤點文件 |

### 未搬入／已拿掉

| 項目 | 原因 |
|---|---|
| `PlyProcessor.App`（WinForms） | UI 接到 ThreeDScan 掃描流程 |
| `PlyProcessor.Api` | 已有 `ThreeDScan.Api` |
| `ply_processor_fixed.exe` + `_internal` | 改走 C# `Pipeline.Run`，不再呼叫 Python exe |

### 為串接新增／修改的 ThreeDScan 檔

| 檔案 | 角色 |
|---|---|
| `RawPlyScanPipeline.cs` | 高度圖 → raw PLY → `Pipeline.Run` → ZIP／PLY／GLB／角度 |
| `PlyProcessorOptions.cs` | `appsettings.json` → C# Pipeline 參數 |
| `Ljs8SurfaceScannerCapture.cs` | 拍攝後接到同一條 Pipeline |
| `ScanAppService.cs` | `POST /api/scan/surface` |
| `ThreeDScan.slnx` | 加入 `PlyProcessor.Core` |
| `appsettings.json` | `PlyProcessor` 區段 |

---

## 2. 專案參考關係

```text
ThreeDScan.slnx
├─ PlyProcessor.Core
├─ ThreeDScan.Domain
├─ ThreeDScan.Infrastructure  ← Domain + PlyProcessor.Core
│     RawPlyScanPipeline
│     Ljs8SurfaceScannerCapture
├─ ThreeDScan (WPF)
└─ ThreeDScan.Api
```

---

## 3. 執行期串接（API 與噴印「開始掃描」同一條）

```text
SDK 拍攝
  → HeightImagePlyExporter → raw_scene.ply
  → Pipeline.Run（ScanProcessor 去地／去雜訊 → 匯出 ZIP）
  → angle_steel.ply / ZIP / GLB / 夾角
```

校正／右上角測試掃描只拍影像，不跑 Pipeline。

---

## 4. DI 註冊

`ThreeDScan.Infrastructure/DependencyInjection.cs`：

```csharp
services.Configure<PlyProcessorOptions>(configuration.GetSection("PlyProcessor"));
services.AddSingleton<ISurfaceScanPipeline, RawPlyScanPipeline>();
```

---

## 5. 設定（`appsettings.json` → `PlyProcessor`）

`ZThresh`、`NoiseVoxel`…`MeshBridgeFill` 對應 `PipelineOptions`。

---

## 6. 單次掃描輸出清單

`export/{ScannerId}/{yyyyMMdd_HHmmss}_{id8}/`

```text
capture_height.png
capture_gray.png
raw_scene.ply
angle_steel.ply
angle_steel_export.zip
angle_steel_coords.json
ply_processor.log          ← 噴印掃描寫處理紀錄
```

# PLY Processor 

> 文件版本：1.0  
> 盤點日期：2026-08-12  
> 適用範圍：本資料夾內的 C# solution、WinForms App、Web API、封裝 Python backend、工具程式與範例資料。

## 1. 系統摘要

本專案將掃描器輸出的 ASCII PLY 點雲轉成：

- 處理後 PLY
- 座標 JSON
- STEP (`.stp`)
- glTF binary (`.glb`)
- top/front/side 三視圖 PNG
- 以上檔案的 ZIP 套件
- 角鋼兩翼板夾角估算結果（WinForms 顯示）

目前有兩條流程：

```text
WinForms App
  └─ PythonBackendClient
      └─ 啟動/連線 ply_processor_fixed.exe
          └─ HTTP 127.0.0.1:8000
              ├─ POST /process (multipart)
              └─ GET  /status  (SSE，data: JSON)
  └─ 取得處理後 PLY
  └─ C# Pipeline.RunProcessed
      └─ ZIP / JSON / STEP / GLB / PNG

Web API + Browser
  └─ POST /api/process (multipart)
      └─ C# Pipeline.Run
          └─ PCA + z threshold + voxel denoise + export
```

重要差異：C# `Pipeline` 已具備取代 Python 前處理的實作，但 WinForms 目前仍實際呼叫封裝 Python backend；Web API 則完全不經 Python。

## 2. 專案結構與責任

| 路徑 | 責任 |
|---|---|
| `csharp/PlyProcessor.sln` | solution 入口 |
| `csharp/PlyProcessor.App` | Windows Forms UI、Python backend 啟動與串接、下載 ZIP/JSON |
| `csharp/PlyProcessor.Core` | PLY 讀取、前處理、分群、角度估算、網格與所有輸出 |
| `csharp/PlyProcessor.Api` | ASP.NET Core Web API 與靜態網頁 |
| `ply_processor/ply_processor_fixed.exe` | 封裝後 Python backend 執行檔 |
| `ply_processor/_internal` | Python 3.11 embedded runtime、依賴 DLL、前端資源 |
| `ply_processor/scans` | backend 產生/使用的掃描輸入與處理後 PLY |
| `tools/json_formatter.py` | JSON 格式化、排序、壓縮工具；不參與主流程 |
| `csharp/_verify_app_build*` | 既有驗證/打包產物，不是主要原始碼 |

原始碼完整清單：C# 原始碼在 `csharp/`；Python 原始碼目前只有 `tools/json_formatter.py`。Python backend 的原始 `.py` 未隨專案提供，只能以封裝 EXE、C# client、輸出檔和日誌推斷其介面。

## 3. WinForms 流程

主要入口：`csharp/PlyProcessor.App/MainForm.cs`。

1. 使用者選擇或拖放 `.ply`。
2. UI 讀取參數，建立 `PipelineOptions`。
3. 尋找 backend 根目錄：
   - 發布模式：`AppContext.BaseDirectory/python_backend`
   - 開發模式：往上回溯至專案根目錄的 `ply_processor`
4. `PythonBackendClient.ProcessAsync` 確認 backend 可用。
5. 若 `scans/<原檔名>_angle_steel.ply` 或 `<原檔名>_obj1.ply` 存在且時間不早於輸入，直接重用。
6. 否則以 multipart POST 呼叫 `/process`。
7. 讀取 `/status` SSE，等待 `done=true`，從 `outputs` 或 `writing ... .ply` 找出輸出。
8. 以 C# 讀取處理後 PLY，執行角度估算與 `Pipeline.RunProcessed`。
9. 顯示物件數、點數和角度，將 ZIP/JSON 存到使用者指定的位置。

### Python backend 啟動與連線契約

- 執行檔：`ply_processor_fixed.exe`
- 工作目錄：backend 根目錄
- 監聽位址：`http://127.0.0.1:8000/`
- C# 先搜尋同一 `_rootDir` 下的 `ply_processor_fixed` process；找到則共用。
- 找不到則以 `CreateNoWindow=true` 啟動。
- readiness probe：根路徑，每 500 ms 一次，最多約 15 秒。
- `/process` timeout：10 分鐘。
- `/status` 必須回傳 SSE 行，格式至少包含 `data: { ... }`。

`/process` multipart 欄位：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `file` | file | 原始 PLY |
| `filename` | string | 原檔名 |
| `z_thresh` | float | 高度門檻，mm |
| `cell` | float | 去雜訊 voxel 尺寸；WinForms 傳 `NoiseVoxel` |

C# 依序優先選擇輸出檔名含 `_angle_steel` 的 PLY，其次選擇非 `door_stopper` PLY。這是相容既有 backend 輸出命名的規則。

`/status` 目前由 client 使用的欄位：

```json
{"log":"writing scans/foo_angle_steel.ply"}
{"done":false,"log":"..."}
{"done":true,"error":null,"outputs":["scans/foo_angle_steel.ply"]}
```

## 4. C# Core 處理流程

### PLY 輸入限制

`PlyReader` 目前讀取 ASCII PLY：

- 由 `element vertex N` 取得點數。
- 讀取前三欄為 `x y z`。
- 第四欄作為 intensity；缺少時預設 180。
- 使用 invariant culture 解析浮點數。
- 不處理 binary PLY、faces、其他複雜 property 排列。
- PLY 點的單位由現有註解和參數假設為 mm。

### 前處理 (`Pipeline.Run` / `ScanProcessor.Process`)

1. PCA 找主要平面。
2. 計算各點到平面的有向高度。
3. 自動判斷 PCA normal 正負方向，保留高度大於 `ZThresh` 的點。
4. 以 voxel BFS 找連通群，移除小於 `MinNoiseSize` 的群。

### 分群與目標物

`Pipeline.BuildExports` 以 voxel 分群；目前分群 voxel 尺寸至少為 `30 mm`（`Math.Max(VoxelSize, 30)`）。相鄰 bounding boxes 距離不超過 `max(PlaneTol*3, 30)` 的群會合併。最後會把剩餘群合併成一個輸出目標物，符合角鋼一鍵輸出模式。

### 網格

`HeightfieldMeshBuilder`/`MeshBuilder`：將點投影到 PCA 平面，建立 2D occupancy grid，執行橋接、侵蝕、平滑，再以厚度擠出。STEP 與 GLB 產出失敗時會被個別略過，ZIP 仍可能成功。

另有 `SurfaceMeshBuilder`：3D voxel 表面重建，可支援 L/T/U 等形狀；目前 `Pipeline` 沒有使用它，改動前需先做輸出相容性驗證。

### 座標回溯

輸出點以 `x/y/z` 的 float bit pattern 對照原始點，建立 raw index。若找不到對應 raw 點，JSON 會寫 `null`；PLY exporter 則寫 `0 0 0`。這個差異需注意。

### 角度估算

`AngleSteelAngleEstimator` 使用固定 seed 的 RANSAC 找兩個不平行平面，再以兩平面 normal 的絕對 dot product 計算較小二面角。點數不足、平面支持度不足或角度小於 5° 時回傳失敗。

## 5. PipelineOptions 預設值與來源

| 參數 | 預設值 | 單位/用途 |
|---|---:|---|
| `ZThresh` | 12 | mm，高度門檻 |
| `NoiseVoxel` | 3 | mm，前處理去雜訊 voxel |
| `MinNoiseSize` | 200 | 點數，最小去雜訊群 |
| `VoxelSize` | 10 | mm，物件分群設定；實際至少 30 |
| `MinClusterSize` | 200 | 點數，物件分群最小群 |
| `PlaneTol` | 8 | mm，主平面/鄰近群容差 |
| `RemoveDominantPlane` | false | 是否移除主背景平面 |
| `SinglePieceMode` | false | 目前 WinForms 固定傳 false；Web UI 傳 true，但 Core 目前仍以單一目標輸出 |
| `MeshCellSize` | 2 | mm，網格格子 |
| `MeshThickness` | 6 | mm，擠出厚度 |
| `SmoothIter` | 4 | 網格平滑次數 |
| `ErodeN` | 1 | 網格侵蝕次數 |
| `MeshBridgeFill` | 30 | 格子橋接次數/最大缺漏 |

WinForms Designer 的部分欄位最小值與 Core 預設不同，例如 UI `NoiseVoxel` 預設 3、`MeshCellSize` 預設 2、`MeshThickness` 預設 6；實際值以 UI 傳入值為準。

## 6. 輸出格式

每個物件通常包含：

```text
<name>.ply
<name>.json
<name>_2d_top.png
<name>_2d_front.png
<name>_2d_side.png
<name>.stp       # mesh 成功才有
<name>.glb       # mesh 成功才有
```

JSON 物件結構：

```json
{
  "name": "object-name",
  "count": 123,
  "columns": ["x","y","z","intensity","raw_x","raw_y","raw_z","raw_intensity"],
  "note": "...",
  "points": [[x,y,z,intensity,raw_x,raw_y,raw_z,raw_intensity]]
}
```

Web API 回應：

```json
{
  "zipBase64": "...",
  "jsonBase64": "...",
  "objectNames": ["..."],
  "objectCounts": [123]
}
```

Web API endpoint：`POST /api/process`，使用 multipart/form-data，檔案欄位為 `plyFile` 或 `processedPly`。錯誤時回傳 ASP.NET Problem Details，HTTP status 通常為 400 或 500。

## 7. 建置、執行與發布

### 建置

```powershell
dotnet restore csharp\PlyProcessor.sln
dotnet build csharp\PlyProcessor.sln --nologo
```

WinForms target：`.NET 9`, `net9.0-windows`, Windows Forms。  
API/Core target：`.NET 9`。

### 啟動 Web API

```powershell
dotnet run --project csharp\PlyProcessor.Api
```

開發設定目前預期使用 `http://localhost:5074`；HTTPS profile 為 `https://localhost:7036`。瀏覽器頁面由 `wwwroot/index.html` 提供。

### 發布 WinForms

發布輸出必須同時包含：

```text
PlyProcessor.App.exe
python_backend\ply_processor_fixed.exe
python_backend\_internal\**\*
```

`PlyProcessor.App.csproj` 會從專案根目錄的 `ply_processor` 複製上述 backend 內容。只複製 C# DLL/EXE 而缺少 `python_backend` 會導致啟動時找不到 backend。

## 8. 排錯手冊

| 現象 | 優先檢查 |
|---|---|
| 找不到 Python backend | 確認 `python_backend/ply_processor_fixed.exe` 與 `_internal` 存在 |
| backend 啟動逾時 | 檢查 8000 port 是否被占用、執行檔是否能單獨啟動、是否被防毒攔截 |
| `/process` 回 4xx/5xx | 查看 WinForms log 的 response body；確認 multipart 欄位名稱與 PLY 格式 |
| `/status` 中斷 | backend process 是否退出、`scans` 是否可寫、輸出是否真的產生 |
| 沒有輸出 PLY | 確認 SSE 的 `outputs` 或 log 是否包含 `writing ... .ply`，以及檔名是否符合選檔規則 |
| z threshold 後無點 | 降低 `ZThresh`，確認掃描單位是否為 mm、PCA 是否把背景當主要平面 |
| 物件消失 | 降低 `MinNoiseSize`/`MinClusterSize`，增加 voxel 尺寸或檢查點雲密度 |
| 缺 STEP/GLB | 網格建構可能對稀疏/過小物件失敗；目前 catch 後只略過 mesh 輸出 |
| raw 座標為 null/0 | 輸出點與原始點不再有完全相同的 float 座標，導致 bit-level 對照失敗 |

## 9. 已知風險與待確認事項

1. **Python backend 原始碼缺失**：無法在本 repo 內驗證其內部演算法、完整輸入限制、版本與依賴。交接時應補交 source repo、commit/tag、建置方式和 API schema。
2. **WinForms 依賴 localhost backend**：port 固定為 8000；多個 App/殘留 backend 可能互相干擾。
3. **輸出快取只依賴檔案時間與命名**：相同 basename 或複製檔案可能誤用舊輸出；沒有 hash 或參數簽章。
4. **SSE/輸出檔判斷依賴字串**：`writing `、`_angle_steel`、`door_stopper` 是隱含契約。
5. **Web API CORS 開放全部來源**：`AllowAnyOrigin/AllowAnyHeader/AllowAnyMethod`，若部署到非本機環境需收斂來源並加入檔案大小/請求逾時限制。
6. **Web API 使用 Base64 回傳完整 ZIP/JSON**：大型點雲會造成記憶體與 HTTP payload 壓力。
7. **PLY parser 僅支援簡單 ASCII 格式**：binary PLY 或 property 順序不同可能讀錯或讀不到。
8. **mesh 失敗被靜默忽略**：使用者可能只看到 ZIP 成功，卻不知道 STEP/GLB 缺失。
9. **核心原始碼有部分中文註解/訊息編碼異常**：不影響目前建置，但會降低維運可讀性。
10. **缺少自動化測試與基準輸出**：目前只有建置驗證，尚未建立輸入 PLY、預期點數、輸出檔案和幾何誤差的 regression test。

## 10. 建議交接清單

- [ ] 補交 Python backend 原始碼、版本/commit、打包腳本與第三方授權。
- [ ] 確認 Python `/process`、`/status`、root readiness endpoint 的正式 schema。
- [ ] 決定 WinForms 是否改用 `Pipeline.Run`，或正式保留 Python 作為必要 runtime。
- [ ] 為每個代表性 PLY 建立 golden output 與參數檔。
- [ ] 增加 PLY parser、前處理、輸出格式與角度估算單元測試。
- [ ] 為 backend process 加入 PID/port/輸入 job ID，避免多實例混用。
- [ ] 將快取鍵改為輸入檔 hash + 參數 hash。
- [ ] 補上 API 檔案大小、取消請求、逾時、錯誤碼與安全設定。
- [ ] 對 mesh skip 記錄具體例外，並在 UI/API 回報缺少的輸出。

## 11. 本次驗證紀錄

已執行：

```text
dotnet build csharp\PlyProcessor.sln --nologo
結果：成功，0 warning，0 error
環境：Windows，.NET SDK 9.0.313，win-x64
```

尚未在本次盤點中執行完整 PLY 端到端處理、實際啟動 Python backend 或 API 壓力測試；原因是 Python backend 原始碼不可見，且該類測試會產生/覆寫處理輸出。接手者應依第 10 節建立可重複的驗證資料集後再執行。

## 附錄 A：C# 檔案責任索引

### `PlyProcessor.App`

| 檔案 | 責任 |
|---|---|
| `Program.cs` | WinForms application entry point |
| `MainForm.cs` | 選檔、參數收集、流程協調、進度/錯誤/角度結果、檔案儲存 |
| `MainForm.Designer.cs` | UI 控制項與版面配置 |
| `MainForm.resx` | WinForms 資源 |
| `PythonBackendClient.cs` | backend process 管理、HTTP multipart `/process`、SSE `/status`、輸出選擇 |
| `PlyProcessor.App.csproj` | WinForms target 與 Python backend 複製規則 |

### `PlyProcessor.Core`

| 檔案 | 責任 |
|---|---|
| `Pipeline.cs` | 主流程、物件合併、raw 座標對照、ZIP 打包 |
| `ScanProcessor.cs` | PCA 高度過濾與去雜訊；C# 版 Python 前處理替代實作 |
| `PlyReader.cs` | ASCII PLY vertex 讀取 |
| `PlyPoint.cs` | 點資料模型：XYZ + intensity |
| `PcaPlane.cs` | PCA 平面、特徵向量、主平面移除 |
| `VoxelClusterer.cs` | 3D voxel BFS 連通元件與最小群過濾 |
| `AngleSteelAngleEstimator.cs` | RANSAC 兩平面與角度估算 |
| `MeshBuilder.cs` | PCA 投影、2D occupancy、橋接/侵蝕/平滑、擠出網格 |
| `HeightfieldMeshBuilder.cs` | heightfield 網格建構與邊界處理 |
| `SurfaceMeshBuilder.cs` | 3D voxel 表面網格；目前未接入主 Pipeline |
| `StepExporter.cs` | ASCII STEP/ISO-10303-21 輸出 |
| `GltfExporter.cs` | GLB binary 輸出 |
| `PlyExporter.cs` | 處理後 ASCII PLY 輸出，含 raw 座標欄位 |
| `JsonExporter.cs` | 物件座標 JSON 輸出 |
| `OrthoViewRenderer.cs` | top/front/side PNG 輸出 |
| `RawCoordMapper.cs` | 以 intensity-run 與 affine transform 對應 raw/processed；目前未被主 Pipeline 呼叫 |
| `Class1.cs` | 預設/未使用的類別檔，未見主流程引用 |
| `PlyProcessor.Core.csproj` | .NET 9 class library 與 `System.Drawing.Common` 依賴 |

### `PlyProcessor.Api`

| 檔案 | 責任 |
|---|---|
| `Program.cs` | ASP.NET host、CORS/static files、`POST /api/process` |
| `wwwroot/index.html` | 瀏覽器上傳 UI、參數表單、Base64 下載 |
| `appsettings.json` / `appsettings.Development.json` | ASP.NET 設定 |
| `Properties/launchSettings.json` | 本機 HTTP/HTTPS 啟動 profile |
| `PlyProcessor.Api.csproj` | .NET 9 Web project 與 Core reference |

### Python/工具

| 檔案 | 責任 |
|---|---|
| `tools/json_formatter.py` | 讀取 JSON、pretty print/minify、排序 key、原地或另檔輸出 |
| `ply_processor/ply_processor_fixed.exe` | 封裝 backend；不是可讀的 Python source |
| `ply_processor/_internal/*` | backend embedded runtime 與靜態資源 |

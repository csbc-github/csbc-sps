# 點雲檢視程式（`point_cloud_viewer.py`）— WPF 顯示對齊參考

本文說明 Python 端 **`point_cloud_viewer.py`** 在顯示 PLY 前做了哪些處理，供 **WPF** 專案實作相同視覺效果時對照。  
Python 程式本身仍為開發／除錯用；**正式產品整合**以 `surface_scan_cli` 匯出 PLY、WPF 載入顯示為主，本文件僅定義「若要長得像 Python viewer，應複製哪些規則」。

---

## 1. 與 LJS8 匯出 PLY 的關係

本專案匯出邏輯見 `ljs8/export/point_cloud_export.py`：

- **格式**：多為 **ASCII PLY**，欄位為 `property float x`、`y`、`z`；若開啟亮度則多 **`property uchar intensity`**。
- **單位**：註解標示為 **mm（掃描器座標系）**；匯出時 **不做 Z 去底**（與 viewer 內「顯示選項」無關）。

因此：**檔案內容 ≠ 畫面上最終點位**，WPF 若只畫檔案原始座標，可能與 Python viewer（預設開檔路徑）在 **Z 平移、顏色** 上不一致。

---

## 2. 顯示前處理流程（必讀順序）

顯示前會對點雲做 **`prepare_point_cloud`**，**順序固定**，實作時請維持同一順序：

| 步驟 | 條件 | 行為 |
|------|------|------|
| 1 | `flatten_z == true` | 令所有點 `z := z - min(z)`（**Z 去底**，最低點 Z 為 0） |
| 2 | `voxel_size > 0` | **體素降採樣**（Open3D：`voxel_down_sample`，單位與座標相同，通常為 mm） |
| 3 | `max_points > 0` 且目前點數 `n > max_points` | **隨機抽點**：比例 `ratio = max_points / n`，保留約 `max_points` 點（Open3D：`random_down_sample`） |
| 4 | 著色 | 見下一節 |

**注意**：著色使用的 `z` 是 **完成上述步驟後** 的 Z（若已去底，則為去底後的 Z）。

---

## 3. 依高度著色（與 LJS8 PLY 預設顯示）

函式：`_z_to_rgb_colors`（不依賴 matplotlib）。

### 3.1 何時套用彩虹色

- 若 **`color_by_height == true`**：強制依 Z 著色（覆蓋檔案原有顏色）。
- 若 **`color_by_height == false`** 且點雲 **沒有 per-vertex 顏色**：仍 **依 Z 著色**。

本專案匯出之 PLY 通常 **僅 x,y,z（+ intensity）**，Open3D 常判定為「無 vertex color」，故 **預設看起來就是依 Z 彩虹色**。  
若 WPF 載入後畫成單色，視覺會與 Python viewer 差很多。

### 3.2 色彩對應（可移植到 C#）

對每個點的標高值 `z`（已為 `float64`／`double` 陣列）：

1. `lo = min(z)`，`hi = max(z)`，`span = max(hi - lo, 1e-12)`
2. 正規化：`t = (z - lo) / span`（`t ∈ [0,1]`，單點時全體同值亦可）
3. **色相**：`h = 0.75 * (1.0 - t)`（**高處偏紅、低處偏藍** 的漸層）
4. **HSV → RGB**：`S = 0.88`，`V = 0.96`，再轉成 sRGB 分量（0～1）

HSV 轉 RGB 請使用標準演算法（與 Python `colorsys.hsv_to_rgb(h, 0.88, 0.96)` 一致）。WPF 端通常將 RGB 乘 255 或直接用 `Color.FromScRgb` 等 API。

---

## 4. 啟動方式與預設參數對照

### 4.1 命令列直接開檔（常見）

```text
python point_cloud_viewer.py <路徑.ply>
```

對應程式碼：`_run_open3d_direct` 內呼叫 `prepare_point_cloud`，預設為：

| 參數 | 值 |
|------|-----|
| `flatten_z` | **true**（Z 去底） |
| `color_by_height` | **false**（但若檔案無顏色，仍會依 Z 著色） |
| `voxel_size` | **0**（關閉體素；可用 `--voxel` 覆寫） |
| `max_points` | **2_000_000**（可用 `--max-points` 覆寫；0 表示不限制） |
| 點大小（Open3D） | **2.5**（`--point-size`） |

### 4.2 tk 設定面板（無命令列路徑或加 `--tk`）

使用者可勾選／輸入：

| 選項 | 預設 |
|------|------|
| Z 去底 | **勾選** |
| 強制依高度著色 | **不勾選**（無 vertex color 時仍會依 Z 著色） |
| 點大小 | **2.5** |
| 體素 | **0** |
| 最多點數 | **2000000** |
| 深色背景 | **勾選** |
| 顯示座標軸 | **勾選** |

---

## 5. 視覺環境（背景、座標軸、點大小）

與「數學上點位」無關，但影響觀感：

- **背景色（深色）**：約 RGB `(0.12, 0.14, 0.18)`（Open3D draw／Filament 使用帶 alpha 的 `(R,G,B,1)`）。
- **背景色（淺色）**：約 `(0.96, 0.97, 0.98)`。
- **座標軸**：若開啟，軸長約為包圍盒對角線長的 **8%**（`max(d * 0.08, 1e-3)`，`d` 為點雲範圍的對角線長）。
- **點大小**：渲染用 `point_size`，預設 **2.5**（與 3D 引擎單位有關，WPF 需依 Helix 等 API 換算）。

---

## 6. 相機／視角

Python 端使用 Open3D 依 **軸對齊包圍盒（AABB）** 與 **視野角約 60°** 設定相機（Filament：`setup_camera(60.0, bbox, center)`）。  
WPF 若要「第一眼」接近，建議：**對 AABB 中心對焦**，視距／縮放依對角線長調整，使整朵雲落在視窗內。

---

## 7. 子程序與環境變數（了解即可）

為降低 Windows 上 Filament 崩潰影響，viewer 可能以 **子程序** 再啟動自身並帶 `--internal-filament-hud`；或以 `o3d.visualization.draw`、舊版 `Visualizer` 後備。  
與 **點雲資料處理** 對齊時，**以第 2～3 節為準**；環境變數如 `LJS8_PCV_DRAW_ONLY`、`LJS8_PCV_NO_SUBPROCESS` 僅影響 **用哪個視窗後端**，不改數學流程。

---

## 8. WPF 實作檢查清單（對齊 Python 預設「命令列開檔」）

1. **解析 PLY**：正確讀取 `x y z`（及可選的 `intensity`）；欄位順序需與 header 一致。
2. **Z 去底**：若要比對 `python point_cloud_viewer.py file.ply`，載入後做 `z -= z.Min()`。
3. **體素／抽點**：預設可不做（voxel=0、max_points=2e6）；大檔再考慮與 Python 類似的降採樣策略。
4. **顏色**：無 per-vertex RGB 時，用 **第 3.2 節** 的 HSV 對 Z 著色。
5. **相機**：依 AABB 與對角線調整，避免「看起來變形」其實是視角太遠／太近。
6. **驗證**：抽樣比對數點 `(x,y,z)` 與 Python `read_point_cloud` 讀入後、**同樣去底後** 是否一致。

---

## 9. 程式碼索引（本儲存庫）

| 項目 | 檔案與位置 |
|------|------------|
| Z 去底、體素、抽點、著色條件 | `point_cloud_viewer.py` → `prepare_point_cloud` |
| Z → 彩虹色 | `point_cloud_viewer.py` → `_z_to_rgb_colors` |
| 命令列預設 | `point_cloud_viewer.py` → `main` 內 `_run_open3d_direct` |
| tk 預設 | `point_cloud_viewer.py` → `ViewerApp._build` |
| PLY 匯出格式 | `ljs8/export/point_cloud_export.py` |

---

*文件版本：與儲存庫內 `point_cloud_viewer.py` 行為對齊撰寫，若程式修改請同步更新本文件。*

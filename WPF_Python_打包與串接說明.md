# WPF 串接 Python 打包程式說明（無視窗版本）

本文只保留 **無 UI 面掃描 CLI** 與 **WPF 串接**。  
不含 PyQt 主視窗與點雲檢視器相關內容。

---

## 1. 建置環境

- Windows x64
- Python 與專案依賴已安裝（與 `requirements.txt` 一致；建置工具另見 `requirements-build.txt`）
- 於專案根目錄 `LJS8_Sample_Python` 下執行

```powershell
pip install -r requirements-build.txt
```

---

## 2. 建置產物（單一 onedir）

| Spec 檔 | 指令 | 輸出目錄 | exe 名稱 | 用途 |
|---------|------|----------|----------|------|
| `surface_scan_cli.spec` | `pyinstaller --clean surface_scan_cli.spec` | `dist\Ljs8SurfaceScanCli\` | `Ljs8SurfaceScanCli.exe` | WPF 呼叫之無視窗面掃描流程 |

建置完成後，請將 **`LJS8_IF.dll`（x64）** 複製到 **`Ljs8SurfaceScanCli.exe` 同目錄**。  
部署時請整包複製 `dist\Ljs8SurfaceScanCli\`（含 `_internal`），不要只複製 exe。

---

## 3. CLI 參數

執行檔：`Ljs8SurfaceScanCli.exe`（或開發時 `python surface_scan_cli.py`）

必填：

- `--ip`：掃描器 IP，例如 `192.168.0.10`
- `--out-ply`：輸出 PLY 的完整路徑

常用：

- `--port`：預設 `24691`
- `--device-id`：`0..5`，預設 `0`
- `--position-mode`：`0` | `2` | `3`
- `--height-image-no`、`--profile-no`、`--profile-count`
- `--erase-after-read`
- `--skip-finalize`（除錯）

流程：`Initialize` → `EthernetOpen` → `Trigger` → `GetHeightImageSimpleArray` → 匯出 PLY → `CommunicationClose` → `Finalize`（除非 `--skip-finalize`）。

---

## 4. 回傳格式（stdout JSON）

- 成功：`exit code = 0`，stdout 一行 JSON
- 失敗：`exit code = 1`，stdout 一行 JSON（含 `step` 與 `rc` 或 `error`）

成功範例：

```json
{"ok":true,"plyPath":"D:\\out\\scan.ply","profileCount":100,"xPoints":3200,"pitchX":...,"pitchY":...,"pitchZ":...}
```

失敗範例：

```json
{"ok":false,"step":"EthernetOpen","rc":4096}
```

---

## 5. WPF 串接步驟

1. 部署 `dist\Ljs8SurfaceScanCli\` 到固定路徑。
2. 將 `LJS8_IF.dll` 放在 `Ljs8SurfaceScanCli.exe` 同目錄。
3. `ProcessStartInfo` 設定：
   - `FileName`：`...\Ljs8SurfaceScanCli.exe`
   - `WorkingDirectory`：exe 目錄
   - `Arguments`：例如 `--ip ... --out-ply "..." --port ...`
   - `UseShellExecute = false`
   - `RedirectStandardOutput = true`
4. 等待結束後解析 stdout JSON；成功時使用 `plyPath` 更新你現有 WPF 畫面。
5. UI 更新請走 `Dispatcher` 或 ViewModel 綁定，不要在背景執行緒直接改控制項。

引數範例：

```text
--ip 192.168.0.10 --port 24691 --out-ply "D:\ScanOutput\job001.ply"
```

---

## 6. 疑難排解

| 現象 | 可能原因 |
|------|----------|
| 無法載入 DLL | `LJS8_IF.dll` 未在 exe 同目錄或 PATH；或 x86/x64 不符 |
| stdout 空 | 未設定 `RedirectStandardOutput` 或 `UseShellExecute=true` |
| JSON 解析失敗 | 編碼未用 UTF-8，或 stdout 混入其他輸出 |
| 建置失敗 | 未在專案根執行，或缺建置依賴 |

---

## 7. 變更摘要（本次）

- `ljs8/ljs8_if.py`：凍結後會搜尋 `sys.executable` / `_MEIPASS` 的 DLL 路徑。
- `ljs8/pitch_util.py`：抽出 pitch 換算，降低 CLI 對圖像模組依賴。
- `surface_scan_cli.py`：新增無 UI 面掃描入口。

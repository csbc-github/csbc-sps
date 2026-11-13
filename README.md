# ThreeDScan

角鋼產線 3D 掃描：儀器校正、掃描與 WPF 監看。解法檔為 `ThreeDScan.slnx`（Api／WPF 請用 **x64**）。

## 專案架構

把「業務規則」和「外接實作」切開，讓 Api 與 WPF 共用同一套流程，又不會被 SQL 或 Keyence SDK 綁死。

| 專案 | 角色 |
|---|---|
| `ThreeDScan.Domain` | 介面與業務資料型別（`I*Repository`、`CompensationOffset` 等）。不參考其他專案。 |
| `ThreeDScan.Application` | 用例：一次操作怎麼走（拍攝 → 尋球 → 合併 → 寫補償）。只參考 Domain。 |
| `ThreeDScan.Infrastructure` | 實作：EF／SQL、LJ SDK 量測、掃描器連線。參考 Domain，由 DI 接到介面。 |
| `ThreeDScan.Api` | REST／SignalR 入口，轉呼叫 Application。 |
| `ThreeDScan` | WPF 監看與操作畫面。 |

依賴方向：

```
Api / WPF  →  Application  →  Domain
                ↑
         Infrastructure（實作 Domain 介面，啟動時注入）
```

Application 不能使用 EF 或 `LjDevMeasure`。換資料庫或量測 SDK 時，理論上只改 Infrastructure。

Infrastructure 慣例：資料庫在 `Repositories/`（表對應型別在 `Repositories/Models/`）；量測在 `Measure/`（行為檔名 `*MeasureService`）。對外契約與業務記錄放各層的 `Models/`。

## 測試 API

連線、參數、各支 REST 的步驟與範例見 [docs/說明/API測試說明.md](docs/說明/API測試說明.md)。

身分驗證見 [docs/說明/身分驗證測試說明.md](docs/說明/身分驗證測試說明.md)。

其他說明都在 [docs/](docs/)。

線掃描的光束排序、桌面接點與原始小數點預覽修正，見 [線掃描取點修正](docs/說明/線掃描取點修正.md)。可重現的離線與 HTTP 回歸工具見 [測試說明](tests/ThreeDScan.LineScan.Regression/README.md)。

Encoder 來源只由 JSON 的 `LineScan:EncoderSimulation:Enabled` 切換：`true` 以設定速度模擬往前，`false` 使用真實 Encoder；變更後重啟 API。Development.json 預設開啟，appsettings.json 預設關閉。

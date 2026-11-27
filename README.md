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

面掃描以分區周邊支持估計傾斜地面，12 mm 高度門檻只用來找工件種子；低邊緣須有連續的原始量測及同板面支持才恢復。兩翼辨識使用不同的實測平面，並檢查可見翼寬與共同長向範圍，避免把地面或薄板側邊當成第二翼。原始 XYZ、來源索引與額外屬性保留；缺少實測的部分不插值成點雲。合成場景與現場 PLY 的驗證方式見 [面掃描回歸測試](tests/ThreeDScan.SurfaceScan.Regression/README.md)。

角鋼放在抬高的大板上時，角鋼選件會辨識局部支承平面，從板面噪聲範圍以上的實測點分離物件，每個物件仍須具有 12 mm 高點證據並通過完整兩翼檢查。共同稜線須接近各翼的橫向端點，避免將橫跨稜線的平台當成第二翼。全域地面與原始座標不變；原候選 rank 保留，驗證通過的子物件新增 rank 並記錄 `ParentComponentRank`。Planar 選件保留原板面，分離依據另存於 `segment-processing.json` 的 `localSupportSeparation`。

STEP／GLTF／GLB 建模也採獨立兩翼量測與完整三維基底，長向可朝任意方向，包含接近掃描器 X 軸或直立翼板。材料厚度沿各翼的法向保留，網格、孔洞及封閉流形檢查仍須通過。`materialThicknessMm` 為使用者提供的材料尺寸，並非單面掃描自行量出的板厚；模型重建與絕對座標是否完成現場校正分開標示。建模驗證見 [實體匯出回歸測試](tests/ThreeDScan.SolidExport.Regression/README.md)。

後續拼接模型預設只修補深度**超過 10 mm** 的外緣缺口，保留較小起伏與原有內孔。API 設定為 `SurfaceScan:FillOuterEdges=true`、`SurfaceScan:OuterEdgeRepairMinDepthMm=10`；設為 `false` 可保留原模型外緣，門檻 `0` 則補成完整直線外框。深度依各實體原有模型外框、逐列／逐欄的原始缺格判定；補入部分在模型報告與 GLB metadata 標記為推估幾何。量測 PLY、接縫品質與校正判定維持原值，分離的主要實體或公稱間隙不因外緣修補而接合。

端部定位保留原本 2% 分位及端部量測支持要求；若少數只在相反端局部出現的分箱把結果推離主要端面，才排除這些異常候選並重算端點。正常端點沿用原結果，量測點仍完整保留；擬合區至少 500 點及原接縫品質門檻不變。

噴印／割切監看從目前任務載入首段已計算的前緣三點，並標明段數、來源及座標校正狀態；點雲與 GLB 共用同一批毫米座標。建模採用板厚與左右側實際量測厚度分開顯示。API 實體 GLB 在檔案中仍採標準公尺／Y-up，監看依其明確座標標記還原顯示；舊版未標記 GLB 保持原有慣例。`CameraPose` 多段合併依各段提供的位姿保留位置，段數增加本身不代表工件已沿長向接長。

`POST /api/scan/surface/start` 新任務省略 `stitchMode` 時，預設為 `1`（`NominalButtJoin`）：依拍攝順序將上一段尾端與下一段頭端相接，`nominalGapMm` 預設為 0 mm。端部沿角鋼實測的完整三維長向辨識，各段以剛體變換對接，首段保留相機座標錨定；原始點、屬性與來源索引可回查。公稱間隙代表使用者選定的接續假設，截面相合不代表物理總長已獨立驗證。需要依每段已知相機位姿保留位置時明確傳 `stitchMode: 0`（`CameraPose`）。既有任務維持建立時保存的模式，不因新版預設改變。完成結果回傳實際 `stitchMode` 與 `nominalGapMm`。

請求範例與對接座標說明見 [面掃描首尾拼接](docs/說明/面掃描首尾拼接.md)。

面掃描效能優化保留完整點數、座標精度與品質門檻：原始 PLY 格式化重用掃描網格／16-bit 高度值，讀檔同時計算整檔 SHA-256，平面擬合以精確分位數選取與可重用緩衝減少排序及暫存配置。相同候選平面在選件與支承板分離間重用；拼接與預覽重用本次已計算的點雲。API 預設輸出正式合併 PLY、接縫報告與附件；底層 `StitchProcessingOptions.ExportDiagnosticPointClouds = true` 可額外保留完整轉換點雲與各段對齊 PLY，直接呼叫 `StitchJob.Run` 仍保留原本完整診斷輸出。這些調整不更動掃描器參數，也不透過抽稀量測點加速。

`POST /api/scan/hole` 使用提供的 OneClick 原生影像擷取流程：觸發新影像、檢查影像編號與逐列標頭，單次最多 1000 列，實際 444 列就保留 444 列；省略 `navigatorScanNo` 時沿用控制器目前程式，明確指定才切換，不重寫控制器參數。原始 PLY 採 OneClick 的 local-grid，孔洞辨識沿用原始掃描列與四個獨立平面的預設參數。API 的 ROI 仍以 scanner XYZ 輸入；公開絕對座標先還原 SDK 原點，再套用相機外參，原始 PLY 與疊圖座標保留 local-grid。

孔洞 API 只記錄經兩翼量測確認的角鋼翼板開口；先在完整原始掃描辨識角鋼，再依同一工件的孔緣來源點歸屬與翼板平面配對，排除地面及支承板開口。孔洞偵測仍使用原掃描列，不因裁切工件而新建缺口；未確認可靠角鋼時回傳空記錄與原因，原始掃描保留。`holeScope` 記載選件、保留面及排除數量；單獨呼叫 `HoleAnalysis.AnalyzeFile` 預設仍可做完整來源掃描的參考分析。

結果回傳角鋼上各孔的 `features`、`closedHoleCount`／`partialHoleCount`，以及 `holeCentersUrl`、`holeEdgesUrl`、`holeBoundariesUrl` 三種原始掃描座標 JSON。`featureBoundary` 維持單一選中完整孔的相容契約；不同孔不串成同一條研磨路徑。研磨監看提供逐孔選單、實測孔緣標記及座標表；部分孔緣明列為檢視資料。驗證見 [孔洞參考比對與角鋼範圍測試](tests/ThreeDScan.Holes.Regression/README.md)、[擷取參考比對](tests/ThreeDScan.HoleCapture.Regression/README.md) 與 [孔洞端到端測試](tests/ThreeDScan.HolePipeline.Regression/README.md)。

儀器校正的標準球以原始高度像素取中央球帽，重新定位後做 Huber 穩健擬合。`instrument/start`／`complete` 使用已確認的標準球尺寸約束球心，並以獨立外環及自由球面模型的相容性檢查排除不合格候選；自由估計直徑仍保留，超出原允收門檻時仍標記超差，不以標準直徑冒充實測值。校正球心可用性與自由球徑是否合格分開回傳。Ref 建立仍使用原本自由半徑及球徑門檻。API 與校正預覽共用量測結果；驗證方式見 [球面擬合回歸測試](tests/ThreeDScan.SphereFit.Regression/README.md)。

圖形搜尋固定取得最多 10 個候選，經位置去重及原始高度球面驗證後，才依分數保留請求的球數。避免重複角度或治具候選佔滿前幾名而漏掉真球；球心偏移、球徑及球面品質門檻維持不變。

標準尺寸求球心模式依賴標準球實際尺寸已獨立確認，不能用來驗收未知球的直徑；高噪聲下相近尺寸可能無法由淺球帽區分。

標準球的局部異常高度點不再直接由全點自由擬合品質擋掉；來源完整性、覆蓋率、定位收斂、球頂與上球帽方向仍須通過。中央取樣分成訓練與獨立驗證，外圈只用於驗證，並檢查各方向的球面支持及與自由球面的相容性。原全點品質未通過時，還須逐一移除八個訓練方向確認球心穩定。API／預覽保留 `FreeMeasurementQualityPassed`、`UsedLocalizedOutlierValidation` 與 `CenterSensitivityMm`，明列局部點品質警告，原始高度資料及自由估徑不會被改寫。

兩拍校正的 `AwaitingNextShot` 只代表第一拍已記錄，尚未通過完整校正品質檢查。每拍 `standardBalls.knownX/Y/Z` 必須是該拍標準球的獨立已知球心座標；墊高球台時更新球心高度，若球也水平移動則一併更新 XY。`cameraExpectedPosition` 對應掃描器本體，球台抬高不代表相機位置改變。範例或測試座標不能用來建立有效現場補償，最終仍須通過兩拍的球距、位置殘差及補償一致性門檻。

Encoder 來源只由 JSON 的 `LineScan:EncoderSimulation:Enabled` 切換：`true` 以設定速度模擬往前，`false` 使用真實 Encoder；變更後重啟 API。Development.json 預設開啟，appsettings.json 預設關閉。

/*
  ThreeDScan — 測試機掃描器「資料」種子（在 schema-full-deploy.sql 之後執行）

  重要：TCP/IP 不寫在資料庫，在 appsettings.json：
    - 面掃 LJ：Scanners.{scannerId}.Ip / Port / HighSpeedPort
    - 線掃 SICK：LineScanners.{scannerId}.Ip / Port
    - 預設線掃 ID：LineScan.DefaultScannerId（須與 LineScanners 鍵名一致）

  請先修改本檔 @@ 變數，再執行。
  兩點桌面／滾輪 Z 無法用假值長期跑正式線掃描，請用 API 或從舊庫匯出真實值覆寫。
*/

SET NOCOUNT ON;
GO

/* ── 依環境修改 ── */
DECLARE @ContentsRoot NVARCHAR(500) = N'D:\3DScan\contents';
DECLARE @Now DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @TraceSeed NVARCHAR(64) = N'test-machine-seed';

/* ═══════════════════════════════════════════════════════════════
   1) 面掃描 LJ（SURF-CAM-01 / 02）
   用途：儀器校正 UC-01、Reference 建立；沒有列會 CAL_CFG_001。
   Ref* 若全 0：須先連上 LJ 再呼叫 POST reference-setup，或從舊機匯出 ScannerConfig。
   ═══════════════════════════════════════════════════════════════ */

MERGE dbo.ScannerConfig AS t
USING (VALUES
    (N'SURF-CAM-01', N'D:\3DScan\contents\SURF-CAM-01'),
    (N'SURF-CAM-02', N'D:\3DScan\contents\SURF-CAM-02')
) AS s (ScannerId, ContentsFolder)
ON t.ScannerId = s.ScannerId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        ScannerId, RefPatternX, RefPatternY, RefSphereX, RefSphereY, ContentsFolder,
        UpdatedAt, UpdatedBy, TraceId,
        ExpectedBallDiameter, DiameterTolerance, MaxDiameterError, BallCount, DatumPlaneEnabled
    )
    VALUES (
        s.ScannerId, 0, 0, 0, 0, s.ContentsFolder,
        @Now, N'seed', @TraceSeed,
        25.4, 0.5, 0.5, 3, 0
    );
GO

/* ═══════════════════════════════════════════════════════════════
   2) 線掃描 LINE-LIDAR-01
   - 未有列仍可 GET settings（DTO 空）；line/start 需要 HasCalibration + RollerCalibration。
   - 下列兩點為佔位，距離 >10mm；請改成現場 profile 點選結果，或從舊庫 COPY。
   ═══════════════════════════════════════════════════════════════ */

DECLARE @LineScannerId NVARCHAR(50) = N'LINE-LIDAR-01';
DECLARE @Now2 DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @Trace2 NVARCHAR(64) = N'test-machine-seed';

MERGE dbo.LineScannerSettings AS t
USING (SELECT @LineScannerId AS ScannerId) AS s
ON t.ScannerId = s.ScannerId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        ScannerId,
        CalibrationFirstX, CalibrationFirstY,
        CalibrationSecondX, CalibrationSecondY,
        RoiXMin, RoiXMax, RoiYMin, RoiYMax,
        UpdatedAt, TraceId
    )
    VALUES (
        s.ScannerId,
        -200, 50,
        200, 50,
        NULL, NULL, NULL, NULL,
        @Now2, @Trace2
    );
GO

/*
   RollerCalibration：不在此 INSERT。請在測試機執行：
     POST /api/calibration/line/roller  （或 WPF／校正流程）
   或從舊庫匯入：
     INSERT INTO dbo.RollerCalibration (ScannerId, ZDistance, CalibratedAt, TraceId)
     VALUES (N'LINE-LIDAR-01', <ZDistance>, SYSDATETIMEOFFSET(), N'migrated');
*/

PRINT N'seed-test-machine 完成。請確認 appsettings Scanners / LineScanners IP，並完成滾輪校正。';
GO

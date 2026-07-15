-- ScannerConfig 擴充：每台掃描器 LJ 校正參數（球徑、基準面 ROI）
-- 已有 schema-v1.2 的 DB 執行本檔；參數值請用 seed-scannerconfig.sql 寫入。
-- NULL 欄位 fallback 程式內建預設（LjCalibrationParameters）。

USE ThreeDScan;
GO

IF COL_LENGTH('dbo.ScannerConfig', 'ExpectedBallDiameter') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD ExpectedBallDiameter FLOAT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'DiameterTolerance') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD DiameterTolerance FLOAT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'MaxDiameterError') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD MaxDiameterError FLOAT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'BallCount') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD BallCount INT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'DatumPlaneEnabled') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD DatumPlaneEnabled BIT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'DatumPlaneCenterX') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD DatumPlaneCenterX FLOAT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'DatumPlaneCenterY') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD DatumPlaneCenterY FLOAT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'DatumPlaneWidth') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD DatumPlaneWidth FLOAT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'DatumPlaneHeight') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD DatumPlaneHeight FLOAT NULL;
IF COL_LENGTH('dbo.ScannerConfig', 'DatumPlaneAngle') IS NULL
    ALTER TABLE dbo.ScannerConfig ADD DatumPlaneAngle FLOAT NULL;
GO

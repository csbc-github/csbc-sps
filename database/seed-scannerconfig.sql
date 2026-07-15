-- 種子 Ref 座標與 LJ 校正參數（每台掃描器一筆，與拍攝群組無關）
-- 前置：schema-v1.2.sql、schema-v1.3-scannerconfig-calibration.sql、schema-v1.6-scannerconfig-per-scanner.sql
-- 首次 reference-setup 前執行；成功後 API 會以實際量測結果覆寫 Ref 欄位。
-- ContentsFolder 須與 appsettings Storage:ContentsRoot 一致（掃描器根目錄，非 program 子目錄）。

USE ThreeDScan;
GO

-- ========== SURF-CAM-01（印刷區）==========
MERGE dbo.ScannerConfig AS t
USING (SELECT
    N'SURF-CAM-01'  AS ScannerId,
    159.735         AS RefPatternX,
    388.755         AS RefPatternY,
    159.735         AS RefSphereX,
    388.755         AS RefSphereY,
    N'D:\3DScan\contents\SURF-CAM-01' AS ContentsFolder,
    40.0            AS ExpectedBallDiameter,
    1.0             AS DiameterTolerance,
    5.0             AS MaxDiameterError,
    3               AS BallCount,
    1               AS DatumPlaneEnabled,
    160.0           AS DatumPlaneCenterX,
    120.0           AS DatumPlaneCenterY,
    120.0           AS DatumPlaneWidth,
    80.0            AS DatumPlaneHeight,
    0.0             AS DatumPlaneAngle
) AS s
ON t.ScannerId = s.ScannerId
WHEN MATCHED THEN
    UPDATE SET
        RefPatternX          = s.RefPatternX,
        RefPatternY          = s.RefPatternY,
        RefSphereX           = s.RefSphereX,
        RefSphereY           = s.RefSphereY,
        ContentsFolder       = s.ContentsFolder,
        ExpectedBallDiameter = s.ExpectedBallDiameter,
        DiameterTolerance    = s.DiameterTolerance,
        MaxDiameterError     = s.MaxDiameterError,
        BallCount            = s.BallCount,
        DatumPlaneEnabled    = s.DatumPlaneEnabled,
        DatumPlaneCenterX    = s.DatumPlaneCenterX,
        DatumPlaneCenterY    = s.DatumPlaneCenterY,
        DatumPlaneWidth      = s.DatumPlaneWidth,
        DatumPlaneHeight     = s.DatumPlaneHeight,
        DatumPlaneAngle      = s.DatumPlaneAngle,
        UpdatedAt            = SYSDATETIMEOFFSET(),
        UpdatedBy            = N'seed',
        TraceId              = N'seed-scannerconfig'
WHEN NOT MATCHED THEN
    INSERT (ScannerId, RefPatternX, RefPatternY, RefSphereX, RefSphereY,
            ContentsFolder, ExpectedBallDiameter, DiameterTolerance, MaxDiameterError, BallCount,
            DatumPlaneEnabled, DatumPlaneCenterX, DatumPlaneCenterY, DatumPlaneWidth, DatumPlaneHeight, DatumPlaneAngle,
            UpdatedAt, UpdatedBy, TraceId)
    VALUES (s.ScannerId, s.RefPatternX, s.RefPatternY, s.RefSphereX, s.RefSphereY,
            s.ContentsFolder, s.ExpectedBallDiameter, s.DiameterTolerance, s.MaxDiameterError, s.BallCount,
            s.DatumPlaneEnabled, s.DatumPlaneCenterX, s.DatumPlaneCenterY, s.DatumPlaneWidth, s.DatumPlaneHeight, s.DatumPlaneAngle,
            SYSDATETIMEOFFSET(), N'seed', N'seed-scannerconfig');
GO

-- ========== SURF-CAM-02（切割區）==========
MERGE dbo.ScannerConfig AS t
USING (SELECT
    N'SURF-CAM-02'  AS ScannerId,
    159.735         AS RefPatternX,
    388.755         AS RefPatternY,
    159.735         AS RefSphereX,
    388.755         AS RefSphereY,
    N'D:\3DScan\contents\SURF-CAM-02' AS ContentsFolder,
    24.9            AS ExpectedBallDiameter,
    1.0             AS DiameterTolerance,
    5.0             AS MaxDiameterError,
    3               AS BallCount,
    1               AS DatumPlaneEnabled,
    160.0           AS DatumPlaneCenterX,
    120.0           AS DatumPlaneCenterY,
    120.0           AS DatumPlaneWidth,
    80.0            AS DatumPlaneHeight,
    0.0             AS DatumPlaneAngle
) AS s
ON t.ScannerId = s.ScannerId
WHEN MATCHED THEN
    UPDATE SET
        RefPatternX          = s.RefPatternX,
        RefPatternY          = s.RefPatternY,
        RefSphereX           = s.RefSphereX,
        RefSphereY           = s.RefSphereY,
        ContentsFolder       = s.ContentsFolder,
        ExpectedBallDiameter = s.ExpectedBallDiameter,
        DiameterTolerance    = s.DiameterTolerance,
        MaxDiameterError     = s.MaxDiameterError,
        BallCount            = s.BallCount,
        DatumPlaneEnabled    = s.DatumPlaneEnabled,
        DatumPlaneCenterX    = s.DatumPlaneCenterX,
        DatumPlaneCenterY    = s.DatumPlaneCenterY,
        DatumPlaneWidth      = s.DatumPlaneWidth,
        DatumPlaneHeight     = s.DatumPlaneHeight,
        DatumPlaneAngle      = s.DatumPlaneAngle,
        UpdatedAt            = SYSDATETIMEOFFSET(),
        UpdatedBy            = N'seed',
        TraceId              = N'seed-scannerconfig'
WHEN NOT MATCHED THEN
    INSERT (ScannerId, RefPatternX, RefPatternY, RefSphereX, RefSphereY,
            ContentsFolder, ExpectedBallDiameter, DiameterTolerance, MaxDiameterError, BallCount,
            DatumPlaneEnabled, DatumPlaneCenterX, DatumPlaneCenterY, DatumPlaneWidth, DatumPlaneHeight, DatumPlaneAngle,
            UpdatedAt, UpdatedBy, TraceId)
    VALUES (s.ScannerId, s.RefPatternX, s.RefPatternY, s.RefSphereX, s.RefSphereY,
            s.ContentsFolder, s.ExpectedBallDiameter, s.DiameterTolerance, s.MaxDiameterError, s.BallCount,
            s.DatumPlaneEnabled, s.DatumPlaneCenterX, s.DatumPlaneCenterY, s.DatumPlaneWidth, s.DatumPlaneHeight, s.DatumPlaneAngle,
            SYSDATETIMEOFFSET(), N'seed', N'seed-scannerconfig');
GO

SELECT ScannerId, ExpectedBallDiameter, DatumPlaneEnabled,
       DatumPlaneCenterX, DatumPlaneCenterY, DatumPlaneWidth, DatumPlaneHeight, ContentsFolder
FROM dbo.ScannerConfig
ORDER BY ScannerId;
GO

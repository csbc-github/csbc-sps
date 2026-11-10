-- 線掃描 v2：LineScannerSettings 與 RollerCalibration 精簡（移除 LineA/B/C）
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.LineScannerSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LineScannerSettings
    (
        ScannerId NVARCHAR(50) NOT NULL,
        CalibrationFirstX FLOAT NULL,
        CalibrationFirstY FLOAT NULL,
        CalibrationSecondX FLOAT NULL,
        CalibrationSecondY FLOAT NULL,
        RoiXMin FLOAT NULL,
        RoiXMax FLOAT NULL,
        RoiYMin FLOAT NULL,
        RoiYMax FLOAT NULL,
        UpdatedAt DATETIMEOFFSET NOT NULL CONSTRAINT DF_LineScannerSettings_UpdatedAt DEFAULT SYSDATETIMEOFFSET(),
        TraceId NVARCHAR(64) NOT NULL CONSTRAINT DF_LineScannerSettings_TraceId DEFAULT N'',
        CONSTRAINT PK_LineScannerSettings PRIMARY KEY (ScannerId)
    );
END
GO

IF COL_LENGTH(N'dbo.RollerCalibration', N'LineA') IS NOT NULL
BEGIN
    ALTER TABLE dbo.RollerCalibration DROP COLUMN LineA;
END
GO

IF COL_LENGTH(N'dbo.RollerCalibration', N'LineB') IS NOT NULL
BEGIN
    ALTER TABLE dbo.RollerCalibration DROP COLUMN LineB;
END
GO

IF COL_LENGTH(N'dbo.RollerCalibration', N'LineC') IS NOT NULL
BEGIN
    ALTER TABLE dbo.RollerCalibration DROP COLUMN LineC;
END
GO

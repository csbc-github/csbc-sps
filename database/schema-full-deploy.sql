/*
  ThreeDScan — 測試機完整資料庫結構（SQL Server）
  對齊 ThreeDScan.Infrastructure/Repositories/ThreeDScanDbContext.cs
  含線掃描 v2：LineScannerSettings、RollerCalibration（無 LineA/B/C）

  使用方式：
  1) 先建立空資料庫（例：ThreeDScan），並讓 appsettings ConnectionStrings 指向該庫
  2) 在 SSMS / sqlcmd 對該庫執行本腳本（可重複執行，僅補缺表／缺欄）
  3) 接著執行同目錄 seed-test-machine.sql（ScannerConfig／LineScannerSettings 種子）
     — IP 仍須改 appsettings；滾輪校正須 API 或手動 INSERT RollerCalibration

  -- 可選：建立資料庫（需權限；若已存在請略過）
  -- CREATE DATABASE ThreeDScan;
  -- GO
  -- USE ThreeDScan;
  -- GO
*/

SET NOCOUNT ON;
GO

/* ── 核心（面掃／校正）── */

IF OBJECT_ID(N'dbo.CompensationHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompensationHistory
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        ScannerId NVARCHAR(50) NOT NULL,
        OffsetX FLOAT NOT NULL,
        OffsetY FLOAT NOT NULL,
        OffsetZ FLOAT NOT NULL,
        Rmse FLOAT NOT NULL,
        MachineArea NVARCHAR(20) NOT NULL,
        CalibratedAt DATETIMEOFFSET NOT NULL,
        Operator NVARCHAR(100) NULL,
        TraceId NVARCHAR(64) NOT NULL,
        CONSTRAINT PK_CompensationHistory PRIMARY KEY (Id)
    );
    CREATE INDEX IX_CompensationHistory_ScannerId_CalibratedAt ON dbo.CompensationHistory (ScannerId, CalibratedAt);
END
GO

IF OBJECT_ID(N'dbo.ScannerConfig', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScannerConfig
    (
        ScannerId NVARCHAR(50) NOT NULL,
        RefPatternX FLOAT NOT NULL,
        RefPatternY FLOAT NOT NULL,
        RefSphereX FLOAT NOT NULL,
        RefSphereY FLOAT NOT NULL,
        ContentsFolder NVARCHAR(500) NOT NULL,
        UpdatedAt DATETIMEOFFSET NOT NULL,
        UpdatedBy NVARCHAR(100) NULL,
        TraceId NVARCHAR(64) NOT NULL,
        ExpectedBallDiameter FLOAT NULL,
        DiameterTolerance FLOAT NULL,
        MaxDiameterError FLOAT NULL,
        BallCount INT NULL,
        DatumPlaneEnabled BIT NULL,
        DatumPlaneCenterX FLOAT NULL,
        DatumPlaneCenterY FLOAT NULL,
        DatumPlaneWidth FLOAT NULL,
        DatumPlaneHeight FLOAT NULL,
        DatumPlaneAngle FLOAT NULL,
        CONSTRAINT PK_ScannerConfig PRIMARY KEY (ScannerId)
    );
END
GO

IF OBJECT_ID(N'dbo.ScannerProgramSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScannerProgramSettings
    (
        ScannerId NVARCHAR(50) NOT NULL,
        ProgramNo TINYINT NOT NULL,
        SettingsBlob VARBINARY(MAX) NOT NULL,
        SettingsHash VARBINARY(MAX) NOT NULL,
        UpdatedAt DATETIMEOFFSET NOT NULL,
        UpdatedBy NVARCHAR(100) NULL,
        TraceId NVARCHAR(64) NOT NULL,
        Source NVARCHAR(20) NOT NULL,
        CONSTRAINT PK_ScannerProgramSettings PRIMARY KEY (ScannerId, ProgramNo)
    );
END
GO

IF OBJECT_ID(N'dbo.ScannerProgramSettingsHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScannerProgramSettingsHistory
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        ScannerId NVARCHAR(50) NOT NULL,
        ProgramNo TINYINT NOT NULL,
        SettingsBlob VARBINARY(MAX) NOT NULL,
        SettingsHash VARBINARY(MAX) NOT NULL,
        SavedAt DATETIMEOFFSET NOT NULL,
        UpdatedBy NVARCHAR(100) NULL,
        TraceId NVARCHAR(64) NOT NULL,
        Source NVARCHAR(20) NOT NULL,
        CONSTRAINT PK_ScannerProgramSettingsHistory PRIMARY KEY (Id)
    );
    CREATE INDEX IX_ScannerProgramSettingsHistory_ScannerId_ProgramNo_SavedAt
        ON dbo.ScannerProgramSettingsHistory (ScannerId, ProgramNo, SavedAt);
END
GO

IF OBJECT_ID(N'dbo.ScannerProgramGroupName', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScannerProgramGroupName
    (
        ScannerId NVARCHAR(50) NOT NULL,
        ProgramNo TINYINT NOT NULL,
        DisplayName NVARCHAR(100) NOT NULL,
        UpdatedAt DATETIMEOFFSET NOT NULL,
        UpdatedBy NVARCHAR(100) NULL,
        CONSTRAINT PK_ScannerProgramGroupName PRIMARY KEY (ScannerId, ProgramNo)
    );
END
GO

IF OBJECT_ID(N'dbo.OperationLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OperationLog
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        Timestamp DATETIMEOFFSET NOT NULL,
        TraceId NVARCHAR(64) NOT NULL,
        ClientId NVARCHAR(100) NOT NULL,
        Operator NVARCHAR(100) NULL,
        Action NVARCHAR(50) NOT NULL,
        Target NVARCHAR(100) NULL,
        Result NVARCHAR(10) NOT NULL,
        ErrorCode NVARCHAR(30) NULL,
        Detail NVARCHAR(MAX) NULL,
        CONSTRAINT PK_OperationLog PRIMARY KEY (Id)
    );
    CREATE INDEX IX_OperationLog_Timestamp ON dbo.OperationLog (Timestamp);
    CREATE INDEX IX_OperationLog_TraceId ON dbo.OperationLog (TraceId);
    CREATE INDEX IX_OperationLog_Action_Timestamp ON dbo.OperationLog (Action, Timestamp);
END
GO

IF OBJECT_ID(N'dbo.InstrumentCalibrationTask', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InstrumentCalibrationTask
    (
        TaskId NVARCHAR(64) NOT NULL,
        ScannerId NVARCHAR(50) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        CreatedAt DATETIMEOFFSET NOT NULL,
        UpdatedAt DATETIMEOFFSET NOT NULL,
        ExpiresAt DATETIMEOFFSET NOT NULL,
        ExpectedX FLOAT NOT NULL,
        ExpectedY FLOAT NOT NULL,
        ExpectedZ FLOAT NOT NULL,
        BallCount INT NOT NULL,
        ScannerToFloorZ FLOAT NOT NULL,
        ActualX FLOAT NOT NULL,
        ActualY FLOAT NOT NULL,
        Rmse FLOAT NOT NULL,
        FrameJson NVARCHAR(MAX) NULL,
        TraceIdSeq1 NVARCHAR(64) NULL,
        CONSTRAINT PK_InstrumentCalibrationTask PRIMARY KEY (TaskId)
    );
    CREATE INDEX IX_InstrumentCalibrationTask_ScannerId_Status ON dbo.InstrumentCalibrationTask (ScannerId, Status);
    CREATE INDEX IX_InstrumentCalibrationTask_ExpiresAt ON dbo.InstrumentCalibrationTask (ExpiresAt);
END
GO

/* ── v1.8 / v1.9 / v1.11 增量（與 database/schema-v1.*.sql 一致）── */

IF OBJECT_ID(N'dbo.JobThickness', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobThickness
    (
        JobId NVARCHAR(64) NOT NULL,
        ThicknessA FLOAT NOT NULL,
        ThicknessB FLOAT NOT NULL,
        ThicknessAOnLowFlange BIT NOT NULL,
        ScannerId NVARCHAR(50) NOT NULL,
        MeasuredAt DATETIMEOFFSET NOT NULL,
        TraceId NVARCHAR(64) NOT NULL,
        CONSTRAINT PK_JobThickness PRIMARY KEY (JobId)
    );
END
GO

IF OBJECT_ID(N'dbo.ScanCallLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScanCallLog
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        Timestamp DATETIMEOFFSET NOT NULL,
        TraceId NVARCHAR(64) NOT NULL,
        Action NVARCHAR(50) NOT NULL,
        ScannerId NVARCHAR(50) NULL,
        JobId NVARCHAR(64) NULL,
        TaskId NVARCHAR(64) NULL,
        Seq INT NULL,
        Success BIT NOT NULL,
        ErrorCode NVARCHAR(30) NULL,
        ErrorMessage NVARCHAR(500) NULL,
        RequestJson NVARCHAR(MAX) NULL,
        ResponseJson NVARCHAR(MAX) NULL,
        PointCloudPath NVARCHAR(1000) NULL,
        PointCloudSha256 NVARCHAR(64) NULL,
        ZipPath NVARCHAR(1000) NULL,
        ZipSha256 NVARCHAR(64) NULL,
        CONSTRAINT PK_ScanCallLog PRIMARY KEY (Id)
    );
    CREATE INDEX IX_ScanCallLog_Timestamp ON dbo.ScanCallLog (Timestamp);
    CREATE INDEX IX_ScanCallLog_TraceId ON dbo.ScanCallLog (TraceId);
    CREATE INDEX IX_ScanCallLog_JobId_Timestamp ON dbo.ScanCallLog (JobId, Timestamp);
    CREATE INDEX IX_ScanCallLog_Action_Timestamp ON dbo.ScanCallLog (Action, Timestamp);
    CREATE INDEX IX_ScanCallLog_TaskId ON dbo.ScanCallLog (TaskId);
END
GO

IF OBJECT_ID(N'dbo.ScanTask', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScanTask
    (
        TaskId NVARCHAR(100) NOT NULL,
        Kind NVARCHAR(30) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        Phase NVARCHAR(40) NOT NULL,
        ScannerId NVARCHAR(50) NOT NULL,
        JobId NVARCHAR(64) NULL,
        DocumentJson NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIMEOFFSET NOT NULL,
        UpdatedAt DATETIMEOFFSET NOT NULL,
        CallbackUrl NVARCHAR(1000) NULL,
        CallbackDelivered BIT NOT NULL CONSTRAINT DF_ScanTask_CallbackDelivered DEFAULT (0),
        CallbackAttempts INT NOT NULL CONSTRAINT DF_ScanTask_CallbackAttempts DEFAULT (0),
        CallbackLastError NVARCHAR(500) NULL,
        CallbackNextAttemptAt DATETIMEOFFSET NULL,
        CONSTRAINT PK_ScanTask PRIMARY KEY (TaskId)
    );
    CREATE INDEX IX_ScanTask_Status_UpdatedAt ON dbo.ScanTask (Status, UpdatedAt);
    CREATE INDEX IX_ScanTask_Kind_Status ON dbo.ScanTask (Kind, Status);
    CREATE INDEX IX_ScanTask_CallbackNextAttemptAt ON dbo.ScanTask (CallbackNextAttemptAt);
END
GO

IF OBJECT_ID(N'dbo.SystemConfig', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemConfig
    (
        ConfigKey NVARCHAR(100) NOT NULL,
        ConfigValue NVARCHAR(MAX) NOT NULL,
        UpdatedAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_SystemConfig PRIMARY KEY (ConfigKey)
    );
END
GO

/* ── 線掃描：滾輪 + v2 設定 ── */

IF OBJECT_ID(N'dbo.RollerCalibration', N'U') IS NULL
BEGIN
  -- v2：僅 ZDistance（不建立 LineA/B/C）
    CREATE TABLE dbo.RollerCalibration
    (
        ScannerId NVARCHAR(50) NOT NULL,
        ZDistance FLOAT NOT NULL,
        CalibratedAt DATETIMEOFFSET NOT NULL,
        TraceId NVARCHAR(64) NOT NULL,
        CONSTRAINT PK_RollerCalibration PRIMARY KEY (ScannerId)
    );
END
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

/* 自舊 v1.10 升級：移除 LineA/B/C（與 Scripts/line-scan-v2-schema.sql 相同） */
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

PRINT N'ThreeDScan schema-full-deploy 完成。';
GO

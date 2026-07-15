-- 掃描器程式設定備份（Save 成功後同步）
-- 已有 schema-v1.2 / v1.3 的 DB 執行本檔。

USE ThreeDScan;
GO

IF OBJECT_ID(N'dbo.ScannerProgramSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScannerProgramSettings (
        ScannerId     NVARCHAR(50)  NOT NULL,
        ProgramNo     TINYINT       NOT NULL,
        SettingsBlob  VARBINARY(8000) NOT NULL,
        SettingsHash  VARBINARY(32) NOT NULL,
        UpdatedAt     DATETIMEOFFSET NOT NULL,
        UpdatedBy     NVARCHAR(100) NULL,
        TraceId       NVARCHAR(64)  NOT NULL,
        Source        NVARCHAR(20)  NOT NULL,
        CONSTRAINT PK_ScannerProgramSettings PRIMARY KEY (ScannerId, ProgramNo)
    );
END
GO

IF OBJECT_ID(N'dbo.ScannerProgramSettingsHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScannerProgramSettingsHistory (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScannerProgramSettingsHistory PRIMARY KEY,
        ScannerId     NVARCHAR(50)  NOT NULL,
        ProgramNo     TINYINT       NOT NULL,
        SettingsBlob  VARBINARY(8000) NOT NULL,
        SettingsHash  VARBINARY(32) NOT NULL,
        SavedAt       DATETIMEOFFSET NOT NULL,
        UpdatedBy     NVARCHAR(100) NULL,
        TraceId       NVARCHAR(64)  NOT NULL,
        Source        NVARCHAR(20)  NOT NULL
    );
    CREATE INDEX IX_ScannerProgramSettingsHistory_Scanner_Program_SavedAt
        ON dbo.ScannerProgramSettingsHistory (ScannerId, ProgramNo, SavedAt DESC);
END
GO

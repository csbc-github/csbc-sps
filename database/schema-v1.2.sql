-- ThreeDScan — SD V1.2 + ScannerConfig (multi-scanner)
-- 在目標資料庫執行（與 appsettings ConnectionStrings 的 Database 一致）

USE ThreeDScan;
GO

-- SD V1.2 §9.2
IF OBJECT_ID(N'dbo.CompensationHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompensationHistory (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompensationHistory PRIMARY KEY,
        ScannerId     NVARCHAR(50) NOT NULL,
        OffsetX       FLOAT NOT NULL,
        OffsetY       FLOAT NOT NULL,
        OffsetZ       FLOAT NOT NULL,
        Rmse          FLOAT NOT NULL,
        MachineArea   NVARCHAR(20) NOT NULL,
        CalibratedAt  DATETIMEOFFSET NOT NULL,
        Operator      NVARCHAR(100) NULL,
        TraceId       NVARCHAR(64) NOT NULL
    );
    CREATE INDEX IX_CompensationHistory_Scanner_CalibratedAt
        ON dbo.CompensationHistory (ScannerId, CalibratedAt DESC);
    CREATE INDEX IX_CompensationHistory_TraceId
        ON dbo.CompensationHistory (TraceId);
END
GO

IF OBJECT_ID(N'dbo.OperationLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OperationLog (
        Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OperationLog PRIMARY KEY,
        Timestamp   DATETIMEOFFSET NOT NULL,
        TraceId     NVARCHAR(64) NOT NULL,
        ClientId    NVARCHAR(100) NOT NULL,
        Operator    NVARCHAR(100) NULL,
        Action      NVARCHAR(50) NOT NULL,
        Target      NVARCHAR(100) NULL,
        Result      NVARCHAR(10) NOT NULL,
        ErrorCode   NVARCHAR(30) NULL,
        Detail      NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_OperationLog_Timestamp ON dbo.OperationLog (Timestamp);
    CREATE INDEX IX_OperationLog_TraceId ON dbo.OperationLog (TraceId);
    CREATE INDEX IX_OperationLog_Action_Timestamp ON dbo.OperationLog (Action, Timestamp);
END
GO

IF OBJECT_ID(N'dbo.SystemLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemLog (
        Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemLog PRIMARY KEY,
        Timestamp     DATETIMEOFFSET NOT NULL,
        TraceId       NVARCHAR(64) NULL,
        Level         NVARCHAR(15) NOT NULL,
        SourceContext NVARCHAR(200) NULL,
        Message       NVARCHAR(MAX) NOT NULL,
        Exception     NVARCHAR(MAX) NULL,
        Properties    NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_SystemLog_Timestamp ON dbo.SystemLog (Timestamp);
    CREATE INDEX IX_SystemLog_TraceId ON dbo.SystemLog (TraceId);
    CREATE INDEX IX_SystemLog_Level_Timestamp ON dbo.SystemLog (Level, Timestamp);
END
GO

-- Extension: per-scanner LJ measure reference (replaces App.config Ref* persistence)
IF OBJECT_ID(N'dbo.ScannerConfig', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScannerConfig (
        ScannerId      NVARCHAR(50) NOT NULL,
        RefPatternX    FLOAT NOT NULL,
        RefPatternY    FLOAT NOT NULL,
        RefSphereX     FLOAT NOT NULL,
        RefSphereY     FLOAT NOT NULL,
        ContentsFolder NVARCHAR(500) NOT NULL,
        UpdatedAt      DATETIMEOFFSET NOT NULL,
        UpdatedBy      NVARCHAR(100) NULL,
        TraceId        NVARCHAR(64) NOT NULL,
        CONSTRAINT PK_ScannerConfig PRIMARY KEY (ScannerId)
    );
END
GO

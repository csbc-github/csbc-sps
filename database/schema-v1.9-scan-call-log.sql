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

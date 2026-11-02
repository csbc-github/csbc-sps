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

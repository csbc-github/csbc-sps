-- UC-01 兩次拍攝任務暫存（Seq=1）
-- 前置：schema-v1.2 起已建立 ThreeDScan 資料庫
-- 可重複執行

USE ThreeDScan;
GO

IF OBJECT_ID(N'dbo.InstrumentCalibrationTask', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InstrumentCalibrationTask (
        TaskId              NVARCHAR(64)       NOT NULL,
        ScannerId           NVARCHAR(50)       NOT NULL,
        Status              NVARCHAR(20)       NOT NULL,
        CreatedAt           DATETIMEOFFSET(7)  NOT NULL,
        UpdatedAt           DATETIMEOFFSET(7)  NOT NULL,
        ExpiresAt           DATETIMEOFFSET(7)  NOT NULL,
        ExpectedX           FLOAT              NOT NULL,
        ExpectedY           FLOAT              NOT NULL,
        ExpectedZ           FLOAT              NOT NULL,
        BallCount           INT                NOT NULL,
        ScannerToFloorZ     FLOAT              NOT NULL,
        ActualX             FLOAT              NOT NULL,
        ActualY             FLOAT              NOT NULL,
        Rmse                FLOAT              NOT NULL,
        FrameJson           NVARCHAR(MAX)      NULL,
        TraceIdSeq1         NVARCHAR(64)       NULL,
        CONSTRAINT PK_InstrumentCalibrationTask PRIMARY KEY (TaskId)
    );

    CREATE INDEX IX_InstrumentCalibrationTask_ScannerId_Status
        ON dbo.InstrumentCalibrationTask (ScannerId, Status);

    CREATE INDEX IX_InstrumentCalibrationTask_ExpiresAt
        ON dbo.InstrumentCalibrationTask (ExpiresAt);
END
GO

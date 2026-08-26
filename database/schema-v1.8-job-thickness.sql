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

IF OBJECT_ID(N'dbo.RollerCalibration', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RollerCalibration
    (
        ScannerId NVARCHAR(50) NOT NULL,
        ZDistance FLOAT NOT NULL,
        LineA FLOAT NOT NULL,
        LineB FLOAT NOT NULL,
        LineC FLOAT NOT NULL,
        CalibratedAt DATETIMEOFFSET NOT NULL,
        TraceId NVARCHAR(64) NOT NULL,
        CONSTRAINT PK_RollerCalibration PRIMARY KEY (ScannerId)
    );
END
GO

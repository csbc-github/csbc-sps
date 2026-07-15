-- 僅補建 dbo.ScannerConfig（CAL_SYS_001：無效的物件名稱 'ScannerConfig'）
-- 在 SSMS 連到與 appsettings 相同的資料庫後執行

USE ThreeDScan;
GO

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
    PRINT 'Created dbo.ScannerConfig';
END
ELSE
    PRINT 'dbo.ScannerConfig already exists';
GO

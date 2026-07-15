-- 掃描器程式群組顯示名稱（每台 16 組，ProgramNo 0～15）
-- 已有 schema-v1.4 的 DB 執行本檔。

USE ThreeDScan;
GO

IF OBJECT_ID(N'dbo.ScannerProgramGroupName', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScannerProgramGroupName (
        ScannerId     NVARCHAR(50)  NOT NULL,
        ProgramNo     TINYINT       NOT NULL,
        DisplayName   NVARCHAR(100) NOT NULL,
        UpdatedAt     DATETIMEOFFSET NOT NULL,
        UpdatedBy     NVARCHAR(100) NULL,
        CONSTRAINT PK_ScannerProgramGroupName PRIMARY KEY (ScannerId, ProgramNo)
    );
END
GO

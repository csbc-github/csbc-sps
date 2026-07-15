-- ScannerConfig 改為每台掃描器一筆（Ref 與拍攝群組解耦）
-- 前置：schema-v1.2、schema-v1.3 已執行
-- 若曾依 ProgramNo 建立多筆，保留 ProgramNo 最小者（通常為 0）
-- 可重複執行（已遷移則跳過 DROP / ADD 步驟）

USE ThreeDScan;
GO

IF OBJECT_ID(N'dbo.ScannerConfig', N'U') IS NULL
BEGIN
    RAISERROR(N'dbo.ScannerConfig 不存在，請先執行 schema-v1.2.sql', 16, 1);
    RETURN;
END
GO

-- 刪除同一 ScannerId 的重複列，保留 ProgramNo 最小者（欄位仍存在時才執行）
IF COL_LENGTH('dbo.ScannerConfig', 'ProgramNo') IS NOT NULL
BEGIN
    DELETE sc
    FROM dbo.ScannerConfig sc
    INNER JOIN (
        SELECT ScannerId, ProgramNo,
               ROW_NUMBER() OVER (PARTITION BY ScannerId ORDER BY ProgramNo ASC) AS rn
        FROM dbo.ScannerConfig
    ) ranked ON sc.ScannerId = ranked.ScannerId AND sc.ProgramNo = ranked.ProgramNo
    WHERE ranked.rn > 1;
END
GO

-- 先移除主鍵（ProgramNo 為 PK 成員時不可直接 DROP COLUMN）
IF EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ScannerConfig')
      AND type = 'PK')
BEGIN
    DECLARE @pkName SYSNAME;
    SELECT @pkName = name
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ScannerConfig')
      AND type = 'PK';

    DECLARE @dropPkSql NVARCHAR(300) =
        N'ALTER TABLE dbo.ScannerConfig DROP CONSTRAINT ' + QUOTENAME(@pkName);
    EXEC sp_executesql @dropPkSql;
END
GO

IF COL_LENGTH('dbo.ScannerConfig', 'ProgramNo') IS NOT NULL
    ALTER TABLE dbo.ScannerConfig DROP COLUMN ProgramNo;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ScannerConfig')
      AND type = 'PK')
    ALTER TABLE dbo.ScannerConfig ADD CONSTRAINT PK_ScannerConfig PRIMARY KEY (ScannerId);
GO

SELECT ScannerId, RefPatternX, RefPatternY, ContentsFolder, UpdatedAt
FROM dbo.ScannerConfig
ORDER BY ScannerId;
GO

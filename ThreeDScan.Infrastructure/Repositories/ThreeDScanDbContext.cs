using Microsoft.EntityFrameworkCore;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>EF Core 資料庫內容。</summary>
public sealed class ThreeDScanDbContext : DbContext
{
    /// <summary>建立資料庫內容。</summary>
    public ThreeDScanDbContext(DbContextOptions<ThreeDScanDbContext> options) : base(options) { }

    /// <summary>儀器校正補償歷程。</summary>
    public DbSet<CompensationHistoryEntity> CompensationHistory
    {
        get
        {
            return Set<CompensationHistoryEntity>();
        }
    }
    /// <summary>掃描器 Ref／量測參數。</summary>
    public DbSet<ScannerConfigEntity> ScannerConfigs
    {
        get
        {
            return Set<ScannerConfigEntity>();
        }
    }
    /// <summary>目前生效的拍攝參數。</summary>
    public DbSet<ScannerProgramSettingsEntity> ScannerProgramSettings
    {
        get
        {
            return Set<ScannerProgramSettingsEntity>();
        }
    }
    /// <summary>拍攝參數歷程。</summary>
    public DbSet<ScannerProgramSettingsHistoryEntity> ScannerProgramSettingsHistory
    {
        get
        {
            return Set<ScannerProgramSettingsHistoryEntity>();
        }
    }
    /// <summary>拍攝群組顯示名稱。</summary>
    public DbSet<ScannerProgramGroupNameEntity> ScannerProgramGroupNames
    {
        get
        {
            return Set<ScannerProgramGroupNameEntity>();
        }
    }
    /// <summary>操作稽核紀錄。</summary>
    public DbSet<OperationLogEntity> OperationLogs
    {
        get
        {
            return Set<OperationLogEntity>();
        }
    }
    /// <summary>儀器校正兩次拍攝任務。</summary>
    public DbSet<InstrumentCalibrationTaskEntity> InstrumentCalibrationTasks
    {
        get
        {
            return Set<InstrumentCalibrationTaskEntity>();
        }
    }

    /// <summary>工單翼板厚度。</summary>
    public DbSet<JobThicknessEntity> JobThickness
    {
        get
        {
            return Set<JobThicknessEntity>();
        }
    }

    /// <summary>掃描 API 呼叫紀錄。</summary>
    public DbSet<ScanCallLogEntity> ScanCallLogs
    {
        get
        {
            return Set<ScanCallLogEntity>();
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompensationHistoryEntity>(e =>
        {
            e.ToTable("CompensationHistory");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScannerId).HasMaxLength(50);
            e.Property(x => x.MachineArea).HasMaxLength(20);
            e.Property(x => x.Operator).HasMaxLength(100);
            e.Property(x => x.TraceId).HasMaxLength(64);
            e.HasIndex(x => new { x.ScannerId, x.CalibratedAt });
        });

        modelBuilder.Entity<ScannerConfigEntity>(e =>
        {
            e.ToTable("ScannerConfig");
            e.HasKey(x => x.ScannerId);
            e.Property(x => x.ScannerId).HasMaxLength(50);
            e.Property(x => x.ContentsFolder).HasMaxLength(500);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.TraceId).HasMaxLength(64);
        });

        modelBuilder.Entity<ScannerProgramSettingsEntity>(e =>
        {
            e.ToTable("ScannerProgramSettings");
            e.HasKey(x => new { x.ScannerId, x.ProgramNo });
            e.Property(x => x.ScannerId).HasMaxLength(50);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.TraceId).HasMaxLength(64);
            e.Property(x => x.Source).HasMaxLength(20);
        });

        modelBuilder.Entity<ScannerProgramSettingsHistoryEntity>(e =>
        {
            e.ToTable("ScannerProgramSettingsHistory");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScannerId).HasMaxLength(50);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.TraceId).HasMaxLength(64);
            e.Property(x => x.Source).HasMaxLength(20);
            e.HasIndex(x => new { x.ScannerId, x.ProgramNo, x.SavedAt });
        });

        modelBuilder.Entity<ScannerProgramGroupNameEntity>(e =>
        {
            e.ToTable("ScannerProgramGroupName");
            e.HasKey(x => new { x.ScannerId, x.ProgramNo });
            e.Property(x => x.ScannerId).HasMaxLength(50);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });

        modelBuilder.Entity<OperationLogEntity>(e =>
        {
            e.ToTable("OperationLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.TraceId).HasMaxLength(64);
            e.Property(x => x.ClientId).HasMaxLength(100);
            e.Property(x => x.Operator).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.Target).HasMaxLength(100);
            e.Property(x => x.Result).HasMaxLength(10);
            e.Property(x => x.ErrorCode).HasMaxLength(30);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.TraceId);
            e.HasIndex(x => new { x.Action, x.Timestamp });
        });

        modelBuilder.Entity<InstrumentCalibrationTaskEntity>(e =>
        {
            e.ToTable("InstrumentCalibrationTask");
            e.HasKey(x => x.TaskId);
            e.Property(x => x.TaskId).HasMaxLength(64);
            e.Property(x => x.ScannerId).HasMaxLength(50);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.TraceIdSeq1).HasMaxLength(64);
            e.HasIndex(x => new { x.ScannerId, x.Status });
            e.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<JobThicknessEntity>(e =>
        {
            e.ToTable("JobThickness");
            e.HasKey(x => x.JobId);
            e.Property(x => x.JobId).HasMaxLength(64);
            e.Property(x => x.ScannerId).HasMaxLength(50);
            e.Property(x => x.TraceId).HasMaxLength(64);
        });

        modelBuilder.Entity<ScanCallLogEntity>(e =>
        {
            e.ToTable("ScanCallLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.TraceId).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.ScannerId).HasMaxLength(50);
            e.Property(x => x.JobId).HasMaxLength(64);
            e.Property(x => x.TaskId).HasMaxLength(64);
            e.Property(x => x.ErrorCode).HasMaxLength(30);
            e.Property(x => x.ErrorMessage).HasMaxLength(500);
            e.Property(x => x.PointCloudPath).HasMaxLength(1000);
            e.Property(x => x.PointCloudSha256).HasMaxLength(64);
            e.Property(x => x.ZipPath).HasMaxLength(1000);
            e.Property(x => x.ZipSha256).HasMaxLength(64);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.TraceId);
            e.HasIndex(x => new { x.JobId, x.Timestamp });
            e.HasIndex(x => new { x.Action, x.Timestamp });
            e.HasIndex(x => x.TaskId);
        });
    }
}

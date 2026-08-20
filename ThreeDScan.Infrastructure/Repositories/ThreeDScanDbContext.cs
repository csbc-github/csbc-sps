using Microsoft.EntityFrameworkCore;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

public sealed class ThreeDScanDbContext : DbContext
{
    public ThreeDScanDbContext(DbContextOptions<ThreeDScanDbContext> options) : base(options) { }

    public DbSet<CompensationHistoryEntity> CompensationHistory
    {
        get
        {
            return Set<CompensationHistoryEntity>();
        }
    }
    public DbSet<ScannerConfigEntity> ScannerConfigs
    {
        get
        {
            return Set<ScannerConfigEntity>();
        }
    }
    public DbSet<ScannerProgramSettingsEntity> ScannerProgramSettings
    {
        get
        {
            return Set<ScannerProgramSettingsEntity>();
        }
    }
    public DbSet<ScannerProgramSettingsHistoryEntity> ScannerProgramSettingsHistory
    {
        get
        {
            return Set<ScannerProgramSettingsHistoryEntity>();
        }
    }
    public DbSet<ScannerProgramGroupNameEntity> ScannerProgramGroupNames
    {
        get
        {
            return Set<ScannerProgramGroupNameEntity>();
        }
    }
    public DbSet<OperationLogEntity> OperationLogs
    {
        get
        {
            return Set<OperationLogEntity>();
        }
    }
    public DbSet<InstrumentCalibrationTaskEntity> InstrumentCalibrationTasks
    {
        get
        {
            return Set<InstrumentCalibrationTaskEntity>();
        }
    }

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
    }
}

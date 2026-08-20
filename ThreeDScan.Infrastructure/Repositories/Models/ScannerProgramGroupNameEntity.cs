namespace ThreeDScan.Infrastructure.Repositories.Models;

public sealed class ScannerProgramGroupNameEntity
{
    public string ScannerId { get; set; } = "";
    public byte ProgramNo { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

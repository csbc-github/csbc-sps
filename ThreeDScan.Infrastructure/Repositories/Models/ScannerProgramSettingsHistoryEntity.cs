namespace ThreeDScan.Infrastructure.Repositories.Models;

public sealed class ScannerProgramSettingsHistoryEntity
{
    public long Id { get; set; }
    public string ScannerId { get; set; } = "";
    public byte ProgramNo { get; set; }
    public byte[] SettingsBlob { get; set; } = Array.Empty<byte>();
    public byte[] SettingsHash { get; set; } = Array.Empty<byte>();
    public DateTimeOffset SavedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public string TraceId { get; set; } = "";
    public string Source { get; set; } = "";
}

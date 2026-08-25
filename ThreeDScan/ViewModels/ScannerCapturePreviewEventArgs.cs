namespace ThreeDScan.ViewModels;

/// <summary>測試掃描完成後傳遞給 3D 預覽視窗的影像路徑。</summary>
public sealed class ScannerCapturePreviewEventArgs : EventArgs
{
    public required string HeightImagePath { get; init; }
    public required string GrayImagePath { get; init; }
    public string? PlyPath { get; init; }
    public int PlyPointCount { get; init; }
}

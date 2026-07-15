namespace ThreeDScan.Domain.Abstractions;

/// <summary>依 ScannerId 取得面掃描器擷取服務實例。</summary>
public interface IScannerCaptureFactory
{
    /// <summary>取得已註冊掃描器的擷取服務；未知 ScannerId 拋出例外。</summary>
    ISurfaceScannerCapture GetCapture(string scannerId);
}

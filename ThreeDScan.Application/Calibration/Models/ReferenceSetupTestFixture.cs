namespace ThreeDScan.Application.Calibration.Models;

/// <summary>測試模式 Ref 建立固定回應（不連掃描器、不覆寫 ScannerConfig）。</summary>
internal static class ReferenceSetupTestFixture
{
    public static ReferenceSetupData Create(string scannerId, string traceId, string contentsFolder)
    {
        return new ReferenceSetupData
        {
            ScannerId = scannerId,
            TraceId = traceId,
            ProgramNo = 1,
            RefPatternX = 270.122,
            RefPatternY = 286.384,
            DiscoveredDiameter = 25.264,
            ContentsFolder = contentsFolder,
            Message = "測試模式：未連掃描器，回傳固定範例（未寫入 ScannerConfig）。",
        };
    }
}

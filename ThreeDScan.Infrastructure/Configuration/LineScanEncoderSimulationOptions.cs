namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>由 JSON 切換線掃描 Encoder 模擬；不依編譯模式或執行環境判斷。</summary>
public sealed class LineScanEncoderSimulationOptions
{
    /// <summary>appsettings 區段。</summary>
    public const string SectionName = "LineScan:EncoderSimulation";

    /// <summary>true 使用模擬位移；false 使用裝置實際 Encoder。</summary>
    public bool Enabled { get; set; }

    /// <summary>模擬往前速度（mm/s），按設備掃描時間換算 Encoder 計數。</summary>
    public double ForwardSpeedMmPerSec { get; set; } = 100;
}

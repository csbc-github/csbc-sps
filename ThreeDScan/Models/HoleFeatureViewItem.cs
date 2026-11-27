using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Models;

/// <summary>孔洞選單保留各開口的獨立邊界與品質狀態。</summary>
public sealed class HoleFeatureViewItem
{
    public HoleFeatureViewItem(HoleFeatureSnapshot feature) => Feature = feature;

    public HoleFeatureSnapshot Feature { get; }
    public string Id => Feature.Id;
    public bool Closed => Feature.Closed;
    public string Label => $"{Id} · {(Closed ? "封閉孔" : "部分開口")} · {Feature.FaceId}";
    public string Description
    {
        get
        {
            string status = Closed ? "封閉孔緣；座標只包含目前選取的這一孔。"
                : "部分開口，僅供檢視；未證實完整封閉孔，不能當作完整研磨路徑。";
            string size = Feature.WidthMm is double width && Feature.HeightMm is double height
                ? $"實測範圍 {width:F2} × {height:F2} mm。" : "";
            string? reason = Feature.Reason;
            if (reason?.StartsWith("One end has no measured closing rim", StringComparison.Ordinal) == true)
                reason = "其中一端未量到封閉孔緣，開口可能超出掃描範圍。";
            return $"{status} {size} {reason}".Trim();
        }
    }
}

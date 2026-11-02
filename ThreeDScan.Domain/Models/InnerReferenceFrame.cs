namespace ThreeDScan.Domain.Models;

/// <summary>內側參考點座標框。對應 Hank <c>Scan3D.Processing.InnerReferenceFrame</c>。</summary>
public enum InnerReferenceFrame
{
    /// <summary>絕對座標。</summary>
    Absolute = 0,

    /// <summary>掃描器座標。</summary>
    Scanner = 1,
}

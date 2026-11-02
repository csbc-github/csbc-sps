namespace ThreeDScan.Domain.Models;

/// <summary>選取哪個候選物件。對應 Hank <c>Scan3D.Processing.ComponentSelection</c>。</summary>
public enum ComponentSelection
{
    /// <summary>兩翼角鋼（預設）。</summary>
    AngleSteel = 0,

    /// <summary>平面物件。</summary>
    Planar = 1,

    /// <summary>點數最多者。</summary>
    Largest = 2,
}

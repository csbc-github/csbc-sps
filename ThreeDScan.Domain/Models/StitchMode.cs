namespace ThreeDScan.Domain.Models;

/// <summary>多段拼接方式。對應 Hank <c>Scan3D.Processing.StitchMode</c>。</summary>
public enum StitchMode
{
    /// <summary>依相機位姿放到絕對座標再合併。</summary>
    CameraPose = 0,

    /// <summary>依公稱對接間隙沿長度串接。</summary>
    NominalButtJoin = 1,
}

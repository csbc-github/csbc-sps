namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>實體匯出參數。</summary>
/// <remarks>由 Hank Scan3D 的 SolidExportOptions 搬入，邏輯不改。</remarks>
public sealed class SolidExportOptions
{
    /// <summary>建立參數。</summary>
    /// <param name="thicknessMm">板厚（mm）。</param>
    /// <param name="cellMm">網格邊長（mm）。</param>
    public SolidExportOptions(double thicknessMm, double cellMm = 1)
    {
        ThicknessMm = thicknessMm;
        CellMm = cellMm;
    }

    /// <summary>板厚（mm）。</summary>
    public double ThicknessMm { get; init; }

    /// <summary>網格邊長（mm）。</summary>
    public double CellMm { get; init; } = 1;

    /// <summary>檢查板厚與網格範圍。</summary>
    public void Validate()
    {
        if (!double.IsFinite(ThicknessMm) || ThicknessMm < .001 || ThicknessMm > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(ThicknessMm), "請輸入 0.001～1000 範圍的板厚（mm）。");
        }

        if (!double.IsFinite(CellMm) || CellMm < .25 || CellMm > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(CellMm), "模型網格解析度須為 0.25～10 mm。");
        }
    }
}

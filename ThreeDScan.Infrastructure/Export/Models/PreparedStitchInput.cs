namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>拼接前準備的輸入描述。</summary>
/// <remarks>由 Hank Scan3D 的 PreparedStitchInput 搬入，邏輯不改。</remarks>
public sealed class PreparedStitchInput
{
    /// <summary>建立準備結果。</summary>
    /// <param name="originalFile">原始檔。</param>
    /// <param name="effectiveFile">實際用於拼接的檔。</param>
    /// <param name="originalSha256">原始 SHA-256。</param>
    /// <param name="originalPointCount">原始點數。</param>
    /// <param name="preparedPointCount">準備後點數。</param>
    /// <param name="addedMeasuredPointCount">補回的外緣量測點數。</param>
    /// <param name="rawFile">配套原始掃描。</param>
    /// <param name="rawSha256">原始掃描 SHA-256。</param>
    /// <param name="rawToInputMatrix">原始→輸入矩陣。</param>
    /// <param name="message">說明。</param>
    public PreparedStitchInput(
        string originalFile,
        string effectiveFile,
        string originalSha256,
        int originalPointCount,
        int preparedPointCount,
        int addedMeasuredPointCount,
        string? rawFile,
        string? rawSha256,
        double[][]? rawToInputMatrix,
        string message)
    {
        OriginalFile = originalFile;
        EffectiveFile = effectiveFile;
        OriginalSha256 = originalSha256;
        OriginalPointCount = originalPointCount;
        PreparedPointCount = preparedPointCount;
        AddedMeasuredPointCount = addedMeasuredPointCount;
        RawFile = rawFile;
        RawSha256 = rawSha256;
        RawToInputMatrix = rawToInputMatrix;
        Message = message;
    }

    /// <summary>原始檔。</summary>
    public string OriginalFile { get; init; }

    /// <summary>實際用於拼接的檔。</summary>
    public string EffectiveFile { get; init; }

    /// <summary>原始 SHA-256。</summary>
    public string OriginalSha256 { get; init; }

    /// <summary>原始點數。</summary>
    public int OriginalPointCount { get; init; }

    /// <summary>準備後點數。</summary>
    public int PreparedPointCount { get; init; }

    /// <summary>補回的外緣量測點數。</summary>
    public int AddedMeasuredPointCount { get; init; }

    /// <summary>配套原始掃描。</summary>
    public string? RawFile { get; init; }

    /// <summary>原始掃描 SHA-256。</summary>
    public string? RawSha256 { get; init; }

    /// <summary>原始→輸入矩陣。</summary>
    public double[][]? RawToInputMatrix { get; init; }

    /// <summary>說明。</summary>
    public string Message { get; init; }
}

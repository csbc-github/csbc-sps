namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>校正／向量運算失敗。對應 Hank <c>CalibrationException</c>。</summary>
public sealed class CalibrationMathException : Exception
{
    /// <summary>建立例外。</summary>
    /// <param name="message">訊息。</param>
    public CalibrationMathException(string message)
        : base(message)
    {
    }
}

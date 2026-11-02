using System.Text.Json.Serialization;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>三維向量。對應 Hank <c>Scan3D.Processing.Legacy.Extraction.ScanVector</c>。</summary>
public readonly struct ScanVector
{
    /// <summary>建立向量。</summary>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    public ScanVector(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>X。</summary>
    public double X { get; }

    /// <summary>Y。</summary>
    public double Y { get; }

    /// <summary>Z。</summary>
    public double Z { get; }

    /// <summary>相加。</summary>
    public static ScanVector operator +(ScanVector a, ScanVector b)
    {
        return new ScanVector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    /// <summary>相減。</summary>
    public static ScanVector operator -(ScanVector a, ScanVector b)
    {
        return new ScanVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    /// <summary>純量倍。</summary>
    /// <param name="s">倍率。</param>
    public ScanVector Scale(double s)
    {
        return new ScanVector(X * s, Y * s, Z * s);
    }

    /// <summary>點積。</summary>
    /// <param name="b">另一向量。</param>
    public double Dot(ScanVector b)
    {
        return X * b.X + Y * b.Y + Z * b.Z;
    }

    /// <summary>叉積。</summary>
    /// <param name="b">另一向量。</param>
    public ScanVector Cross(ScanVector b)
    {
        return new ScanVector(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    }

    /// <summary>長度。</summary>
    [JsonIgnore]
    public double Length
    {
        get
        {
            return Math.Sqrt(Dot(this));
        }
    }

    /// <summary>單位向量。</summary>
    public ScanVector Normalize()
    {
        var n = Length;
        if (n < 1e-12)
        {
            throw new CalibrationMathException("Cannot normalize zero vector");
        }

        return Scale(1.0 / n);
    }
}

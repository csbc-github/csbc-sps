namespace ThreeDScan.Infrastructure.Export;

/// <summary>整數 XY 格子。</summary>
/// <remarks>由 Hank Scan3D 的 GridCell 搬入，邏輯不改。</remarks>
internal readonly struct GridCell : IEquatable<GridCell>
{
    /// <summary>建立格子。</summary>
    /// <param name="x">X 格。</param>
    /// <param name="y">Y 格。</param>
    public GridCell(long x, long y)
    {
        X = x;
        Y = y;
    }

    /// <summary>X 格。</summary>
    public long X { get; }

    /// <summary>Y 格。</summary>
    public long Y { get; }

    /// <summary>值相等。</summary>
    public bool Equals(GridCell other)
    {
        return X == other.X && Y == other.Y;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GridCell other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    /// <summary>值相等。</summary>
    public static bool operator ==(GridCell left, GridCell right)
    {
        return left.Equals(right);
    }

    /// <summary>值不相等。</summary>
    public static bool operator !=(GridCell left, GridCell right)
    {
        return !left.Equals(right);
    }
}

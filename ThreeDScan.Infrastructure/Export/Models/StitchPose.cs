namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>拼接剛體姿態（列向量列）。</summary>
/// <remarks>由 Hank Scan3D 的 StitchPose 搬入，邏輯不改。</remarks>
public sealed class StitchPose
{
    /// <summary>建立姿態。</summary>
    /// <param name="rowX">第一列。</param>
    /// <param name="rowY">第二列。</param>
    /// <param name="rowZ">第三列。</param>
    /// <param name="translation">平移。</param>
    public StitchPose(ScanVector rowX, ScanVector rowY, ScanVector rowZ, ScanVector translation)
    {
        RowX = rowX;
        RowY = rowY;
        RowZ = rowZ;
        Translation = translation;
    }

    /// <summary>第一列。</summary>
    public ScanVector RowX { get; init; }

    /// <summary>第二列。</summary>
    public ScanVector RowY { get; init; }

    /// <summary>第三列。</summary>
    public ScanVector RowZ { get; init; }

    /// <summary>平移。</summary>
    public ScanVector Translation { get; init; }

    /// <summary>套用到點。</summary>
    /// <param name="p">點。</param>
    public ScanVector Apply(ScanVector p)
    {
        return new ScanVector(RowX.Dot(p) + Translation.X, RowY.Dot(p) + Translation.Y, RowZ.Dot(p) + Translation.Z);
    }

    /// <summary>回傳 this(inner(p))；不量化座標。</summary>
    /// <param name="inner">內層姿態。</param>
    public StitchPose Compose(StitchPose inner)
    {
        ScanVector Row(ScanVector r)
        {
            return inner.RowX.Scale(r.X) + inner.RowY.Scale(r.Y) + inner.RowZ.Scale(r.Z);
        }

        return new StitchPose(Row(RowX), Row(RowY), Row(RowZ), Apply(inner.Translation));
    }

    /// <summary>反姿態。</summary>
    public StitchPose Inverse()
    {
        var x = new ScanVector(RowX.X, RowY.X, RowZ.X);
        var y = new ScanVector(RowX.Y, RowY.Y, RowZ.Y);
        var z = new ScanVector(RowX.Z, RowY.Z, RowZ.Z);
        return new StitchPose(x, y, z, new ScanVector(-x.Dot(Translation), -y.Dot(Translation), -z.Dot(Translation)));
    }

    /// <summary>4×4 矩陣。</summary>
    public double[][] Matrix
    {
        get
        {
            return
            [
                [RowX.X, RowX.Y, RowX.Z, Translation.X],
                [RowY.X, RowY.Y, RowY.Z, Translation.Y],
                [RowZ.X, RowZ.Y, RowZ.Z, Translation.Z],
                [0, 0, 0, 1],
            ];
        }
    }

    /// <summary>行列式。</summary>
    public double Determinant
    {
        get
        {
            return RowX.Dot(RowY.Cross(RowZ));
        }
    }

    /// <summary>正交誤差。</summary>
    public double OrthogonalityError
    {
        get
        {
            return new[]
            {
                Math.Abs(RowX.Dot(RowY)),
                Math.Abs(RowX.Dot(RowZ)),
                Math.Abs(RowY.Dot(RowZ)),
                Math.Abs(RowX.Length - 1),
                Math.Abs(RowY.Length - 1),
                Math.Abs(RowZ.Length - 1),
            }.Max();
        }
    }
}

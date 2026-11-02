using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 右手剛體轉換（mm、度、欄向量）。不用地面仿射校正。
/// </summary>
/// <remarks>來源：Hank <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
public static class RigidTransforms
{
    /// <summary>4×4 單位矩陣。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    public static double[][] Identity()
    {
        return new[]
        {
            new[] { 1.0, 0.0, 0.0, 0.0 },
            new[] { 0.0, 1.0, 0.0, 0.0 },
            new[] { 0.0, 0.0, 1.0, 0.0 },
            new[] { 0.0, 0.0, 0.0, 1.0 },
        };
    }

    /// <summary>由相機位姿組成掃描器→絕對座標矩陣。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    /// <param name="pose">相機位姿。</param>
    /// <param name="options">座標選項；null 用預設。</param>
    public static double[][] FromPose(Pose5 pose, CoordinateTransformOptions? options = null)
    {
        options ??= new CoordinateTransformOptions();
        var values = new[]
        {
            pose.X, pose.Y, pose.Z, pose.A, pose.C, options.ASign, options.CSign,
            options.WorldOriginOffset.X, options.WorldOriginOffset.Y, options.WorldOriginOffset.Z,
        };
        if (values.Any(v => !double.IsFinite(v)))
        {
            throw new ArgumentException("相機姿態與補償值必須為有限數值。");
        }

        if (Math.Abs(options.ASign) != 1 || Math.Abs(options.CSign) != 1
            || options.AAxis == options.CAxis
            || !Enum.IsDefined(options.AAxis) || !Enum.IsDefined(options.CAxis))
        {
            throw new ArgumentException("A/C 必須使用不同軸，角度正負號必須為 +1 或 -1。");
        }

        Validate(options.ScannerToMount);
        var a = Rotation(options.AAxis, pose.A * options.ASign);
        var c = Rotation(options.CAxis, pose.C * options.CSign);
        var r = options.ApplyCFirst ? Multiply(a, c) : Multiply(c, a);
        r[0][3] = pose.X + options.WorldOriginOffset.X;
        r[1][3] = pose.Y + options.WorldOriginOffset.Y;
        r[2][3] = pose.Z + options.WorldOriginOffset.Z;
        return Multiply(r, options.ScannerToMount);
    }

    /// <summary>套用剛體到點。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    public static Point3 Apply(double[][] m, Point3 p)
    {
        return ToPoint(Apply(m, ToVector(p)));
    }

    /// <summary>套用剛體到向量（含平移）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    internal static ScanVector Apply(double[][] m, ScanVector p)
    {
        return new ScanVector(
            m[0][0] * p.X + m[0][1] * p.Y + m[0][2] * p.Z + m[0][3],
            m[1][0] * p.X + m[1][1] * p.Y + m[1][2] * p.Z + m[1][3],
            m[2][0] * p.X + m[2][1] * p.Y + m[2][2] * p.Z + m[2][3]);
    }

    /// <summary>只轉方向、不平移。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    internal static ScanVector Direction(double[][] m, ScanVector p)
    {
        return new ScanVector(
            m[0][0] * p.X + m[0][1] * p.Y + m[0][2] * p.Z,
            m[1][0] * p.X + m[1][1] * p.Y + m[1][2] * p.Z,
            m[2][0] * p.X + m[2][1] * p.Y + m[2][2] * p.Z);
    }

    /// <summary>4×4 相乘（outer×inner）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    public static double[][] Multiply(double[][] outer, double[][] inner)
    {
        var result = new double[4][];
        for (var i = 0; i < 4; i++)
        {
            result[i] = new double[4];
            for (var j = 0; j < 4; j++)
            {
                var sum = 0.0;
                for (var k = 0; k < 4; k++)
                {
                    sum += outer[i][k] * inner[k][j];
                }

                result[i][j] = sum;
            }
        }

        return result;
    }

    /// <summary>剛體反矩陣。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    public static double[][] Inverse(double[][] matrix)
    {
        Validate(matrix);
        var result = Identity();
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                result[i][j] = matrix[j][i];
            }
        }

        for (var i = 0; i < 3; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < 3; j++)
            {
                sum += result[i][j] * matrix[j][3];
            }

            result[i][3] = -sum;
        }

        return result;
    }

    /// <summary>確認為有限正向剛體（無剪切／縮放）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    public static void Validate(double[][] m)
    {
        if (m is null || m.Length != 4
            || m.Any(row => row is null || row.Length != 4 || row.Any(v => !double.IsFinite(v)))
            || m[3].Where((v, i) => Math.Abs(v - (i == 3 ? 1 : 0)) > 1e-9).Any())
        {
            throw new ArgumentException("需要有限的 4×4 剛體轉換矩陣。");
        }

        var rows = new[]
        {
            new ScanVector(m[0][0], m[0][1], m[0][2]),
            new ScanVector(m[1][0], m[1][1], m[1][2]),
            new ScanVector(m[2][0], m[2][1], m[2][2]),
        };
        if (rows.Any(r => Math.Abs(r.Length - 1) > 1e-6)
            || Math.Abs(rows[0].Dot(rows[1])) > 1e-6
            || Math.Abs(rows[0].Dot(rows[2])) > 1e-6
            || Math.Abs(rows[1].Dot(rows[2])) > 1e-6
            || Math.Abs(rows[0].Dot(rows[1].Cross(rows[2])) - 1) > 1e-6)
        {
            throw new ArgumentException("座標補償須為正向剛體轉換；不可使用含剪切/縮放的地面校正矩陣。");
        }
    }

    /// <summary>軸向旋轉（度）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    private static double[][] Rotation(AxisName axis, double angle)
    {
        var c = Math.Cos(angle * Math.PI / 180);
        var s = Math.Sin(angle * Math.PI / 180);
        if (axis == AxisName.X)
        {
            return new[]
            {
                new[] { 1.0, 0.0, 0.0, 0.0 },
                new[] { 0.0, c, -s, 0.0 },
                new[] { 0.0, s, c, 0.0 },
                new[] { 0.0, 0.0, 0.0, 1.0 },
            };
        }

        if (axis == AxisName.Y)
        {
            return new[]
            {
                new[] { c, 0.0, s, 0.0 },
                new[] { 0.0, 1.0, 0.0, 0.0 },
                new[] { -s, 0.0, c, 0.0 },
                new[] { 0.0, 0.0, 0.0, 1.0 },
            };
        }

        return new[]
        {
            new[] { c, -s, 0.0, 0.0 },
            new[] { s, c, 0.0, 0.0 },
            new[] { 0.0, 0.0, 1.0, 0.0 },
            new[] { 0.0, 0.0, 0.0, 1.0 },
        };
    }

    /// <summary>向量轉 Point3。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    internal static Point3 ToPoint(ScanVector p)
    {
        return new Point3 { X = p.X, Y = p.Y, Z = p.Z };
    }

    /// <summary>Point3 轉向量。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.RigidTransforms</c>。</remarks>
    internal static ScanVector ToVector(Point3 p)
    {
        return new ScanVector(p.X, p.Y, p.Z);
    }
}

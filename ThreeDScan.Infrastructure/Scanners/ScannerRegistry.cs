using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>依 appsettings「Scanners」解析掃描器端點與內容目錄路徑。</summary>
public sealed class ScannerRegistry : IScannerRegistry
{
    private readonly ScannerRegistryOptions _options;

    /// <summary>以 appsettings Scanners 建立註冊表。</summary>
    public ScannerRegistry(IOptions<ScannerRegistryOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public bool TryGetEndpoint(string scannerId, out ScannerEndpoint endpoint)
    {
        if (!_options.TryGetValue(scannerId, out var opt))
        {
            endpoint = null!;
            return false;
        }

        endpoint = new ScannerEndpoint
        {
            ScannerId = scannerId,
            Ip = opt.Ip,
            Port = opt.Port,
            HighSpeedPort = opt.HighSpeedPort,
            NavigatorScanNo = opt.NavigatorScanNo,
            MachineArea = opt.MachineArea,
            OfflineCaptureDirectory = opt.OfflineCaptureDirectory,
        };
        return true;
    }

    /// <inheritdoc />
    public byte ResolveProgramNo(string scannerId, int? navigatorScanNo)
    {
        if (navigatorScanNo.HasValue)
            return (byte)Math.Clamp(navigatorScanNo.Value - 1, 0, 15);

        if (_options.TryGetValue(scannerId, out var opt) && opt.NavigatorScanNo.HasValue)
            return (byte)Math.Clamp(opt.NavigatorScanNo.Value - 1, 0, 15);

        return 0;
    }

    /// <inheritdoc />
    public string ResolveScannerContentsFolder(string scannerId, string contentsRoot)
    {
        return Path.Combine(contentsRoot, scannerId);
    }

    /// <inheritdoc />
    public string ResolveContentsFolder(string scannerId, byte programNo, string contentsRoot)
    {
        var basePath = Path.Combine(contentsRoot, scannerId);
        return programNo == 0
            ? basePath
            : Path.Combine(basePath, $"program_{programNo}");
    }

    /// <summary>回傳 appsettings 中已註冊的全部 ScannerId。</summary>
    public IReadOnlyList<string> GetAllScannerIds()
    {
        return _options.Keys.ToList();
    }

    /// <inheritdoc />
    public bool TryGetCoordinateDefaults(string scannerId, out CoordinateTransformOptions coordinates)
    {
        if (!_options.TryGetValue(scannerId, out var opt))
        {
            coordinates = null!;
            return false;
        }

        coordinates = CopyCoordinates(opt.Coordinates);
        return true;
    }

    /// <inheritdoc />
    public bool TryGetProcessingDefaults(string scannerId, out SegmentProcessingOptions processing)
    {
        if (!_options.TryGetValue(scannerId, out var opt))
        {
            processing = null!;
            return false;
        }

        processing = CopyProcessing(opt.Processing);
        return true;
    }

    /// <inheritdoc />
    public double GetMeshCellMm(string scannerId)
    {
        if (!_options.TryGetValue(scannerId, out var opt))
        {
            return 1.0;
        }

        var meshCell = opt.MeshCellMm;
        if (!double.IsFinite(meshCell) || meshCell <= 0)
        {
            return 1.0;
        }

        return meshCell;
    }

    /// <inheritdoc />
    public double? GetDefaultMaterialThicknessMm(string scannerId)
    {
        if (!_options.TryGetValue(scannerId, out var opt))
        {
            return null;
        }

        return opt.MaterialThicknessMm;
    }

    /// <inheritdoc />
    public bool TryGetHoleRoi(string scannerId, out ScannerRoi? holeRoi)
    {
        holeRoi = null;
        if (!_options.TryGetValue(scannerId, out var opt) || opt.HoleRoi is null)
        {
            return false;
        }

        holeRoi = CopyRoi(opt.HoleRoi);
        return true;
    }

    /// <inheritdoc />
    public bool TryGetFixedCameraPosition(string scannerId, out Pose5? fixedCameraPosition)
    {
        fixedCameraPosition = null;
        if (!_options.TryGetValue(scannerId, out var opt) || opt.FixedCameraPosition is null)
        {
            return false;
        }

        fixedCameraPosition = CopyPose(opt.FixedCameraPosition);
        return true;
    }

    private static CoordinateTransformOptions CopyCoordinates(CoordinateTransformOptions source)
    {
        return new CoordinateTransformOptions
        {
            ScannerToMount = CopyMatrix(source.ScannerToMount),
            WorldOriginOffset = new Point3
            {
                X = source.WorldOriginOffset.X,
                Y = source.WorldOriginOffset.Y,
                Z = source.WorldOriginOffset.Z,
            },
            AAxis = source.AAxis,
            CAxis = source.CAxis,
            ASign = source.ASign,
            CSign = source.CSign,
            ApplyCFirst = source.ApplyCFirst,
            IsCalibrated = source.IsCalibrated,
            CalibrationId = source.CalibrationId,
            InnerReferenceFrame = source.InnerReferenceFrame,
        };
    }

    private static SegmentProcessingOptions CopyProcessing(SegmentProcessingOptions source)
    {
        return new SegmentProcessingOptions
        {
            Selection = source.Selection,
            ComponentRank = source.ComponentRank,
            ScannerRoi = source.ScannerRoi,
            ZThresholdMm = source.ZThresholdMm,
            CellMm = source.CellMm,
            MinimumComponentCells = source.MinimumComponentCells,
            MaximumPlaneRmsMm = source.MaximumPlaneRmsMm,
            ThicknessLocalRadiusMm = source.ThicknessLocalRadiusMm,
            RecoverMeasuredEdges = source.RecoverMeasuredEdges,
        };
    }

    private static double[][] CopyMatrix(double[][] matrix)
    {
        var copy = new double[matrix.Length][];
        for (var i = 0; i < matrix.Length; i++)
        {
            copy[i] = (double[])matrix[i].Clone();
        }

        return copy;
    }

    private static ScannerRoi CopyRoi(ScannerRoi source)
    {
        return new ScannerRoi
        {
            MinX = source.MinX,
            MaxX = source.MaxX,
            MinY = source.MinY,
            MaxY = source.MaxY,
            MinZ = source.MinZ,
            MaxZ = source.MaxZ,
        };
    }

    private static Pose5 CopyPose(Pose5 source)
    {
        return new Pose5
        {
            X = source.X,
            Y = source.Y,
            Z = source.Z,
            A = source.A,
            C = source.C,
        };
    }
}

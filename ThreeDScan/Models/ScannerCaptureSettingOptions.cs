namespace ThreeDScan.Models;

public sealed class SelectOption<T>
{
    public SelectOption(string label, T value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public T Value { get; }

    public override string ToString() => Label;
}

public static class ScannerCaptureSettingOptions
{
    public static IReadOnlyList<SelectOption<byte>> ExposureTimes { get; } =
    [
        new("15 μs", 0),
        new("30 μs", 1),
        new("60 μs", 2),
        new("80 μs", 3),
        new("120 μs", 4),
        new("160 μs", 5),
        new("210 μs", 6),
        new("240 μs", 7),
        new("320 μs", 8),
        new("380 μs", 9),
        new("480 μs", 10),
        new("640 μs", 11),
        new("960 μs", 12),
        new("1700 μs", 13),
        new("4800 μs", 14),
        new("9600 μs", 15),
    ];

    public static IReadOnlyList<SelectOption<byte>> LightControlModes { get; } =
    [
        new("手動", 0),
        new("自動", 1),
        new("SLOPE", 2),
    ];

    public static IReadOnlyList<SelectOption<byte>> PeakSelections { get; } =
    [
        new("標準（最大光量）", 0),
        new("NEAR", 1),
        new("FAR", 2),
        new("X方向異常反射光抑制", 3),
        new("Y方向異常反射光抑制", 4),
        new("XY方向異常反射光抑制", 5),
    ];

    public static IReadOnlyList<SelectOption<byte>> DeadZoneInterpolations { get; } =
    [
        new("關閉", 0),
        new("水平/垂直差補", 1),
        new("線性差補", 2),
    ];

    public static IReadOnlyList<SelectOption<byte>> HaloFilterStrengths { get; } =
    [
        new("1", 1),
        new("2", 2),
        new("3", 3),
        new("4", 4),
        new("5", 5),
    ];

    /// <summary>UI 值：0=OFF，2～8=倍數。</summary>
    public static IReadOnlyList<SelectOption<byte>> YIntervalMultipliers { get; } =
    [
        new("OFF", 0),
        new("2", 2),
        new("3", 3),
        new("4", 4),
        new("5", 5),
        new("6", 6),
        new("7", 7),
        new("8", 8),
    ];
}

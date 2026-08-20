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

    public override string ToString()
    {
        return Label;
    }
}

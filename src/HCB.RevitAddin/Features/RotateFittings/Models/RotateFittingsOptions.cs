namespace HCB.RevitAddin.Features.RotateFittings.Models;

public enum RotateFittingsAxisMode
{
    ConnectorAxis,
    VerticalAxis
}

public sealed class RotateFittingsOptions
{
    public double AngleDegrees { get; init; }

    public RotateFittingsAxisMode AxisMode { get; init; }

    public bool IncludeDuctFittings { get; init; }

    public bool IncludePipeFittings { get; init; }

    public bool IncludeConduitFittings { get; init; }
}

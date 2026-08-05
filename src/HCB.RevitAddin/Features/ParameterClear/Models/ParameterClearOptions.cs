namespace HCB.RevitAddin.Features.ParameterClear.Models;

public sealed class ParameterClearOptions
{
    public string ParameterName { get; init; } = string.Empty;

    public ParameterClearScope Scope { get; init; }

    public string? CategoryName { get; init; }
}

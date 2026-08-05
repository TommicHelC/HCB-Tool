namespace HCB.RevitAddin.Features.ParameterCopy.Models;

public sealed class ParameterCopyOptions
{
    public string SourceParameterName { get; init; } = string.Empty;

    public string TargetParameterName { get; init; } = string.Empty;

    public ParameterCopyScope Scope { get; init; }
}

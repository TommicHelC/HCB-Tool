namespace HCB.RevitAddin.Features.AlignToAxis.Models;

public sealed class AlignToAxisResult
{
    public bool HasChanges { get; init; }

    public bool IsError { get; init; }

    public string Message { get; init; } = string.Empty;
}

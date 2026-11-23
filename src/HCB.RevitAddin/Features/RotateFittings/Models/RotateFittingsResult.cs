namespace HCB.RevitAddin.Features.RotateFittings.Models;

public sealed class RotateFittingsResult
{
    public bool HasChanges { get; init; }

    public long ElementId { get; init; }

    public int DisconnectedCount { get; init; }

    public string Message { get; init; } = string.Empty;
}

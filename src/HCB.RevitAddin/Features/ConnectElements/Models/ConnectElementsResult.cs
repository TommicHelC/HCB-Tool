namespace HCB.RevitAddin.Features.ConnectElements.Models;

public sealed class ConnectElementsResult
{
    public bool HasChanges { get; init; }

    public int DisconnectedCount { get; init; }

    public string Message { get; init; } = string.Empty;
}

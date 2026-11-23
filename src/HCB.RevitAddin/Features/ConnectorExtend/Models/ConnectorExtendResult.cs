using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.ConnectorExtend.Models;

public sealed class ConnectorExtendResult
{
    public bool HasChanges { get; init; }

    public ElementId? CreatedId { get; init; }

    public string Message { get; init; } = string.Empty;
}

using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.ElbowBranch.Models;

public enum ElbowBranchDirection
{
    Up,
    Side45
}

public sealed class ElbowBranchResult
{
    public bool HasChanges { get; init; }

    public IReadOnlyList<ElementId> CreatedIds { get; init; } = [];

    public string Message { get; init; } = string.Empty;
}

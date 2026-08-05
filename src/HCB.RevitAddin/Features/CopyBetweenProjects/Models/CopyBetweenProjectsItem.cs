using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.CopyBetweenProjects.Models;

public sealed class CopyBetweenProjectsItem
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string FilterGroup { get; init; } = string.Empty;

    public string SecondaryFilterGroup { get; init; } = string.Empty;

    public IReadOnlyList<ElementId> ElementIds { get; init; } = [];
}

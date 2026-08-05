using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.HvacTagging.Models;

public sealed class HvacTagPreviewRow
{
    public View? View { get; init; }

    public Element? Element { get; init; }

    public BuiltInCategory? ModelCategory { get; init; }

    public string ViewName { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public string ElementName { get; init; } = string.Empty;

    public string ElementIdText { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public XYZ? TagPoint { get; init; }

    public bool CanCreateTag { get; init; }

    public bool HasExistingTag { get; init; }

    public bool IsDuplicate { get; init; }
}

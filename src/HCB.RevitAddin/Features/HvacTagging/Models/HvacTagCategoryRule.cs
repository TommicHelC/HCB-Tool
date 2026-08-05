using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.HvacTagging.Models;

public sealed class HvacTagCategoryRule
{
    public HvacTagCategoryRule(BuiltInCategory modelCategory, BuiltInCategory tagCategory, string displayName)
    {
        ModelCategory = modelCategory;
        TagCategory = tagCategory;
        DisplayName = displayName;
    }

    public BuiltInCategory ModelCategory { get; }

    public BuiltInCategory TagCategory { get; }

    public string DisplayName { get; }

    public FamilySymbol? SelectedTagType { get; set; }

    public IReadOnlyList<FamilySymbol> AvailableTagTypes { get; set; } = [];

    public string TagTypeDisplayName => SelectedTagType == null
        ? "Brak"
        : $"{SelectedTagType.FamilyName}: {SelectedTagType.Name}";
}

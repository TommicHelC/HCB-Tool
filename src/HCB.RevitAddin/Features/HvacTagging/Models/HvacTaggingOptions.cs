using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.HvacTagging.Models;

public sealed class HvacTaggingOptions
{
    public bool UseLeader { get; set; }

    public double OffsetXMillimeters { get; set; }

    public double OffsetYMillimeters { get; set; }

    public List<HvacTagCategoryRule> CategoryRules { get; set; } = [];

    public ElementId? GetTagTypeId(BuiltInCategory category)
    {
        return CategoryRules
            .FirstOrDefault(rule => rule.ModelCategory == category)
            ?.SelectedTagType
            ?.Id;
    }
}

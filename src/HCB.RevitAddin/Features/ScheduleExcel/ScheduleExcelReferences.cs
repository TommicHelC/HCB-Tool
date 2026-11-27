using System;
using Autodesk.Revit.DB;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// Parameters that point at another element (type, level, material, phase...): which values are allowed and how
/// they read in a schedule. Excel shows them as a drop-down of display names; sync maps the name back to an id.
/// </summary>
internal static class ScheduleExcelReferences
{
    private const int MaxChoices = 2000;

    private static readonly HashSet<BuiltInParameter> TypeParameters =
    [
        BuiltInParameter.ELEM_TYPE_PARAM,
        BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
        BuiltInParameter.SYMBOL_ID_PARAM
    ];

    /// <summary>"Type" / "Family and Type": changed with Element.ChangeTypeId, not Parameter.Set.</summary>
    public static bool IsTypeChange(Parameter parameter)
    {
        return parameter.Definition is InternalDefinition definition && TypeParameters.Contains(definition.BuiltInParameter);
    }

    /// <summary>Allowed values for one element's parameter: valid types for a type change, else elements of the same class.</summary>
    public static IReadOnlyList<(ElementId Id, string Text)> Candidates(Document document, Element target, Parameter parameter)
    {
        try
        {
            if (IsTypeChange(parameter))
            {
                return target.GetValidTypes()
                    .Select(document.GetElement)
                    .OfType<ElementType>()
                    .Select(type => (type.Id, TypeText(parameter, type)))
                    .ToList();
            }

            Element? current = document.GetElement(parameter.AsElementId());
            if (current == null)
            {
                return [];
            }

            FilteredElementCollector collector = new FilteredElementCollector(document).OfClass(current.GetType());
            if (current.Category != null)
            {
                collector = collector.OfCategoryId(current.Category.Id);
            }

            return collector
                .Take(MaxChoices)
                .Select(element => (element.Id, ElementText(element)))
                .Where(choice => choice.Item2.Length > 0)
                .ToList();
        }
        catch (Exception exception)
        {
            // Some classes cannot be collected with OfClass; such a column simply stays read only.
            ScheduleExcelLog.Error($"Lista wartości dla parametru '{parameter.Definition?.Name}' niedostępna.", exception);
            return [];
        }
    }

    /// <summary>Text as the schedule shows it: "Family: Type" for Family and Type, else the name.</summary>
    public static string TypeText(Parameter parameter, ElementType type)
    {
        bool withFamily = parameter.Definition is InternalDefinition definition
            && definition.BuiltInParameter == BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM;
        return withFamily && type.FamilyName.Length > 0 ? $"{type.FamilyName}: {type.Name}" : type.Name;
    }

    private static string ElementText(Element element)
    {
        return element is ElementType type && type.FamilyName.Length > 0 ? type.Name : element.Name ?? string.Empty;
    }

    /// <summary>
    /// Id for a chosen text. Several candidates can share a name ("Type 1" in two families): prefer the one in the
    /// element's current family, then the first.
    /// </summary>
    public static ElementId? Resolve(Document document, Element target, Parameter parameter, string text)
    {
        List<(ElementId Id, string Text)> matches = Candidates(document, target, parameter)
            .Where(choice => string.Equals(choice.Text, text, StringComparison.Ordinal))
            .ToList();
        if (matches.Count <= 1)
        {
            return matches.Count == 1 ? matches[0].Id : null;
        }

        string? currentFamily = (document.GetElement(target.GetTypeId()) as ElementType)?.FamilyName;
        return matches.FirstOrDefault(choice => (document.GetElement(choice.Id) as ElementType)?.FamilyName == currentFamily).Id
            ?? matches[0].Id;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ParameterClear.Models;

namespace HCB.RevitAddin.Features.ParameterClear;

public sealed class ParameterClearService
{
    public IReadOnlyList<Element> GetSelectedSupportedElements(Document document, ICollection<ElementId> selectedElementIds)
    {
        return selectedElementIds
            .Select(document.GetElement)
            .Where(IsSupportedElement)
            .DistinctBy(element => element!.Id.Value)
            .Cast<Element>()
            .ToList();
    }

    public IReadOnlyList<string> GetWritableParameterNames(IEnumerable<Element> elements)
    {
        return DistinctParameterCarriers(elements)
            .SelectMany(element => element.Parameters.Cast<Parameter>())
            .Where(IsWritableParameter)
            .Select(GetParameterName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ParameterClearCatalog BuildCatalog(Document document)
    {
        List<Element> elements = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .Where(IsSupportedElement)
            .ToList();

        List<string> modelParameterNames = GetWritableParameterNames(elements).ToList();
        Dictionary<string, IReadOnlyList<string>> categoryParameterNames = elements
            .GroupBy(
                element => element.Category!.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new
            {
                CategoryName = group.Key,
                ParameterNames = GetWritableParameterNames(group)
            })
            .Where(entry => entry.ParameterNames.Count > 0)
            .OrderBy(entry => entry.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(
                entry => entry.CategoryName,
                entry => entry.ParameterNames,
                StringComparer.CurrentCultureIgnoreCase);

        return new ParameterClearCatalog
        {
            ModelParameterNames = modelParameterNames,
            CategoryParameterNames = categoryParameterNames
        };
    }

    public ParameterClearResult Preview(Document document, ICollection<ElementId> selectedElementIds, ParameterClearOptions options)
    {
        return Analyze(document, selectedElementIds, options, applyChanges: false);
    }

    public ParameterClearResult Apply(Document document, ICollection<ElementId> selectedElementIds, ParameterClearOptions options)
    {
        return Analyze(document, selectedElementIds, options, applyChanges: true);
    }

    private ParameterClearResult Analyze(
        Document document,
        ICollection<ElementId> selectedElementIds,
        ParameterClearOptions options,
        bool applyChanges)
    {
        ParameterClearResult result = new();
        List<Element> scopedElements = GetScopedElements(document, selectedElementIds, options).ToList();
        result.ScopedElementCount = scopedElements.Count;

        List<(Element Element, Parameter Parameter)> targets = scopedElements
            .Select(element => new { Element = element, Parameter = element.LookupParameter(options.ParameterName) })
            .Where(item => IsWritableParameter(item.Parameter))
            .Select(item => (item.Element, item.Parameter!))
            .ToList();

        result.TargetCount = targets.Count;
        result.SkippedCount = scopedElements.Count - targets.Count;

        if (!applyChanges || targets.Count == 0)
        {
            foreach ((Element _, Parameter parameter) in targets)
            {
                if (IsParameterEmpty(parameter))
                {
                    result.AlreadyEmptyCount++;
                }
                else
                {
                    result.ClearedCount++;
                }
            }

            return result;
        }

        using Transaction transaction = new(document, "Clear Parameter Values");
        transaction.Start();

        foreach ((Element _, Parameter parameter) in targets)
        {
            if (IsParameterEmpty(parameter))
            {
                result.AlreadyEmptyCount++;
                continue;
            }

            try
            {
                if (ClearParameter(parameter))
                {
                    result.ClearedCount++;
                }
                else
                {
                    result.SkippedCount++;
                    result.TargetCount--;
                }
            }
            catch
            {
                result.SkippedCount++;
                result.TargetCount--;
            }
        }

        if (result.ClearedCount > 0)
        {
            transaction.Commit();
        }
        else
        {
            transaction.RollBack();
        }

        return result;
    }

    private static IEnumerable<Element> GetScopedElements(Document document, ICollection<ElementId> selectedElementIds, ParameterClearOptions options)
    {
        return options.Scope switch
        {
            ParameterClearScope.Selection => selectedElementIds
                .Select(document.GetElement)
                .Where(IsSupportedElement)
                .DistinctBy(element => element!.Id.Value)
                .Cast<Element>(),
            ParameterClearScope.Category => GetCategoryElements(document, options.CategoryName),
            ParameterClearScope.Model => new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .Where(IsSupportedElement)
                .Where(element => IsWritableParameter(element.LookupParameter(options.ParameterName))),
            _ => Array.Empty<Element>()
        };
    }

    /// <summary>
    /// Resolves the category once and lets Revit filter on it, instead of walking the whole model
    /// and comparing category names element by element. Falls back to the name scan when the name
    /// cannot be resolved, so exotic (sub)categories keep working.
    /// </summary>
    private static IEnumerable<Element> GetCategoryElements(Document document, string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return [];
        }

        ElementId categoryId = FindCategoryId(document, categoryName);
        if (categoryId == ElementId.InvalidElementId)
        {
            return new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .Where(IsSupportedElement)
                .Where(element => string.Equals(element.Category!.Name, categoryName, StringComparison.CurrentCultureIgnoreCase));
        }

        return new FilteredElementCollector(document)
            .OfCategoryId(categoryId)
            .WhereElementIsNotElementType()
            .Where(IsSupportedElement);
    }

    private static ElementId FindCategoryId(Document document, string categoryName)
    {
        foreach (Category category in document.Settings.Categories)
        {
            if (string.Equals(category.Name, categoryName, StringComparison.CurrentCultureIgnoreCase))
            {
                return category.Id;
            }

            foreach (Category subCategory in category.SubCategories)
            {
                if (string.Equals(subCategory.Name, categoryName, StringComparison.CurrentCultureIgnoreCase))
                {
                    return subCategory.Id;
                }
            }
        }

        return ElementId.InvalidElementId;
    }

    private static bool IsWritableParameter(Parameter? parameter)
    {
        return parameter is { IsReadOnly: false, StorageType: not StorageType.None };
    }

    private static bool IsParameterEmpty(Parameter parameter)
    {
        return parameter.StorageType switch
        {
            StorageType.String => string.IsNullOrEmpty(parameter.AsString()),
            StorageType.Integer => parameter.AsInteger() == 0,
            StorageType.Double => parameter.AsDouble() == 0.0,
            StorageType.ElementId => parameter.AsElementId() == ElementId.InvalidElementId,
            _ => true
        };
    }

    private static bool ClearParameter(Parameter parameter)
    {
        return parameter.StorageType switch
        {
            StorageType.String => parameter.Set(string.Empty),
            StorageType.Integer => parameter.Set(0),
            StorageType.Double => parameter.Set(0.0),
            StorageType.ElementId => parameter.Set(ElementId.InvalidElementId),
            _ => false
        };
    }

    private static string GetParameterName(Parameter parameter)
    {
        return parameter.Definition?.Name?.Trim() ?? string.Empty;
    }

    private static bool IsSupportedElement(Element? element)
    {
        return element is { Category: not null };
    }

    /// <summary>
    /// Every instance of the same type in the same category exposes the same set of parameters,
    /// so one sample per (category, type) is enough to build the catalog. On real models this
    /// turns millions of Parameter lookups into a few thousand.
    /// </summary>
    private static IEnumerable<Element> DistinctParameterCarriers(IEnumerable<Element> elements)
    {
        HashSet<(long CategoryId, long TypeId)> seen = [];
        foreach (Element element in elements)
        {
            if (seen.Add((element.Category?.Id.Value ?? 0L, element.GetTypeId().Value)))
            {
                yield return element;
            }
        }
    }
}

using System;
using System.Text;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ProjectParameterAudit.Models;

namespace HCB.RevitAddin.Features.ProjectParameterAudit;

public sealed class ProjectParameterAuditService
{
    private static readonly IReadOnlyList<ParameterAuditTarget> DefaultTargets =
    [
        new("Nominal width", SpecTypeId.Length, "Length", "049391e0-c9b7-4d13-bcfb-84c309686c1f"),
        new("Weight Net (Kg)", SpecTypeId.String.Text, "Text", "2ec69171-2ed7-43a5-bee7-c504ed385854"),
        new("Nominal height", SpecTypeId.Length, "Length", "51ba8e78-a45c-4491-97da-88159bcf2c4a")
    ];

    public IReadOnlyList<ProjectParameterAuditRow> BuildDefaultReport(Document document)
    {
        IReadOnlyList<ParameterElementInfo> parameterElements = GetParameterElements(document);
        IReadOnlyList<ViewSchedule> schedules = GetSchedules(document);
        IReadOnlyList<ParameterFilterElement> filters = GetFilters(document);
        List<ProjectParameterAuditRow> rows = [];

        foreach (ParameterAuditTarget target in DefaultTargets)
        {
            IReadOnlyList<BindingEntry> bindings = GetBindings(document, target.Name);
            if (bindings.Count == 0)
            {
                rows.Add(BuildMissingRow(target, parameterElements, schedules, filters));
                continue;
            }

            foreach (BindingEntry binding in bindings)
            {
                IReadOnlyList<ParameterElementInfo> matchingParameterElements = FindMatchingParameterElements(parameterElements, binding);
                HashSet<long> parameterElementIds = matchingParameterElements
                    .Select(info => info.ElementId.Value)
                    .ToHashSet();

                ValueUsage usage = CountValues(document, binding);
                IReadOnlyList<string> scheduleNames = GetScheduleNamesUsingParameter(schedules, parameterElementIds);
                IReadOnlyList<string> filterNames = GetFilterNamesUsingParameter(filters, parameterElementIds);
                bool hasTypeMismatch = !AreTypeIdsEqual(binding.DataType, target.ExpectedSpecTypeId);
                bool hasGuidMismatch = !ContainsExpectedGuid(matchingParameterElements, target.ExpectedGuid);
                string status = BuildStatus(hasTypeMismatch, hasGuidMismatch, usage.ElementsWithValue, scheduleNames.Count, filterNames.Count);

                rows.Add(new ProjectParameterAuditRow
                {
                    ParameterName = target.Name,
                    Status = status,
                    ExpectedSpec = target.ExpectedSpecLabel,
                    FoundSpec = GetSpecLabel(binding.DataType),
                    ExpectedGuid = target.ExpectedGuid,
                    FoundGuids = FormatGuids(matchingParameterElements),
                    BindingKind = binding.BindingKind,
                    GroupTypeId = binding.GroupTypeId,
                    Categories = string.Join("; ", binding.Categories.Select(category => category.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)),
                    BoundCategoryCount = binding.Categories.Count,
                    ElementsWithParameter = usage.ElementsWithParameter,
                    ElementsWithValue = usage.ElementsWithValue,
                    StorageTypes = string.Join("; ", usage.StorageTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
                    ScheduleCount = scheduleNames.Count,
                    Schedules = string.Join("; ", scheduleNames),
                    FilterCount = filterNames.Count,
                    Filters = string.Join("; ", filterNames),
                    Recommendation = BuildRecommendation(hasTypeMismatch, hasGuidMismatch, usage.ElementsWithValue, scheduleNames.Count, filterNames.Count)
                });
            }
        }

        return rows;
    }

    public void ExportCsv(IEnumerable<ProjectParameterAuditRow> rows, string outputPath)
    {
        StringBuilder builder = new();
        builder.AppendLine("ParameterName,Status,ExpectedSpec,FoundSpec,ExpectedGuid,FoundGuids,BindingKind,GroupTypeId,BoundCategoryCount,Categories,ElementsWithParameter,ElementsWithValue,StorageTypes,ScheduleCount,Schedules,FilterCount,Filters,Recommendation");

        foreach (ProjectParameterAuditRow row in rows)
        {
            builder.AppendLine(string.Join(",",
                Escape(row.ParameterName),
                Escape(row.Status),
                Escape(row.ExpectedSpec),
                Escape(row.FoundSpec),
                Escape(row.ExpectedGuid),
                Escape(row.FoundGuids),
                Escape(row.BindingKind),
                Escape(row.GroupTypeId),
                row.BoundCategoryCount.ToString(CultureInfo.InvariantCulture),
                Escape(row.Categories),
                row.ElementsWithParameter.ToString(CultureInfo.InvariantCulture),
                row.ElementsWithValue.ToString(CultureInfo.InvariantCulture),
                Escape(row.StorageTypes),
                row.ScheduleCount.ToString(CultureInfo.InvariantCulture),
                Escape(row.Schedules),
                row.FilterCount.ToString(CultureInfo.InvariantCulture),
                Escape(row.Filters),
                Escape(row.Recommendation)));
        }

        File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
    }

    private static ProjectParameterAuditRow BuildMissingRow(
        ParameterAuditTarget target,
        IReadOnlyList<ParameterElementInfo> parameterElements,
        IReadOnlyList<ViewSchedule> schedules,
        IReadOnlyList<ParameterFilterElement> filters)
    {
        IReadOnlyList<ParameterElementInfo> matchingParameterElements = parameterElements
            .Where(info => string.Equals(info.Name, target.Name, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        HashSet<long> parameterElementIds = matchingParameterElements
            .Select(info => info.ElementId.Value)
            .ToHashSet();

        IReadOnlyList<string> scheduleNames = GetScheduleNamesUsingParameter(schedules, parameterElementIds);
        IReadOnlyList<string> filterNames = GetFilterNamesUsingParameter(filters, parameterElementIds);

        return new ProjectParameterAuditRow
        {
            ParameterName = target.Name,
            Status = "Brak w BindingMap",
            ExpectedSpec = target.ExpectedSpecLabel,
            FoundGuids = FormatGuids(matchingParameterElements),
            ExpectedGuid = target.ExpectedGuid,
            ScheduleCount = scheduleNames.Count,
            Schedules = string.Join("; ", scheduleNames),
            FilterCount = filterNames.Count,
            Filters = string.Join("; ", filterNames),
            Recommendation = "Nie znaleziono bindowanego parametru projektu o tej nazwie. Sprawdz, czy konflikt nie pochodzi z parametru rodzinnego albo nieaktywnych definicji."
        };
    }

    private static IReadOnlyList<BindingEntry> GetBindings(Document document, string parameterName)
    {
        List<BindingEntry> entries = [];
        BindingMap map = document.ParameterBindings;
        DefinitionBindingMapIterator iterator = map.ForwardIterator();
        iterator.Reset();

        while (iterator.MoveNext())
        {
            Definition? definition = iterator.Key;
            if (definition == null ||
                !string.Equals(definition.Name, parameterName, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            ElementBinding? binding = iterator.Current as ElementBinding;
            if (binding == null)
            {
                continue;
            }

            entries.Add(new BindingEntry(
                definition.Name,
                definition.GetDataType(),
                definition.GetGroupTypeId().TypeId,
                binding is InstanceBinding ? "Instance" : "Type",
                GetCategories(binding.Categories)));
        }

        return entries;
    }

    private static IReadOnlyList<Category> GetCategories(CategorySet categorySet)
    {
        List<Category> categories = [];
        foreach (Category category in categorySet)
        {
            categories.Add(category);
        }

        return categories;
    }

    private static ValueUsage CountValues(Document document, BindingEntry binding)
    {
        int elementsWithParameter = 0;
        int elementsWithValue = 0;
        HashSet<string> storageTypes = new(StringComparer.OrdinalIgnoreCase);

        foreach (Element element in CollectBoundElements(document, binding))
        {
            Parameter? parameter = element.LookupParameter(binding.Name);
            if (parameter?.Definition == null ||
                !string.Equals(parameter.Definition.Name, binding.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            elementsWithParameter++;
            storageTypes.Add(parameter.StorageType.ToString());
            if (HasNonEmptyValue(parameter))
            {
                elementsWithValue++;
            }
        }

        return new ValueUsage(elementsWithParameter, elementsWithValue, storageTypes);
    }

    private static IEnumerable<Element> CollectBoundElements(Document document, BindingEntry binding)
    {
        foreach (Category category in binding.Categories)
        {
            if (category.Id == ElementId.InvalidElementId)
            {
                continue;
            }

            FilteredElementCollector collector;
            try
            {
                collector = new FilteredElementCollector(document).OfCategoryId(category.Id);
            }
            catch
            {
                continue;
            }

            collector = string.Equals(binding.BindingKind, "Type", StringComparison.OrdinalIgnoreCase)
                ? collector.WhereElementIsElementType()
                : collector.WhereElementIsNotElementType();

            foreach (Element element in collector)
            {
                yield return element;
            }
        }
    }

    private static bool HasNonEmptyValue(Parameter parameter)
    {
        if (!parameter.HasValue)
        {
            return false;
        }

        return parameter.StorageType switch
        {
            StorageType.String => !string.IsNullOrWhiteSpace(parameter.AsString()),
            StorageType.ElementId => parameter.AsElementId() != ElementId.InvalidElementId,
            StorageType.Double or StorageType.Integer => true,
            _ => false
        };
    }

    private static IReadOnlyList<ParameterElementInfo> GetParameterElements(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ParameterElement))
            .Cast<ParameterElement>()
            .Select(ToParameterElementInfo)
            .Where(info => !string.IsNullOrWhiteSpace(info.Name))
            .ToList();
    }

    private static ParameterElementInfo ToParameterElementInfo(ParameterElement element)
    {
        Definition? definition = element.GetDefinition();
        string guid = element is SharedParameterElement sharedParameter
            ? sharedParameter.GuidValue.ToString()
            : string.Empty;

        return new ParameterElementInfo(
            element.Id,
            definition?.Name ?? element.Name,
            definition?.GetDataType(),
            guid);
    }

    private static IReadOnlyList<ParameterElementInfo> FindMatchingParameterElements(
        IReadOnlyList<ParameterElementInfo> parameterElements,
        BindingEntry binding)
    {
        return parameterElements
            .Where(info => string.Equals(info.Name, binding.Name, StringComparison.CurrentCultureIgnoreCase) &&
                           AreTypeIdsEqual(info.DataType, binding.DataType))
            .ToList();
    }

    private static IReadOnlyList<ViewSchedule> GetSchedules(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(schedule => !schedule.IsTemplate)
            .ToList();
    }

    private static IReadOnlyList<string> GetScheduleNamesUsingParameter(
        IReadOnlyList<ViewSchedule> schedules,
        HashSet<long> parameterElementIds)
    {
        if (parameterElementIds.Count == 0)
        {
            return [];
        }

        List<string> names = [];
        foreach (ViewSchedule schedule in schedules)
        {
            try
            {
                ScheduleDefinition definition = schedule.Definition;
                bool usesParameter = definition
                    .GetFieldOrder()
                    .Select(fieldId => definition.GetField(fieldId))
                    .Any(field => parameterElementIds.Contains(field.ParameterId.Value));

                if (usesParameter)
                {
                    names.Add(schedule.Name);
                }
            }
            catch
            {
                // Some internal schedules can reject field inspection.
            }
        }

        return names
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ParameterFilterElement> GetFilters(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .ToList();
    }

    private static IReadOnlyList<string> GetFilterNamesUsingParameter(
        IReadOnlyList<ParameterFilterElement> filters,
        HashSet<long> parameterElementIds)
    {
        if (parameterElementIds.Count == 0)
        {
            return [];
        }

        List<string> names = [];
        foreach (ParameterFilterElement filter in filters)
        {
            try
            {
                if (GetFilterParameterIds(filter).Any(id => parameterElementIds.Contains(id.Value)))
                {
                    names.Add(filter.Name);
                }
            }
            catch
            {
                // Complex or version-specific filter definitions are ignored instead of failing the report.
            }
        }

        return names
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<ElementId> GetFilterParameterIds(ParameterFilterElement filter)
    {
        foreach (ElementId parameterId in filter.GetElementFilterParameters())
        {
            yield return parameterId;
        }

        foreach (ElementId categoryId in filter.GetCategories())
        {
            foreach (ElementId parameterId in filter.GetElementFilterParametersForCategory(categoryId))
            {
                yield return parameterId;
            }
        }
    }

    private static string BuildStatus(bool hasTypeMismatch, bool hasGuidMismatch, int valuesCount, int scheduleCount, int filterCount)
    {
        if (!hasTypeMismatch && !hasGuidMismatch)
        {
            return "OK";
        }

        if (hasTypeMismatch && (valuesCount > 0 || scheduleCount > 0 || filterCount > 0))
        {
            return "Konflikt typu - w uzyciu";
        }

        if (hasTypeMismatch)
        {
            return "Konflikt typu - kandydat do usuniecia";
        }

        return "GUID inny niz z rodziny";
    }

    private static string BuildRecommendation(bool hasTypeMismatch, bool hasGuidMismatch, int valuesCount, int scheduleCount, int filterCount)
    {
        if (!hasTypeMismatch && !hasGuidMismatch)
        {
            return "Parametr projektu ma oczekiwany typ i GUID.";
        }

        if (hasTypeMismatch && (valuesCount > 0 || scheduleCount > 0 || filterCount > 0))
        {
            return "Nie usuwaj bez migracji. Parametr ma dane albo jest uzyty w zestawieniach/filtrach; najpierw przenies wartosci i popraw zaleznosci.";
        }

        if (hasTypeMismatch)
        {
            return "Prawdopodobnie mozna usunac parametr z Project Parameters i ponownie wczytac rodzine, ale najpierw sprawdz raport recznie.";
        }

        return "Typ jest zgodny, ale GUID nie pasuje do parametru z rodziny. Jesli tagi/harmonogramy musza mapowac BimObject, rozwaz migracje na parametr z oczekiwanym GUID.";
    }

    private static string FormatGuids(IReadOnlyList<ParameterElementInfo> parameterElements)
    {
        IReadOnlyList<string> guids = parameterElements
            .Select(info => info.Guid)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return guids.Count == 0 ? string.Empty : string.Join("; ", guids);
    }

    private static bool ContainsExpectedGuid(IReadOnlyList<ParameterElementInfo> parameterElements, string expectedGuid)
    {
        return parameterElements.Any(info => string.Equals(info.Guid, expectedGuid, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AreTypeIdsEqual(ForgeTypeId? first, ForgeTypeId? second)
    {
        return first != null && second != null && string.Equals(first.TypeId, second.TypeId, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSpecLabel(ForgeTypeId? specTypeId)
    {
        if (specTypeId == null)
        {
            return string.Empty;
        }

        if (specTypeId == SpecTypeId.Length)
        {
            return "Length";
        }

        if (specTypeId == SpecTypeId.Number)
        {
            return "Number";
        }

        if (specTypeId == SpecTypeId.String.Text)
        {
            return "Text";
        }

        if (specTypeId == SpecTypeId.Int.Integer)
        {
            return "Integer";
        }

        if (specTypeId == SpecTypeId.Boolean.YesNo)
        {
            return "Yes/No";
        }

        return specTypeId.TypeId;
    }

    private static string Escape(string? value)
    {
        string normalized = value ?? string.Empty;
        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }

    private sealed record ParameterAuditTarget(
        string Name,
        ForgeTypeId ExpectedSpecTypeId,
        string ExpectedSpecLabel,
        string ExpectedGuid);

    private sealed record BindingEntry(
        string Name,
        ForgeTypeId DataType,
        string GroupTypeId,
        string BindingKind,
        IReadOnlyList<Category> Categories);

    private sealed record ParameterElementInfo(
        ElementId ElementId,
        string Name,
        ForgeTypeId? DataType,
        string Guid);

    private sealed record ValueUsage(
        int ElementsWithParameter,
        int ElementsWithValue,
        IReadOnlySet<string> StorageTypes);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AccessoryTerminalNumbering.Models;

namespace HCB.RevitAddin.Features.AccessoryTerminalNumbering;

public sealed class AccessoryTerminalNumberingService
{
    public IReadOnlyList<Element> CollectTargets(Document document, ICollection<ElementId> selectedIds)
    {
        if (selectedIds.Count > 0)
        {
            return selectedIds
                .Select(document.GetElement)
                .Where(IsSupported)
                .Where(element => !IsFabricAir(document, element!))
                .Cast<Element>()
                .ToList();
        }

        return new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_DuctAccessory)
            .WhereElementIsNotElementType()
            .Where(element => !IsFabricAir(document, element))
            .ToList();
    }

    public IReadOnlyList<string> GetWritableStringTargetParameters(IEnumerable<Element> elements)
    {
        List<Element> targetList = elements.ToList();
        if (targetList.Count == 0)
        {
            return [];
        }

        HashSet<string>? commonNames = null;
        foreach (Element element in targetList)
        {
            HashSet<string> elementNames = element.Parameters
                .Cast<Parameter>()
                .Where(parameter => !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                .Select(parameter => parameter.Definition?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

            commonNames = commonNames == null
                ? elementNames
                : commonNames.Intersect(elementNames, StringComparer.CurrentCultureIgnoreCase).ToHashSet(StringComparer.CurrentCultureIgnoreCase);

            if (commonNames.Count == 0)
            {
                break;
            }
        }

        return commonNames == null
            ? []
            : commonNames
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }

    public AccessoryTerminalNumberingResult Apply(Document document, IEnumerable<Element> elements, AccessoryTerminalNumberingOptions options)
    {
        Dictionary<AccessoryGroupKey, List<Element>> groups = GroupElements(document, elements);
        AccessoryTerminalNumberingResult result = new()
        {
            SystemsCount = groups.Keys
                .Select(key => key.SystemValue)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Count(),
            TargetParameterName = options.TargetParameterName
        };

        using Transaction transaction = new(document, "Duct Accessory Numbering");
        transaction.Start();

        int currentNumber = options.UseLastProjectNumber
            ? GetNextProjectNumber(document, options.TargetParameterName)
            : options.StartNumber;

        foreach ((AccessoryGroupKey key, List<Element> groupElements) in groups.OrderBy(pair => pair.Key, AccessoryGroupKeyComparer.Instance))
        {
            string value = FormatAccessoryNumber(key.TypeMark, key.SystemValue, currentNumber);
            int writtenCount = 0;
            foreach (Element element in groupElements)
            {
                if (TrySetPositionNumber(element, options.TargetParameterName, value))
                {
                    writtenCount++;
                }
            }

            result.DuctAccessoryCount += writtenCount;
            result.TotalCount += writtenCount;
            result.SharedNumberCount += Math.Max(0, writtenCount - 1);
            currentNumber++;
        }

        transaction.Commit();
        result.Messages.Add("Numeracja dotyczy tylko Duct Accessory.");
        result.Messages.Add("Format numeru: TypeMark.HC_System.KolejnyNumer.");
        result.Messages.Add("Elementy o tym samym ukladzIe w obrebie tego samego HC_System dostaja wspolny numer.");
        result.Messages.Add("Pominieto typy z Manufacturer = 'FabricAir'.");
        if (result.TotalCount == 0)
        {
            result.Messages.Add("Nie zapisano zadnej wartosci. Wybrany parametr docelowy nie byl dostepny do zapisu na elementach.");
        }

        return result;
    }

    private static int GetNextProjectNumber(Document document, string targetParameterName)
    {
        int maxNumber = 0;

        IEnumerable<Element> elements = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_DuctAccessory)
            .WhereElementIsNotElementType();

        foreach (Element element in elements)
        {
            Parameter? parameter = element.LookupParameter(targetParameterName);
            string text = (parameter?.AsString() ?? parameter?.AsValueString() ?? string.Empty).Trim();
            int? parsed = ExtractLastNumber(text);
            if (parsed.HasValue && parsed.Value > maxNumber)
            {
                maxNumber = parsed.Value;
            }
        }

        return maxNumber + 1;
    }

    private static int? ExtractLastNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(['.', '-', '/', ' ', '_'], StringSplitOptions.RemoveEmptyEntries);
        for (int index = parts.Length - 1; index >= 0; index--)
        {
            if (int.TryParse(parts[index], out int value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsSupported(Element? element)
    {
        return GetCategoryId(element) == (long)BuiltInCategory.OST_DuctAccessory;
    }

    private static Dictionary<AccessoryGroupKey, List<Element>> GroupElements(Document document, IEnumerable<Element> elements)
    {
        Dictionary<AccessoryGroupKey, List<Element>> groups = new(AccessoryGroupKeyComparer.Instance);

        foreach (Element element in elements.Where(IsSupported))
        {
            AccessoryGroupKey key = new(
                NormalizeValue(GetTypeMark(document, element)),
                NormalizeValue(GetText(element, "HC_System")),
                NormalizeValue(GetText(element, "Size")),
                NormalizeValue(GetTypeName(document, element)));

            groups.TryAdd(key, []);
            groups[key].Add(element);
        }

        return groups;
    }

    private static string FormatAccessoryNumber(string typeMark, string systemValue, int number)
    {
        List<string> parts = [];

        if (!string.IsNullOrWhiteSpace(typeMark))
        {
            parts.Add(typeMark);
        }

        if (!string.IsNullOrWhiteSpace(systemValue))
        {
            parts.Add(systemValue);
        }

        parts.Add(number.ToString());
        return string.Join('.', parts);
    }

    private static string GetTypeMark(Document document, Element element)
    {
        Element? typeElement = document.GetElement(element.GetTypeId());
        Parameter? typeMarkParameter = typeElement?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
        string typeMark = (typeMarkParameter?.AsString() ?? typeMarkParameter?.AsValueString() ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(typeMark))
        {
            return typeMark;
        }

        return GetTypeName(document, element);
    }

    private static string GetTypeName(Document document, Element element)
    {
        Element? symbol = document.GetElement(element.GetTypeId());
        return (symbol?.Name ?? element.Name ?? string.Empty).Trim();
    }

    private static bool IsFabricAir(Document document, Element element)
    {
        Element? symbol = document.GetElement(element.GetTypeId());
        Parameter? manufacturerParameter = symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER);
        string manufacturer = manufacturerParameter?.AsString() ?? string.Empty;
        return string.Equals(manufacturer.Trim(), "FabricAir", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetText(Element element, string parameterName)
    {
        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || !parameter.HasValue)
        {
            return string.Empty;
        }

        return (parameter.AsString() ?? parameter.AsValueString() ?? string.Empty).Trim();
    }

    private static bool TrySetPositionNumber(Element element, string targetParameterName, string value)
    {
        Parameter? parameter = element.LookupParameter(targetParameterName);
        if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
        {
            return false;
        }

        parameter.Set(value);
        return true;
    }

    private static string NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static long? GetCategoryId(Element? element)
    {
        return element?.Category?.Id?.Value;
    }

    private sealed record AccessoryGroupKey(string TypeMark, string SystemValue, string Size, string TypeName);

    private sealed class AccessoryGroupKeyComparer : IComparer<AccessoryGroupKey>, IEqualityComparer<AccessoryGroupKey>
    {
        public static AccessoryGroupKeyComparer Instance { get; } = new();

        public int Compare(AccessoryGroupKey? x, AccessoryGroupKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int systemComparison = string.Compare(x.SystemValue, y.SystemValue, StringComparison.CurrentCultureIgnoreCase);
            if (systemComparison != 0)
            {
                return systemComparison;
            }

            int typeMarkComparison = string.Compare(x.TypeMark, y.TypeMark, StringComparison.CurrentCultureIgnoreCase);
            if (typeMarkComparison != 0)
            {
                return typeMarkComparison;
            }

            int typeNameComparison = string.Compare(x.TypeName, y.TypeName, StringComparison.CurrentCultureIgnoreCase);
            if (typeNameComparison != 0)
            {
                return typeNameComparison;
            }

            return string.Compare(x.Size, y.Size, StringComparison.CurrentCultureIgnoreCase);
        }

        public bool Equals(AccessoryGroupKey? x, AccessoryGroupKey? y)
        {
            return Compare(x, y) == 0;
        }

        public int GetHashCode(AccessoryGroupKey obj)
        {
            return HashCode.Combine(
                obj.TypeMark.ToUpperInvariant(),
                obj.SystemValue.ToUpperInvariant(),
                obj.Size.ToUpperInvariant(),
                obj.TypeName.ToUpperInvariant());
        }
    }
}

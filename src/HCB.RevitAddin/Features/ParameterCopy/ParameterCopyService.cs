using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ParameterCopy.Models;

namespace HCB.RevitAddin.Features.ParameterCopy;

public sealed class ParameterCopyService
{
    private static readonly BuiltInCategory[] SupportedCategories =
    [
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_FlexDuctCurves,
        BuiltInCategory.OST_DuctFitting,
        BuiltInCategory.OST_DuctAccessory,
        BuiltInCategory.OST_DuctTerminal,
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_FlexPipeCurves,
        BuiltInCategory.OST_PipeFitting,
        BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_PlumbingEquipment,
        BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_CommunicationDevices,
        BuiltInCategory.OST_PlumbingFixtures
    ];

    private readonly record struct ParameterSignature(StorageType StorageType, string DataTypeId);
    private enum CopyAttemptStatus
    {
        Success = 0,
        EmptySource = 1,
        FailedToSet = 2
    }

    public IReadOnlyList<Element> GetSelectedSupportedElements(Document document, ICollection<ElementId> selectedElementIds)
    {
        return selectedElementIds
            .Select(document.GetElement)
            .Where(IsSupportedElement)
            .DistinctBy(element => element!.Id.Value)
            .Cast<Element>()
            .ToList();
    }

    public IReadOnlyList<Element> GetAllSupportedElements(Document document)
    {
        List<Element> elements = [];
        foreach (BuiltInCategory category in SupportedCategories)
        {
            elements.AddRange(new FilteredElementCollector(document)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(IsSupportedElement));
        }

        return elements
            .DistinctBy(element => element.Id.Value)
            .ToList();
    }

    public ParameterCopyCatalog BuildCatalog(IReadOnlyList<Element> elements)
    {
        Dictionary<string, HashSet<ParameterSignature>> sourceSignatures = BuildSignatureMap(elements, writableOnly: false);
        Dictionary<string, HashSet<ParameterSignature>> targetSignatures = BuildSignatureMap(elements, writableOnly: true);
        Dictionary<string, IReadOnlyList<string>> compatibleTargets = new(StringComparer.CurrentCultureIgnoreCase);
        List<string> sourceNames = new();

        foreach ((string sourceName, HashSet<ParameterSignature> sourceSet) in sourceSignatures
                     .OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            List<string> compatible = targetSignatures
                .Where(pair => !string.Equals(pair.Key, sourceName, StringComparison.CurrentCultureIgnoreCase) &&
                               pair.Value.Overlaps(sourceSet))
                .Select(pair => pair.Key)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (compatible.Count == 0)
            {
                continue;
            }

            sourceNames.Add(sourceName);
            compatibleTargets[sourceName] = compatible;
        }

        return new ParameterCopyCatalog
        {
            ElementCount = elements.Count,
            SourceParameterNames = sourceNames,
            CompatibleTargetNamesBySource = compatibleTargets
        };
    }

    public ParameterCopyResult Preview(IReadOnlyList<Element> elements, ParameterCopyOptions options)
    {
        return Evaluate(null, elements, options, writeValues: false);
    }

    public ParameterCopyResult Apply(Document document, IReadOnlyList<Element> elements, ParameterCopyOptions options)
    {
        return Evaluate(document, elements, options, writeValues: true);
    }

    private static ParameterCopyResult Evaluate(
        Document? document,
        IReadOnlyList<Element> elements,
        ParameterCopyOptions options,
        bool writeValues)
    {
        ParameterCopyResult result = new()
        {
            ScopedElementCount = elements.Count
        };

        if (elements.Count == 0)
        {
            return result;
        }

        Transaction? transaction = null;
        if (writeValues && document != null)
        {
            transaction = new Transaction(document, "Copy Parameter Values");
            transaction.Start();
        }

        foreach (Element element in elements)
        {
            Parameter? sourceParameter = element.LookupParameter(options.SourceParameterName);
            if (!TryGetSignature(sourceParameter, out ParameterSignature sourceSignature))
            {
                result.MissingSourceCount++;
                continue;
            }

            Parameter? targetParameter = element.LookupParameter(options.TargetParameterName);
            if (targetParameter == null)
            {
                result.MissingTargetCount++;
                continue;
            }

            if (targetParameter.IsReadOnly)
            {
                result.ReadOnlyTargetCount++;
                continue;
            }

            if (!TryGetSignature(targetParameter, out ParameterSignature targetSignature))
            {
                result.MissingTargetCount++;
                continue;
            }

            if (!sourceSignature.Equals(targetSignature))
            {
                result.IncompatibleTypeCount++;
                continue;
            }

            CopyAttemptStatus copyStatus = TryCopyValue(sourceParameter, targetParameter, writeValues);
            if (copyStatus == CopyAttemptStatus.EmptySource)
            {
                result.EmptySourceCount++;
                continue;
            }

            if (copyStatus == CopyAttemptStatus.FailedToSet)
            {
                result.FailedSetCount++;
                continue;
            }

            result.CopiedCount++;
        }

        if (transaction != null)
        {
            transaction.Commit();
        }

        return result;
    }

    private static Dictionary<string, HashSet<ParameterSignature>> BuildSignatureMap(
        IEnumerable<Element> elements,
        bool writableOnly)
    {
        Dictionary<string, HashSet<ParameterSignature>> map = new(StringComparer.CurrentCultureIgnoreCase);

        foreach (Parameter parameter in elements
                     .SelectMany(element => element.Parameters.Cast<Parameter>())
                     .Where(parameter => !writableOnly || !parameter.IsReadOnly))
        {
            if (!TryGetSignature(parameter, out ParameterSignature signature))
            {
                continue;
            }

            string name = GetParameterName(parameter);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!map.TryGetValue(name, out HashSet<ParameterSignature>? signatures))
            {
                signatures = new HashSet<ParameterSignature>();
                map[name] = signatures;
            }

            signatures.Add(signature);
        }

        return map;
    }

    private static CopyAttemptStatus TryCopyValue(Parameter sourceParameter, Parameter targetParameter, bool writeValues)
    {
        try
        {
            switch (sourceParameter.StorageType)
            {
                case StorageType.String:
                {
                    string value = sourceParameter.AsString() ?? string.Empty;
                    return !writeValues || targetParameter.Set(value)
                        ? CopyAttemptStatus.Success
                        : CopyAttemptStatus.FailedToSet;
                }
                case StorageType.Double:
                {
                    if (!sourceParameter.HasValue)
                    {
                        return CopyAttemptStatus.EmptySource;
                    }

                    double value = sourceParameter.AsDouble();
                    return !writeValues || targetParameter.Set(value)
                        ? CopyAttemptStatus.Success
                        : CopyAttemptStatus.FailedToSet;
                }
                case StorageType.Integer:
                {
                    if (!sourceParameter.HasValue)
                    {
                        return CopyAttemptStatus.EmptySource;
                    }

                    int value = sourceParameter.AsInteger();
                    return !writeValues || targetParameter.Set(value)
                        ? CopyAttemptStatus.Success
                        : CopyAttemptStatus.FailedToSet;
                }
                default:
                    return CopyAttemptStatus.FailedToSet;
            }
        }
        catch
        {
            return CopyAttemptStatus.FailedToSet;
        }
    }

    private static bool TryGetSignature(Parameter? parameter, out ParameterSignature signature)
    {
        signature = default;
        if (!IsCopyableParameter(parameter) || parameter == null)
        {
            return false;
        }

        string dataTypeId = parameter.Definition?.GetDataType()?.TypeId ?? string.Empty;
        signature = new ParameterSignature(parameter.StorageType, dataTypeId);
        return true;
    }

    private static string GetParameterName(Parameter parameter)
    {
        return parameter.Definition?.Name?.Trim() ?? string.Empty;
    }

    private static bool IsCopyableParameter(Parameter? parameter)
    {
        return parameter is { Definition: not null } &&
               parameter.StorageType is StorageType.String or StorageType.Double or StorageType.Integer;
    }

    private static bool IsSupportedElement(Element? element)
    {
        if (element?.Category == null)
        {
            return false;
        }

        long categoryId = element.Category.Id.Value;
        return SupportedCategories.Any(category => categoryId == (long)category);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ManualNumbering.Models;

namespace HCB.RevitAddin.Features.ManualNumbering;

public sealed class ManualNumberingService
{
    public IReadOnlyList<string> GetWritableStringParameterNames(IEnumerable<Element> elements)
    {
        return elements
            .SelectMany(element => element.Parameters.Cast<Parameter>())
            .Where(parameter => !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
            .Select(parameter => parameter.Definition?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    public ManualNumberingResult Apply(Document document, IEnumerable<Element> elements, ManualNumberingOptions options)
    {
        ManualNumberingResult result = new();
        List<Element> targets = elements.ToList();
        int currentNumber = options.UseLastProjectNumber
            ? GetNextProjectNumber(document, options.ParameterName, targets)
            : options.StartNumber;

        using Transaction transaction = new(document, "Manual Numbering");
        transaction.Start();

        foreach (Element element in targets)
        {
            Parameter? parameter = element.LookupParameter(options.ParameterName);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                result.SkippedCount++;
                result.Messages.Add($"Element {element.Id.Value}: pominiety, brak zapisywalnego parametru '{options.ParameterName}'.");
                continue;
            }

            parameter.Set($"{options.Prefix}{currentNumber}{options.Suffix}");
            currentNumber++;
            result.UpdatedCount++;
        }

        transaction.Commit();

        if (result.Messages.Count == 0)
        {
            result.Messages.Add("Numeracja zakonczona bez bledow.");
        }

        return result;
    }

    private static ElementId ResolveParameterId(IReadOnlyList<Element> elements, string parameterName)
    {
        foreach (Element element in elements)
        {
            Parameter? parameter = element.LookupParameter(parameterName);
            if (parameter != null && parameter.Id != ElementId.InvalidElementId)
            {
                return parameter.Id;
            }
        }

        return ElementId.InvalidElementId;
    }

    private static int GetNextProjectNumber(Document document, string parameterName, IReadOnlyList<Element> sampleElements)
    {
        int maxNumber = 0;

        FilteredElementCollector collector = new FilteredElementCollector(document)
            .WhereElementIsNotElementType();

        // Narrow the scan inside the Revit engine. Running LookupParameter over every element in
        // the model is orders of magnitude slower than letting Revit skip elements that do not
        // carry the parameter at all.
        ElementId parameterId = ResolveParameterId(sampleElements, parameterName);
        if (parameterId != ElementId.InvalidElementId)
        {
            collector = collector.WherePasses(new ElementParameterFilter(new HasValueFilterRule(parameterId)));
        }

        foreach (Element element in collector)
        {
            Parameter? parameter = element.LookupParameter(parameterName);
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
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ParameterCalculator.Models;

namespace HCB.RevitAddin.Features.ParameterCalculator;

public sealed class ParameterCalculatorService
{
    public IReadOnlyList<Element> GetBaseElements(Document document, ICollection<ElementId> selectedElementIds)
    {
        if (selectedElementIds.Count > 0)
        {
            return selectedElementIds
                .Select(document.GetElement)
                .Where(IsSupportedElement)
                .DistinctBy(element => element!.Id.Value)
                .Cast<Element>()
                .ToList();
        }

        return new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .Where(IsSupportedElement)
            .ToList();
    }

    public IReadOnlyList<string> GetNumericParameterNames(IReadOnlyList<Element> elements)
    {
        return elements
            .SelectMany(element => element.Parameters.Cast<Parameter>())
            .Where(IsNumericParameter)
            .Select(GetParameterName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetWritableNumericTargetParameterNames(IReadOnlyList<Element> elements)
    {
        return elements
            .SelectMany(element => element.Parameters.Cast<Parameter>())
            .Where(parameter => IsNumericParameter(parameter) && !parameter.IsReadOnly)
            .Select(GetParameterName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ParameterCalculatorResult Apply(Document document, IReadOnlyList<Element> baseElements, ParameterCalculatorOptions options)
    {
        ParameterCalculatorResult result = new();
        List<Element> applicableElements = baseElements
            .Where(element => HasAllRequiredParameters(element, options))
            .ToList();

        if (applicableElements.Count == 0)
        {
            result.SkippedCount = baseElements.Count;
            return result;
        }

        using Transaction transaction = new(document, "Parameter Calculator");
        transaction.Start();

        foreach (Element element in applicableElements)
        {
            Parameter? targetParameter = element.LookupParameter(options.TargetParameterName);
            if (targetParameter == null || targetParameter.IsReadOnly || !IsNumericParameter(targetParameter))
            {
                result.SkippedCount++;
                continue;
            }

            if (!TryEvaluateFormula(element, options.Operands, out double value))
            {
                result.SkippedCount++;
                continue;
            }

            double roundedValue = Math.Round(value, options.DecimalPlaces, MidpointRounding.AwayFromZero);
            if (!TrySetNumericValue(targetParameter, roundedValue))
            {
                result.SkippedCount++;
                continue;
            }

            result.UpdatedCount++;
        }

        result.SkippedCount += baseElements.Count - applicableElements.Count;
        transaction.Commit();
        return result;
    }

    private static bool HasAllRequiredParameters(Element element, ParameterCalculatorOptions options)
    {
        Parameter? targetParameter = element.LookupParameter(options.TargetParameterName);
        if (targetParameter == null || targetParameter.IsReadOnly || !IsNumericParameter(targetParameter))
        {
            return false;
        }

        return options.Operands.All(operand =>
        {
            Parameter? parameter = element.LookupParameter(operand.ParameterName);
            return IsNumericParameter(parameter) && parameter!.HasValue;
        });
    }

    private static bool TryEvaluateFormula(Element element, IReadOnlyList<FormulaOperand> operands, out double value)
    {
        value = 0;
        if (operands.Count < 2)
        {
            return false;
        }

        Parameter? firstParameter = element.LookupParameter(operands[0].ParameterName);
        if (!TryGetNumericValue(firstParameter, out value))
        {
            return false;
        }

        for (int index = 1; index < operands.Count; index++)
        {
            FormulaOperand operand = operands[index];
            Parameter? parameter = element.LookupParameter(operand.ParameterName);
            if (!TryGetNumericValue(parameter, out double operandValue))
            {
                return false;
            }

            switch (operand.OperatorSymbol)
            {
                case "+":
                    value += operandValue;
                    break;
                case "-":
                    value -= operandValue;
                    break;
                case "*":
                    value *= operandValue;
                    break;
                case "/":
                    if (Math.Abs(operandValue) < 1e-9)
                    {
                        return false;
                    }

                    value /= operandValue;
                    break;
                default:
                    return false;
            }
        }

        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool TrySetNumericValue(Parameter parameter, double value)
    {
        switch (parameter.StorageType)
        {
            case StorageType.Double:
                return parameter.Set(value);
            case StorageType.Integer:
                return parameter.Set((int)Math.Round(value, 0, MidpointRounding.AwayFromZero));
            default:
                return false;
        }
    }

    private static bool TryGetNumericValue(Parameter? parameter, out double value)
    {
        value = 0;
        if (!IsNumericParameter(parameter) || parameter == null || !parameter.HasValue)
        {
            return false;
        }

        switch (parameter.StorageType)
        {
            case StorageType.Double:
                value = parameter.AsDouble();
                return true;
            case StorageType.Integer:
                value = parameter.AsInteger();
                return true;
            default:
                return false;
        }
    }

    private static bool IsNumericParameter(Parameter? parameter)
    {
        return parameter?.StorageType is StorageType.Double or StorageType.Integer;
    }

    private static string GetParameterName(Parameter parameter)
    {
        return parameter.Definition?.Name?.Trim() ?? string.Empty;
    }

    private static bool IsSupportedElement(Element? element)
    {
        return element is { Category: not null };
    }
}

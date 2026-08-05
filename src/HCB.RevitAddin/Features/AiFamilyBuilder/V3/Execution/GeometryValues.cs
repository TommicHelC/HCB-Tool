using System;
using System.Text.Json;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Validation;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Wspolne rozwiazywanie wartosci geometrii (mm) dla DTO v3: stala, referencja do parametru lub wyrazenie.
/// </summary>
public static class GeometryValues
{
    /// <summary>Wartosci bazowe (mm) z pierwszego typu — uzywane do wstepnego rozmiaru geometrii.</summary>
    public static Dictionary<string, double> BuildBaseline(AiFamilyBuildRequest request)
    {
        Dictionary<string, double> baseline = new(StringComparer.OrdinalIgnoreCase);
        TypeSpec? firstType = request.Types.FirstOrDefault();
        if (firstType == null)
        {
            return baseline;
        }

        foreach ((string name, JsonElement value) in firstType.Values)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            {
                baseline[name] = number;
            }
        }

        return baseline;
    }

    public static double? ResolveMillimeters(ValueRefSpec? spec, IReadOnlyDictionary<string, double> baseline)
    {
        if (spec == null || spec.IsEmpty)
        {
            return null;
        }

        if (spec.HasValue)
        {
            return spec.Value;
        }

        if (spec.HasRef)
        {
            return baseline.TryGetValue(spec.Ref!, out double value) ? value : null;
        }

        if (spec.HasExpr &&
            ExpressionParser.TryEvaluate(spec.Expr, name => baseline.TryGetValue(name, out double v) ? v : null, out double result, out _))
        {
            return result;
        }

        return null;
    }

    public static bool TryGetRefParameter(ValueRefSpec? spec, out string? parameterName)
    {
        parameterName = spec?.Ref?.Trim();
        return !string.IsNullOrWhiteSpace(parameterName);
    }

    public static double ToFeet(double millimeters) =>
        UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
}

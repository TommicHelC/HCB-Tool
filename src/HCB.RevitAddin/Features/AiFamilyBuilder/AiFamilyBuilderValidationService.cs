using System;
using System.Text.Json;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;

namespace HCB.RevitAddin.Features.AiFamilyBuilder;

public sealed class AiFamilyBuilderValidationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AiFamilyBuilderValidationResult ValidateJson(string jsonText)
    {
        AiFamilyBuilderValidationResult result = new();
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            result.Errors.Add("Pole JSON jest puste.");
            return result;
        }

        FamilyDefinitionSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<FamilyDefinitionSpec>(jsonText, JsonOptions);
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Niepoprawny JSON: {ex.Message}");
            return result;
        }

        if (spec == null)
        {
            result.Errors.Add("Nie udalo sie zdeserializowac JSON.");
            return result;
        }

        ValidateSpec(spec, result);
        return result;
    }

    public void ValidateSpec(FamilyDefinitionSpec spec, AiFamilyBuilderValidationResult result)
    {
        HashSet<string> parameterNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (FamilyParameterSpec parameter in spec.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                result.Errors.Add("Znaleziono parametr bez nazwy.");
                continue;
            }

            if (!parameterNames.Add(parameter.Name))
            {
                result.Errors.Add($"Powielona nazwa parametru: {parameter.Name}");
            }

            if (!IsSupportedParameterKind(GetParameterKind(parameter)))
            {
                result.Errors.Add($"Nieobslugiwany kind/source parametru '{GetParameterKind(parameter)}' dla {parameter.Name}");
            }

            if (string.Equals(GetParameterKind(parameter), "family", StringComparison.OrdinalIgnoreCase) &&
                !IsSupportedSpecType(GetParameterSpecType(parameter)))
            {
                result.Errors.Add($"Nieobslugiwany spec_type/data_type '{GetParameterSpecType(parameter)}' dla {parameter.Name}");
            }

            if (!IsSupportedScope(parameter))
            {
                result.Errors.Add($"Nieobslugiwany scope parametru '{parameter.Scope}' dla {parameter.Name}");
            }
        }

        HashSet<string> typeNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (FamilyTypeSpec type in spec.Types)
        {
            if (string.IsNullOrWhiteSpace(type.Name))
            {
                result.Errors.Add("Znaleziono typ bez nazwy.");
                continue;
            }

            if (!typeNames.Add(type.Name))
            {
                result.Errors.Add($"Powielona nazwa typu: {type.Name}");
            }

            foreach (string valueName in type.Values.Keys)
            {
                if (!parameterNames.Contains(valueName))
                {
                    result.Warnings.Add($"Typ '{type.Name}' ustawia parametr, ktory nie jest zadeklarowany w parameters: {valueName}");
                }
            }
        }

        HashSet<string> referenceNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReferencePlaneSpec plane in spec.ReferencePlanes)
        {
            if (string.IsNullOrWhiteSpace(plane.Name))
            {
                result.Errors.Add("Znaleziono reference plane bez nazwy.");
                continue;
            }

            if (!referenceNames.Add(plane.Name))
            {
                result.Errors.Add($"Powielona nazwa reference plane: {plane.Name}");
            }

            if (!IsSupportedOrientation(plane.Orientation))
            {
                result.Errors.Add($"Nieobslugiwana orientacja reference plane '{plane.Orientation}' dla {plane.Name}");
            }

            if (!string.IsNullOrWhiteSpace(plane.View) && !IsSupportedWorkingView(plane.View))
            {
                result.Errors.Add($"Nieobslugiwany view '{plane.View}' dla reference plane {plane.Name}");
            }

            if (!string.IsNullOrWhiteSpace(plane.LineDirection) && !IsSupportedLineDirection(plane.LineDirection))
            {
                result.Errors.Add($"Nieobslugiwany line_direction '{plane.LineDirection}' dla reference plane {plane.Name}");
            }
        }

        foreach (DimensionConstraintSpec dimension in GetDimensionSpecs(spec))
        {
            if (string.IsNullOrWhiteSpace(dimension.Parameter))
            {
                result.Errors.Add($"Constraint '{dimension.Name}' nie ma przypisanego parametru.");
            }
            else if (!parameterNames.Contains(dimension.Parameter))
            {
                result.Errors.Add($"Constraint '{dimension.Name}' odwoluje sie do nieistniejacego parametru '{dimension.Parameter}'.");
            }

            if (!referenceNames.Contains(dimension.ReferenceA) || !referenceNames.Contains(dimension.ReferenceB))
            {
                result.Errors.Add($"Constraint '{dimension.Name}' odwoluje sie do nieistniejacych reference planes.");
            }

            if (!string.IsNullOrWhiteSpace(dimension.View) && !IsSupportedWorkingView(dimension.View))
            {
                result.Errors.Add($"Constraint '{dimension.Name}' ma nieobslugiwany view '{dimension.View}'.");
            }
        }

        foreach (GeometryOperationSpec operation in spec.Geometry)
        {
            string operationKind = GetGeometryOperationKind(operation);
            if (!IsSupportedGeometryOperation(operationKind))
            {
                result.Errors.Add($"Nieobslugiwana operacja geometrii: {operationKind}");
                continue;
            }

            ValidateGeometryOperation(operation, parameterNames, result);
        }
    }

    private static void ValidateGeometryOperation(GeometryOperationSpec operation, IReadOnlySet<string> parameterNames, AiFamilyBuilderValidationResult result)
    {
        string operationKind = GetGeometryOperationKind(operation);
        switch (operationKind)
        {
            case "extrusion_rectangle":
            case "void_rectangle":
                RequireValue(operation.Width, operationKind, "width", parameterNames, result);
                RequireValue(operation.Height, operationKind, "height", parameterNames, result);
                RequireValue(operation.Depth, operationKind, "depth", parameterNames, result);
                break;

            case "extrusion_circle":
            case "void_circle":
                RequireValue(operation.Radius, operationKind, "radius", parameterNames, result);
                RequireValue(operation.Depth, operationKind, "depth", parameterNames, result);
                break;

            case "blend_rectangle":
                RequireValue(operation.TopWidth, operationKind, "top_width", parameterNames, result);
                RequireValue(operation.TopHeight, operationKind, "top_height", parameterNames, result);
                RequireValue(operation.BottomWidth, operationKind, "bottom_width", parameterNames, result);
                RequireValue(operation.BottomHeight, operationKind, "bottom_height", parameterNames, result);
                RequireValue(operation.Depth, operationKind, "depth", parameterNames, result);
                break;

            case "rectangular_prism":
                RequireParameterReference(operation.Width, operationKind, "width", parameterNames, result);
                RequireParameterReference(operation.Height, operationKind, "height", parameterNames, result);
                RequireValue(operation.Depth, operationKind, "depth", parameterNames, result);
                break;
        }
    }

    private static void RequireValue(JsonElement value, string op, string fieldName, IReadOnlySet<string> parameterNames, AiFamilyBuilderValidationResult result)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string raw = value.GetString() ?? string.Empty;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return;
            }

            if (parameterNames.Contains(raw))
            {
                return;
            }
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("value", out JsonElement objectValue) &&
                objectValue.ValueKind == JsonValueKind.Number)
            {
                return;
            }

            if (value.TryGetProperty("ref", out JsonElement refElement) &&
                refElement.ValueKind == JsonValueKind.String &&
                parameterNames.Contains(refElement.GetString() ?? string.Empty))
            {
                return;
            }
        }

        result.Errors.Add($"Operacja '{op}' ma niepoprawne pole '{fieldName}'.");
    }

    private static void RequireParameterReference(JsonElement value, string op, string fieldName, IReadOnlySet<string> parameterNames, AiFamilyBuilderValidationResult result)
    {
        if (TryGetParameterReference(value, out string? parameterName) &&
            parameterName != null &&
            parameterNames.Contains(parameterName))
        {
            return;
        }

        result.Errors.Add($"Operacja '{op}' wymaga pola '{fieldName}' jako referencji do parametru, np. {{ \"ref\": \"Width\" }}.");
    }

    private static bool IsSupportedParameterKind(string value)
        => value.Trim().Equals("family", StringComparison.OrdinalIgnoreCase) ||
           value.Trim().Equals("shared", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedSpecType(string value)
        => value.Trim().ToLowerInvariant() is
            "text" or "yesno" or "integer" or "number" or "length" or "area" or "volume" or "angle" or "material";

    private static bool IsSupportedOrientation(string value)
        => value.Trim().ToLowerInvariant() is "vertical" or "horizontal";

    private static bool IsSupportedWorkingView(string value)
        => value.Trim().ToLowerInvariant() is "plan" or "elevation";

    private static bool IsSupportedLineDirection(string value)
        => value.Trim().ToLowerInvariant() is "vertical" or "horizontal";

    private static bool IsSupportedGeometryOperation(string value)
        => value.Trim().ToLowerInvariant() is
            "extrusion_rectangle" or "void_rectangle" or "extrusion_circle" or "void_circle" or "blend_rectangle" or "rectangular_prism";

    private static bool IsSupportedScope(FamilyParameterSpec parameter)
    {
        return string.IsNullOrWhiteSpace(parameter.Scope) ||
               parameter.Scope.Trim().ToLowerInvariant() is "type" or "instance";
    }

    private static string GetParameterKind(FamilyParameterSpec parameter)
    {
        return string.IsNullOrWhiteSpace(parameter.Source)
            ? parameter.Kind
            : parameter.Source!;
    }

    private static string GetParameterSpecType(FamilyParameterSpec parameter)
    {
        return string.IsNullOrWhiteSpace(parameter.DataType)
            ? parameter.SpecType
            : parameter.DataType!;
    }

    private static string GetGeometryOperationKind(GeometryOperationSpec operation)
    {
        return string.IsNullOrWhiteSpace(operation.Kind)
            ? operation.Op.Trim().ToLowerInvariant()
            : operation.Kind.Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<DimensionConstraintSpec> GetDimensionSpecs(FamilyDefinitionSpec spec)
    {
        if (spec.Constraints.Count == 0)
        {
            return spec.Dimensions;
        }

        if (spec.Dimensions.Count == 0)
        {
            return spec.Constraints;
        }

        return spec.Dimensions.Concat(spec.Constraints).ToList();
    }

    private static bool TryGetParameterReference(JsonElement value, out string? parameterName)
    {
        parameterName = null;
        if (value.ValueKind == JsonValueKind.String)
        {
            string? raw = value.GetString();
            if (!string.IsNullOrWhiteSpace(raw) &&
                !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                parameterName = raw.Trim();
                return true;
            }
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("ref", out JsonElement refElement) &&
            refElement.ValueKind == JsonValueKind.String)
        {
            string? raw = refElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                parameterName = raw.Trim();
                return true;
            }
        }

        return false;
    }
}

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

            if (!IsSupportedParameterKind(parameter.Kind))
            {
                result.Errors.Add($"Nieobslugiwany kind parametru '{parameter.Kind}' dla {parameter.Name}");
            }

            if (string.Equals(parameter.Kind, "family", StringComparison.OrdinalIgnoreCase) &&
                !IsSupportedSpecType(parameter.SpecType))
            {
                result.Errors.Add($"Nieobslugiwany spec_type '{parameter.SpecType}' dla {parameter.Name}");
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
        }

        foreach (DimensionConstraintSpec dimension in spec.Dimensions)
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
        }

        foreach (GeometryOperationSpec operation in spec.Geometry)
        {
            if (!IsSupportedGeometryOperation(operation.Op))
            {
                result.Errors.Add($"Nieobslugiwana operacja geometrii: {operation.Op}");
                continue;
            }

            ValidateGeometryOperation(operation, parameterNames, result);
        }
    }

    private static void ValidateGeometryOperation(GeometryOperationSpec operation, IReadOnlySet<string> parameterNames, AiFamilyBuilderValidationResult result)
    {
        switch (operation.Op.Trim().ToLowerInvariant())
        {
            case "extrusion_rectangle":
            case "void_rectangle":
                RequireValue(operation.Width, operation.Op, "width", parameterNames, result);
                RequireValue(operation.Height, operation.Op, "height", parameterNames, result);
                RequireValue(operation.Depth, operation.Op, "depth", parameterNames, result);
                break;

            case "extrusion_circle":
            case "void_circle":
                RequireValue(operation.Radius, operation.Op, "radius", parameterNames, result);
                RequireValue(operation.Depth, operation.Op, "depth", parameterNames, result);
                break;

            case "blend_rectangle":
                RequireValue(operation.TopWidth, operation.Op, "top_width", parameterNames, result);
                RequireValue(operation.TopHeight, operation.Op, "top_height", parameterNames, result);
                RequireValue(operation.BottomWidth, operation.Op, "bottom_width", parameterNames, result);
                RequireValue(operation.BottomHeight, operation.Op, "bottom_height", parameterNames, result);
                RequireValue(operation.Depth, operation.Op, "depth", parameterNames, result);
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
            if (double.TryParse(raw, out _))
            {
                return;
            }

            if (parameterNames.Contains(raw))
            {
                return;
            }
        }

        result.Errors.Add($"Operacja '{op}' ma niepoprawne pole '{fieldName}'.");
    }

    private static bool IsSupportedParameterKind(string value)
        => value.Trim().Equals("family", StringComparison.OrdinalIgnoreCase) ||
           value.Trim().Equals("shared", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedSpecType(string value)
        => value.Trim().ToLowerInvariant() is
            "text" or "yesno" or "integer" or "number" or "length" or "area" or "volume" or "angle" or "material";

    private static bool IsSupportedOrientation(string value)
        => value.Trim().ToLowerInvariant() is "vertical" or "horizontal";

    private static bool IsSupportedGeometryOperation(string value)
        => value.Trim().ToLowerInvariant() is
            "extrusion_rectangle" or "void_rectangle" or "extrusion_circle" or "void_circle" or "blend_rectangle";
}

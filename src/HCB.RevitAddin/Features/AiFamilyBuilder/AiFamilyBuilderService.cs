using System;
using System.Globalization;
using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;

namespace HCB.RevitAddin.Features.AiFamilyBuilder;

public sealed class AiFamilyBuilderService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AiFamilyBuilderValidationService _validationService = new();

    public AiFamilyBuilderResult Apply(Document document, Application application, AiFamilyBuilderOptions options)
    {
        if (!document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Aktywny dokument nie jest rodzina.");
        }

        FamilyDefinitionSpec spec = Parse(options.JsonText);
        AiFamilyBuilderValidationResult validation = new();
        _validationService.ValidateSpec(spec, validation);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join("\n", validation.Errors));
        }

        AiFamilyBuilderResult result = new();

        using Transaction transaction = new(document, "AI Family Builder");
        transaction.Start();

        DefinitionFile? sharedParameterFile = NeedsSharedParameters(spec)
            ? application.OpenSharedParameterFile()
            : null;

        if (NeedsSharedParameters(spec) && sharedParameterFile == null)
        {
            throw new InvalidOperationException("JSON wymaga shared parameters, ale Revit nie ma ustawionego pliku Shared Parameters.");
        }

        FamilyManager familyManager = document.FamilyManager;
        Dictionary<string, FamilyParameter> parameterMap = familyManager.Parameters
            .Cast<FamilyParameter>()
            .Where(parameter => parameter.Definition != null)
            .ToDictionary(parameter => parameter.Definition.Name, StringComparer.OrdinalIgnoreCase);

        AddParameters(familyManager, parameterMap, sharedParameterFile, spec.Parameters, result);
        EnsureTypeExists(familyManager, spec.Types, result);
        ApplyTypeValues(familyManager, parameterMap, spec.Types, result);

        Dictionary<string, ReferencePlane> referencePlanes = CreateReferencePlanes(document, spec.ReferencePlanes, result);
        CreateDimensions(document, familyManager, parameterMap, referencePlanes, spec.Dimensions, result);
        ApplyGeometry(document, parameterMap, spec.Geometry, spec.Types, result);

        transaction.Commit();
        return result;
    }

    private static FamilyDefinitionSpec Parse(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            throw new InvalidOperationException("Wklej kod JSON.");
        }

        FamilyDefinitionSpec? spec = JsonSerializer.Deserialize<FamilyDefinitionSpec>(jsonText, JsonOptions);
        if (spec == null)
        {
            throw new InvalidOperationException("Nie udalo sie odczytac JSON.");
        }

        return spec;
    }

    private static bool NeedsSharedParameters(FamilyDefinitionSpec spec)
    {
        return spec.Parameters.Any(parameter =>
            string.Equals(parameter.Kind, "shared", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddParameters(
        FamilyManager familyManager,
        IDictionary<string, FamilyParameter> parameterMap,
        DefinitionFile? sharedParameterFile,
        IReadOnlyList<FamilyParameterSpec> parameters,
        AiFamilyBuilderResult result)
    {
        foreach (FamilyParameterSpec parameterSpec in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameterSpec.Name))
            {
                result.Messages.Add("Pominieto parametr bez nazwy.");
                result.SkippedParametersCount++;
                continue;
            }

            if (parameterMap.ContainsKey(parameterSpec.Name))
            {
                result.Messages.Add($"Parametr juz istnieje: {parameterSpec.Name}");
                result.SkippedParametersCount++;
                continue;
            }

            ForgeTypeId groupTypeId = ResolveGroupTypeId(parameterSpec.Group);
            FamilyParameter parameter;

            if (string.Equals(parameterSpec.Kind, "shared", StringComparison.OrdinalIgnoreCase))
            {
                ExternalDefinition definition = FindSharedDefinition(sharedParameterFile, parameterSpec.Name);
                parameter = familyManager.AddParameter(definition, groupTypeId, parameterSpec.IsInstance);
            }
            else
            {
                ForgeTypeId specTypeId = ResolveSpecTypeId(parameterSpec.SpecType);
                parameter = familyManager.AddParameter(parameterSpec.Name, groupTypeId, specTypeId, parameterSpec.IsInstance);
            }

            parameterMap[parameterSpec.Name] = parameter;
            result.AddedParametersCount++;

            if (!string.IsNullOrWhiteSpace(parameterSpec.Formula))
            {
                familyManager.SetFormula(parameter, parameterSpec.Formula);
                result.Messages.Add($"Dodano parametr z formula: {parameterSpec.Name}");
            }
            else
            {
                result.Messages.Add($"Dodano parametr: {parameterSpec.Name}");
            }
        }
    }

    private static void EnsureTypeExists(FamilyManager familyManager, IReadOnlyList<FamilyTypeSpec> types, AiFamilyBuilderResult result)
    {
        if (familyManager.CurrentType == null && types.Count == 0)
        {
            familyManager.NewType("Default");
            result.AddedTypesCount++;
            result.Messages.Add("Dodano domyslny typ rodziny: Default");
            return;
        }

        HashSet<string> existingTypes = familyManager.Types
            .Cast<FamilyType>()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (FamilyTypeSpec typeSpec in types)
        {
            if (string.IsNullOrWhiteSpace(typeSpec.Name) || existingTypes.Contains(typeSpec.Name))
            {
                continue;
            }

            familyManager.NewType(typeSpec.Name);
            existingTypes.Add(typeSpec.Name);
            result.AddedTypesCount++;
            result.Messages.Add($"Dodano typ: {typeSpec.Name}");
        }
    }

    private static void ApplyTypeValues(
        FamilyManager familyManager,
        IDictionary<string, FamilyParameter> parameterMap,
        IReadOnlyList<FamilyTypeSpec> types,
        AiFamilyBuilderResult result)
    {
        if (types.Count == 0 || familyManager.CurrentType == null)
        {
            return;
        }

        FamilyType? originalType = familyManager.CurrentType;
        Dictionary<string, FamilyType> typeMap = familyManager.Types
            .Cast<FamilyType>()
            .ToDictionary(type => type.Name, StringComparer.OrdinalIgnoreCase);

        foreach (FamilyTypeSpec typeSpec in types)
        {
            if (!typeMap.TryGetValue(typeSpec.Name, out FamilyType? familyType))
            {
                continue;
            }

            familyManager.CurrentType = familyType;
            foreach ((string parameterName, JsonElement valueElement) in typeSpec.Values)
            {
                if (!parameterMap.TryGetValue(parameterName, out FamilyParameter? parameter))
                {
                    result.Messages.Add($"Brak parametru do ustawienia wartosci: {parameterName}");
                    continue;
                }

                if (!TrySetParameterValue(familyManager, parameter, valueElement))
                {
                    result.Messages.Add($"Nie udalo sie ustawic wartosci parametru: {parameterName} dla typu {typeSpec.Name}");
                    continue;
                }

                result.UpdatedValuesCount++;
            }
        }

        familyManager.CurrentType = originalType;
    }

    private static Dictionary<string, ReferencePlane> CreateReferencePlanes(Document document, IReadOnlyList<ReferencePlaneSpec> specs, AiFamilyBuilderResult result)
    {
        Dictionary<string, ReferencePlane> map = new(StringComparer.OrdinalIgnoreCase);
        View view = document.ActiveView;

        foreach (ReferencePlaneSpec spec in specs)
        {
            double offset = UnitUtils.ConvertToInternalUnits(spec.Offset, UnitTypeId.Millimeters);
            ReferencePlane plane = string.Equals(spec.Orientation, "horizontal", StringComparison.OrdinalIgnoreCase)
                ? document.FamilyCreate.NewReferencePlane2(
                    new XYZ(-100, offset, 0),
                    new XYZ(100, offset, 0),
                    XYZ.BasisZ,
                    view)
                : document.FamilyCreate.NewReferencePlane2(
                    new XYZ(offset, -100, 0),
                    new XYZ(offset, 100, 0),
                    XYZ.BasisZ,
                    view);

            plane.Name = spec.Name;
            map[spec.Name] = plane;
            result.Messages.Add($"Dodano reference plane: {spec.Name}");
        }

        return map;
    }

    private static void CreateDimensions(
        Document document,
        FamilyManager familyManager,
        IReadOnlyDictionary<string, FamilyParameter> parameterMap,
        IReadOnlyDictionary<string, ReferencePlane> referencePlanes,
        IReadOnlyList<DimensionConstraintSpec> dimensions,
        AiFamilyBuilderResult result)
    {
        View view = document.ActiveView;

        foreach (DimensionConstraintSpec spec in dimensions)
        {
            if (!referencePlanes.TryGetValue(spec.ReferenceA, out ReferencePlane? referenceA) ||
                !referencePlanes.TryGetValue(spec.ReferenceB, out ReferencePlane? referenceB) ||
                !parameterMap.TryGetValue(spec.Parameter, out FamilyParameter? parameter))
            {
                result.Messages.Add($"Pominieto dimension constraint: {spec.Name}");
                continue;
            }

            ReferenceArray references = new();
            references.Append(referenceA.GetReference());
            references.Append(referenceB.GetReference());

            double offset = UnitUtils.ConvertToInternalUnits(spec.Offset, UnitTypeId.Millimeters);
            Line dimensionLine = string.Equals(spec.Orientation, "vertical", StringComparison.OrdinalIgnoreCase)
                ? Line.CreateBound(new XYZ(offset, -100, 0), new XYZ(offset, 100, 0))
                : Line.CreateBound(new XYZ(-100, offset, 0), new XYZ(100, offset, 0));

            Dimension dimension = document.FamilyCreate.NewLinearDimension(view, dimensionLine, references);
            dimension.FamilyLabel = parameter;
            result.Messages.Add($"Dodano dimension constraint: {spec.Name} -> {spec.Parameter}");
        }
    }

    private static void ApplyGeometry(
        Document document,
        IDictionary<string, FamilyParameter> parameterMap,
        IReadOnlyList<GeometryOperationSpec> operations,
        IReadOnlyList<FamilyTypeSpec> types,
        AiFamilyBuilderResult result)
    {
        if (operations.Count == 0)
        {
            return;
        }

        Dictionary<string, JsonElement> defaultValues = types.FirstOrDefault()?.Values
            ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (GeometryOperationSpec operation in operations)
        {
            bool success = operation.Op.Trim().ToLowerInvariant() switch
            {
                "extrusion_rectangle" => TryCreateRectangleExtrusion(document, operation, parameterMap, defaultValues, true),
                "void_rectangle" => TryCreateRectangleExtrusion(document, operation, parameterMap, defaultValues, false),
                "extrusion_circle" => TryCreateCircleExtrusion(document, operation, parameterMap, defaultValues, true),
                "void_circle" => TryCreateCircleExtrusion(document, operation, parameterMap, defaultValues, false),
                "blend_rectangle" => TryCreateRectangleBlend(document, operation, parameterMap, defaultValues),
                _ => false
            };

            if (success)
            {
                result.ExecutedGeometryOperationsCount++;
                result.Messages.Add($"Wykonano geometrie: {operation.Name ?? operation.Op}");
            }
            else
            {
                result.SkippedGeometryOperationsCount++;
                result.Messages.Add($"Pominieto geometrie: {operation.Name ?? operation.Op}");
            }
        }
    }

    private static bool TryCreateRectangleExtrusion(
        Document document,
        GeometryOperationSpec operation,
        IDictionary<string, FamilyParameter> parameterMap,
        IReadOnlyDictionary<string, JsonElement> defaultValues,
        bool isSolid)
    {
        if (!TryResolveLength(operation.Width, parameterMap, defaultValues, out double widthFeet) ||
            !TryResolveLength(operation.Height, parameterMap, defaultValues, out double heightFeet) ||
            !TryResolveLength(operation.Depth, parameterMap, defaultValues, out double depthFeet))
        {
            return false;
        }

        CurveArrArray profile = new();
        profile.Append(BuildRectangleProfile(widthFeet, heightFeet));
        SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        document.FamilyCreate.NewExtrusion(isSolid, profile, sketchPlane, depthFeet);
        return true;
    }

    private static bool TryCreateCircleExtrusion(
        Document document,
        GeometryOperationSpec operation,
        IDictionary<string, FamilyParameter> parameterMap,
        IReadOnlyDictionary<string, JsonElement> defaultValues,
        bool isSolid)
    {
        if (!TryResolveLength(operation.Radius, parameterMap, defaultValues, out double radiusFeet) ||
            !TryResolveLength(operation.Depth, parameterMap, defaultValues, out double depthFeet))
        {
            return false;
        }

        CurveArrArray profile = new();
        profile.Append(BuildCircleProfile(radiusFeet));
        SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        document.FamilyCreate.NewExtrusion(isSolid, profile, sketchPlane, depthFeet);
        return true;
    }

    private static bool TryCreateRectangleBlend(
        Document document,
        GeometryOperationSpec operation,
        IDictionary<string, FamilyParameter> parameterMap,
        IReadOnlyDictionary<string, JsonElement> defaultValues)
    {
        if (!TryResolveLength(operation.TopWidth, parameterMap, defaultValues, out double topWidthFeet) ||
            !TryResolveLength(operation.TopHeight, parameterMap, defaultValues, out double topHeightFeet) ||
            !TryResolveLength(operation.BottomWidth, parameterMap, defaultValues, out double bottomWidthFeet) ||
            !TryResolveLength(operation.BottomHeight, parameterMap, defaultValues, out double bottomHeightFeet) ||
            !TryResolveLength(operation.Depth, parameterMap, defaultValues, out double depthFeet))
        {
            return false;
        }

        CurveArray top = BuildRectangleProfile(topWidthFeet, topHeightFeet);
        CurveArray bottom = BuildRectangleProfile(bottomWidthFeet, bottomHeightFeet, depthFeet);
        SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero));
        document.FamilyCreate.NewBlend(true, top, bottom, sketchPlane);
        return true;
    }

    private static CurveArray BuildRectangleProfile(double widthFeet, double heightFeet, double z = 0)
    {
        double halfWidth = widthFeet / 2.0;
        double halfHeight = heightFeet / 2.0;

        CurveArray rectangle = new();
        rectangle.Append(Line.CreateBound(new XYZ(-halfWidth, -halfHeight, z), new XYZ(halfWidth, -halfHeight, z)));
        rectangle.Append(Line.CreateBound(new XYZ(halfWidth, -halfHeight, z), new XYZ(halfWidth, halfHeight, z)));
        rectangle.Append(Line.CreateBound(new XYZ(halfWidth, halfHeight, z), new XYZ(-halfWidth, halfHeight, z)));
        rectangle.Append(Line.CreateBound(new XYZ(-halfWidth, halfHeight, z), new XYZ(-halfWidth, -halfHeight, z)));
        return rectangle;
    }

    private static CurveArray BuildCircleProfile(double radiusFeet)
    {
        CurveArray profile = new();
        profile.Append(Arc.Create(new XYZ(-radiusFeet, 0, 0), new XYZ(radiusFeet, 0, 0), new XYZ(0, radiusFeet, 0)));
        profile.Append(Arc.Create(new XYZ(radiusFeet, 0, 0), new XYZ(-radiusFeet, 0, 0), new XYZ(0, -radiusFeet, 0)));
        return profile;
    }

    private static bool TryResolveLength(
        JsonElement valueElement,
        IDictionary<string, FamilyParameter> parameterMap,
        IReadOnlyDictionary<string, JsonElement> defaultValues,
        out double valueFeet)
    {
        valueFeet = 0;

        if (valueElement.ValueKind == JsonValueKind.Number)
        {
            valueFeet = UnitUtils.ConvertToInternalUnits(valueElement.GetDouble(), UnitTypeId.Millimeters);
            return true;
        }

        if (valueElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string raw = valueElement.GetString() ?? string.Empty;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double millimeters))
        {
            valueFeet = UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
            return true;
        }

        if (defaultValues.TryGetValue(raw, out JsonElement referencedValue) &&
            referencedValue.ValueKind == JsonValueKind.Number)
        {
            valueFeet = UnitUtils.ConvertToInternalUnits(referencedValue.GetDouble(), UnitTypeId.Millimeters);
            return true;
        }

        return false;
    }

    private static bool TrySetParameterValue(FamilyManager familyManager, FamilyParameter parameter, JsonElement valueElement)
    {
        try
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    familyManager.Set(parameter, valueElement.ValueKind == JsonValueKind.String
                        ? valueElement.GetString() ?? string.Empty
                        : valueElement.ToString());
                    return true;

                case StorageType.Integer:
                    if (valueElement.ValueKind == JsonValueKind.True || valueElement.ValueKind == JsonValueKind.False)
                    {
                        familyManager.Set(parameter, valueElement.GetBoolean() ? 1 : 0);
                        return true;
                    }

                    if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt32(out int intValue))
                    {
                        familyManager.Set(parameter, intValue);
                        return true;
                    }

                    break;

                case StorageType.Double:
                    if (valueElement.ValueKind != JsonValueKind.Number)
                    {
                        break;
                    }

                    double sourceValue = valueElement.GetDouble();
                    double internalValue = ConvertToInternalValue(parameter.Definition?.GetDataType(), sourceValue);
                    familyManager.Set(parameter, internalValue);
                    return true;

                case StorageType.ElementId:
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static double ConvertToInternalValue(ForgeTypeId? specTypeId, double sourceValue)
    {
        if (specTypeId == null)
        {
            return sourceValue;
        }

        if (specTypeId == SpecTypeId.Length)
        {
            return UnitUtils.ConvertToInternalUnits(sourceValue, UnitTypeId.Millimeters);
        }

        if (specTypeId == SpecTypeId.Area)
        {
            return UnitUtils.ConvertToInternalUnits(sourceValue, UnitTypeId.SquareMeters);
        }

        if (specTypeId == SpecTypeId.Volume)
        {
            return UnitUtils.ConvertToInternalUnits(sourceValue, UnitTypeId.CubicMeters);
        }

        if (specTypeId == SpecTypeId.Angle)
        {
            return UnitUtils.ConvertToInternalUnits(sourceValue, UnitTypeId.Degrees);
        }

        return sourceValue;
    }

    private static ExternalDefinition FindSharedDefinition(DefinitionFile? sharedParameterFile, string name)
    {
        if (sharedParameterFile == null)
        {
            throw new InvalidOperationException($"Brak pliku Shared Parameters dla parametru: {name}");
        }

        foreach (DefinitionGroup group in sharedParameterFile.Groups)
        {
            foreach (Definition definition in group.Definitions)
            {
                if (definition is ExternalDefinition externalDefinition &&
                    string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return externalDefinition;
                }
            }
        }

        throw new InvalidOperationException($"Nie znaleziono shared parameter w pliku definicji: {name}");
    }

    private static ForgeTypeId ResolveGroupTypeId(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "data" => GroupTypeId.Data,
            "text" => GroupTypeId.Text,
            "identitydata" => GroupTypeId.IdentityData,
            "construction" => GroupTypeId.Construction,
            "geometry" => GroupTypeId.Geometry,
            "materials" => GroupTypeId.Materials,
            "mechanical" => GroupTypeId.Mechanical,
            "electrical" => GroupTypeId.Electrical,
            "plumbing" => GroupTypeId.Plumbing,
            _ => GroupTypeId.Data
        };
    }

    private static ForgeTypeId ResolveSpecTypeId(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "text" => SpecTypeId.String.Text,
            "yesno" => SpecTypeId.Boolean.YesNo,
            "integer" => SpecTypeId.Int.Integer,
            "number" => SpecTypeId.Number,
            "length" => SpecTypeId.Length,
            "area" => SpecTypeId.Area,
            "volume" => SpecTypeId.Volume,
            "angle" => SpecTypeId.Angle,
            "material" => SpecTypeId.Reference.Material,
            _ => SpecTypeId.String.Text
        };
    }
}

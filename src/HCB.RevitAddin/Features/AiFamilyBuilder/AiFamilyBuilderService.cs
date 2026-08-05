using System;
using System.Globalization;
using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;

namespace HCB.RevitAddin.Features.AiFamilyBuilder;

public sealed class AiFamilyBuilderService
{
    private sealed record ReferencePlaneContext(ReferencePlane Plane, View View, bool IsVerticalInView, double OffsetInternal);
    private sealed record ParameterAvailability(
        Dictionary<string, FamilyParameter> ExistingParameters,
        Dictionary<string, ExternalDefinition> SharedDefinitions);

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
        bool needsSharedParameters = NeedsSharedParameters(spec);
        DefinitionFile? sharedParameterFile = needsSharedParameters
            ? application.OpenSharedParameterFile()
            : null;

        if (needsSharedParameters && sharedParameterFile == null)
        {
            throw new InvalidOperationException("JSON wymaga shared parameters, ale Revit nie ma ustawionego pliku Shared Parameters.");
        }

        using Transaction transaction = new(document, "AI Family Builder");
        transaction.Start();
        try
        {
            FamilyManager familyManager = document.FamilyManager;
            Dictionary<string, FamilyParameter> parameterMap = familyManager.Parameters
                .Cast<FamilyParameter>()
                .Where(parameter => parameter.Definition != null)
                .ToDictionary(parameter => parameter.Definition.Name, StringComparer.OrdinalIgnoreCase);
            ParameterAvailability availableParameters = BuildAvailableParameters(familyManager, sharedParameterFile, result);

            AddParameters(familyManager, parameterMap, availableParameters, spec.Parameters, result);
            EnsureTypeExists(familyManager, spec.Types, result);
            ApplyTypeValues(familyManager, parameterMap, spec.Types, result);

            Dictionary<string, View> workingViews = GetWorkingFamilyViews(document, result);
            Dictionary<string, ReferencePlaneContext> referencePlanes = CreateReferencePlanes(document, workingViews, spec.ReferencePlanes, result);
            document.Regenerate();
            CreateDimensions(document, workingViews, familyManager, parameterMap, referencePlanes, GetDimensionSpecs(spec), result);
            ApplyGeometry(document, workingViews, familyManager, parameterMap, referencePlanes, spec.Geometry, spec.Types, result);

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

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
            string.Equals(GetParameterKind(parameter), "shared", StringComparison.OrdinalIgnoreCase));
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

    private static void AddParameters(
        FamilyManager familyManager,
        IDictionary<string, FamilyParameter> parameterMap,
        ParameterAvailability availableParameters,
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

            if (availableParameters.ExistingParameters.TryGetValue(parameterSpec.Name, out FamilyParameter? existingParameter))
            {
                parameterMap[parameterSpec.Name] = existingParameter;
                result.SkippedParametersCount++;

                string? mismatch = DescribeParameterMismatch(existingParameter, parameterSpec);
                if (string.IsNullOrWhiteSpace(mismatch))
                {
                    result.Messages.Add($"Uzyto istniejacego parametru: {parameterSpec.Name}");
                }
                else
                {
                    result.Messages.Add($"Uzyto istniejacego parametru z roznicami: {parameterSpec.Name} ({mismatch})");
                }

                continue;
            }

            ForgeTypeId groupTypeId = ResolveGroupTypeId(parameterSpec.Group);
            FamilyParameter parameter;

            if (string.Equals(GetParameterKind(parameterSpec), "shared", StringComparison.OrdinalIgnoreCase))
            {
                ExternalDefinition definition = FindSharedDefinition(availableParameters, parameterSpec.Name);
                parameter = familyManager.AddParameter(definition, groupTypeId, IsInstanceParameter(parameterSpec));
            }
            else
            {
                ForgeTypeId specTypeId = ResolveSpecTypeId(GetParameterSpecType(parameterSpec));
                parameter = familyManager.AddParameter(parameterSpec.Name, groupTypeId, specTypeId, IsInstanceParameter(parameterSpec));
            }

            parameterMap[parameterSpec.Name] = parameter;
            availableParameters.ExistingParameters[parameterSpec.Name] = parameter;
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

    private static ParameterAvailability BuildAvailableParameters(
        FamilyManager familyManager,
        DefinitionFile? sharedParameterFile,
        AiFamilyBuilderResult result)
    {
        Dictionary<string, FamilyParameter> existingParameters = familyManager.Parameters
            .Cast<FamilyParameter>()
            .Where(parameter => parameter.Definition != null && !string.IsNullOrWhiteSpace(parameter.Definition.Name))
            .GroupBy(parameter => parameter.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ExternalDefinition> sharedDefinitions = new(StringComparer.OrdinalIgnoreCase);
        if (sharedParameterFile != null)
        {
            foreach (DefinitionGroup group in sharedParameterFile.Groups)
            {
                foreach (Definition definition in group.Definitions)
                {
                    if (definition is ExternalDefinition externalDefinition &&
                        !sharedDefinitions.ContainsKey(definition.Name))
                    {
                        sharedDefinitions[definition.Name] = externalDefinition;
                    }
                }
            }
        }

        result.Messages.Add($"Wykryto dostepne parametry rodziny: {existingParameters.Count}");
        if (sharedParameterFile != null)
        {
            result.Messages.Add($"Wykryto dostepne definicje shared parameters: {sharedDefinitions.Count}");
        }

        return new ParameterAvailability(existingParameters, sharedDefinitions);
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

    private static Dictionary<string, ReferencePlaneContext> CreateReferencePlanes(Document document, IReadOnlyDictionary<string, View> workingViews, IReadOnlyList<ReferencePlaneSpec> specs, AiFamilyBuilderResult result)
    {
        Dictionary<string, ReferencePlaneContext> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (ReferencePlaneSpec spec in specs)
        {
            try
            {
                (View view, bool isVerticalInView) = ResolveReferencePlaneLayout(spec, workingViews);
                double offset = UnitUtils.ConvertToInternalUnits(spec.Offset, UnitTypeId.Millimeters);
                ReferencePlane plane = CreateReferencePlane(document, view, isVerticalInView, offset);

                plane.Name = spec.Name;
                map[spec.Name] = new ReferencePlaneContext(plane, view, isVerticalInView, offset);
                result.CreatedReferencePlanesCount++;
                result.Messages.Add($"Dodano reference plane: {spec.Name} [widok: {view.Name}]");
            }
            catch (Exception ex)
            {
                result.SkippedReferencePlanesCount++;
                result.Messages.Add($"Blad reference plane '{spec.Name}': {ex.Message}");
            }
        }

        return map;
    }

    private static void CreateDimensions(
        Document document,
        IReadOnlyDictionary<string, View> workingViews,
        FamilyManager familyManager,
        IReadOnlyDictionary<string, FamilyParameter> parameterMap,
        IReadOnlyDictionary<string, ReferencePlaneContext> referencePlanes,
        IReadOnlyList<DimensionConstraintSpec> dimensions,
        AiFamilyBuilderResult result)
    {
        foreach (DimensionConstraintSpec spec in dimensions)
        {
            if (!referencePlanes.TryGetValue(spec.ReferenceA, out ReferencePlaneContext? referenceA) ||
                !referencePlanes.TryGetValue(spec.ReferenceB, out ReferencePlaneContext? referenceB) ||
                !parameterMap.TryGetValue(spec.Parameter, out FamilyParameter? parameter))
            {
                result.SkippedDimensionsCount++;
                result.Messages.Add($"Pominieto dimension constraint: {spec.Name}");
                continue;
            }

            try
            {
                View view = ResolveDimensionView(spec, workingViews, referenceA, referenceB);
                if (referenceA.View.Id != view.Id || referenceB.View.Id != view.Id)
                {
                    throw new InvalidOperationException("Reference planes dla constraintu nie sa w tym samym widoku.");
                }

                ReferenceArray references = new();
                references.Append(referenceA.Plane.GetReference());
                references.Append(referenceB.Plane.GetReference());

                double offset = UnitUtils.ConvertToInternalUnits(spec.Offset, UnitTypeId.Millimeters);
                Line dimensionLine = CreateDimensionLine(view, referenceA, referenceB, offset);

                Dimension dimension = document.FamilyCreate.NewLinearDimension(view, dimensionLine, references);
                dimension.FamilyLabel = parameter;
                result.CreatedDimensionsCount++;
                result.Messages.Add($"Dodano dimension constraint: {spec.Name} -> {spec.Parameter} [widok: {view.Name}]");
            }
            catch (Exception ex)
            {
                result.SkippedDimensionsCount++;
                result.Messages.Add($"Blad dimension constraint '{spec.Name}': {ex.Message}");
            }
        }
    }

    private static void ApplyGeometry(
        Document document,
        IReadOnlyDictionary<string, View> workingViews,
        FamilyManager familyManager,
        IDictionary<string, FamilyParameter> parameterMap,
        IDictionary<string, ReferencePlaneContext> referencePlanes,
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
            bool success;
            try
            {
                string operationKind = GetGeometryOperationKind(operation);
                success = operationKind switch
                {
                    "extrusion_rectangle" => TryCreateRectangleExtrusion(document, operation, parameterMap, defaultValues, true),
                    "void_rectangle" => TryCreateRectangleExtrusion(document, operation, parameterMap, defaultValues, false),
                    "extrusion_circle" => TryCreateCircleExtrusion(document, operation, parameterMap, defaultValues, true),
                    "void_circle" => TryCreateCircleExtrusion(document, operation, parameterMap, defaultValues, false),
                    "blend_rectangle" => TryCreateRectangleBlend(document, operation, parameterMap, defaultValues),
                    "rectangular_prism" => TryCreateParametricRectanglePrism(document, workingViews, familyManager, operation, parameterMap, referencePlanes, defaultValues, result),
                    _ => false
                };
            }
            catch (Exception ex)
            {
                result.SkippedGeometryOperationsCount++;
                result.Messages.Add($"Blad geometrii '{operation.Name ?? operation.Op}': {ex.Message}");
                continue;
            }

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

    private static bool TryCreateParametricRectanglePrism(
        Document document,
        IReadOnlyDictionary<string, View> workingViews,
        FamilyManager familyManager,
        GeometryOperationSpec operation,
        IDictionary<string, FamilyParameter> parameterMap,
        IDictionary<string, ReferencePlaneContext> referencePlanes,
        IReadOnlyDictionary<string, JsonElement> defaultValues,
        AiFamilyBuilderResult result)
    {
        if (!TryResolveLength(operation.Width, parameterMap, defaultValues, out double widthFeet) ||
            !TryResolveLength(operation.Height, parameterMap, defaultValues, out double heightFeet) ||
            !TryResolveLength(operation.Depth, parameterMap, defaultValues, out double depthFeet))
        {
            return false;
        }

        if (!TryGetParameterReference(operation.Width, out string? widthParameterName) ||
            widthParameterName == null ||
            !parameterMap.TryGetValue(widthParameterName, out FamilyParameter? widthParameter) ||
            !TryGetParameterReference(operation.Height, out string? heightParameterName) ||
            heightParameterName == null ||
            !parameterMap.TryGetValue(heightParameterName, out FamilyParameter? heightParameter))
        {
            result.Messages.Add($"Parametric prism '{operation.Name ?? operation.Kind}': width i height musza byc referencjami do parametrow.");
            return false;
        }

        View planView = workingViews["plan"];
        string baseName = SanitizeElementName(operation.Name ?? "Body");
        double halfWidth = widthFeet / 2.0;
        double halfHeight = heightFeet / 2.0;

        ReferencePlaneContext left = CreateNamedReferencePlane(document, planView, $"{baseName}_Left", true, -halfWidth, referencePlanes);
        ReferencePlaneContext right = CreateNamedReferencePlane(document, planView, $"{baseName}_Right", true, halfWidth, referencePlanes);
        ReferencePlaneContext bottom = CreateNamedReferencePlane(document, planView, $"{baseName}_Bottom", false, -halfHeight, referencePlanes);
        ReferencePlaneContext top = CreateNamedReferencePlane(document, planView, $"{baseName}_Top", false, halfHeight, referencePlanes);
        result.CreatedReferencePlanesCount += 4;

        CreateDimensionBetweenPlanes(document, planView, familyManager, left, right, widthParameter, -halfHeight - UnitUtils.ConvertToInternalUnits(250, UnitTypeId.Millimeters), result, $"{baseName}_WidthDim");
        CreateDimensionBetweenPlanes(document, planView, familyManager, bottom, top, heightParameter, halfWidth + UnitUtils.ConvertToInternalUnits(250, UnitTypeId.Millimeters), result, $"{baseName}_HeightDim");

        CurveArrArray profile = new();
        profile.Append(BuildRectangleProfileFromBounds(-halfWidth, halfWidth, -halfHeight, halfHeight));
        SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        Extrusion extrusion = document.FamilyCreate.NewExtrusion(operation.Solid, profile, sketchPlane, depthFeet);
        extrusion.Name = baseName;
        document.Regenerate();

        int alignedEdges = AlignExtrusionSketchToReferencePlanes(document, planView, extrusion, left, right, bottom, top);
        result.Messages.Add($"Parametric prism '{baseName}': zablokowano krawedzie szkicu: {alignedEdges}/4.");

        if (TryGetParameterReference(operation.Depth, out string? depthParameterName) &&
            depthParameterName != null &&
            parameterMap.TryGetValue(depthParameterName, out FamilyParameter? depthParameter))
        {
            AssociateExtrusionDepth(familyManager, extrusion, depthParameter, result, baseName);
        }

        return true;
    }

    private static ReferencePlaneContext CreateNamedReferencePlane(
        Document document,
        View view,
        string requestedName,
        bool isVerticalInView,
        double offset,
        IDictionary<string, ReferencePlaneContext> referencePlanes)
    {
        ReferencePlane plane = CreateReferencePlane(document, view, isVerticalInView, offset);
        string name = EnsureUniqueReferencePlaneName(referencePlanes, requestedName);
        plane.Name = name;
        ReferencePlaneContext context = new(plane, view, isVerticalInView, offset);
        referencePlanes[name] = context;
        return context;
    }

    private static string EnsureUniqueReferencePlaneName(IDictionary<string, ReferencePlaneContext> referencePlanes, string requestedName)
    {
        string name = requestedName;
        int index = 2;
        while (referencePlanes.ContainsKey(name))
        {
            name = $"{requestedName}_{index}";
            index++;
        }

        return name;
    }

    private static void CreateDimensionBetweenPlanes(
        Document document,
        View view,
        FamilyManager familyManager,
        ReferencePlaneContext referenceA,
        ReferencePlaneContext referenceB,
        FamilyParameter parameter,
        double offset,
        AiFamilyBuilderResult result,
        string name)
    {
        ReferenceArray references = new();
        references.Append(referenceA.Plane.GetReference());
        references.Append(referenceB.Plane.GetReference());

        Line dimensionLine = CreateDimensionLine(view, referenceA, referenceB, offset);
        Dimension dimension = document.FamilyCreate.NewLinearDimension(view, dimensionLine, references);
        dimension.FamilyLabel = parameter;
        result.CreatedDimensionsCount++;
        result.Messages.Add($"Dodano dimension constraint: {name} -> {parameter.Definition.Name}");
    }

    private static int AlignExtrusionSketchToReferencePlanes(
        Document document,
        View view,
        Extrusion extrusion,
        ReferencePlaneContext left,
        ReferencePlaneContext right,
        ReferencePlaneContext bottom,
        ReferencePlaneContext top)
    {
        int aligned = 0;
        foreach (ModelCurve curve in extrusion.Sketch.GetAllElements()
                     .Select(document.GetElement)
                     .OfType<ModelCurve>())
        {
            Curve geometryCurve = curve.GeometryCurve;
            Reference? curveReference = geometryCurve.Reference;
            if (curveReference == null)
            {
                continue;
            }

            XYZ start = geometryCurve.GetEndPoint(0);
            XYZ end = geometryCurve.GetEndPoint(1);
            ReferencePlaneContext? target = ResolveProfileEdgeTarget(start, end, left, right, bottom, top);
            if (target == null)
            {
                continue;
            }

            try
            {
                document.FamilyCreate.NewAlignment(view, target.Plane.GetReference(), curveReference);
                aligned++;
            }
            catch
            {
                // Alignment can fail for some templates or if Revit already has an equivalent constraint.
            }
        }

        return aligned;
    }

    private static ReferencePlaneContext? ResolveProfileEdgeTarget(
        XYZ start,
        XYZ end,
        ReferencePlaneContext left,
        ReferencePlaneContext right,
        ReferencePlaneContext bottom,
        ReferencePlaneContext top)
    {
        const double tolerance = 1e-6;
        if (Math.Abs(start.X - end.X) < tolerance)
        {
            return Math.Abs(start.X - left.OffsetInternal) < Math.Abs(start.X - right.OffsetInternal)
                ? left
                : right;
        }

        if (Math.Abs(start.Y - end.Y) < tolerance)
        {
            return Math.Abs(start.Y - bottom.OffsetInternal) < Math.Abs(start.Y - top.OffsetInternal)
                ? bottom
                : top;
        }

        return null;
    }

    private static void AssociateExtrusionDepth(
        FamilyManager familyManager,
        Extrusion extrusion,
        FamilyParameter depthParameter,
        AiFamilyBuilderResult result,
        string baseName)
    {
        Parameter? extrusionDepth =
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM) ??
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_DEPTH_PARAM) ??
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_LENGTH);

        if (extrusionDepth == null)
        {
            result.Messages.Add($"Parametric prism '{baseName}': nie znaleziono parametru glebokosci ekstrudowania do powiazania z {depthParameter.Definition.Name}.");
            return;
        }

        try
        {
            familyManager.AssociateElementParameterToFamilyParameter(extrusionDepth, depthParameter);
            result.Messages.Add($"Parametric prism '{baseName}': powiazano glebokosc z {depthParameter.Definition.Name}.");
        }
        catch (Exception ex)
        {
            result.Messages.Add($"Parametric prism '{baseName}': nie udalo sie powiazac glebokosci z {depthParameter.Definition.Name}: {ex.Message}");
        }
    }

    private static CurveArray BuildRectangleProfile(double widthFeet, double heightFeet, double z = 0)
    {
        double halfWidth = widthFeet / 2.0;
        double halfHeight = heightFeet / 2.0;

        return BuildRectangleProfileFromBounds(-halfWidth, halfWidth, -halfHeight, halfHeight, z);
    }

    private static CurveArray BuildRectangleProfileFromBounds(double minX, double maxX, double minY, double maxY, double z = 0)
    {
        CurveArray rectangle = new();
        rectangle.Append(Line.CreateBound(new XYZ(minX, minY, z), new XYZ(maxX, minY, z)));
        rectangle.Append(Line.CreateBound(new XYZ(maxX, minY, z), new XYZ(maxX, maxY, z)));
        rectangle.Append(Line.CreateBound(new XYZ(maxX, maxY, z), new XYZ(minX, maxY, z)));
        rectangle.Append(Line.CreateBound(new XYZ(minX, maxY, z), new XYZ(minX, minY, z)));
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

        if (valueElement.ValueKind == JsonValueKind.Object)
        {
            if (valueElement.TryGetProperty("value", out JsonElement objectValue) &&
                objectValue.ValueKind == JsonValueKind.Number)
            {
                valueFeet = UnitUtils.ConvertToInternalUnits(objectValue.GetDouble(), UnitTypeId.Millimeters);
                return true;
            }

            if (TryGetParameterReference(valueElement, out string? referencedParameter) &&
                referencedParameter != null &&
                defaultValues.TryGetValue(referencedParameter, out JsonElement objectReferencedValue) &&
                objectReferencedValue.ValueKind == JsonValueKind.Number)
            {
                valueFeet = UnitUtils.ConvertToInternalUnits(objectReferencedValue.GetDouble(), UnitTypeId.Millimeters);
                return true;
            }

            return false;
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

    private static bool TryGetParameterReference(JsonElement valueElement, out string? parameterName)
    {
        parameterName = null;
        if (valueElement.ValueKind == JsonValueKind.String)
        {
            string? raw = valueElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw) &&
                !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                parameterName = raw.Trim();
                return true;
            }
        }

        if (valueElement.ValueKind == JsonValueKind.Object &&
            valueElement.TryGetProperty("ref", out JsonElement refElement) &&
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

    private static Dictionary<string, View> GetWorkingFamilyViews(Document document, AiFamilyBuilderResult result)
    {
        Dictionary<string, View> views = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<View> candidates = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(IsSupportedFamilySketchView)
            .ToList();

        View? planView = candidates.FirstOrDefault(view =>
            view.ViewType is ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan);
        View? elevationView = candidates.FirstOrDefault(view =>
            view.ViewType is ViewType.Elevation or ViewType.Section or ViewType.Detail);

        if (planView == null || elevationView == null)
        {
            throw new InvalidOperationException("Nie znaleziono wymaganych widokow rodziny do reference planes i constraints.");
        }

        views["plan"] = planView;
        views["elevation"] = elevationView;
        result.Messages.Add($"Uzyto widoku planu roboczego: {planView.Name}");
        result.Messages.Add($"Uzyto widoku elewacji/sekcji roboczego: {elevationView.Name}");
        return views;
    }

    private static View ResolveDimensionView(
        DimensionConstraintSpec spec,
        IReadOnlyDictionary<string, View> workingViews,
        ReferencePlaneContext referenceA,
        ReferencePlaneContext referenceB)
    {
        if (!string.IsNullOrWhiteSpace(spec.View))
        {
            return ResolveWorkingView(spec.View, workingViews);
        }

        if (referenceA.View.Id == referenceB.View.Id)
        {
            return referenceA.View;
        }

        return string.Equals(spec.Orientation, "vertical", StringComparison.OrdinalIgnoreCase)
            ? workingViews["elevation"]
            : workingViews["plan"];
    }

    private static (View View, bool IsVerticalInView) ResolveReferencePlaneLayout(
        ReferencePlaneSpec spec,
        IReadOnlyDictionary<string, View> workingViews)
    {
        if (!string.IsNullOrWhiteSpace(spec.View) && !string.IsNullOrWhiteSpace(spec.LineDirection))
        {
            return (
                ResolveWorkingView(spec.View, workingViews),
                string.Equals(spec.LineDirection, "vertical", StringComparison.OrdinalIgnoreCase));
        }

        return string.Equals(spec.Orientation, "horizontal", StringComparison.OrdinalIgnoreCase)
            ? (workingViews["elevation"], false)
            : (workingViews["plan"], true);
    }

    private static ReferencePlane CreateReferencePlane(Document document, View view, bool isVerticalInView, double offset)
    {
        const double extent = 1000;

        if (IsPlanLikeView(view))
        {
            return isVerticalInView
                ? document.FamilyCreate.NewReferencePlane2(
                    new XYZ(offset, -extent, 0),
                    new XYZ(offset, extent, 0),
                    XYZ.BasisZ,
                    view)
                : document.FamilyCreate.NewReferencePlane2(
                    new XYZ(-extent, offset, 0),
                    new XYZ(extent, offset, 0),
                    XYZ.BasisZ,
                    view);
        }

        return isVerticalInView
            ? document.FamilyCreate.NewReferencePlane2(
                new XYZ(offset, 0, -extent),
                new XYZ(offset, 0, extent),
                XYZ.BasisY,
                view)
            : document.FamilyCreate.NewReferencePlane2(
                new XYZ(-extent, 0, offset),
                new XYZ(extent, 0, offset),
                XYZ.BasisY,
                view);
    }

    private static Line CreateDimensionLine(View view, ReferencePlaneContext referenceA, ReferencePlaneContext referenceB, double offset)
    {
        const double extent = 1000;
        bool dimensionLineIsVertical = !referenceA.IsVerticalInView && !referenceB.IsVerticalInView;

        if (IsPlanLikeView(view))
        {
            return dimensionLineIsVertical
                ? Line.CreateBound(new XYZ(offset, -extent, 0), new XYZ(offset, extent, 0))
                : Line.CreateBound(new XYZ(-extent, offset, 0), new XYZ(extent, offset, 0));
        }

        return dimensionLineIsVertical
            ? Line.CreateBound(new XYZ(offset, 0, -extent), new XYZ(offset, 0, extent))
            : Line.CreateBound(new XYZ(-extent, 0, offset), new XYZ(extent, 0, offset));
    }

    private static bool IsSupportedFamilySketchView(View view)
    {
        if (view.IsTemplate)
        {
            return false;
        }

        return view.ViewType is ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan or ViewType.Elevation or ViewType.Section or ViewType.Detail;
    }

    private static bool IsPlanLikeView(View view)
    {
        return view.ViewType is ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan;
    }

    private static View ResolveWorkingView(string value, IReadOnlyDictionary<string, View> workingViews)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "plan" => workingViews["plan"],
            "elevation" => workingViews["elevation"],
            _ => throw new InvalidOperationException($"Nieobslugiwany working view: {value}")
        };
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

    private static string? DescribeParameterMismatch(FamilyParameter existingParameter, FamilyParameterSpec requestedParameter)
    {
        List<string> differences = [];
        bool requestedShared = string.Equals(GetParameterKind(requestedParameter), "shared", StringComparison.OrdinalIgnoreCase);

        if (existingParameter.IsShared != requestedShared)
        {
            differences.Add($"kind: oczekiwano {GetParameterKind(requestedParameter)}, znaleziono {GetParameterSourceLabel(existingParameter)}");
        }

        if (existingParameter.IsInstance != IsInstanceParameter(requestedParameter))
        {
            differences.Add($"is_instance: oczekiwano {IsInstanceParameter(requestedParameter)}, znaleziono {existingParameter.IsInstance}");
        }

        ForgeTypeId requestedGroupTypeId = ResolveGroupTypeId(requestedParameter.Group);
        ForgeTypeId? existingGroupTypeId = existingParameter.Definition?.GetGroupTypeId();
        if (!AreTypeIdsEqual(existingGroupTypeId, requestedGroupTypeId))
        {
            differences.Add($"group: oczekiwano {requestedParameter.Group}, znaleziono {existingGroupTypeId?.TypeId ?? "brak"}");
        }

        ForgeTypeId requestedSpecTypeId = ResolveSpecTypeId(GetParameterSpecType(requestedParameter));
        ForgeTypeId? existingSpecTypeId = existingParameter.Definition?.GetDataType();
        if (!AreTypeIdsEqual(existingSpecTypeId, requestedSpecTypeId))
        {
            differences.Add($"spec_type: oczekiwano {GetParameterSpecType(requestedParameter)}, znaleziono {existingSpecTypeId?.TypeId ?? "brak"}");
        }

        return differences.Count == 0
            ? null
            : string.Join("; ", differences);
    }

    private static ExternalDefinition FindSharedDefinition(ParameterAvailability availableParameters, string name)
    {
        if (availableParameters.SharedDefinitions.TryGetValue(name, out ExternalDefinition? definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Nie znaleziono shared parameter wsrod dostepnych definicji: {name}");
    }

    private static bool AreTypeIdsEqual(ForgeTypeId? left, ForgeTypeId? right)
    {
        return string.Equals(left?.TypeId, right?.TypeId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBuiltInParameter(FamilyParameter parameter)
    {
        return parameter.Definition is InternalDefinition internalDefinition &&
               internalDefinition.BuiltInParameter != BuiltInParameter.INVALID;
    }

    private static string GetParameterSourceLabel(FamilyParameter parameter)
    {
        if (parameter.IsShared)
        {
            return "shared";
        }

        return IsBuiltInParameter(parameter)
            ? "system"
            : "family";
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

    private static bool IsInstanceParameter(FamilyParameterSpec parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter.Scope))
        {
            return parameter.IsInstance;
        }

        return parameter.Scope.Trim().Equals("instance", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGeometryOperationKind(GeometryOperationSpec operation)
    {
        return string.IsNullOrWhiteSpace(operation.Kind)
            ? operation.Op.Trim().ToLowerInvariant()
            : operation.Kind.Trim().ToLowerInvariant();
    }

    private static string SanitizeElementName(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "Body" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '_');
        }

        return trimmed.Replace(' ', '_');
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

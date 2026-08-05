using System;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Buduje parametryczny prostopadloscian: reference planes profilu, etykietowane wymiary Width/Height,
/// extrusion, alignment/lock krawedzi szkicu do planes oraz powiazanie glebokosci z parametrem.
/// Logika portowana z dotychczasowego <c>AiFamilyBuilderService.TryCreateParametricRectanglePrism</c>.
/// Gdy width/height nie sa referencja do parametru — fallback do prostej (nieparametrycznej) bryly.
/// </summary>
public sealed class RectangularPrismExecutor : IGeometryExecutor
{
    private const double Extent = 1000.0;

    private sealed record PlaneRef(ReferencePlane Plane, double Offset, bool IsVerticalInView);

    public bool CanExecute(string kind) =>
        kind.Trim().ToLowerInvariant() is "rectangular_prism" or "box" or "void_rectangular_prism";

    public void Execute(BuildContext context, GeometrySpec spec)
    {
        Document document = context.Document;
        string kind = spec.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
        bool isSolid = !kind.Contains("void") && spec.Solid;

        Dictionary<string, double> baseline = GeometryValues.BuildBaseline(context.Request);
        double? widthMm = GeometryValues.ResolveMillimeters(spec.Width, baseline);
        double? heightMm = GeometryValues.ResolveMillimeters(spec.Height, baseline);
        double? depthMm = GeometryValues.ResolveMillimeters(spec.Depth, baseline);

        if (widthMm is null || heightMm is null || depthMm is null)
        {
            context.Result.SkippedGeometryOperationsCount++;
            context.Result.Messages.Add($"Pominieto bryle '{spec.Name}': brak wartosci bazowych dla width/height/depth.");
            return;
        }

        double widthFeet = GeometryValues.ToFeet(widthMm.Value);
        double heightFeet = GeometryValues.ToFeet(heightMm.Value);
        double depthFeet = GeometryValues.ToFeet(depthMm.Value);

        bool widthIsParam = GeometryValues.TryGetRefParameter(spec.Width, out string? widthParamName) &&
                            widthParamName != null && context.Parameters.Contains(widthParamName);
        bool heightIsParam = GeometryValues.TryGetRefParameter(spec.Height, out string? heightParamName) &&
                            heightParamName != null && context.Parameters.Contains(heightParamName);
        View? planView = GetPlanView(document);

        if (!widthIsParam || !heightIsParam || planView == null)
        {
            CreateSimpleBox(context, spec, isSolid, widthFeet, heightFeet, depthFeet, widthMm.Value, heightMm.Value, depthMm.Value, planView == null);
            return;
        }

        // Pelna parametrycznosc.
        context.Parameters.TryGet(widthParamName!, out FamilyParameter widthParam);
        context.Parameters.TryGet(heightParamName!, out FamilyParameter heightParam);

        string baseName = SanitizeElementName(spec.Name);
        double halfWidth = widthFeet / 2.0;
        double halfHeight = heightFeet / 2.0;
        double margin = GeometryValues.ToFeet(250);

        // Diagnostyka per-krok: gdy ktorys Revit API zglosi blad, komunikat wskaze dokladny krok.
        string step = "start";
        try
        {
            step = "reference plane Left";
            PlaneRef left = CreateNamedReferencePlane(context, planView, $"{baseName}_Left", true, -halfWidth);
            step = "reference plane Right";
            PlaneRef right = CreateNamedReferencePlane(context, planView, $"{baseName}_Right", true, halfWidth);
            step = "reference plane Bottom";
            PlaneRef bottom = CreateNamedReferencePlane(context, planView, $"{baseName}_Bottom", false, -halfHeight);
            step = "reference plane Top";
            PlaneRef top = CreateNamedReferencePlane(context, planView, $"{baseName}_Top", false, halfHeight);

            // Regeneracja po utworzeniu planes: Revit musi policzyc geometrie referencji, zanim posluza do wymiarow.
            step = "regenerate planes";
            document.Regenerate();

            step = "wymiar Width";
            CreateLabeledDimension(context, planView, left, right, widthParam, -halfHeight - margin, $"{baseName}_WidthDim");
            step = "wymiar Height";
            CreateLabeledDimension(context, planView, bottom, top, heightParam, halfWidth + margin, $"{baseName}_HeightDim");

            // Centrowanie EQ do srodkowych plaszczyzn origin (symetria wzgledem origin przy flexie).
            step = "centrowanie Width (EQ)";
            ReferencePlane? centerX = FindCenterPlane(document, axisX: true);
            if (centerX != null)
            {
                CreateEqualityDimension(context, planView, left.Plane, centerX, right.Plane, dimLineAlongX: true, halfHeight + margin, $"{baseName}_WidthEQ");
            }
            else
            {
                context.Result.Messages.Add($"Bryla '{baseName}': nie znaleziono srodkowej plaszczyzny X — pominieto EQ Width.");
            }

            step = "centrowanie Height (EQ)";
            ReferencePlane? centerY = FindCenterPlane(document, axisX: false);
            if (centerY != null)
            {
                CreateEqualityDimension(context, planView, bottom.Plane, centerY, top.Plane, dimLineAlongX: false, -halfWidth - margin, $"{baseName}_HeightEQ");
            }
            else
            {
                context.Result.Messages.Add($"Bryla '{baseName}': nie znaleziono srodkowej plaszczyzny Y — pominieto EQ Height.");
            }

            step = "extrusion";
            CurveArrArray profile = new();
            profile.Append(BuildRectangle(-halfWidth, halfWidth, -halfHeight, halfHeight));
            SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
            Extrusion extrusion = document.FamilyCreate.NewExtrusion(isSolid, profile, sketchPlane, depthFeet);
            extrusion.Name = baseName;
            step = "regenerate";
            document.Regenerate();

            step = "alignment";
            int aligned = AlignSketchToReferencePlanes(document, planView, extrusion, left, right, bottom, top);

            GeometryValues.TryGetRefParameter(spec.Depth, out string? depthParamName);
            FamilyParameter? depthParameter = depthParamName != null && context.Parameters.TryGet(depthParamName, out FamilyParameter dp) ? dp : null;

            step = "powiazanie glebokosci";
            if (depthParameter != null)
            {
                AssociateDepth(context, extrusion, depthParameter, baseName);
            }

            // Plaszczyzna sterujaca Depth (pozioma, rownolegla do planu) w elewacji frontowej. Nieblokujaca.
            step = "plaszczyzna Depth";
            TryCreateDepthControlPlane(context, baseName, depthFeet, halfWidth + margin, depthParameter);

            context.Result.ExecutedGeometryOperationsCount++;
            context.Result.Messages.Add(
                $"Parametryczna bryla '{baseName}': planes 4 + EQ, wymiary Width/Height, zablokowane krawedzie {aligned}/4 ({(isSolid ? "solid" : "void")}).");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Bryla '{baseName}' — krok '{step}': {ex.Message}", ex);
        }
    }

    private static void CreateSimpleBox(
        BuildContext context,
        GeometrySpec spec,
        bool isSolid,
        double widthFeet,
        double heightFeet,
        double depthFeet,
        double widthMm,
        double heightMm,
        double depthMm,
        bool noPlanView)
    {
        CurveArrArray profile = new();
        profile.Append(BuildRectangle(-widthFeet / 2.0, widthFeet / 2.0, -heightFeet / 2.0, heightFeet / 2.0));
        SketchPlane sketchPlane = SketchPlane.Create(context.Document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        context.Document.FamilyCreate.NewExtrusion(isSolid, profile, sketchPlane, depthFeet);

        context.Result.ExecutedGeometryOperationsCount++;
        string reason = noPlanView
            ? "brak widoku planu roboczego"
            : "width/height nie sa referencja do parametru";
        context.Result.Messages.Add(
            $"Bryla '{SanitizeElementName(spec.Name)}': {widthMm}x{heightMm}x{depthMm} mm, nieparametryczna ({reason}).");
    }

    private static View? GetPlanView(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(view => !view.IsTemplate &&
                view.ViewType is ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan);
    }

    private static PlaneRef CreateNamedReferencePlane(BuildContext context, View planView, string requestedName, bool isVerticalInView, double offset)
    {
        ReferencePlane plane = CreatePlanReferencePlane(context.Document, planView, isVerticalInView, offset);
        string name = EnsureUniqueName(context, requestedName);
        plane.Name = name;
        context.References.Planes[name] = plane.Id;
        context.Result.CreatedReferencePlanesCount++;
        return new PlaneRef(plane, offset, isVerticalInView);
    }

    private static string EnsureUniqueName(BuildContext context, string requestedName)
    {
        string name = requestedName;
        int index = 2;
        while (context.References.Planes.ContainsKey(name))
        {
            name = $"{requestedName}_{index}";
            index++;
        }

        return name;
    }

    private static ReferencePlane CreatePlanReferencePlane(Document document, View planView, bool isVerticalInView, double offset)
    {
        // NewReferencePlane (przeciazenie z cutVec): cutVec = BasisZ to kierunek patrzenia widoku planu,
        // co daje prawidlowa plaszczyzne pionowa. (NewReferencePlane2 traktuje 3. argument jako punkt -> tilt.)
        return isVerticalInView
            ? document.FamilyCreate.NewReferencePlane(
                new XYZ(offset, -Extent, 0),
                new XYZ(offset, Extent, 0),
                XYZ.BasisZ,
                planView)
            : document.FamilyCreate.NewReferencePlane(
                new XYZ(-Extent, offset, 0),
                new XYZ(Extent, offset, 0),
                XYZ.BasisZ,
                planView);
    }

    private static void CreateLabeledDimension(
        BuildContext context,
        View planView,
        PlaneRef referenceA,
        PlaneRef referenceB,
        FamilyParameter parameter,
        double offset,
        string name)
    {
        ReferenceArray references = new();
        references.Append(referenceA.Plane.GetReference());
        references.Append(referenceB.Plane.GetReference());

        // Wymiar miedzy planes pionowymi (stale X) jest pozioma linia; miedzy poziomymi (stale Y) — pionowa.
        bool dimensionLineIsVertical = !referenceA.IsVerticalInView && !referenceB.IsVerticalInView;
        Line dimensionLine = dimensionLineIsVertical
            ? Line.CreateBound(new XYZ(offset, -Extent, 0), new XYZ(offset, Extent, 0))
            : Line.CreateBound(new XYZ(-Extent, offset, 0), new XYZ(Extent, offset, 0));

        Dimension dimension = context.Document.FamilyCreate.NewLinearDimension(planView, dimensionLine, references);
        dimension.FamilyLabel = parameter;
        context.Result.CreatedDimensionsCount++;
        context.Result.Messages.Add($"Dodano wymiar: {name} -> {parameter.Definition.Name}");
    }

    private static ReferencePlane? FindCenterPlane(Document document, bool axisX)
    {
        const double tolerance = 1e-6;
        foreach (ReferencePlane plane in new FilteredElementCollector(document).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>())
        {
            Plane geometryPlane;
            try
            {
                geometryPlane = plane.GetPlane();
            }
            catch
            {
                continue;
            }

            XYZ normal = geometryPlane.Normal;
            XYZ origin = geometryPlane.Origin;

            if (axisX)
            {
                if (Math.Abs(Math.Abs(normal.X) - 1) < 1e-3 && Math.Abs(normal.Z) < 1e-3 && Math.Abs(origin.X) < tolerance)
                {
                    return plane;
                }
            }
            else if (Math.Abs(Math.Abs(normal.Y) - 1) < 1e-3 && Math.Abs(normal.Z) < 1e-3 && Math.Abs(origin.Y) < tolerance)
            {
                return plane;
            }
        }

        return null;
    }

    private static void CreateEqualityDimension(
        BuildContext context,
        View planView,
        ReferencePlane outerA,
        ReferencePlane center,
        ReferencePlane outerB,
        bool dimLineAlongX,
        double offset,
        string name)
    {
        ReferenceArray references = new();
        references.Append(outerA.GetReference());
        references.Append(center.GetReference());
        references.Append(outerB.GetReference());

        Line line = dimLineAlongX
            ? Line.CreateBound(new XYZ(-Extent, offset, 0), new XYZ(Extent, offset, 0))
            : Line.CreateBound(new XYZ(offset, -Extent, 0), new XYZ(offset, Extent, 0));

        Dimension dimension = context.Document.FamilyCreate.NewLinearDimension(planView, line, references);
        dimension.AreSegmentsEqual = true;
        context.Result.CreatedDimensionsCount++;
        context.Result.Messages.Add($"Dodano EQ centrowanie: {name}");
    }

    private static void TryCreateDepthControlPlane(BuildContext context, string baseName, double depthFeet, double sideOffset, FamilyParameter? depthParameter)
    {
        Document document = context.Document;
        View? elevation = GetFrontElevation(document);
        if (elevation == null)
        {
            context.Result.Messages.Add($"Bryla '{baseName}': brak elewacji frontowej — pominieto plaszczyzne Depth.");
            return;
        }

        try
        {
            ReferencePlane basePlane = CreateHorizontalElevationPlane(document, elevation, 0);
            basePlane.Name = EnsureUniqueName(context, $"{baseName}_DepthBase");
            context.References.Planes[basePlane.Name] = basePlane.Id;
            context.Result.CreatedReferencePlanesCount++;

            ReferencePlane depthPlane = CreateHorizontalElevationPlane(document, elevation, depthFeet);
            depthPlane.Name = EnsureUniqueName(context, $"{baseName}_Depth");
            context.References.Planes[depthPlane.Name] = depthPlane.Id;
            context.Result.CreatedReferencePlanesCount++;

            document.Regenerate();

            ReferenceArray references = new();
            references.Append(basePlane.GetReference());
            references.Append(depthPlane.GetReference());
            Line line = Line.CreateBound(new XYZ(sideOffset, 0, -Extent), new XYZ(sideOffset, 0, Extent));
            Dimension dimension = document.FamilyCreate.NewLinearDimension(elevation, line, references);
            if (depthParameter != null)
            {
                dimension.FamilyLabel = depthParameter;
            }

            context.Result.CreatedDimensionsCount++;
            context.Result.Messages.Add($"Bryla '{baseName}': dodano plaszczyzne sterujaca Depth + wymiar.");
        }
        catch (Exception ex)
        {
            context.Result.Messages.Add($"Bryla '{baseName}': nie udalo sie utworzyc plaszczyzny Depth: {ex.Message}");
        }
    }

    private static View? GetFrontElevation(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => !view.IsTemplate && view.ViewType is ViewType.Elevation or ViewType.Section)
            .FirstOrDefault(view => Math.Abs(Math.Abs(view.ViewDirection.Y) - 1) < 1e-3);
    }

    private static ReferencePlane CreateHorizontalElevationPlane(Document document, View elevation, double z)
    {
        // Plaszczyzna pozioma (stale Z) widziana w elewacji frontowej: linia wzdluz X, cutVec = BasisY (kierunek patrzenia).
        return document.FamilyCreate.NewReferencePlane(
            new XYZ(-Extent, 0, z),
            new XYZ(Extent, 0, z),
            XYZ.BasisY,
            elevation);
    }

    private static int AlignSketchToReferencePlanes(Document document, View view, Extrusion extrusion, PlaneRef left, PlaneRef right, PlaneRef bottom, PlaneRef top)
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
            PlaneRef? target = ResolveEdgeTarget(start, end, left, right, bottom, top);
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
                // Alignment moze sie nie udac dla niektorych szablonow lub gdy istnieje juz rownowazne wiazanie.
            }
        }

        return aligned;
    }

    private static PlaneRef? ResolveEdgeTarget(XYZ start, XYZ end, PlaneRef left, PlaneRef right, PlaneRef bottom, PlaneRef top)
    {
        const double tolerance = 1e-6;
        if (Math.Abs(start.X - end.X) < tolerance)
        {
            return Math.Abs(start.X - left.Offset) < Math.Abs(start.X - right.Offset) ? left : right;
        }

        if (Math.Abs(start.Y - end.Y) < tolerance)
        {
            return Math.Abs(start.Y - bottom.Offset) < Math.Abs(start.Y - top.Offset) ? bottom : top;
        }

        return null;
    }

    private static void AssociateDepth(BuildContext context, Extrusion extrusion, FamilyParameter depthParameter, string baseName)
    {
        Parameter? extrusionDepth =
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM) ??
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_DEPTH_PARAM) ??
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_LENGTH);

        if (extrusionDepth == null)
        {
            context.Result.Messages.Add($"Bryla '{baseName}': nie znaleziono parametru glebokosci do powiazania z {depthParameter.Definition.Name}.");
            return;
        }

        try
        {
            context.Document.FamilyManager.AssociateElementParameterToFamilyParameter(extrusionDepth, depthParameter);
            context.Result.Messages.Add($"Bryla '{baseName}': powiazano glebokosc z {depthParameter.Definition.Name}.");
        }
        catch (Exception ex)
        {
            context.Result.Messages.Add($"Bryla '{baseName}': nie udalo sie powiazac glebokosci z {depthParameter.Definition.Name}: {ex.Message}");
        }
    }

    private static CurveArray BuildRectangle(double minX, double maxX, double minY, double maxY)
    {
        CurveArray rectangle = new();
        rectangle.Append(Line.CreateBound(new XYZ(minX, minY, 0), new XYZ(maxX, minY, 0)));
        rectangle.Append(Line.CreateBound(new XYZ(maxX, minY, 0), new XYZ(maxX, maxY, 0)));
        rectangle.Append(Line.CreateBound(new XYZ(maxX, maxY, 0), new XYZ(minX, maxY, 0)));
        rectangle.Append(Line.CreateBound(new XYZ(minX, maxY, 0), new XYZ(minX, minY, 0)));
        return rectangle;
    }

    private static string SanitizeElementName(string? value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "Body" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '_');
        }

        return trimmed.Replace(' ', '_');
    }
}

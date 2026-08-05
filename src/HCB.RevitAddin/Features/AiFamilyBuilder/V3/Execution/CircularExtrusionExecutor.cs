using System;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Tworzy okragle ekstruzje z wartosci bazowych typu. Obecnie parametryzuje glebokosc,
/// a promien tworzy jako geometrie bazowa wedlug pierwszego typu.
/// </summary>
public sealed class CircularExtrusionExecutor : IGeometryExecutor
{
    public bool CanExecute(string kind) =>
        kind.Trim().ToLowerInvariant() is "circular_extrusion" or "extrusion_circle" or "circle" or "void_circular_extrusion" or "void_circle";

    public void Execute(BuildContext context, GeometrySpec spec)
    {
        Document document = context.Document;
        string kind = spec.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
        bool isSolid = !kind.Contains("void") && spec.Solid;

        Dictionary<string, double> baseline = GeometryValues.BuildBaseline(context.Request);
        double? radiusMm = GeometryValues.ResolveMillimeters(spec.Radius, baseline);
        double? depthMm = GeometryValues.ResolveMillimeters(spec.Depth, baseline);

        if (radiusMm is null || depthMm is null)
        {
            context.Result.SkippedGeometryOperationsCount++;
            context.Result.Messages.Add($"Pominieto okragla ekstruzje '{spec.Name}': brak wartosci bazowych dla radius/depth.");
            return;
        }

        if (radiusMm.Value <= 0 || depthMm.Value <= 0)
        {
            context.Result.SkippedGeometryOperationsCount++;
            context.Result.Messages.Add($"Pominieto okragla ekstruzje '{spec.Name}': radius i depth musza byc dodatnie.");
            return;
        }

        double xMm = ResolveOffset(spec, baseline, "x", "center_x");
        double yMm = ResolveOffset(spec, baseline, "y", "center_y");
        double zMm = ResolveOffset(spec, baseline, "z", "center_z");
        double startMm = ResolveOffset(spec, baseline, "start", "extrusion_start");
        XYZ center = new(GeometryValues.ToFeet(xMm), GeometryValues.ToFeet(yMm), GeometryValues.ToFeet(zMm));
        AxisFrame frame = ResolveAxisFrame(spec, GeometryValues.ToFeet(startMm));
        double radiusFeet = GeometryValues.ToFeet(radiusMm.Value);
        double depthFeet = GeometryValues.ToFeet(depthMm.Value);

        CurveArrArray profile = new();
        profile.Append(BuildCircleProfile(center.Add(frame.Normal.Multiply(frame.StartOffset)), frame.U, frame.V, radiusFeet));
        SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(frame.Normal, frame.Normal.Multiply(frame.StartOffset)));
        Extrusion extrusion = document.FamilyCreate.NewExtrusion(isSolid, profile, sketchPlane, depthFeet);

        string name = SanitizeElementName(spec.Name);
        extrusion.Name = name;

        GeometryValues.TryGetRefParameter(spec.Depth, out string? depthParamName);
        if (!string.IsNullOrWhiteSpace(depthParamName) &&
            context.Parameters.TryGet(depthParamName, out FamilyParameter depthParameter))
        {
            AssociateDepth(context, extrusion, depthParameter, name);
        }

        context.Result.ExecutedGeometryOperationsCount++;
        context.Result.Messages.Add(
            $"Okragla ekstruzja '{name}': R={radiusMm.Value} mm, depth={depthMm.Value} mm, center=({xMm}, {yMm}, {zMm}) mm, plane={frame.Name}.");
    }

    private static double ResolveOffset(GeometrySpec spec, IReadOnlyDictionary<string, double> baseline, params string[] keys)
    {
        if (spec.Offsets == null)
        {
            return 0;
        }

        foreach (string key in keys)
        {
            if (spec.Offsets.TryGetValue(key, out ValueRefSpec? value))
            {
                return GeometryValues.ResolveMillimeters(value, baseline) ?? 0;
            }
        }

        return 0;
    }

    private static AxisFrame ResolveAxisFrame(GeometrySpec spec, double startOffset)
    {
        string workPlane = spec.Placement?.WorkPlane?.Trim().ToLowerInvariant() ?? "xy";
        return workPlane switch
        {
            "x" or "yz" or "side" => new AxisFrame("yz", XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ, startOffset),
            "y" or "xz" or "front" or "back" => new AxisFrame("xz", XYZ.BasisY, XYZ.BasisX, XYZ.BasisZ, startOffset),
            "z" or "xy" or "plan" or "top" or "bottom" => new AxisFrame("xy", XYZ.BasisZ, XYZ.BasisX, XYZ.BasisY, startOffset),
            _ => new AxisFrame("xy", XYZ.BasisZ, XYZ.BasisX, XYZ.BasisY, startOffset)
        };
    }

    private static CurveArray BuildCircleProfile(XYZ center, XYZ u, XYZ v, double radius)
    {
        CurveArray profile = new();
        XYZ top = center.Add(v.Multiply(radius));
        XYZ bottom = center.Add(v.Multiply(-radius));
        XYZ right = center.Add(u.Multiply(radius));
        XYZ left = center.Add(u.Multiply(-radius));

        profile.Append(Arc.Create(top, bottom, right));
        profile.Append(Arc.Create(bottom, top, left));
        return profile;
    }

    private static void AssociateDepth(BuildContext context, Extrusion extrusion, FamilyParameter depthParameter, string baseName)
    {
        Parameter? extrusionDepth =
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM) ??
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_DEPTH_PARAM) ??
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_LENGTH);

        if (extrusionDepth == null)
        {
            context.Result.Messages.Add($"Okragla ekstruzja '{baseName}': nie znaleziono parametru glebokosci do powiazania z {depthParameter.Definition.Name}.");
            return;
        }

        try
        {
            context.Document.FamilyManager.AssociateElementParameterToFamilyParameter(extrusionDepth, depthParameter);
            context.Result.Messages.Add($"Okragla ekstruzja '{baseName}': powiazano glebokosc z {depthParameter.Definition.Name}.");
        }
        catch (Exception ex)
        {
            context.Result.Messages.Add($"Okragla ekstruzja '{baseName}': nie udalo sie powiazac glebokosci z {depthParameter.Definition.Name}: {ex.Message}");
        }
    }

    private static string SanitizeElementName(string? value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "CircularBody" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '_');
        }

        return trimmed.Replace(' ', '_');
    }

    private sealed record AxisFrame(string Name, XYZ Normal, XYZ U, XYZ V, double StartOffset);
}

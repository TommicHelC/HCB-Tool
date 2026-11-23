using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using HCB.RevitAddin.Features.ConnectorExtend.Models;

namespace HCB.RevitAddin.Features.ConnectorExtend;

public sealed class ConnectorExtendService
{
    public static readonly double DefaultLengthInternal = UnitUtils.ConvertToInternalUnits(500d, UnitTypeId.Millimeters);

    private enum SegmentKind
    {
        None,
        Duct,
        Pipe,
        Conduit
    }

    public bool IsSupported(Element element)
    {
        return MepConnectorHelper.GetPhysicalConnectors(element).Any(connector => !connector.IsConnected);
    }

    public ConnectorExtendResult Extend(Document document, View view, Element element, XYZ? pickPoint, double lengthInternal)
    {
        Connector? connector = MepConnectorHelper.FindClosestConnector(element, pickPoint, preferFree: true);
        if (connector == null || connector.IsConnected)
        {
            return BuildFailure(element, "brak wolnego konektora");
        }

        MEPCurve? created = CreateSegment(
            document,
            view,
            element,
            connector,
            connector.Origin,
            MepConnectorHelper.GetDirection(connector),
            lengthInternal,
            connectToSource: true,
            out string? error);
        if (created == null)
        {
            return BuildFailure(element, error ?? "nie udalo sie wstawic odcinka");
        }

        return new ConnectorExtendResult
        {
            HasChanges = true,
            CreatedId = created.Id,
            Message = $"{MepConnectorHelper.Describe(element)}: wstawiono odcinek {created.Id.Value}."
        };
    }

    /// <summary>
    /// Creates a straight duct, pipe or conduit matching the source connector's domain, size, type and system.
    /// </summary>
    public MEPCurve? CreateSegment(
        Document document,
        View view,
        Element source,
        Connector sourceConnector,
        XYZ start,
        XYZ direction,
        double lengthInternal,
        bool connectToSource,
        out string? error)
    {
        error = null;
        SegmentKind kind = ResolveKind(sourceConnector);
        if (kind == SegmentKind.None)
        {
            error = "nieobslugiwany typ konektora";
            return null;
        }

        ElementId levelId = ResolveLevelId(document, view, source, start);
        if (levelId == ElementId.InvalidElementId)
        {
            error = "nie znaleziono poziomu";
            return null;
        }

        XYZ end = start + direction.Normalize().Multiply(lengthInternal);
        MEPCurve? created = kind switch
        {
            SegmentKind.Duct => CreateDuct(document, source, sourceConnector, levelId, start, end),
            SegmentKind.Pipe => CreatePipe(document, source, sourceConnector, levelId, start, end),
            SegmentKind.Conduit => CreateConduit(document, source, sourceConnector, levelId, start, end),
            _ => null
        };
        if (created == null)
        {
            error = $"brak typu {kind} w projekcie";
            return null;
        }

        ApplySize(created, sourceConnector);
        document.Regenerate();

        if (connectToSource)
        {
            Connector? newConnector = MepConnectorHelper.FindConnectorAt(created, sourceConnector.Origin);
            try
            {
                if (newConnector != null && !newConnector.IsConnected)
                {
                    newConnector.ConnectTo(sourceConnector);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // The segment stays in place; the user can link it with Connect.
            }
        }

        return created;
    }

    private static SegmentKind ResolveKind(Connector connector)
    {
        return connector.Domain switch
        {
            Domain.DomainHvac => SegmentKind.Duct,
            Domain.DomainPiping => SegmentKind.Pipe,
            // Cable trays share this domain but have rectangular profiles; conduits are round.
            Domain.DomainCableTrayConduit when connector.Shape == ConnectorProfileType.Round => SegmentKind.Conduit,
            _ => SegmentKind.None
        };
    }

    private static MEPCurve? CreateDuct(Document document, Element element, Connector connector, ElementId levelId, XYZ start, XYZ end)
    {
        Duct? neighbor = FindNeighborCurve<Duct>(element, connector.Shape);
        ElementId typeId = neighbor?.GetTypeId()
            ?? new FilteredElementCollector(document)
                .OfClass(typeof(DuctType))
                .Cast<DuctType>()
                .FirstOrDefault(type => type.Shape == connector.Shape)?.Id
            ?? ElementId.InvalidElementId;
        if (typeId == ElementId.InvalidElementId)
        {
            return null;
        }

        ElementId systemTypeId = ResolveSystemTypeId(neighbor, element, connector, BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
        if (systemTypeId == ElementId.InvalidElementId)
        {
            List<MechanicalSystemType> systemTypes = new FilteredElementCollector(document)
                .OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>()
                .ToList();
            DuctSystemType classification = connector.DuctSystemType;
            systemTypeId = (systemTypes.FirstOrDefault(type => type.SystemClassification == ToClassification(classification))
                ?? systemTypes.FirstOrDefault())?.Id ?? ElementId.InvalidElementId;
        }

        return systemTypeId == ElementId.InvalidElementId
            ? null
            : Duct.Create(document, systemTypeId, typeId, levelId, start, end);
    }

    private static MEPCurve? CreatePipe(Document document, Element element, Connector connector, ElementId levelId, XYZ start, XYZ end)
    {
        Pipe? neighbor = FindNeighborCurve<Pipe>(element, null);
        ElementId typeId = neighbor?.GetTypeId()
            ?? new FilteredElementCollector(document).OfClass(typeof(PipeType)).FirstElementId();
        if (typeId == ElementId.InvalidElementId)
        {
            return null;
        }

        ElementId systemTypeId = ResolveSystemTypeId(neighbor, element, connector, BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
        if (systemTypeId == ElementId.InvalidElementId)
        {
            List<PipingSystemType> systemTypes = new FilteredElementCollector(document)
                .OfClass(typeof(PipingSystemType))
                .Cast<PipingSystemType>()
                .ToList();
            PipeSystemType classification = connector.PipeSystemType;
            systemTypeId = (systemTypes.FirstOrDefault(type => type.SystemClassification == ToClassification(classification))
                ?? systemTypes.FirstOrDefault())?.Id ?? ElementId.InvalidElementId;
        }

        return systemTypeId == ElementId.InvalidElementId
            ? null
            : Pipe.Create(document, systemTypeId, typeId, levelId, start, end);
    }

    private static MEPCurve? CreateConduit(Document document, Element element, Connector connector, ElementId levelId, XYZ start, XYZ end)
    {
        Conduit? neighbor = FindNeighborCurve<Conduit>(element, null);
        ElementId typeId = neighbor?.GetTypeId()
            ?? new FilteredElementCollector(document).OfClass(typeof(ConduitType)).FirstElementId();
        return typeId == ElementId.InvalidElementId
            ? null
            : Conduit.Create(document, typeId, start, end, levelId);
    }

    private static T? FindNeighborCurve<T>(Element element, ConnectorProfileType? shape) where T : MEPCurve
    {
        List<T> candidates = [];
        if (element is T self)
        {
            candidates.Add(self);
        }

        foreach (Connector connector in MepConnectorHelper.GetPhysicalConnectors(element))
        {
            foreach (Connector partner in MepConnectorHelper.GetConnectedPartners(connector))
            {
                if (partner.Owner is T curve)
                {
                    candidates.Add(curve);
                }
            }
        }

        if (shape == null)
        {
            return candidates.FirstOrDefault();
        }

        return candidates.FirstOrDefault(curve => (curve.Document.GetElement(curve.GetTypeId()) as MEPCurveType)?.Shape == shape);
    }

    private static ElementId ResolveSystemTypeId(MEPCurve? neighbor, Element element, Connector connector, BuiltInParameter parameter)
    {
        foreach (Element? source in new[] { neighbor, element })
        {
            ElementId? id = source?.get_Parameter(parameter)?.AsElementId();
            if (id != null && id != ElementId.InvalidElementId)
            {
                return id;
            }
        }

        try
        {
            return connector.MEPSystem?.GetTypeId() ?? ElementId.InvalidElementId;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return ElementId.InvalidElementId;
        }
    }

    private static MEPSystemClassification ToClassification(DuctSystemType systemType)
    {
        return systemType switch
        {
            DuctSystemType.SupplyAir => MEPSystemClassification.SupplyAir,
            DuctSystemType.ReturnAir => MEPSystemClassification.ReturnAir,
            DuctSystemType.ExhaustAir => MEPSystemClassification.ExhaustAir,
            _ => MEPSystemClassification.OtherAir
        };
    }

    private static MEPSystemClassification ToClassification(PipeSystemType systemType)
    {
        return Enum.TryParse(systemType.ToString(), out MEPSystemClassification classification)
            ? classification
            : MEPSystemClassification.OtherPipe;
    }

    private static ElementId ResolveLevelId(Document document, View view, Element element, XYZ point)
    {
        if (element.LevelId != ElementId.InvalidElementId)
        {
            return element.LevelId;
        }

        foreach (BuiltInParameter parameter in new[]
                 {
                     BuiltInParameter.RBS_START_LEVEL_PARAM,
                     BuiltInParameter.FAMILY_LEVEL_PARAM,
                     BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                     BuiltInParameter.SCHEDULE_LEVEL_PARAM
                 })
        {
            ElementId? id = element.get_Parameter(parameter)?.AsElementId();
            if (id != null && id != ElementId.InvalidElementId)
            {
                return id;
            }
        }

        if (view.GenLevel != null)
        {
            return view.GenLevel.Id;
        }

        List<Level> levels = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .ToList();
        return (levels.LastOrDefault(level => level.Elevation <= point.Z + 1e-6) ?? levels.FirstOrDefault())?.Id
            ?? ElementId.InvalidElementId;
    }

    private static void ApplySize(MEPCurve curve, Connector connector)
    {
        switch (connector.Shape)
        {
            case ConnectorProfileType.Round:
                BuiltInParameter diameterParameter = curve switch
                {
                    Pipe => BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,
                    Conduit => BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM,
                    _ => BuiltInParameter.RBS_CURVE_DIAMETER_PARAM
                };
                TrySet(curve, diameterParameter, connector.Radius * 2d);
                break;
            case ConnectorProfileType.Rectangular:
            case ConnectorProfileType.Oval:
                TrySet(curve, BuiltInParameter.RBS_CURVE_WIDTH_PARAM, connector.Width);
                TrySet(curve, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, connector.Height);
                break;
        }
    }

    private static void TrySet(Element element, BuiltInParameter parameter, double value)
    {
        Parameter? target = element.get_Parameter(parameter);
        if (target == null || target.IsReadOnly || value <= 0d)
        {
            return;
        }

        try
        {
            target.Set(value);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Size outside the type's allowed range; keep the default size of the created segment.
        }
    }

    private static ConnectorExtendResult BuildFailure(Element element, string reason)
    {
        return new ConnectorExtendResult
        {
            HasChanges = false,
            Message = $"{MepConnectorHelper.Describe(element)}: pominieto, {reason}."
        };
    }
}

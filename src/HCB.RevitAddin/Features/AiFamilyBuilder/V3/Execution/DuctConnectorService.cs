using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Tworzy konektory HVAC na scianach utworzonej geometrii rodzinnej.
/// </summary>
public sealed class DuctConnectorService : IConnectorService
{
    public void Create(BuildContext context)
    {
        if (context.Request.Connectors.Count == 0)
        {
            return;
        }

        Dictionary<string, ConnectorElement> created = new(StringComparer.OrdinalIgnoreCase);
        List<string> failures = [];
        foreach (ConnectorSpec spec in context.Request.Connectors)
        {
            if (!string.Equals(spec.Domain, "hvac", StringComparison.OrdinalIgnoreCase))
            {
                string message = $"Pominieto konektor '{spec.Name}': obslugiwane sa teraz tylko konektory HVAC.";
                failures.Add(message);
                context.Result.Messages.Add(message);
                continue;
            }

            try
            {
                ConnectorElement connector = CreateDuctConnector(context, spec);
                created[spec.Name] = connector;
                context.Result.CreatedConnectorsCount++;

                if (spec.IsPrimary)
                {
                    connector.AssignAsPrimary();
                }

                context.Result.Messages.Add($"Dodano konektor HVAC: {spec.Name}");
            }
            catch (Exception ex)
            {
                context.Result.SkippedConnectorsCount++;
                string message = $"Blad konektora '{spec.Name}': {ex.Message}";
                failures.Add(message);
                context.Result.Messages.Add(message);
            }
        }

        LinkConnectors(context, created);

        if (context.Request.Validation.RequireConnectors &&
            created.Count < context.Request.Connectors.Count)
        {
            throw new InvalidOperationException(
                $"Nie utworzono wszystkich wymaganych konektorow: {created.Count}/{context.Request.Connectors.Count}. " +
                string.Join(" | ", failures));
        }
    }

    private static ConnectorElement CreateDuctConnector(BuildContext context, ConnectorSpec spec)
    {
        if (spec.Host == null || string.IsNullOrWhiteSpace(spec.Host.Geometry))
        {
            throw new InvalidOperationException("Konektor wymaga host.geometry.");
        }

        Element host = FindHostGeometry(context.Document, spec.Host.Geometry!);
        HostFace hostFace = FindHostFace(host, spec.Host.Face);
        ConnectorProfileType profileType = ResolveProfileType(spec.Shape);
        DuctSystemType systemType = ResolveDuctSystemType(spec.SystemType);

        ConnectorElement connector = CreateConnectorElement(
            context.Document,
            systemType,
            profileType,
            hostFace);

        ApplySize(context, connector, spec);
        ApplyFlowDirection(connector, spec.Direction);
        return connector;
    }

    private static ConnectorElement CreateConnectorElement(
        Document document,
        DuctSystemType systemType,
        ConnectorProfileType profileType,
        HostFace hostFace)
    {
        if (hostFace.Edge != null)
        {
            try
            {
                return ConnectorElement.CreateDuctConnector(
                    document,
                    systemType,
                    profileType,
                    hostFace.Face.Reference,
                    hostFace.Edge);
            }
            catch (Exception edgeException)
            {
                try
                {
                    return ConnectorElement.CreateDuctConnector(
                        document,
                        systemType,
                        profileType,
                        hostFace.Face.Reference);
                }
                catch (Exception faceException)
                {
                    throw new InvalidOperationException(
                        $"API Revita odrzucilo konektor na scianie i krawedzi: {edgeException.Message}; " +
                        $"proba tylko na scianie: {faceException.Message}",
                        faceException);
                }
            }
        }

        return ConnectorElement.CreateDuctConnector(
            document,
            systemType,
            profileType,
            hostFace.Face.Reference);
    }

    private static Element FindHostGeometry(Document document, string geometryName)
    {
        Element? host = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .FirstOrDefault(element =>
                element is GenericForm &&
                string.Equals(element.Name, geometryName, StringComparison.OrdinalIgnoreCase));

        return host ?? throw new InvalidOperationException($"Nie znaleziono geometrii hostujacej '{geometryName}'.");
    }

    private static HostFace FindHostFace(Element host, string? faceName)
    {
        XYZ expectedNormal = ResolveFaceNormal(faceName);
        Options options = new()
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = true
        };

        PlanarFace? bestFace = null;
        double bestDot = double.NegativeInfinity;
        GeometryElement? geometry = host.get_Geometry(options);
        if (geometry != null)
        {
            foreach (Solid solid in EnumerateSolids(geometry))
            {
                foreach (Face face in solid.Faces)
                {
                    if (face is not PlanarFace planar || planar.Reference == null)
                    {
                        continue;
                    }

                    double dot = planar.FaceNormal.Normalize().DotProduct(expectedNormal);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestFace = planar;
                    }
                }
            }
        }

        if (bestFace == null || bestDot < 0.85)
        {
            throw new InvalidOperationException($"Nie znaleziono plaskiej sciany '{faceName ?? "front"}' na geometrii '{host.Name}'.");
        }

        return new HostFace(bestFace, FindPlacementEdge(bestFace));
    }

    private static Edge? FindPlacementEdge(PlanarFace face)
    {
        Edge? bestEdge = null;
        double bestLength = double.NegativeInfinity;

        foreach (EdgeArray loop in face.EdgeLoops)
        {
            foreach (Edge edge in loop)
            {
                double length = edge.ApproximateLength;
                if (length > bestLength)
                {
                    bestLength = length;
                    bestEdge = edge;
                }
            }
        }

        return bestEdge;
    }

    private static IEnumerable<Solid> EnumerateSolids(GeometryElement geometry)
    {
        foreach (GeometryObject obj in geometry)
        {
            switch (obj)
            {
                case Solid solid when solid.Volume > 0:
                    yield return solid;
                    break;

                case GeometryInstance instance:
                    foreach (Solid nested in EnumerateSolids(instance.GetInstanceGeometry()))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static void ApplySize(BuildContext context, ConnectorElement connector, ConnectorSpec spec)
    {
        if (spec.Size == null)
        {
            return;
        }

        Dictionary<string, double> baseline = GeometryValues.BuildBaseline(context.Request);
        if (connector.Shape == ConnectorProfileType.Round)
        {
            double? diameterMm = GeometryValues.ResolveMillimeters(spec.Size.Diameter, baseline);
            if (diameterMm.HasValue)
            {
                double diameterFeet = GeometryValues.ToFeet(diameterMm.Value);
                double radiusFeet = GeometryValues.ToFeet(diameterMm.Value / 2.0);
                if (!TrySetDouble(connector, BuiltInParameter.CONNECTOR_DIAMETER, diameterFeet))
                {
                    TrySetDouble(connector, BuiltInParameter.CONNECTOR_RADIUS, radiusFeet);
                }
            }

            return;
        }

        double? widthMm = GeometryValues.ResolveMillimeters(spec.Size.Width, baseline);
        double? heightMm = GeometryValues.ResolveMillimeters(spec.Size.Height, baseline);
        if (widthMm.HasValue)
        {
            TrySetDouble(connector, BuiltInParameter.CONNECTOR_WIDTH, GeometryValues.ToFeet(widthMm.Value));
        }

        if (heightMm.HasValue)
        {
            TrySetDouble(connector, BuiltInParameter.CONNECTOR_HEIGHT, GeometryValues.ToFeet(heightMm.Value));
        }
    }

    private static void ApplyFlowDirection(ConnectorElement connector, string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
        {
            return;
        }

        FlowDirectionType desired = direction.Trim().ToLowerInvariant() switch
        {
            "in" => FlowDirectionType.In,
            "out" => FlowDirectionType.Out,
            "bidirectional" => FlowDirectionType.Bidirectional,
            _ => FlowDirectionType.Bidirectional
        };

        Parameter? parameter = connector.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_DIRECTION_PARAM);
        if (parameter == null || parameter.IsReadOnly)
        {
            return;
        }

        try
        {
            parameter.Set((int)desired);
        }
        catch
        {
            // Nie wszystkie szablony pozwalaja ustawic kierunek przeplywu na etapie tworzenia konektora.
        }
    }

    private static bool TrySetDouble(Element element, BuiltInParameter builtInParameter, double value)
    {
        Parameter? parameter = element.get_Parameter(builtInParameter);
        if (parameter == null || parameter.IsReadOnly)
        {
            return false;
        }

        try
        {
            parameter.Set(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LinkConnectors(BuildContext context, IReadOnlyDictionary<string, ConnectorElement> created)
    {
        foreach (ConnectorSpec spec in context.Request.Connectors)
        {
            if (string.IsNullOrWhiteSpace(spec.LinkedTo) ||
                !created.TryGetValue(spec.Name, out ConnectorElement? connector) ||
                !created.TryGetValue(spec.LinkedTo!, out ConnectorElement? linked))
            {
                continue;
            }

            try
            {
                connector.SetLinkedConnectorElement(linked);
                context.Result.Messages.Add($"Polaczono konektory: {spec.Name} -> {spec.LinkedTo}");
            }
            catch (Exception ex)
            {
                context.Result.Messages.Add($"Nie udalo sie polaczyc konektorow '{spec.Name}' i '{spec.LinkedTo}': {ex.Message}");
            }
        }
    }

    private static XYZ ResolveFaceNormal(string? faceName)
    {
        return faceName?.Trim().ToLowerInvariant() switch
        {
            "top" => XYZ.BasisZ,
            "bottom" => XYZ.BasisZ.Negate(),
            "right" => XYZ.BasisX,
            "left" => XYZ.BasisX.Negate(),
            "back" => XYZ.BasisY,
            "front" => XYZ.BasisY.Negate(),
            _ => XYZ.BasisY.Negate()
        };
    }

    private static ConnectorProfileType ResolveProfileType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "round" => ConnectorProfileType.Round,
            "rectangular" => ConnectorProfileType.Rectangular,
            "oval" => ConnectorProfileType.Oval,
            _ => ConnectorProfileType.Round
        };
    }

    private static DuctSystemType ResolveDuctSystemType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "supplyair" or "supply" => DuctSystemType.SupplyAir,
            "returnair" or "return" => DuctSystemType.ReturnAir,
            "exhaustair" or "exhaust" => DuctSystemType.ExhaustAir,
            "otherair" or "other" => DuctSystemType.OtherAir,
            "fitting" => DuctSystemType.Fitting,
            "global" => DuctSystemType.Global,
            _ => DuctSystemType.SupplyAir
        };
    }

    private sealed record HostFace(PlanarFace Face, Edge? Edge);
}

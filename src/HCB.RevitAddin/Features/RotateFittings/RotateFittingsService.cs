using System;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.RotateFittings.Models;

namespace HCB.RevitAddin.Features.RotateFittings;

public sealed class RotateFittingsService
{
    public bool IsSupported(Element element, RotateFittingsOptions options)
    {
        if (element is not FamilyInstance || element.Category == null)
        {
            return false;
        }

        return element.Category.BuiltInCategory switch
        {
            BuiltInCategory.OST_DuctFitting => options.IncludeDuctFittings,
            BuiltInCategory.OST_PipeFitting => options.IncludePipeFittings,
            BuiltInCategory.OST_ConduitFitting => options.IncludeConduitFittings,
            _ => false
        };
    }

    public RotateFittingsResult Rotate(Document document, Element element, XYZ? pickPoint, RotateFittingsOptions options)
    {
        long elementId = element.Id.Value;
        if (element.Pinned)
        {
            return BuildSkipped(elementId, "element jest przypiety (pin)");
        }

        List<Connector> connectors = MepConnectorHelper.GetPhysicalConnectors(element).ToList();
        if (connectors.Count == 0)
        {
            return BuildSkipped(elementId, "element nie ma konektorow");
        }

        Connector pivot = SelectPivotConnector(connectors, pickPoint);
        Line axis = BuildAxis(element, pivot, options.AxisMode);
        bool keepPivot = options.AxisMode == RotateFittingsAxisMode.ConnectorAxis;

        List<(XYZ Origin, ElementId PartnerOwnerId, XYZ PartnerOrigin)> brokenLinks = [];
        foreach (Connector connector in connectors)
        {
            if (keepPivot && connector.Id == pivot.Id)
            {
                continue;
            }

            foreach (Connector partner in MepConnectorHelper.GetConnectedPartners(connector))
            {
                brokenLinks.Add((connector.Origin, partner.Owner.Id, partner.Origin));
                connector.DisconnectFrom(partner);
            }
        }

        double angleRadians = options.AngleDegrees * Math.PI / 180d;
        ElementTransformUtils.RotateElement(document, element.Id, axis, angleRadians);
        document.Regenerate();

        int reconnected = ReconnectCoincident(document, element, brokenLinks);
        int disconnected = brokenLinks.Count - reconnected;

        string axisText = options.AxisMode == RotateFittingsAxisMode.ConnectorAxis ? "os konektora" : "os pionowa";
        string disconnectText = disconnected > 0 ? $", rozlaczono {disconnected} polaczen" : string.Empty;
        return new RotateFittingsResult
        {
            HasChanges = true,
            ElementId = elementId,
            DisconnectedCount = disconnected,
            Message = $"{MepConnectorHelper.Describe(element)}: obrot o {options.AngleDegrees:0.##} st. ({axisText}){disconnectText}."
        };
    }

    private static Connector SelectPivotConnector(List<Connector> connectors, XYZ? pickPoint)
    {
        // The fitting turns around the connection to the main run, so connected connectors win over free ones.
        List<Connector> connected = connectors.Where(connector => connector.IsConnected).ToList();
        List<Connector> candidates = connected.Count > 0 ? connected : connectors;
        if (pickPoint == null)
        {
            return candidates[0];
        }

        return candidates.OrderBy(connector => connector.Origin.DistanceTo(pickPoint)).First();
    }

    private static Line BuildAxis(Element element, Connector pivot, RotateFittingsAxisMode axisMode)
    {
        if (axisMode == RotateFittingsAxisMode.VerticalAxis)
        {
            XYZ origin = element.Location is LocationPoint locationPoint ? locationPoint.Point : pivot.Origin;
            return Line.CreateUnbound(origin, XYZ.BasisZ);
        }

        return Line.CreateUnbound(pivot.Origin, MepConnectorHelper.GetDirection(pivot));
    }

    private static int ReconnectCoincident(
        Document document,
        Element element,
        List<(XYZ Origin, ElementId PartnerOwnerId, XYZ PartnerOrigin)> brokenLinks)
    {
        int reconnected = 0;
        foreach ((XYZ _, ElementId partnerOwnerId, XYZ partnerOrigin) in brokenLinks)
        {
            Element? partnerOwner = document.GetElement(partnerOwnerId);
            Connector? partner = partnerOwner == null ? null : MepConnectorHelper.FindConnectorAt(partnerOwner, partnerOrigin);
            if (partner == null || partner.IsConnected)
            {
                continue;
            }

            Connector? match = MepConnectorHelper.GetPhysicalConnectors(element)
                .FirstOrDefault(connector =>
                    !connector.IsConnected &&
                    connector.Origin.DistanceTo(partner.Origin) < MepConnectorHelper.PositionTolerance &&
                    MepConnectorHelper.AreOpposite(MepConnectorHelper.GetDirection(connector), MepConnectorHelper.GetDirection(partner)));
            if (match == null)
            {
                continue;
            }

            try
            {
                match.ConnectTo(partner);
                reconnected++;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Leave the link open; the summary reports it as disconnected.
            }
        }

        return reconnected;
    }

    private static RotateFittingsResult BuildSkipped(long elementId, string reason)
    {
        return new RotateFittingsResult
        {
            HasChanges = false,
            ElementId = elementId,
            Message = $"Element {elementId}: pominieto, {reason}."
        };
    }
}

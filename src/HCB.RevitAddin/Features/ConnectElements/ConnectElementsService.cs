using System;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ConnectElements.Models;

namespace HCB.RevitAddin.Features.ConnectElements;

public sealed class ConnectElementsService
{
    private const double AngleTolerance = 1e-9;

    public bool IsSupported(Element element)
    {
        return MepConnectorHelper.HasPhysicalConnectors(element);
    }

    public ConnectElementsResult Connect(Document document, Element target, XYZ? targetPoint, Element moving, XYZ? movingPoint)
    {
        if (target.Id == moving.Id)
        {
            return BuildFailure("wskazano dwa razy ten sam element");
        }

        if (moving.Pinned)
        {
            return BuildFailure($"{MepConnectorHelper.Describe(moving)} jest przypiety (pin)");
        }

        Connector? targetConnector = MepConnectorHelper.FindClosestConnector(target, targetPoint, preferFree: true);
        if (targetConnector == null || targetConnector.IsConnected)
        {
            return BuildFailure($"{MepConnectorHelper.Describe(target)} nie ma wolnego konektora");
        }

        Connector? movingConnector = MepConnectorHelper.FindClosestConnector(moving, movingPoint, preferFree: true);
        if (movingConnector == null)
        {
            return BuildFailure($"{MepConnectorHelper.Describe(moving)} nie ma konektorow");
        }

        if (targetConnector.Domain != movingConnector.Domain)
        {
            return BuildFailure($"konektory sa z roznych dziedzin ({targetConnector.Domain} / {movingConnector.Domain})");
        }

        int movingConnectorId = movingConnector.Id;
        int disconnected = DisconnectAll(moving);

        AlignDirection(document, moving, movingConnectorId, targetConnector);
        AlignProfile(document, moving, movingConnectorId, targetConnector);

        movingConnector = GetConnectorById(moving, movingConnectorId);
        XYZ translation = targetConnector.Origin - movingConnector.Origin;
        if (translation.GetLength() > MepConnectorHelper.PositionTolerance)
        {
            ElementTransformUtils.MoveElement(document, moving.Id, translation);
            document.Regenerate();
        }

        movingConnector = GetConnectorById(moving, movingConnectorId);
        movingConnector.ConnectTo(targetConnector);

        string disconnectText = disconnected > 0 ? $" (rozlaczono {disconnected} wczesniejszych polaczen)" : string.Empty;
        return new ConnectElementsResult
        {
            HasChanges = true,
            DisconnectedCount = disconnected,
            Message = $"{MepConnectorHelper.Describe(moving)} -> {MepConnectorHelper.Describe(target)}: polaczono{disconnectText}."
        };
    }

    private static int DisconnectAll(Element element)
    {
        int count = 0;
        foreach (Connector connector in MepConnectorHelper.GetPhysicalConnectors(element).ToList())
        {
            foreach (Connector partner in MepConnectorHelper.GetConnectedPartners(connector))
            {
                connector.DisconnectFrom(partner);
                count++;
            }
        }

        return count;
    }

    private static void AlignDirection(Document document, Element moving, int movingConnectorId, Connector targetConnector)
    {
        Connector movingConnector = GetConnectorById(moving, movingConnectorId);
        XYZ current = MepConnectorHelper.GetDirection(movingConnector);
        XYZ desired = MepConnectorHelper.GetDirection(targetConnector).Negate();

        double angle = current.AngleTo(desired);
        if (angle < AngleTolerance)
        {
            return;
        }

        XYZ axisDirection = current.CrossProduct(desired);
        axisDirection = axisDirection.GetLength() < 1e-9
            ? MepConnectorHelper.GetAnyPerpendicular(current)
            : axisDirection.Normalize();

        Rotate(document, moving, movingConnector.Origin, axisDirection, angle);
    }

    private static void AlignProfile(Document document, Element moving, int movingConnectorId, Connector targetConnector)
    {
        Connector movingConnector = GetConnectorById(moving, movingConnectorId);
        if (targetConnector.Shape is not (ConnectorProfileType.Rectangular or ConnectorProfileType.Oval) ||
            movingConnector.Shape != targetConnector.Shape)
        {
            return;
        }

        // Width maps onto width; a rectangle is symmetric under 180 degrees, so take the smaller turn.
        XYZ axis = MepConnectorHelper.GetDirection(movingConnector);
        XYZ currentX = movingConnector.CoordinateSystem.BasisX;
        XYZ targetX = targetConnector.CoordinateSystem.BasisX;
        double angle = SignedAngle(currentX, targetX, axis);
        if (Math.Abs(angle) > Math.PI / 2d)
        {
            angle -= Math.Sign(angle) * Math.PI;
        }

        if (Math.Abs(angle) < 1e-6)
        {
            return;
        }

        Rotate(document, moving, movingConnector.Origin, axis, angle);
    }

    private static double SignedAngle(XYZ from, XYZ to, XYZ axis)
    {
        double angle = from.AngleTo(to);
        return from.CrossProduct(to).DotProduct(axis) < 0d ? -angle : angle;
    }

    private static void Rotate(Document document, Element element, XYZ origin, XYZ axisDirection, double angle)
    {
        ElementTransformUtils.RotateElement(document, element.Id, Line.CreateUnbound(origin, axisDirection), angle);
        document.Regenerate();
    }

    private static Connector GetConnectorById(Element element, int connectorId)
    {
        return MepConnectorHelper.GetPhysicalConnectors(element).First(connector => connector.Id == connectorId);
    }

    private static ConnectElementsResult BuildFailure(string reason)
    {
        return new ConnectElementsResult
        {
            HasChanges = false,
            Message = $"Pominieto: {reason}."
        };
    }
}

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

    // Guards against dragging a whole building when the picked element is part of a large network.
    private const int MaxGroupSize = 500;

    public ConnectElementsResult Connect(Document document, Element target, XYZ? targetPoint, Element moving, XYZ? movingPoint)
    {
        if (target.Id == moving.Id)
        {
            return BuildFailure("wskazano dwa razy ten sam element");
        }

        Connector? targetConnector = MepConnectorHelper.FindClosestConnector(target, targetPoint, preferFree: true);
        if (targetConnector == null || targetConnector.IsConnected)
        {
            return BuildFailure($"{MepConnectorHelper.Describe(target)} nie ma wolnego konektora");
        }

        Connector? movingConnector = MepConnectorHelper.FindClosestConnector(moving, movingPoint, preferFree: true);
        if (movingConnector == null || movingConnector.IsConnected)
        {
            return BuildFailure($"{MepConnectorHelper.Describe(moving)} nie ma wolnego konektora");
        }

        if (targetConnector.Domain != movingConnector.Domain)
        {
            return BuildFailure($"konektory sa z roznych dziedzin ({targetConnector.Domain} / {movingConnector.Domain})");
        }

        // The moved element travels together with everything connected to it, so its connections stay intact.
        if (!MepConnectorHelper.TryCollectConnectedGroup(moving, MaxGroupSize, out HashSet<ElementId> group))
        {
            return BuildFailure($"{MepConnectorHelper.Describe(moving)} jest polaczony z ponad {MaxGroupSize} elementami");
        }

        if (group.Contains(target.Id))
        {
            return BuildFailure("oba elementy sa juz w jednej polaczonej sieci - nie mozna jej przesunac");
        }

        Element? pinned = group.Select(document.GetElement).FirstOrDefault(element => element?.Pinned == true);
        if (pinned != null)
        {
            return BuildFailure($"{MepConnectorHelper.Describe(pinned)} w przesuwanej grupie jest przypiety (pin)");
        }

        List<ElementId> groupIds = group.ToList();
        int movingConnectorId = movingConnector.Id;

        AlignDirection(document, groupIds, moving, movingConnectorId, targetConnector);
        AlignProfile(document, groupIds, moving, movingConnectorId, targetConnector);

        movingConnector = GetConnectorById(moving, movingConnectorId);
        XYZ translation = targetConnector.Origin - movingConnector.Origin;
        if (translation.GetLength() > MepConnectorHelper.PositionTolerance)
        {
            ElementTransformUtils.MoveElements(document, groupIds, translation);
            document.Regenerate();
        }

        movingConnector = GetConnectorById(moving, movingConnectorId);
        movingConnector.ConnectTo(targetConnector);

        string groupText = groupIds.Count > 1 ? $" razem z {groupIds.Count - 1} podlaczonymi elementami" : string.Empty;
        return new ConnectElementsResult
        {
            HasChanges = true,
            Message = $"{MepConnectorHelper.Describe(moving)} -> {MepConnectorHelper.Describe(target)}: polaczono{groupText}."
        };
    }

    private static void AlignDirection(Document document, ICollection<ElementId> groupIds, Element moving, int movingConnectorId, Connector targetConnector)
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

        Rotate(document, groupIds, movingConnector.Origin, axisDirection, angle);
    }

    private static void AlignProfile(Document document, ICollection<ElementId> groupIds, Element moving, int movingConnectorId, Connector targetConnector)
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

        Rotate(document, groupIds, movingConnector.Origin, axis, angle);
    }

    private static double SignedAngle(XYZ from, XYZ to, XYZ axis)
    {
        double angle = from.AngleTo(to);
        return from.CrossProduct(to).DotProduct(axis) < 0d ? -angle : angle;
    }

    private static void Rotate(Document document, ICollection<ElementId> groupIds, XYZ origin, XYZ axisDirection, double angle)
    {
        ElementTransformUtils.RotateElements(document, groupIds, Line.CreateUnbound(origin, axisDirection), angle);
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

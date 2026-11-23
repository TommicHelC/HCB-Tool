using System;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ConnectorExtend;
using HCB.RevitAddin.Features.ElbowBranch.Models;

namespace HCB.RevitAddin.Features.ElbowBranch;

public sealed class ElbowBranchService
{
    private static readonly double StubMinimumLength = UnitUtils.ConvertToInternalUnits(150d, UnitTypeId.Millimeters);

    private readonly ConnectorExtendService segmentFactory = new();

    public bool IsSupported(Element element)
    {
        return segmentFactory.IsSupported(element);
    }

    /// <summary>
    /// Adds an elbow at a free connector and a straight segment turning up (+Z) or to the right in global coordinates.
    /// </summary>
    public ElbowBranchResult Add(Document document, View view, Element element, XYZ? pickPoint, ElbowBranchDirection direction)
    {
        Connector? connector = MepConnectorHelper.FindClosestConnector(element, pickPoint, preferFree: true);
        if (connector == null || connector.IsConnected)
        {
            return BuildFailure(element, "brak wolnego konektora");
        }

        XYZ outward = MepConnectorHelper.GetDirection(connector);
        if (!TryGetBranchDirection(outward, direction, out XYZ branchDirection))
        {
            return BuildFailure(element, "konektor jest pionowy - kolano w gore nie ma sensu");
        }

        if (element is MEPCurve curve)
        {
            return AddOnCurve(document, view, curve, connector, branchDirection);
        }

        return AddOnFitting(document, view, element, connector, outward, branchDirection);
    }

    private static bool TryGetBranchDirection(XYZ outward, ElbowBranchDirection direction, out XYZ branchDirection)
    {
        bool isVertical = Math.Abs(outward.DotProduct(XYZ.BasisZ)) > 0.999;
        if (direction == ElbowBranchDirection.Up)
        {
            branchDirection = XYZ.BasisZ;
            return !isVertical;
        }

        // "Right" looking along the connector from above; a vertical connector turns towards global +X.
        branchDirection = isVertical ? XYZ.BasisX : outward.CrossProduct(XYZ.BasisZ).Normalize();
        return true;
    }

    private ElbowBranchResult AddOnCurve(Document document, View view, MEPCurve curve, Connector connector, XYZ branchDirection)
    {
        XYZ corner = connector.Origin;
        MEPCurve? branch = segmentFactory.CreateSegment(
            document, view, curve, connector, corner, branchDirection, ConnectorExtendService.DefaultLengthInternal, connectToSource: false, out string? error);
        if (branch == null)
        {
            return BuildFailure(curve, error ?? "nie udalo sie wstawic odcinka");
        }

        FamilyInstance elbow = CreateElbow(document, curve, branch, corner);
        return BuildSuccess(curve, elbow.Id, branch.Id);
    }

    private ElbowBranchResult AddOnFitting(Document document, View view, Element element, Connector connector, XYZ outward, XYZ branchDirection)
    {
        // Revit builds elbows between two curves, so a temporary stub carries the elbow and is removed afterwards.
        double stubLength = Math.Max(StubMinimumLength, GetConnectorSize(connector) * 2d);
        MEPCurve? stub = segmentFactory.CreateSegment(
            document, view, element, connector, connector.Origin, outward, stubLength, connectToSource: false, out string? error);
        if (stub == null)
        {
            return BuildFailure(element, error ?? "nie udalo sie wstawic odcinka");
        }

        XYZ corner = connector.Origin + outward.Multiply(stubLength);
        Connector stubEnd = MepConnectorHelper.FindConnectorAt(stub, corner)
            ?? throw new InvalidOperationException("Brak konektora na koncu odcinka pomocniczego.");
        MEPCurve? branch = segmentFactory.CreateSegment(
            document, view, stub, stubEnd, corner, branchDirection, ConnectorExtendService.DefaultLengthInternal, connectToSource: false, out error);
        if (branch == null)
        {
            return BuildFailure(element, error ?? "nie udalo sie wstawic odcinka");
        }

        FamilyInstance elbow = CreateElbow(document, stub, branch, corner);
        document.Delete(stub.Id);
        document.Regenerate();

        // Slide the elbow and branch back so the elbow sits directly on the clicked connector.
        Connector elbowInlet = MepConnectorHelper.GetPhysicalConnectors(elbow)
            .Where(candidate => !candidate.IsConnected)
            .OrderBy(candidate => candidate.Origin.DistanceTo(connector.Origin))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Kolano nie ma wolnego konektora wejsciowego.");
        XYZ translation = connector.Origin - elbowInlet.Origin;
        ElementTransformUtils.MoveElements(document, [elbow.Id, branch.Id], translation);
        document.Regenerate();

        elbowInlet = MepConnectorHelper.FindConnectorAt(elbow, connector.Origin)
            ?? throw new InvalidOperationException("Nie udalo sie dosunac kolana do konektora.");
        elbowInlet.ConnectTo(connector);
        return BuildSuccess(element, elbow.Id, branch.Id);
    }

    private static FamilyInstance CreateElbow(Document document, MEPCurve first, MEPCurve second, XYZ corner)
    {
        Connector firstConnector = MepConnectorHelper.FindConnectorAt(first, corner)
            ?? throw new InvalidOperationException("Brak konektora odcinka w narozu.");
        Connector secondConnector = MepConnectorHelper.FindConnectorAt(second, corner)
            ?? throw new InvalidOperationException("Brak konektora nowego odcinka w narozu.");
        FamilyInstance elbow = document.Create.NewElbowFitting(firstConnector, secondConnector);
        document.Regenerate();
        return elbow;
    }

    private static double GetConnectorSize(Connector connector)
    {
        return connector.Shape == ConnectorProfileType.Round
            ? connector.Radius * 2d
            : Math.Max(connector.Width, connector.Height);
    }

    private static ElbowBranchResult BuildSuccess(Element element, ElementId elbowId, ElementId branchId)
    {
        return new ElbowBranchResult
        {
            HasChanges = true,
            CreatedIds = [elbowId, branchId],
            Message = $"{MepConnectorHelper.Describe(element)}: dodano kolano {elbowId.Value} i odcinek {branchId.Value}."
        };
    }

    private static ElbowBranchResult BuildFailure(Element element, string reason)
    {
        return new ElbowBranchResult
        {
            HasChanges = false,
            Message = $"{MepConnectorHelper.Describe(element)}: pominieto, {reason}."
        };
    }
}

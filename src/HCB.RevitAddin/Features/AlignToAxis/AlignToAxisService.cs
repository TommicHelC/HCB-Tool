using System.Diagnostics.CodeAnalysis;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AlignToAxis.Models;

namespace HCB.RevitAddin.Features.AlignToAxis;

public sealed class AlignToAxisService
{
    public bool IsReference(Element element)
    {
        return TryGetLine(element, out _);
    }

    public bool IsAlignable(Element element)
    {
        return IsReference(element) || MepConnectorHelper.HasPhysicalConnectors(element);
    }

    public bool TryGetLine(Element element, [NotNullWhen(true)] out Line? line)
    {
        line = element is MEPCurve { Location: LocationCurve { Curve: Line curveLine } } ? curveLine : null;
        return line != null;
    }

    /// <summary>
    /// Moves the target so its axis meets the reference axis. Only the offset across both axes is removed,
    /// so the target keeps its position along its own run and its connections stay intact.
    /// </summary>
    public AlignToAxisResult Align(Document document, Line referenceLine, Element target, XYZ? pickPoint)
    {
        if (target.Pinned)
        {
            return BuildFailure(target, "element jest przypiety (pin)");
        }

        if (!TryGetTargetAxis(target, pickPoint, out XYZ origin, out XYZ direction))
        {
            return BuildFailure(target, "nie mozna odczytac osi elementu");
        }

        XYZ referenceDirection = referenceLine.Direction.Normalize();
        XYZ offset = referenceLine.GetEndPoint(0) - origin;
        XYZ cross = referenceDirection.CrossProduct(direction);

        XYZ translation;
        if (cross.GetLength() < 1e-6)
        {
            // Parallel axes: make them collinear.
            translation = offset - referenceDirection.Multiply(offset.DotProduct(referenceDirection));
        }
        else
        {
            // Crossing axes: remove only the distance along the common perpendicular.
            XYZ normal = cross.Normalize();
            translation = normal.Multiply(offset.DotProduct(normal));
        }

        if (translation.GetLength() < MepConnectorHelper.PositionTolerance)
        {
            return new AlignToAxisResult
            {
                HasChanges = false,
                Message = $"{MepConnectorHelper.Describe(target)}: juz w osi."
            };
        }

        ElementTransformUtils.MoveElement(document, target.Id, translation);
        return new AlignToAxisResult
        {
            HasChanges = true,
            Message = $"{MepConnectorHelper.Describe(target)}: wyrownano."
        };
    }

    private bool TryGetTargetAxis(Element target, XYZ? pickPoint, out XYZ origin, out XYZ direction)
    {
        if (TryGetLine(target, out Line? line))
        {
            origin = line.GetEndPoint(0);
            direction = line.Direction.Normalize();
            return true;
        }

        Connector? connector = MepConnectorHelper.FindClosestConnector(target, pickPoint, preferFree: false);
        if (connector != null)
        {
            origin = connector.Origin;
            direction = MepConnectorHelper.GetDirection(connector);
            return true;
        }

        origin = XYZ.Zero;
        direction = XYZ.BasisX;
        return false;
    }

    private static AlignToAxisResult BuildFailure(Element element, string reason)
    {
        return new AlignToAxisResult
        {
            HasChanges = false,
            IsError = true,
            Message = $"{MepConnectorHelper.Describe(element)}: pominieto, {reason}."
        };
    }
}

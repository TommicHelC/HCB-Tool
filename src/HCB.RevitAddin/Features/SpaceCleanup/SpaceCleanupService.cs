using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using HCB.RevitAddin.Features.SpaceCleanup.Models;

namespace HCB.RevitAddin.Features.SpaceCleanup;

public sealed class SpaceCleanupService
{
    private const string NoLevelLabel = "(brak poziomu)";

    public IReadOnlyList<SpaceCleanupItem> GetSpaces(Document document)
    {
        SpatialElementBoundaryOptions boundaryOptions = new();

        return new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .OfType<Space>()
            .Select(space => new SpaceCleanupItem(
                space.Id.Value,
                space.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty,
                space.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? string.Empty,
                space.Level?.Name ?? NoLevelLabel,
                GetStatus(space, boundaryOptions)))
            .ToList();
    }

    public static SpacePlacementStatus GetStatus(Space space, SpatialElementBoundaryOptions boundaryOptions)
    {
        if (space.Location == null)
        {
            return SpacePlacementStatus.NotPlaced;
        }

        if (space.Area > 0)
        {
            return SpacePlacementStatus.Placed;
        }

        // Placed but without area: no boundary loops means "Not Enclosed",
        // loops present means the space shares its enclosure with another one ("Redundant").
        IList<IList<BoundarySegment>>? loops = space.GetBoundarySegments(boundaryOptions);
        return loops == null || loops.Count == 0
            ? SpacePlacementStatus.NotEnclosed
            : SpacePlacementStatus.Redundant;
    }

    public static string GetStatusLabel(SpacePlacementStatus status) => status switch
    {
        SpacePlacementStatus.NotPlaced => "Nieumieszczona",
        SpacePlacementStatus.NotEnclosed => "Nieobwiedziona",
        SpacePlacementStatus.Redundant => "Nadmiarowa",
        _ => "Umieszczona"
    };

    public static string GetDisplayLabel(SpaceCleanupItem item)
    {
        string number = string.IsNullOrWhiteSpace(item.Number) ? "-" : item.Number;
        string name = string.IsNullOrWhiteSpace(item.Name) ? "(bez nazwy)" : item.Name;
        return $"{name}  |  Nr {number}  |  {item.LevelName}  |  {GetStatusLabel(item.Status)}  [{item.Id}]";
    }

    public SpaceCleanupResult DeleteSpaces(Document document, IEnumerable<SpaceCleanupItem> items)
    {
        SpaceCleanupResult result = new();

        using Transaction transaction = new(document, "HCB Space Cleanup");
        transaction.Start();

        foreach (SpaceCleanupItem item in items)
        {
            string label = GetDisplayLabel(item);
            try
            {
                ICollection<ElementId> deleted = document.Delete(new ElementId(item.Id));
                result.DeletedCount++;
                result.DependentDeletedCount += System.Math.Max(0, deleted.Count - 1);
                result.Messages.Add($"Usunieto: {label}");
            }
            catch (System.Exception ex)
            {
                result.FailedCount++;
                result.Messages.Add($"Nie udalo sie usunac: {label} ({ex.Message})");
            }
        }

        transaction.Commit();
        return result;
    }
}

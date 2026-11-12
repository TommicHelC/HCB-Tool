using System.Collections.Generic;

namespace HCB.RevitAddin.Features.SpaceCleanup.Models;

public enum SpacePlacementStatus
{
    Placed,
    NotPlaced,
    NotEnclosed,
    Redundant
}

public sealed record SpaceCleanupItem(
    long Id,
    string Name,
    string Number,
    string LevelName,
    SpacePlacementStatus Status);

public sealed class SpaceCleanupResult
{
    public int DeletedCount { get; set; }

    public int DependentDeletedCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Messages { get; } = new();
}

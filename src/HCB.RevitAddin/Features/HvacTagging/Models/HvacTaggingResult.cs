namespace HCB.RevitAddin.Features.HvacTagging.Models;

public sealed class HvacTaggingResult
{
    public int ViewsCount { get; set; }

    public int CandidatesCount { get; set; }

    public int CreatedCount { get; set; }

    public int ExistingCount { get; set; }

    public int DuplicateCount { get; set; }

    public int BlockedCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Messages { get; } = [];
}

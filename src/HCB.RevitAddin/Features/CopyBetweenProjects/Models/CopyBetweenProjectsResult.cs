namespace HCB.RevitAddin.Features.CopyBetweenProjects.Models;

public sealed class CopyBetweenProjectsResult
{
    public int RequestedItemsCount { get; set; }

    public int RequestedElementIdsCount { get; set; }

    public int CopiedElementIdsCount { get; set; }

    public string SourceDocumentTitle { get; set; } = string.Empty;

    public string TargetDocumentTitle { get; set; } = string.Empty;

    public List<string> Messages { get; } = [];
}

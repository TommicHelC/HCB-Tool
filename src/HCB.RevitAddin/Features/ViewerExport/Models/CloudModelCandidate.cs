using System;

namespace HCB.RevitAddin.Features.ViewerExport.Models;

public sealed class CloudModelCandidate
{
    public Guid ProjectGuid { get; init; }

    public Guid ModelGuid { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public string CachedFilePath { get; init; } = string.Empty;

    public DateTime LastUsed { get; init; }

    public string DisplayName => $"{ProjectName} / {ModelName}";
}

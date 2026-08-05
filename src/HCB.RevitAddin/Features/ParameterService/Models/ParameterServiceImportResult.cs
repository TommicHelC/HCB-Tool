using System.Collections.Generic;

namespace HCB.RevitAddin.Features.ParameterService.Models;

public sealed class ParameterServiceImportResult
{
    public int SelectedCount { get; set; }

    public int ImportedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Messages { get; } = [];
}
